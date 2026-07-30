using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Regression for the avatar-swap face-driver crash: swapping a blink-capable avatar
/// straight to a blink-less one (far avatar, generic import) re-initializes the shared
/// per-player driver while the OUTGOING avatar — and its BasisMeshRendererCheck — stays
/// alive until its end-of-frame destroy, then fires a final visibility notification into
/// the driver. With stale state left behind that walked a cleared index list with the old
/// count: ArgumentOutOfRangeException per swap, hundreds per session in a bot lobby.
/// </summary>
public class BasisRemoteFaceDriverSwapTests
{
    [Test]
    public void SwapToBlinklessAvatar_StaleDestroyNotification_IsSafe()
    {
        GameObject blinkObject = new GameObject("BlinkAvatar");
        GameObject facelessObject = new GameObject("FarAvatar");
        Mesh mesh = new Mesh();
        try
        {
            SkinnedMeshRenderer blinkMesh = blinkObject.AddComponent<SkinnedMeshRenderer>();
            mesh.vertices = new Vector3[1];
            mesh.AddBlendShapeFrame("blink", 100f, new Vector3[1], null, null);
            blinkMesh.sharedMesh = mesh;
            BasisAvatar blinkAvatar = blinkObject.AddComponent<BasisAvatar>();
            blinkAvatar.FaceBlinkMesh = blinkMesh;
            blinkAvatar.BlinkViseme = new int[] { 0 };

            BasisMeshRendererCheck oldCheck = blinkObject.AddComponent<BasisMeshRendererCheck>();
            BasisRemotePlayer remote = new BasisRemotePlayer
            {
                DisplayName = "face-swap-test",
                UUID = System.Guid.NewGuid().ToString("N"),
                FaceRenderer = oldCheck,
            };

            BasisRemoteFaceDriver driver = new BasisRemoteFaceDriver();
            driver.Initialize(remote, blinkAvatar);
            Assert.AreEqual(1, driver.blendShapeCount, "blink avatar wires one blendshape");
            Assert.IsNotNull(oldCheck.Check, "driver subscribes the blink avatar's check");

            // The swap: far avatars carry a FaceVisemeMesh but no FaceBlinkMesh, so the
            // driver re-initializes down the early-return path.
            BasisAvatar farAvatar = facelessObject.AddComponent<BasisAvatar>();
            driver.Initialize(remote, farAvatar);

            Assert.AreEqual(0, driver.blendShapeCount, "stale count must not survive the re-init");
            Assert.IsNull(driver.meshRenderer, "stale renderer must not survive the re-init");
            Assert.IsNull(oldCheck.Check, "the old check must be unsubscribed at swap time");

            // The outgoing avatar's destroy-frame OnBecameInvisible — the crash in the wild.
            Assert.DoesNotThrow(() => oldCheck.Check?.Invoke(false));
            Assert.DoesNotThrow(() => driver.UpdateFaceVisibility(false));
        }
        finally
        {
            Object.DestroyImmediate(blinkObject);
            Object.DestroyImmediate(facelessObject);
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void SwapBackToBlinkAvatar_Resubscribes()
    {
        GameObject blinkObject = new GameObject("BlinkAvatar2");
        Mesh mesh = new Mesh();
        try
        {
            SkinnedMeshRenderer blinkMesh = blinkObject.AddComponent<SkinnedMeshRenderer>();
            mesh.vertices = new Vector3[1];
            mesh.AddBlendShapeFrame("blink", 100f, new Vector3[1], null, null);
            blinkMesh.sharedMesh = mesh;
            BasisAvatar blinkAvatar = blinkObject.AddComponent<BasisAvatar>();
            blinkAvatar.FaceBlinkMesh = blinkMesh;
            blinkAvatar.BlinkViseme = new int[] { 0 };
            BasisMeshRendererCheck check = blinkObject.AddComponent<BasisMeshRendererCheck>();

            BasisRemotePlayer remote = new BasisRemotePlayer
            {
                DisplayName = "face-reinit-test",
                UUID = System.Guid.NewGuid().ToString("N"),
                FaceRenderer = check,
            };

            BasisRemoteFaceDriver driver = new BasisRemoteFaceDriver();
            driver.Initialize(remote, null);
            Assert.AreEqual(0, driver.blendShapeCount);

            driver.Initialize(remote, blinkAvatar);
            Assert.AreEqual(1, driver.blendShapeCount);
            Assert.IsNotNull(check.Check);

            driver.OnDestroy();
            Assert.IsNull(check.Check, "teardown unsubscribes the tracked check");
        }
        finally
        {
            Object.DestroyImmediate(blinkObject);
            Object.DestroyImmediate(mesh);
        }
    }
}
