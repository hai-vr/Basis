using UnityEngine;

namespace Basis.Shims
{
	/// <summary>
	/// Mirrors the server-pushed admin Cilbox lock into ContentPolice. While set, ContentPolice
	/// strips the Cilbox sandbox host + proxies from each avatar as it loads, so no avatar script
	/// runs. Props and worlds keep their own Cilbox. Load-time only — avatars already loaded keep
	/// their Cilbox until they reload.
	/// </summary>
	public static class BasisCilboxLockBridge
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Hook()
		{
			BasisNetworkModeration.OnGlobalCilboxLockChanged -= Apply;
			BasisNetworkModeration.OnGlobalCilboxLockChanged += Apply;
			Apply(BasisNetworkModeration.GlobalCilboxLocked);
		}

		private static void Apply(bool locked)
		{
			ContentPoliceControl.AvatarCilboxLocked = locked;
		}
	}
}
