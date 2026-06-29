using UnityEngine;

namespace Basis.Shims
{
	/// <summary>
	/// Bridges the server-pushed global Cilbox admin lock to the sandbox runtime. While the lock is
	/// set, avatar boxes (the ones that opt in via ObeysGlobalDisable) abort in InterpreterEntry, so
	/// no sandboxed avatar code runs. This is the only assembly that references both the Basis
	/// network layer and Cilbox.
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
			BasisDebug.LogError("Not Implementated!");
		}
	}
}
