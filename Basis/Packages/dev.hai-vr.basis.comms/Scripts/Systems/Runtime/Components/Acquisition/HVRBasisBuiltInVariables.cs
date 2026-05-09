using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/HVR Basis Built In Variables")]
    public class HVRBasisBuiltInVariables : MonoBehaviour, IHVRInitializable
    {
        private static readonly Dictionary<HVRAvatarComms, List<HVRBasisBuiltInVariables>> Required = new();
        private static readonly Dictionary<HVRAvatarComms, HVRBasisAcquisitionVisemeFlags> Flags = new();
        private static readonly int[] _addressIds = new int[BasisOpenLipSyncContext.VisemeCount];
        private static int _addressMax;
        private static FieldInfo _lastAppliedField;

        public const string VisemeAddressPrefix = "@System/Viseme/";
        // Names are based on the Viseme MPEG-4 Standard
        private const string sil = nameof(sil);
        private const string PP = nameof(PP);
        private const string FF = nameof(FF);
        private const string TH = nameof(TH);
        private const string DD = nameof(DD);
        private const string kk = nameof(kk);
        private const string CH = nameof(CH);
        private const string SS = nameof(SS);
        private const string nn = nameof(nn);
        private const string RR = nameof(RR);
        private const string aa = nameof(aa);
        private const string E = nameof(E);
        private const string ih = nameof(ih);
        private const string oh = nameof(oh);
        private const string ou = nameof(ou);
        private const string Gain = "Gain";

        public HVRBasisAcquisitionKind trait;
        public HVRBasisAcquisitionVisemeFlags requiredFlags;

        private HVRAvatarComms _comms;
        private BasisAvatar _avatar;

        private bool _isWearer;
        private BasisLocalPlayer _localPlayer;
        private BasisNetworkReceiver _remoteReceiver;

        private BasisOpenLipSyncContext _contextNullable;
        private float[] _lastAppliedRef;
        private float[] _lastRead;
        private float _lastMax;

        public void OnHVRAvatarReady(bool isWearer)
        {
            BasisDebug.Log("INIT");
            if (_lastAppliedField == null)
            {
                BasisDebug.Log("INIT2");
                _addressIds[0] = HVRAddress.AddressToId(VisemeAddressPrefix + sil);
                _addressIds[1] = HVRAddress.AddressToId(VisemeAddressPrefix + PP);
                _addressIds[2] = HVRAddress.AddressToId(VisemeAddressPrefix + FF);
                _addressIds[3] = HVRAddress.AddressToId(VisemeAddressPrefix + TH);
                _addressIds[4] = HVRAddress.AddressToId(VisemeAddressPrefix + DD);
                _addressIds[5] = HVRAddress.AddressToId(VisemeAddressPrefix + kk);
                _addressIds[6] = HVRAddress.AddressToId(VisemeAddressPrefix + CH);
                _addressIds[7] = HVRAddress.AddressToId(VisemeAddressPrefix + SS);
                _addressIds[8] = HVRAddress.AddressToId(VisemeAddressPrefix + nn);
                _addressIds[9] = HVRAddress.AddressToId(VisemeAddressPrefix + RR);
                _addressIds[10] = HVRAddress.AddressToId(VisemeAddressPrefix + aa);
                _addressIds[11] = HVRAddress.AddressToId(VisemeAddressPrefix + E);
                _addressIds[12] = HVRAddress.AddressToId(VisemeAddressPrefix + ih);
                _addressIds[13] = HVRAddress.AddressToId(VisemeAddressPrefix + oh);
                _addressIds[14] = HVRAddress.AddressToId(VisemeAddressPrefix + ou);
                _addressMax = HVRAddress.AddressToId(VisemeAddressPrefix + Gain);
                _lastAppliedField ??= typeof(BasisOpenLipSyncContext).GetField("_lastApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            BasisDebug.Log("INIT END");

            _avatar = HVRCommsUtil.GetAvatar(this);
            _isWearer = isWearer;
            if (isWearer)
            {
                BasisDebug.Log("HVRBasisSystemsAcquisition is wearer");
                _localPlayer = BasisLocalPlayer.Instance;
                BasisDebug.Log($"setting localplayer to {_localPlayer}");
            }
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isWearer)
        {
            if (!isWearer)
            {
                if (BasisNetworkPlayers.AvatarToPlayer(_avatar, out _, out var netPlayer) && netPlayer is BasisNetworkReceiver netReceiver)
                {
                    _remoteReceiver = netReceiver;
                }
            }
        }

        private void OnEnable()
        {
            if (requiredFlags == 0) return;

            _comms = HVRCommsUtil.GetComms(this);
            if (_comms == null) return;

            if (!Required.ContainsKey(_comms)) Required[_comms] = new List<HVRBasisBuiltInVariables>();
            Required[_comms].Add(this);
            Flags[_comms] = requiredFlags;
            ReaggregateFlags();
        }

        private void OnDisable()
        {
            if (_comms == null) return;
            if (_contextNullable == null) return;

            Required[_comms].Remove(this);
            if (Required[_comms].Count == 0)
            {
                Required.Remove(_comms);
                Flags.Remove(_comms);
            }
            else
            {
                ReaggregateFlags();
            }
        }

        private void ReaggregateFlags()
        {
            Flags[_comms] = Required[_comms].Aggregate((HVRBasisAcquisitionVisemeFlags)0, (current, acquisition) => current | acquisition.requiredFlags);
        }

        public static void Simulate()
        {
            foreach (var commsToRequired in Required)
            {
                commsToRequired.Value[0].ApplyForAllInComms(commsToRequired.Key);
            }
        }

        private void ApplyForAllInComms(HVRAvatarComms comms)
        {
            if (_isWearer && _localPlayer == null || !_isWearer && _remoteReceiver == null) return;

            _contextNullable ??= _isWearer
                ? BasisLocalPlayer.Instance.LocalVisemeDriver.openLipSyncContext
                : _remoteReceiver.AudioReceiverModule.BasisRemoteVisemeAudioDriver.BasisAudioAndVisemeDriver.openLipSyncContext;
            if (_contextNullable == null) return;

            _lastAppliedRef = (float[])_lastAppliedField.GetValue(_contextNullable);

            var flagsForThisComms = Flags[comms];

            var variableStore = comms.VariableStore;
            _lastRead ??= new float[BasisOpenLipSyncContext.VisemeCount];

            var max = 0f;
            for (var index = 0; index < _lastRead.Length; index++)
            {
                var lastApplied = _lastAppliedRef[index];
                if (index != 0) // Ignore "sil"
                {
                    max = Mathf.Max(max, lastApplied);
                }
                if ((flagsForThisComms & (HVRBasisAcquisitionVisemeFlags)(1 << index)) != 0)
                {
                    var lastRead = _lastRead[index];

                    if (!Mathf.Approximately(lastApplied, lastRead))
                    {
                        variableStore.SubmitOrDefineDefaultValue(_addressIds[index], lastApplied / 100f);
                        _lastRead[index] = lastApplied;
                    }
                }
            }

            if ((flagsForThisComms & HVRBasisAcquisitionVisemeFlags.Gain) != 0 && !Mathf.Approximately(max, _lastMax))
            {
                variableStore.SubmitOrDefineDefaultValue(_addressMax, max / 100f);
                _lastMax = max;
            }
        }
    }

    [Flags]
    public enum HVRBasisAcquisitionVisemeFlags
    {
        sil = 1 << 0,
        PP = 1 << 1,
        FF = 1 << 2,
        TH = 1 << 3,
        DD = 1 << 4,
        kk = 1 << 5,
        CH = 1 << 6,
        SS = 1 << 7,
        nn = 1 << 8,
        RR = 1 << 9,
        aa = 1 << 10,
        E = 1 << 11,
        ih = 1 << 12,
        oh = 1 << 13,
        ou = 1 << 14,
        Gain = 1 << 30
    }

    [Serializable]
    public enum HVRBasisAcquisitionKind
    {
        Viseme,
        FingerCurl,
    }
}
