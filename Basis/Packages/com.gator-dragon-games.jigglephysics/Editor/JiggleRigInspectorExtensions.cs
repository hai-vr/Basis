#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GatorDragonGames.JigglePhysics {

/// <summary>
/// Lets a host project append its own controls to the bottom of the JiggleRig inspector without
/// replacing <see cref="JiggleRigEditor"/> — two [CustomEditor] attributes on the same type would
/// leave which one wins up to Unity. Subscribe from an [InitializeOnLoadMethod]; the rig is passed
/// as a Component so a subscriber needs no reference to this package's runtime assembly.
/// </summary>
public static class JiggleRigInspectorExtensions {
    public static event Action<Component, VisualElement> DrawAdditionalGUI;

    internal static void Invoke(Component rig, VisualElement container) {
        DrawAdditionalGUI?.Invoke(rig, container);
    }
}

}

#endif
