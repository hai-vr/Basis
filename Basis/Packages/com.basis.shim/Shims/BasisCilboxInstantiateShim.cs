using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cilbox;
using UnityEngine;
using UnityEngine.Events;

namespace Basis.Shims
{
	// Sanitizing replacement for UnityEngine.Object.Instantiate when called from
	// inside a cilbox sandbox. Prefabs referenced by a cilbox script can carry
	// MonoBehaviours and persistent UnityEvent listeners that would run native
	// code the moment the clone's Awake/OnEnable fires. To keep the sandbox
	// honest we:
	//   1. Instantiate the prefab as a child of a persistent, inactive host so
	//      the clone is not active-in-hierarchy and Awake is deferred.
	//   2. Destroy every component whose type is not in the prop content-police
	//      approved list (the same list that gates top-level prop loading).
	//   3. Walk every surviving component and disable every persistent UnityEvent
	//      listener. Cilbox scripts that want event callbacks must wire them at
	//      runtime via AddListener, which goes through the sandboxed delegate
	//      proxy path — persistent listeners from the bundle are never legitimate
	//      for cilbox-authored content.
	//   4. Reparent / position / activate the clone so the caller sees exactly
	//      what UnityEngine.Object.Instantiate would have returned.
	public static class BasisCilboxInstantiateShim
	{
		private static GameObject sanitationHost;

		private static Transform GetSanitationHost()
		{
			if( sanitationHost != null ) return sanitationHost.transform;

			sanitationHost = new GameObject( "BasisCilboxSanitationHost" );
			sanitationHost.SetActive( false );
			sanitationHost.hideFlags = HideFlags.HideAndDontSave;
			UnityEngine.Object.DontDestroyOnLoad( sanitationHost );
			return sanitationHost.transform;
		}

		// Called from CilboxPropBasis.CheckMethodAllowed / CilboxSceneBasis.CheckMethodAllowed
		// to resolve the shim overload that matches the original UnityEngine.Object.Instantiate
		// call site. Reuses the cilbox usage helper so overload selection (including generics)
		// matches exactly what the interpreter would have done against UnityEngine.Object.
		public static MethodInfo ResolveShim(
			CilboxUsage usage,
			String name,
			Serializee[] parametersIn,
			Serializee[] genericArgumentsIn,
			String fullSignature )
		{
			if( usage == null ) return null;
			if( name != "Instantiate" && name != "InstantiateAsync" ) return null;

			Type[] parameters = usage.TypeNamesToArrayOfNativeTypes( parametersIn );
			Type[] genericArguments = usage.TypeNamesToArrayOfNativeTypes( genericArgumentsIn );
			if( parameters == null ) return null;
			for( int i = 0; i < parameters.Length; i++ )
				if( parameters[i] == null ) return null;
			if( genericArguments == null ) genericArguments = Type.EmptyTypes;
			for( int i = 0; i < genericArguments.Length; i++ )
				if( genericArguments[i] == null ) return null;

			MethodBase m = usage.InternalGetNativeMethodFromTypeAndNameNoSecurity(
				typeof(BasisCilboxInstantiateShim),
				name,
				parameters,
				genericArguments,
				fullSignature );
			return m as MethodInfo;
		}

		// ------------------------------------------------------------------
		// Overloads that mirror UnityEngine.Object.Instantiate one-for-one.
		// ------------------------------------------------------------------

		public static UnityEngine.Object Instantiate( UnityEngine.Object original )
		{
			return ParkSanitizeReparent( original, null, false, null, null );
		}

		public static UnityEngine.Object Instantiate( UnityEngine.Object original, Transform parent )
		{
			return ParkSanitizeReparent( original, parent, true, null, null );
		}

		public static UnityEngine.Object Instantiate( UnityEngine.Object original, Transform parent, bool worldPositionStays )
		{
			return ParkSanitizeReparent( original, parent, worldPositionStays, null, null );
		}

		public static UnityEngine.Object Instantiate( UnityEngine.Object original, Vector3 position, Quaternion rotation )
		{
			return ParkSanitizeReparent( original, null, false, position, rotation );
		}

		public static UnityEngine.Object Instantiate( UnityEngine.Object original, Vector3 position, Quaternion rotation, Transform parent )
		{
			return ParkSanitizeReparent( original, parent, false, position, rotation );
		}

