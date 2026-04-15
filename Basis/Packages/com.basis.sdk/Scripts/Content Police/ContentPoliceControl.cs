using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public static class ContentPoliceControl
{
    /// <summary>
    /// Creates a copy of a GameObject, removes any unapproved MonoBehaviours, and returns the cleaned copy through instantiation. 
    /// </summary>
    /// <param name="SearchAndDestroy">The original GameObject to copy and clean.</param>
    /// <param name="ChecksRequired">Whether to remove unapproved MonoBehaviours or not.</param>
    /// <param name="Position">The position to instantiate the cleaned copy.</param>
    /// <param name="Rotation">The rotation to instantiate the cleaned copy.</param>
    /// <param name="Parent">The parent transform for the instantiated copy. Defaults to null.</param>
    /// <returns>A copy of the GameObject with unapproved scripts removed.</returns>
    public static GameObject ContentControl(GameObject DisabledGameobject, GameObject SearchAndDestroy, ChecksRequired ChecksRequired, Vector3 Position, Quaternion Rotation, bool ModifyScale, Vector3 Scale, BundledContentHolder.Selector Selector, Transform Parent = null,int colliderlayer = -1)
    {
        if (ChecksRequired.UseContentRemoval)
        {
            SearchAndDestroy = GameObject.Instantiate(SearchAndDestroy, Position, Rotation, DisabledGameobject.transform);
            if (ModifyScale)
            {
                BasisDebug.Log($"Overriding Default scale is now {Scale} for Game object {SearchAndDestroy.name}");
                SearchAndDestroy.transform.localScale = Scale;
            }
            // Create a list to hold all components in the original GameObject
            UnityEngine.Component[] components = SearchAndDestroy.GetComponentsInChildren<UnityEngine.Component>(true);

            int count = components.Length;

            if (BundledContentHolder.Instance.GetSelector(Selector, out ContentPoliceSelector PoliceCheck))
            {
                for (int Index = 0; Index < count; Index++)
                {
                    Component component = components[Index];
                    //do this first before we nuke stuff
                    switch (component)
                    {
                        case Animator animator:
                            if (ChecksRequired.DisableAnimatorEvents)
                            {
                                animator.fireEvents = false;
                            }
                            break;
                        case Collider collider:

                            if (ChecksRequired.RemoveColliders)
                            {
                                BasisDebug.Log("Remove Collider ", BasisDebug.LogTag.Avatar);
                                GameObject.Destroy(collider);
                            }
                            else
                            {
                                if (ChecksRequired.ChangeCollidersToCorrectLayer)
                                {
                                    BasisDebug.Log("Changing Collider To Correct Layer", BasisDebug.LogTag.Avatar);
                                    collider.gameObject.layer = colliderlayer;
                                }
                            }
                            break;
                        case AudioSource source:
                            source.outputAudioMixerGroup = PoliceCheck.AudioMixer;
                            break;
                    }
                    // Check if the component is a MonoBehaviour and not in the approved list
                    if (component is UnityEngine.Component monoBehaviour)
                    {
                        string monoTypeName = monoBehaviour.GetType().FullName;
                        if (!PoliceCheck.ApprovedTypeNames.Contains(monoTypeName))
                        {
                            Debug.LogError($"MonoBehaviour {monoTypeName} is not approved and will be removed.");
                            GameObject.DestroyImmediate(monoBehaviour); // Destroy the unapproved MonoBehaviour immediately
                        }
                    }
                }

                // Persistent UnityEvent listeners are the second attack surface:
                // a Button.onClick wired in the editor to Application.OpenURL /
                // File.WriteAllText / Process.Start fires the moment Awake runs.
                // Strip dangerous ones here while the clone is still parked under
                // the disabled host so no MonoBehaviour callback has executed yet.
                if (ChecksRequired.ScrubPersistentUnityEvents)
                {
                    ScrubDangerousPersistentListeners(SearchAndDestroy, PoliceCheck);
                }

                // Instantiate the cleaned GameObject copy
                if (Parent == null)
                {
                    SearchAndDestroy.transform.parent = null;
                    SearchAndDestroy.SetActive(true);
                }
                else
                {
                    SearchAndDestroy.transform.parent = Parent;
                    SearchAndDestroy.SetActive(true);
                }
            }
            else
            {
                BasisDebug.LogError("cant find Police check for " + Selector, BasisDebug.LogTag.Event);
            }
        }
        else
        {
            if (Parent == null)
            {
                SearchAndDestroy = GameObject.Instantiate(SearchAndDestroy, Position, Rotation);
            }
            else
            {
                SearchAndDestroy = GameObject.Instantiate(SearchAndDestroy, Position, Rotation, Parent);
            }
        }
        return SearchAndDestroy;
    }
    /// <summary>
    /// Scrubs a scene by removing any unapproved MonoBehaviours and applying optional safety checks.
    /// </summary>
    public static void ContentControl(ChecksRequired checks, BundledContentHolder.Selector selector, Scene targetScene, bool includeInactive = true)
    {
        if (!checks.UseContentRemoval)
        {
            return;
        }

        if (!BundledContentHolder.Instance.GetSelector(selector, out ContentPoliceSelector policeCheck))
        {
            BasisDebug.LogError("Can't find Police check for " + selector, BasisDebug.LogTag.Event);
            return;
        }
        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            Debug.LogError("Target scene is not valid or not loaded.");
            return;
        }

        GameObject[] roots = targetScene.GetRootGameObjects();
        for (int RootIndex = 0; RootIndex < roots.Length; RootIndex++)
        {
            // Get ALL components in this subtree
            Component[] components = roots[RootIndex].transform.GetComponentsInChildren<Component>(includeInactive);
            // Check if the component is a MonoBehaviour and not in the approved list
            for (int ComponentIndex = 0; ComponentIndex < components.Length; ComponentIndex++)
            {
                Component component = components[ComponentIndex];
                //do this first before we nuke stuff
                // Check if the component is a MonoBehaviour and not in the approved list
                if (component is UnityEngine.Component monoBehaviour)
                {
                    string monoTypeName = monoBehaviour.GetType().FullName;
                    if (!policeCheck.ApprovedTypeNames.Contains(monoTypeName))
                    {
                        Debug.LogError($"MonoBehaviour {monoTypeName} is not approved and will be removed.");
                        GameObject.DestroyImmediate(monoBehaviour); // Destroy the unapproved MonoBehaviour immediately
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Persistent UnityEvent scrubbing.
    //
    // Unity's PersistentCall stores (target, methodName, mode, argument). At
    // Awake the event resolves the method by name via reflection — so a
    // Button.onClick saved as (null, "OpenURL") with the type-name encoded in
    // the private m_TargetAssemblyTypeName field calls UnityEngine.Application.OpenURL
    // without any script on the prefab. Walking the surviving components and
    // disabling listeners that point at dangerous types/methods closes this
    // without breaking legit listeners that target an approved component.
    // ------------------------------------------------------------------

    private const int MaxEventWalkDepth = 6;

    // Static calls (null target) use a private assembly-type-name field we
    // can't portably read. Deny them outright — persistent listeners pointing
    // at free-floating static methods are rare in legit prop prefabs and
    // every known escape hatch here is a static call.
    // Target types we always refuse, regardless of method name.
    private static readonly HashSet<string> BlockedTargetFullNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "UnityEngine.Application",
        "UnityEngine.PlayerPrefs",
        "UnityEngine.SceneManagement.SceneManager",
        "UnityEngine.Resources",
        "UnityEngine.AssetBundle",
        "System.Environment",
        "System.AppDomain",
        "System.Activator",
        "System.Type",
        "System.Reflection.Assembly",
        "System.Reflection.MethodBase",
    };

    // Namespace prefixes that describe "anything that can reach outside Unity".
    private static readonly string[] BlockedTargetNamespacePrefixes = new string[]
    {
        "System.IO.",
        "System.Diagnostics.",
        "System.Net.",
        "System.Reflection.",
        "System.Runtime.InteropServices.",
        "Microsoft.Win32.",
        "UnityEngine.Networking.",
    };

    // Method names that should never be reachable through a persistent listener
    // regardless of target type. Intentionally short: each entry is an
    // unambiguous escape hatch name that won't false-positive on legit UI
    // wiring. The primary gate is the target-type allow-list above; this is
    // defense in depth for when an approved component exposes (or wraps) a
    // dangerous API under a distinctive name.
    private static readonly HashSet<string> BlockedMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        // Browser / shell-out
        "OpenURL",
        // Process lifetime
        "Quit",
        // Scene escape
        "LoadScene", "LoadSceneAsync",
        // Filesystem writes ("save functions")
        "WriteAllText", "WriteAllBytes", "WriteAllLines",
        "AppendAllText", "AppendAllLines",
        // Reflection / assembly loading
        "LoadFrom", "LoadFile", "LoadAssembly",
        // Environment tampering
        "SetEnvironmentVariable",
    };

    private static void ScrubDangerousPersistentListeners(GameObject root, ContentPoliceSelector police)
    {
        if (root == null) return;
        HashSet<string> approved = police != null ? police.ApprovedTypeNames : null;

        Component[] comps = root.GetComponentsInChildren<Component>(true);
        HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparerLocal.Instance);
        for (int i = 0; i < comps.Length; i++)
        {
            Component c = comps[i];
            if (c == null) continue;
            WalkForUnityEvents(c, visited, approved, 0);
        }
    }

    private static void WalkForUnityEvents(object obj, HashSet<object> visited, HashSet<string> approved, int depth)
    {
        if (obj == null) return;
        if (depth > MaxEventWalkDepth) return;

        Type t = obj.GetType();
        if (t.IsPrimitive || t == typeof(string) || t.IsEnum) return;

        if (!t.IsValueType && !visited.Add(obj)) return;

        if (obj is UnityEventBase evt)
        {
            ScrubEvent(evt, approved);
            return;
        }

        // Never follow UnityEngine.Object references away from the clone we
        // own — they lead into unrelated scene/asset graphs.
        if (obj is UnityEngine.Object && depth > 0) return;

        Type cursor = t;
        while (cursor != null
            && cursor != typeof(object)
            && cursor != typeof(UnityEngine.Object)
            && cursor != typeof(Component)
            && cursor != typeof(MonoBehaviour)
            && cursor != typeof(Behaviour))
        {
            FieldInfo[] fields = cursor.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                Type ft = f.FieldType;
                if (ft.IsPrimitive || ft == typeof(string) || ft.IsEnum) continue;

                object value;
                try { value = f.GetValue(obj); }
                catch { continue; }
                if (value == null) continue;

                if (value is UnityEventBase ue)
                {
                    ScrubEvent(ue, approved);
                    continue;
                }

                if (value is IList list)
                {
                    for (int j = 0; j < list.Count; j++)
                        WalkForUnityEvents(list[j], visited, approved, depth + 1);
                    continue;
                }

                if (ft.IsClass || (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum))
                {
                    WalkForUnityEvents(value, visited, approved, depth + 1);
                }
            }
            cursor = cursor.BaseType;
        }
    }

    private static void ScrubEvent(UnityEventBase evt, HashSet<string> approved)
    {
        if (evt == null) return;
        int count = evt.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
        {
            UnityEngine.Object target = evt.GetPersistentTarget(i);
            string methodName = evt.GetPersistentMethodName(i);
            if (IsDangerousListener(target, methodName, approved))
            {
                Debug.LogWarning($"[ContentPolice] Disabling persistent UnityEvent listener -> {(target != null ? target.GetType().FullName : "<static>")}.{methodName}");
                evt.SetPersistentListenerState(i, UnityEventCallState.Off);
            }
        }
    }

    private static bool IsDangerousListener(UnityEngine.Object target, string methodName, HashSet<string> approved)
    {
        if (string.IsNullOrEmpty(methodName)) return true;

        // Null target means a static call with the target-type name stored in
        // a private field. We can't read it portably, so rip it.
        if (target == null) return true;

        string typeName = target.GetType().FullName;
        if (typeName == null) return true;

        if (BlockedTargetFullNames.Contains(typeName)) return true;

        for (int i = 0; i < BlockedTargetNamespacePrefixes.Length; i++)
        {
            if (typeName.StartsWith(BlockedTargetNamespacePrefixes[i], StringComparison.Ordinal))
                return true;
        }

        // If the target type isn't in the content-police approved list the
        // listener survived only because the target is an asset reference
        // outside the clone (scriptable object, material, etc.) — refuse.
        if (approved != null && !approved.Contains(typeName)) return true;

        if (BlockedMethodNames.Contains(methodName)) return true;

        return false;
    }

    // .NET Standard 2.0 has no ReferenceEqualityComparer; supply one so the
    // visited set uses identity, not UnityEngine.Object.Equals (which is
    // value-equal across destroyed objects).
    private sealed class ReferenceEqualityComparerLocal : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparerLocal Instance = new ReferenceEqualityComparerLocal();
        public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
    }
}
/// <summary>
/// Defines the checks required for content control.
/// </summary>
public struct ChecksRequired
{
    public bool UseContentRemoval;
    public bool DisableAnimatorEvents;
    public bool RemoveColliders;
    public bool ChangeCollidersToCorrectLayer;
    // When true, ContentPoliceControl walks every surviving component and
    // disables any persistent UnityEvent listener whose target type or method
    // name looks like an escape hatch (Application.OpenURL, File.*,
    // Process.Start, assembly loading, etc.). Opt-in so legacy prop bundles
    // with wired-up Buttons keep working; cilbox-initiated spawns enable it.
    public bool ScrubPersistentUnityEvents;
    public ChecksRequired(bool useContentRemoval, bool disableAnimatorEvents, bool removeColliders,bool changeColidersToCorrectLayer)
    {
        UseContentRemoval = useContentRemoval;
        DisableAnimatorEvents = disableAnimatorEvents;
        RemoveColliders = removeColliders;
        ChangeCollidersToCorrectLayer = changeColidersToCorrectLayer;
        ScrubPersistentUnityEvents = false;
    }
}
