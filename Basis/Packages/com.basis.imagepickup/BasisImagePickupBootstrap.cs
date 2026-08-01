using Basis.Scripts.Platform;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Arms the image pickup service at startup and subscribes it to the client's shared OS
    /// file-drop bridge. The bridge owns the one window subclass there can be; this package only
    /// says which of the dropped paths are its own — <see cref="BasisImagePickupManager.SpawnFromFiles"/>
    /// keeps the supported image formats and ignores everything else in the batch.
    /// </summary>
    public static class BasisImagePickupBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            BasisImagePickupManager.Initialize();

            BasisDesktopFileDrop.RegisterAcceptedExtensions(".png", ".jpg", ".jpeg", ".gif");
            BasisDesktopFileDrop.OnFilesDropped -= OnFilesDropped;
            BasisDesktopFileDrop.OnFilesDropped += OnFilesDropped;
        }

        private static void OnFilesDropped(string[] paths)
        {
            BasisImagePickupManager.SpawnFromFiles(paths);
        }
    }
}