		public static T Instantiate<T>( T original ) where T : UnityEngine.Object
		{
			return ParkSanitizeReparent( original, null, false, null, null ) as T;
		}

		public static T Instantiate<T>( T original, Transform parent ) where T : UnityEngine.Object
		{
			return ParkSanitizeReparent( original, parent, true, null, null ) as T;
		}

		public static T Instantiate<T>( T original, Transform parent, bool worldPositionStays ) where T : UnityEngine.Object
		{
			return ParkSanitizeReparent( original, parent, worldPositionStays, null, null ) as T;
		}

		public static T Instantiate<T>( T original, Vector3 position, Quaternion rotation ) where T : UnityEngine.Object
		{
			return ParkSanitizeReparent( original, null, false, position, rotation ) as T;
		}

		public static T Instantiate<T>( T original, Vector3 position, Quaternion rotation, Transform parent ) where T : UnityEngine.Object
		{
			return ParkSanitizeReparent( original, parent, false, position, rotation ) as T;
		}

		// ------------------------------------------------------------------
		// Core pipeline: instantiate -> park under disabled host -> sanitize
		// -> re-home per the caller's requested transform state.
		// ------------------------------------------------------------------

		private static UnityEngine.Object ParkSanitizeReparent(
			UnityEngine.Object original,
			Transform finalParent,
			bool worldPositionStays,
			Vector3? position,
			Quaternion? rotation )
		{
			if( original == null )
			{
				Debug.LogWarning( "[BasisCilbox] Instantiate called with null original." );
				return null;
			}

			// Non-GameObject/Component clones (Material, Texture2D, AudioClip, ...)
			// can't carry MonoBehaviours or UnityEvents, so they skip the sanitizer.
			GameObject rootPrefab = ExtractRootGameObject( original );
			if( rootPrefab == null )
				return UnityEngine.Object.Instantiate( original );

			Transform host = GetSanitationHost();
			UnityEngine.Object cloneObj = UnityEngine.Object.Instantiate( original, host );
			GameObject cloneRoot = ExtractRootGameObject( cloneObj );
			if( cloneRoot == null )
			{
				Debug.LogWarning( "[BasisCilbox] Instantiate produced a clone with no GameObject root; destroying." );
				if( cloneObj != null ) UnityEngine.Object.Destroy( cloneObj );
				return null;
			}

			try
			{
				Sanitize( cloneRoot );
			}
			catch( Exception e )
			{
				Debug.LogError( $"[BasisCilbox] Sanitize threw, destroying clone: {e}" );
				UnityEngine.Object.Destroy( cloneRoot );
				return null;
			}

			Transform cloneTransform = cloneRoot.transform;
			if( finalParent != null )
			{
				cloneTransform.SetParent( finalParent, worldPositionStays );
			}
			else
			{
				cloneTransform.SetParent( null, false );
			}

			if( position.HasValue && rotation.HasValue )
				cloneTransform.SetPositionAndRotation( position.Value, rotation.Value );

			return cloneObj;
		}

		private static GameObject ExtractRootGameObject( UnityEngine.Object o )
		{
			if( o == null ) return null;
			if( o is GameObject go ) return go;
			if( o is Component comp ) return comp.gameObject;
			return null;
		}

		// ------------------------------------------------------------------
		// Sanitization pass. Runs while the clone is parked under the
		// disabled sanitation host, so no MonoBehaviour callbacks have fired.
		// ------------------------------------------------------------------

		private static void Sanitize( GameObject clone )
		{
			HashSet<string> approvedTypeNames = GetApprovedTypeNames();

			ScrubComponents( clone, approvedTypeNames );
			ScrubPersistentEvents( clone );
		}

		private static HashSet<string> GetApprovedTypeNames()
		{
			BundledContentHolder holder = BundledContentHolder.Instance;
			if( holder == null ) return null;
			if( !holder.GetSelector( BundledContentHolder.Selector.Prop, out ContentPoliceSelector selector ) ) return null;
			if( selector == null ) return null;
			return selector.ApprovedTypeNames;
		}

