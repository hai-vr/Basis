using System;
using System.Collections.Generic;
using UnityEngine;
using Basis.Scripts.Drivers;
using Cilbox;

namespace Basis.Shims
{
	/// <summary>
	/// Gives a sandboxed script the late-latch callback that fires after all pose work for the frame
	/// but before anything is drawn — the place to pin something to the head, a hand or the camera
	/// without a frame of lag. <c>Update</c>/<c>LateUpdate</c> both run before Basis has finished
	/// solving IK, so a transform written there trails the head by a frame in VR.
	///
	/// Declare <c>void OnBeforeRender()</c> and opt in once from <c>Start</c>:
	/// <code>
	/// void Start() { GetComponent&lt;BasisBeforeRenderShim&gt;(); }
	/// void OnBeforeRender() { transform.position = someHeadTransform.position; }
	/// </code>
	/// Cilbox creates the component on demand, so fetching it is the whole opt-in. The callback is
	/// resolved by name off the interpreted class and invoked directly — no host delegate is ever
	/// built for it, so there is no wrapper to leak and nothing to unsubscribe.
	///
	/// This is the sandbox bridge onto <see cref="BasisLocalCameraDriver.BeforeRender"/>, which is
	/// the same hook native code uses. It runs with the whole render waiting on it, so keep the body
	/// short. It does not fire on a headless client, which never renders.
	/// </summary>
	public class BasisBeforeRenderShim : CilboxShim
	{
		public const string CallbackName = "OnBeforeRender";

		private readonly List<CilboxProxy> targets = new List<CilboxProxy>();
		private readonly List<CilboxMethod> callbacks = new List<CilboxMethod>();
		private bool bound = false;

		private void OnEnable()
		{
			Bind();
			BasisLocalCameraDriver.BeforeRender += Dispatch;
		}

		private void OnDisable()
		{
			BasisLocalCameraDriver.BeforeRender -= Dispatch;
		}

		/// <summary>
		/// Re-scans this GameObject for interpreted classes declaring the callback. Only needed if
		/// proxies appear after the shim; the scan otherwise happens once when it is enabled.
		/// </summary>
		public void Rebind()
		{
			bound = false;
			Bind();
		}

		private void Bind()
		{
			if( bound ) return;
			bound = true;
			targets.Clear();
			callbacks.Clear();

			// One GameObject can carry several cilboxed scripts, each its own proxy.
			CilboxProxy[] proxies = GetComponents<CilboxProxy>();
			for( int i = 0; i < proxies.Length; i++ )
			{
				CilboxProxy p = proxies[i];
				CilboxClass cls = p != null ? p.cls : null;
				if( cls == null || cls.methodNameToIndex == null ) continue;

				uint idx;
				if( !cls.methodNameToIndex.TryGetValue( CallbackName, out idx ) ) continue;

				// Interpret() is called with no arguments and an instance receiver, so a static or
				// parameterised OnBeforeRender is not a match — binding it would corrupt the frame.
				CilboxMethod m = cls.methods[idx];
				if( m.isStatic || ( m.signatureParameters != null && m.signatureParameters.Length > 0 ) ) continue;

				targets.Add( p );
				callbacks.Add( m );
			}
		}

		private void Dispatch()
		{
			for( int i = 0; i < callbacks.Count; i++ )
			{
				CilboxProxy p = targets[i];
				if( p == null || p.disabled || !p.enabled ) continue;

				try
				{
					callbacks[i].Interpret( p, null );
				}
				catch( Exception e )
				{
					// One faulting script must not cost the other proxies on this object their
					// frame. Cilbox has already disabled the offender by this point.
					Debug.LogException( e );
				}
			}
		}
	}
}
