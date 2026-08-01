using System;
using System.Collections.Generic;
using UnityEngine;
using Basis.Scripts.BasisSdk.Interactions;
using Cilbox;

namespace Basis.Shims
{
	/// <summary>
	/// Lets a sandboxed script react when a player handles this object's jiggle physics — the avatar,
	/// prop or world object can answer back when someone grabs its hair or rests a hand on it
	/// (issue #685).
	///
	/// Declare either callback and opt in once from <c>Start</c>:
	/// <code>
	/// void Start() { GetComponent&lt;BasisJiggleEventShim&gt;(); }
	/// void OnJiggleGrab( int playerId, bool began, int hand, int rigIndex ) { }
	/// void OnJiggleTouch( int playerId, bool began, int hand, int rigIndex ) { }
	/// </code>
	/// A no-argument version of either is accepted too, for scripts that only care that it happened.
	/// Cilbox creates the component on demand, so fetching it is the whole opt-in.
	///
	/// <b>Nothing is detected until a script asks.</b> Registering is what turns detection on for
	/// this object, and the framework does no per-frame work at all while no shim is registered, so
	/// content that does not use this costs nothing in a crowded room.
	///
	/// Touch means a hand is close enough to grab, measured with the same palm-to-fingertip grip
	/// volume grabbing uses. Both callbacks are edge triggered: one call when it starts, one when it
	/// ends, with a short dwell so a resting hand cannot spam the interpreter. Only plain numbers
	/// cross the boundary — no transform, player or simulation handle is ever handed over.
	///
	/// This type is method-restricted in <c>CilboxSceneBasis.extraMethodWhitelist</c>,
	/// <c>CilboxAvatarBasis.extraMethodWhitelist</c> and <c>CilboxPropBasis.extraMethodWhitelist</c>.
	/// </summary>
	public class BasisJiggleEventShim : CilboxShim
	{
		public const string GrabCallbackName = "OnJiggleGrab";
		public const string TouchCallbackName = "OnJiggleTouch";

		/// <summary>Ceiling on callbacks delivered to one object per frame.</summary>
		public const int MaxDispatchesPerFrame = 32;

		private struct Binding
		{
			public CilboxProxy Proxy;
			public CilboxMethod Method;
			public bool WantsArguments;
		}

		private readonly List<Binding> grabBindings = new List<Binding>();
		private readonly List<Binding> touchBindings = new List<Binding>();
		private readonly object[] arguments = new object[4];
		private Action<BasisJiggleInteractionEvents.InteractionEvent> handler;
		private bool bound = false;
		private int dispatchedThisFrame;
		private int dispatchFrame = -1;

		private void OnEnable()
		{
			Bind();
			handler ??= OnInteraction;
			BasisJiggleInteractionEvents.RegisterListener( transform, handler );
		}

		private void OnDisable()
		{
			if( handler != null )
			{
				BasisJiggleInteractionEvents.UnregisterListener( transform, handler );
			}
		}

		/// <summary>
		/// How many jiggle rigs this object has. The <c>rigIndex</c> in a callback is a position in
		/// this set, in hierarchy order.
		/// </summary>
		public int GetJiggleRigCount()
		{
			return BasisJiggleInteractionEvents.GetRigCount( transform );
		}

		/// <summary>
		/// Name of a rig's root bone, so a callback can be told which chain it was about.
		/// </summary>
		public string GetJiggleRigName( int rigIndex )
		{
			return BasisJiggleInteractionEvents.GetRigName( transform, rigIndex );
		}

		/// <summary>
		/// Index of the rig whose root bone has this name, or -1. Resolve the chains you care about
		/// once in <c>Start</c> and compare against them in the callbacks — hierarchy order is stable
		/// for a given object, but an index hard-coded against one avatar means nothing on another.
		/// <code>
		/// int tail;
		/// void Start() { GetComponent&lt;BasisJiggleEventShim&gt;(); tail = GetComponent&lt;BasisJiggleEventShim&gt;().FindJiggleRig( "Tail" ); }
		/// void OnJiggleTouch( int playerId, bool began, int hand, int rigIndex )
		/// {
		///     if( rigIndex == tail &amp;&amp; began ) { /* react */ }
		/// }
		/// </code>
		/// </summary>
		public int FindJiggleRig( string rigName )
		{
			return BasisJiggleInteractionEvents.FindRig( transform, rigName );
		}

		/// <summary>
		/// Re-scans this GameObject for interpreted classes declaring either callback. Only needed if
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
			grabBindings.Clear();
			touchBindings.Clear();

			// One GameObject can carry several cilboxed scripts, each its own proxy.
			CilboxProxy[] proxies = GetComponents<CilboxProxy>();
			for( int i = 0; i < proxies.Length; i++ )
			{
				CilboxProxy p = proxies[i];
				CilboxClass cls = p != null ? p.cls : null;
				if( cls == null || cls.methodNameToIndex == null ) continue;

				TryBind( p, cls, GrabCallbackName, grabBindings );
				TryBind( p, cls, TouchCallbackName, touchBindings );
			}
		}

		private static void TryBind( CilboxProxy proxy, CilboxClass cls, string name, List<Binding> into )
		{
			uint idx;
			if( !cls.methodNameToIndex.TryGetValue( name, out idx ) ) return;

			// Arity is settled here rather than at call time: Interpret() pushes exactly what it is
			// given, so a signature that does not match would corrupt the interpreter stack.
			CilboxMethod m = cls.methods[idx];
			if( m.isStatic ) return;
			int parameterCount = m.signatureParameters != null ? m.signatureParameters.Length : 0;
			if( parameterCount != 0 && parameterCount != 4 ) return;

			into.Add( new Binding { Proxy = proxy, Method = m, WantsArguments = parameterCount == 4 } );
		}

		private void OnInteraction( BasisJiggleInteractionEvents.InteractionEvent interaction )
		{
			List<Binding> bindings = interaction.Kind == BasisJiggleInteractionEvents.InteractionKind.Grab
				? grabBindings
				: touchBindings;
			if( bindings.Count == 0 ) return;

			// Cilbox meters interpreted opcodes, not the native work that reaches them, so the cap on
			// how often content can be re-entered has to live here.
			if( dispatchFrame != Time.frameCount )
			{
				dispatchFrame = Time.frameCount;
				dispatchedThisFrame = 0;
			}
			if( dispatchedThisFrame >= MaxDispatchesPerFrame ) return;

			arguments[0] = (int)interaction.PlayerId;
			arguments[1] = interaction.Began;
			arguments[2] = (int)interaction.Hand;
			arguments[3] = (int)interaction.RigIndex;

			for( int i = 0; i < bindings.Count; i++ )
			{
				Binding binding = bindings[i];
				CilboxProxy p = binding.Proxy;
				if( p == null || p.disabled || !p.enabled ) continue;
				if( dispatchedThisFrame >= MaxDispatchesPerFrame ) return;
				dispatchedThisFrame++;

				try
				{
					binding.Method.Interpret( p, binding.WantsArguments ? arguments : null );
				}
				catch( Exception e )
				{
					// One faulting script must not cost the other proxies on this object their
					// events. Cilbox has already disabled the offender by this point.
					Debug.LogException( e );
				}
			}
		}
	}
}