		private static void ScrubComponents( GameObject root, HashSet<string> approvedTypeNames )
		{
			// If the prop selector is unavailable we can't tell approved from not —
			// destroy the whole clone rather than silently allow arbitrary components.
			if( approvedTypeNames == null )
			{
				Debug.LogError( "[BasisCilbox] PropScriptSelector unavailable; destroying clone to fail closed." );
				UnityEngine.Object.DestroyImmediate( root );
				return;
			}

			Component[] comps = root.GetComponentsInChildren<Component>( true );
			for( int i = 0; i < comps.Length; i++ )
			{
				Component c = comps[i];
				if( c == null ) continue;
				if( c is Transform ) continue;
				string fullName = c.GetType().FullName;
				if( fullName == null || !approvedTypeNames.Contains( fullName ) )
				{
					UnityEngine.Object.DestroyImmediate( c );
				}
			}
		}

		// Persistent UnityEvent listeners are the attack vector reported for this
		// exploit (e.g. a Button.onClick wired in the editor to call an approved
		// component's method that ultimately invokes Application.OpenURL). Cilbox
		// scripts never need them — they wire callbacks at runtime via AddListener
		// through the sandboxed delegate proxy. Disable them all.
		private static void ScrubPersistentEvents( GameObject root )
		{
			Component[] comps = root.GetComponentsInChildren<Component>( true );
			HashSet<object> visited = new HashSet<object>( ReferenceEqualityComparer.Instance );
			for( int i = 0; i < comps.Length; i++ )
			{
				Component c = comps[i];
				if( c == null ) continue;
				// The generic walker handles EventTrigger transparently: it recurses
				// into the triggers List<Entry>, into each Entry, and finds the
				// UnityEvent<BaseEventData> callback via the UnityEventBase check.
				WalkForUnityEvents( c, visited, 0 );
			}
		}

		private const int MaxWalkDepth = 6;

		private static void WalkForUnityEvents( object obj, HashSet<object> visited, int depth )
		{
			if( obj == null ) return;
			if( depth > MaxWalkDepth ) return;

			Type t = obj.GetType();
			if( t.IsPrimitive || t == typeof(string) || t.IsEnum ) return;

			// Reference-type cycle guard. Value types are copies so we can't dedupe.
			if( !t.IsValueType && !visited.Add( obj ) ) return;

			if( obj is UnityEventBase evt )
			{
				DisableAllPersistentListeners( evt );
				return;
			}

			// UnityEngine.Object fields usually point at other scene/asset objects
			// we don't own — never recurse across that boundary.
			if( obj is UnityEngine.Object && depth > 0 ) return;

			Type cursor = t;
			while( cursor != null && cursor != typeof(object) && cursor != typeof(UnityEngine.Object) && cursor != typeof(Component) && cursor != typeof(MonoBehaviour) && cursor != typeof(Behaviour) )
			{
				FieldInfo[] fields = cursor.GetFields(
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
				for( int i = 0; i < fields.Length; i++ )
				{
					FieldInfo f = fields[i];
					Type ft = f.FieldType;
					if( ft.IsPrimitive || ft == typeof(string) || ft.IsEnum ) continue;

					object value;
					try { value = f.GetValue( obj ); }
					catch { continue; }
					if( value == null ) continue;

					if( value is UnityEventBase ue )
					{
						DisableAllPersistentListeners( ue );
						continue;
					}

					if( value is IList list )
					{
						for( int j = 0; j < list.Count; j++ )
							WalkForUnityEvents( list[j], visited, depth + 1 );
						continue;
					}

					if( ft.IsClass || (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum) )
					{
						WalkForUnityEvents( value, visited, depth + 1 );
					}
				}
				cursor = cursor.BaseType;
			}
		}

		private static void DisableAllPersistentListeners( UnityEventBase evt )
		{
			if( evt == null ) return;
			int count = evt.GetPersistentEventCount();
			for( int i = 0; i < count; i++ )
				evt.SetPersistentListenerState( i, UnityEventCallState.Off );
		}

		// .NET Standard 2.0 lacks ReferenceEqualityComparer; provide a local one so
		// the visited set doesn't fall back on MonoBehaviour.Equals (which is
		// value-equal across destroyed objects and can recurse into Unity internals).
		private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
			public new bool Equals( object x, object y ) { return ReferenceEquals( x, y ); }
			public int GetHashCode( object obj ) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( obj ); }
		}
	}
}
