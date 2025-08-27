using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Behaviour;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;

[assembly: InternalsVisibleTo("HVR.Basis.Comms.Editor")]
namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Automatic Face Tracking")]
    public class AutomaticFaceTracking : BasisAvatarMonoBehaviour
    {
        [SerializeField] internal bool useCustomMultiplier;
        [SerializeField] internal float eyeTrackingMultiplyX = 1f;
        [SerializeField] internal float eyeTrackingMultiplyY = 1f;

        [SerializeField] internal bool useOverrideDefinitionFiles;
        [SerializeField] internal BlendshapeActuationDefinitionFile[] overrideDefinitionFiles = Array.Empty<BlendshapeActuationDefinitionFile>();

        [SerializeField] internal bool useSupplementalDefinitionFiles;
        [SerializeField] internal BlendshapeActuationDefinitionFile[] supplementalDefinitionFiles = Array.Empty<BlendshapeActuationDefinitionFile>();

        private static BlendshapeActuationDefinitionFile _ueHandle = null;
        private static BlendshapeActuationDefinitionFile _arKitHandle = null;

        private BasisAvatar _avatar;
        private readonly Nethack _nethack;

        // Exposed to the Unity editor for this component
        [NonSerialized] internal bool successful;
        [NonSerialized] internal NamingConvention namingConvention;
        [NonSerialized] internal List<SkinnedMeshRenderer> renderers;
        [NonSerialized] internal OSCAcquisition oscAcquisition;
        [NonSerialized] internal BlendshapeActuation blendshapeActuation;
        [NonSerialized] internal EyeTrackingBoneActuation eyeTrackingBoneActuation;

        private AvatarMessageProcessing _network;

        public AutomaticFaceTracking()
        {
            _nethack = new Nethack(OnReadyBothAvatarAndNetwork);
        }

        private void Awake()
        {
            overrideDefinitionFiles = CommsUtil.SlowSanitizeEndUserProvidedObjectArray(overrideDefinitionFiles);
            supplementalDefinitionFiles = CommsUtil.SlowSanitizeEndUserProvidedObjectArray(supplementalDefinitionFiles);

            if (_avatar == null)
            {
                _avatar = CommsUtil.GetAvatar(this);
            }
            _avatar.OnAvatarReady += OnAvatarReady;

            _ueHandle ??= Addressables.LoadAssetAsync<BlendshapeActuationDefinitionFile>("HVR.Basis.Comms.FaceTracking.DefaultUnifiedExpressionsDefinitionFile").WaitForCompletion();
            _arKitHandle ??= Addressables.LoadAssetAsync<BlendshapeActuationDefinitionFile>("HVR.Basis.Comms.FaceTracking.DefaultARKitDefinitionFile").WaitForCompletion();

            Discover();
        }

        private void Discover()
        {
            var smrs = _avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            if (useOverrideDefinitionFiles && overrideDefinitionFiles != null && overrideDefinitionFiles.Length != 0)
            {
                namingConvention = NamingConvention.UserDefined;

                var files = AppendSupplemental(overrideDefinitionFiles);
                var foundSmrs = FindSkinnedMeshes(files, smrs);
                if (foundSmrs.Count > 0)
                {
                    SetupFaceTracking(files, foundSmrs);
                }
                else Failed();
            }
            else
            {
                namingConvention = GuessNamingConvention(smrs);

                if (namingConvention is NamingConvention.UnifiedExpressions or NamingConvention.ARKit)
                {
                    var files = AppendSupplemental(new []{ namingConvention == NamingConvention.UnifiedExpressions ? _ueHandle : _arKitHandle });
                    var foundSmrs = FindSkinnedMeshes(files, smrs);
                    if (foundSmrs.Count > 0)
                    {
                        SetupFaceTracking(files, foundSmrs);
                    }
                    else Failed();
                }
                else Failed();
            }
        }

        private BlendshapeActuationDefinitionFile[] AppendSupplemental(BlendshapeActuationDefinitionFile[] initial)
        {
            var toSearch = initial.ToList();
            if (useSupplementalDefinitionFiles && supplementalDefinitionFiles != null && supplementalDefinitionFiles.Length != 0)
            {
                toSearch.AddRange(supplementalDefinitionFiles);
            }
            return toSearch.ToArray();
        }

        private void Failed()
        {
            enabled = false;
        }

        private void SetupFaceTracking(BlendshapeActuationDefinitionFile definitionFile, List<SkinnedMeshRenderer> smrs)
        {
            SetupFaceTracking(new []{ definitionFile }, smrs);
        }

        private void SetupFaceTracking(BlendshapeActuationDefinitionFile[] definitionFiles, List<SkinnedMeshRenderer> smrs)
        {
            renderers = smrs;
            oscAcquisition = CreateOSCAcquisitionIfNotExists();

            blendshapeActuation = CreateGameObject(nameof(BlendshapeActuation), false)
                .AddComponent<BlendshapeActuation>();
            blendshapeActuation.AutoDefine(definitionFiles, smrs);
            blendshapeActuation.gameObject.SetActive(true);

            eyeTrackingBoneActuation = CreateGameObject(nameof(EyeTrackingBoneActuation), false)
                .AddComponent<EyeTrackingBoneActuation>();
            if (useCustomMultiplier)
            {
                eyeTrackingBoneActuation.multiplyX = eyeTrackingMultiplyX;
                eyeTrackingBoneActuation.multiplyY = eyeTrackingMultiplyY;
            }
            eyeTrackingBoneActuation.gameObject.SetActive(true);

            successful = true;
        }

        private OSCAcquisition CreateOSCAcquisitionIfNotExists()
        {
            var acquisition = _avatar.GetComponentInChildren<OSCAcquisition>();
            if (acquisition == null)
            {
                var acquisitionGo = CreateGameObject(nameof(OSCAcquisition));

                acquisition = acquisitionGo.AddComponent<OSCAcquisition>();
            }

            return acquisition;
        }

        private GameObject CreateGameObject(string suffix, bool active = true)
        {
            var go = new GameObject
            {
                name = $"Generated__{suffix}",
                transform =
                {
                    parent = _avatar.transform,
                }
            };
            if (!active) go.SetActive(false);
            return go;
        }

        internal enum NamingConvention
        {
            Unknown,
            UnifiedExpressions,
            ARKit,
            UserDefined
        }

        private NamingConvention GuessNamingConvention(SkinnedMeshRenderer[] smrs)
        {
            var unifiedExpressions = new HashSet<string> { "MouthRaiserLower", "MouthRaiserLowerLeft" };
            var arKit = new HashSet<string> { "mouthShrugLower" };
            foreach (var smr in smrs)
            {
                if (HasAnyBlendshape(smr, unifiedExpressions))
                {
                    return NamingConvention.UnifiedExpressions;
                }
                if (HasAnyBlendshape(smr, arKit))
                {
                    return NamingConvention.ARKit;
                }
            }

            return NamingConvention.Unknown;
        }

        private List<SkinnedMeshRenderer> FindSkinnedMeshes(BlendshapeActuationDefinitionFile[] definitionFiles, SkinnedMeshRenderer[] smrs)
        {
            var foundSmrs = new HashSet<SkinnedMeshRenderer>();
            foreach (var definitionFile in definitionFiles)
            {
                foundSmrs.UnionWith(FindSkinnedMeshes(definitionFile, smrs));
            }
            var foundSmrsAsList = foundSmrs.ToList();
            return foundSmrsAsList;
        }

        private List<SkinnedMeshRenderer> FindSkinnedMeshes(BlendshapeActuationDefinitionFile definitionFile, SkinnedMeshRenderer[] smrs)
        {
            var possibleBlendshapes = definitionFile.definitions
                .SelectMany(definition => definition.blendshapes)
                .Distinct()
                .ToHashSet();

            var validSmrs = new List<SkinnedMeshRenderer>();

            foreach (var smr in smrs)
            {
                if (HasAnyBlendshape(smr, possibleBlendshapes))
                {
                    validSmrs.Add(smr);
                }
            }

            return validSmrs;
        }

        private static bool HasAnyBlendshape(SkinnedMeshRenderer smr, HashSet<string> possibleBlendshapes)
        {
            var sharedMesh = smr.sharedMesh;
            if (sharedMesh != null)
            {
                for (var i = 0; i < sharedMesh.blendShapeCount; i++)
                {
                    var blendShapeName = sharedMesh.GetBlendShapeName(i);
                    if (possibleBlendshapes.Contains(blendShapeName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnAvatarReady(bool isWearer)
        {
            _nethack.AfterAvatarReady();
        }

        public override void OnNetworkReady(bool isLocallyOwned)
        {
            _nethack.AfterNetworkReady(isLocallyOwned);
        }

        private void OnReadyBothAvatarAndNetwork(bool isLocallyOwned)
        {
            _network = AvatarMessageProcessing.ForFeature(this, isLocallyOwned, _avatar.LinkedPlayerID, new AutoReceiver());
            _network.SendInitialPacket();
        }
    }

    internal class AutoReceiver : IFeatureReceiver
    {
        public void OnPacketReceived(ArraySegment<byte> data)
        {
        }

        public void OnResyncEveryoneRequested()
        {
        }

        public void OnResyncRequested(ushort[] whoAsked)
        {
        }
    }
}
