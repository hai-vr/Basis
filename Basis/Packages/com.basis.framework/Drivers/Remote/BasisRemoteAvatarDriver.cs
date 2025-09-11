using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using GatorDragonGames.JigglePhysics;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
namespace Basis.Scripts.Drivers
{
    [System.Serializable]
    public class BasisRemoteAvatarDriver : BasisAvatarDriver
    {
        public Action CalibrationComplete;
        [SerializeField]
        public BasisTransformMapping References = new BasisTransformMapping();
        public SkinnedMeshRenderer[] SkinnedMeshRenderer;
        public BasisPlayer Player;
        public bool HasEvents = false;
        public int SkinnedMeshRendererLength;
        public Vector3 AvatarInitalScale = Vector3.one;
        public void RemoteCalibration(BasisRemotePlayer player)
        {
            if (!IsAble(player))
            {
                return;
            }
            else
            {
                //  BasisDebug.Log("RemoteCalibration Underway", BasisDebug.LogTag.Avatar);
            }

            Player = player;


            SkinnedMeshRenderer = Player.BasisAvatar.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRendererLength = SkinnedMeshRenderer.Length;
            SetupAvatarLayers(Player, BasisLayerMapper.RemoteAvatarLayer);
            PutAvatarIntoTPose();

            AvatarInitalScale = Player.BasisAvatar.transform.localScale;
            BasisTransformMapping.AutoDetectReferences(Player.BasisAvatar.Animator, player.BasisAvatar.transform, ref References);
            References.RecordPoses(Player.BasisAvatar.Animator);
            var JiggleRigs = player.BasisAvatar.GetComponentsInChildren<JiggleRig>();

            foreach (JiggleRig Rig in JiggleRigs)
            {
                Rig.OnInitialize();
            }

            Player.FaceIsVisible = false;
            if (player.BasisAvatar == null)
            {
                BasisDebug.LogError("Missing Avatar On Remote", BasisDebug.LogTag.Avatar);
            }
            if (player.BasisAvatar.FaceVisemeMesh == null)
            {
                BasisDebug.Log("Missing Face for " + Player.DisplayName, BasisDebug.LogTag.Avatar);
            }
            Player.UpdateFaceVisibility(player.BasisAvatar.FaceVisemeMesh.isVisible);
            if (Player.FaceRenderer != null)
            {
                GameObject.Destroy(Player.FaceRenderer);
            }
            Player.FaceRenderer = BasisHelpers.GetOrAddComponent<BasisMeshRendererCheck>(player.BasisAvatar.FaceVisemeMesh.gameObject);
            Player.FaceRenderer.Check += Player.UpdateFaceVisibility;

            if (BasisFacialBlinkDriver.MeetsRequirements(player.BasisAvatar))
            {
                Player.FacialBlinkDriver.Initialize(Player, player.BasisAvatar);
            }
            player.RemoteEyeDriver.Initalize(this, player);
            UpdateWhenOffscreenAndDisableMatrixRecal(false);
            player.BasisAvatar.Animator.logWarnings = false;
            player.RemoteBoneDriver.CalculateTransformPositions(player);
            player.BasisAvatar.Animator.enabled = false;

            SetupAvatarJiggleColliders();
            ResetAvatarAnimator();
        }
        public bool CurrentlyTposing;
        public RuntimeAnimatorController SavedruntimeAnimatorController;
        public void PutAvatarIntoTPose()
        {
            BasisDebug.Log("PutAvatarIntoTPose", BasisDebug.LogTag.Avatar);
            CurrentlyTposing = true;
            if (SavedruntimeAnimatorController == null)
            {
                SavedruntimeAnimatorController = Player.BasisAvatar.Animator.runtimeAnimatorController;
            }
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<RuntimeAnimatorController> op = Addressables.LoadAssetAsync<RuntimeAnimatorController>(TPose);
            RuntimeAnimatorController RAC = op.WaitForCompletion();
            Player.BasisAvatar.Animator.runtimeAnimatorController = RAC;
            ForceUpdateAnimator(Player.BasisAvatar.Animator);
        }
        public const string TPose = "Assets/Animator/Animated TPose.controller";
        public void ForceUpdateAnimator(Animator Anim)
        {
            // Specify the time you want the Animator to update to (in seconds)
            float desiredTime = Time.deltaTime;

            // Call the Update method to force the Animator to update to the desired time
            Anim.Update(desiredTime);
        }

        public void ResetAvatarAnimator()
        {
            BasisDebug.Log("ResetAvatarAnimator", BasisDebug.LogTag.Avatar);
            Player.BasisAvatar.Animator.runtimeAnimatorController = SavedruntimeAnimatorController;
            SavedruntimeAnimatorController = null;
            CurrentlyTposing = false;
        }
        public async void SetupAvatarJiggleColliders()
        {
            RemoveJiggleRigColliders();
            BasisPlayerSettingsData BasisPlayerSettingsData = await BasisPlayerSettingsManager.RequestPlayerSettings(Player.UUID);
            if (BasisPlayerSettingsData.AvatarInteraction)
            {
                AddJiggleRigColliders(References);
            }
        }
        public bool IsAble(BasisRemotePlayer remotePlayer)
        {
            if (IsNull(remotePlayer.BasisAvatar))
            {
                return false;
            }
            if (remotePlayer.RemoteBoneDriver == null)
            {
                return false;
            }
            if (IsNull(remotePlayer.BasisAvatar.Animator))
            {
                return false;
            }
            if (IsNull(remotePlayer))
            {
                return false;
            }
            return true;
        }
        public bool IsNull(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                BasisDebug.LogError("Missing Object during calibration");
                return true;
            }
            else
            {
                return false;
            }
        }
        public void UpdateWhenOffscreenAndDisableMatrixRecal(bool State)
        {
            for (int Index = 0; Index < SkinnedMeshRendererLength; Index++)
            {
                SkinnedMeshRenderer Render = SkinnedMeshRenderer[Index];
                Render.updateWhenOffscreen = State;
                Render.forceMatrixRecalculationPerRender = false;
            }
        }
    }
}
