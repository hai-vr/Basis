using UnityEngine;

namespace Basis.ImagePickup
{
	/// <summary>
	/// Creates the persistent image pickup manager at startup, plus the desktop file-drop hook on Windows players.
	/// </summary>
	public static class BasisImagePickupBootstrap
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Initialize()
		{
			if (BasisImagePickupManager.Instance != null)
			{
				if (
					!BasisImagePickupManager.Instance.TryGetComponent(
						out BasisAnimatedImageScheduler _
					)
				)
				{
					BasisImagePickupManager.Instance.gameObject.AddComponent<BasisAnimatedImageScheduler>();
				}
				return;
			}

			var host = new GameObject("BasisImagePickupManager");
			host.AddComponent<BasisImagePickupManager>();
			host.AddComponent<BasisAnimatedImageScheduler>();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			host.AddComponent<BasisDesktopImageDropHook>();
#endif
		}
	}
}
