using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class BasisHorizontalLayout : HorizontalLayoutGroup
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// A resize that lands WHILE the canvas is rebuilding layout makes uGUI's LayoutGroup.SetDirty
        /// take its coroutine branch (DelayedSetDirty), which marks this layout dirty again next frame —
        /// which resizes it again. That loop never settles, and it is invisible in a profile unless you
        /// know that a steady DelayedSetDirty call count IS the symptom. Name the object once so the
        /// oscillation can be found, then get out of the way.
        /// </summary>
        protected override void OnRectTransformDimensionsChange()
        {
            if (Application.isPlaying && IsActive() && CanvasUpdateRegistry.IsRebuildingLayout())
            {
                string path = HierarchyPath(transform);
                BasisDebug.LogWarningOnce($"layout-rebuild-loop-{path}",
                    $"BasisHorizontalLayout '{path}' was resized DURING a layout rebuild, so uGUI queued a DelayedSetDirty coroutine that will mark it dirty again next frame — a self-sustaining rebuild loop. Look for a ContentSizeFitter fighting a LayoutGroup on this object or an ancestor.",
                    BasisDebug.LogTag.System);
            }
            base.OnRectTransformDimensionsChange();
        }

        static string HierarchyPath(Transform leaf)
        {
            var sb = new System.Text.StringBuilder(128);
            for (Transform walk = leaf; walk != null; walk = walk.parent)
            {
                sb.Insert(0, walk.name).Insert(0, '/');
            }
            return sb.ToString();
        }
#endif

        [ContextMenu("Set Alignment Left")]
        public void SetAlignmentLeft() => SetAlignment(TextAlignment.Left);
        [ContextMenu("Set Alignment Center")]
        public void SetAlignmentCenter() => SetAlignment(TextAlignment.Center);
        [ContextMenu("Set Alignment Right")]
        public void SetAlignmentRight() => SetAlignment(TextAlignment.Right);


        public void SetAlignment(TextAlignment alignment)
        {
#if UNITY_EDITOR
            Undo.RecordObjects(new UnityEngine.Object[] { this, rectTransform }, "Updated alignment of layout.");
#endif

            switch (alignment)
            {
                case TextAlignment.Left:
                    childAlignment = TextAnchor.MiddleLeft;
                    rectTransform.anchorMin = new Vector2(0, 0);
                    rectTransform.anchorMax = new Vector2(0, 1);
                    rectTransform.pivot = new Vector2(0, 0.5f);
                    break;
                case TextAlignment.Center:
                    childAlignment = TextAnchor.MiddleCenter;
                    rectTransform.anchorMin = new Vector2(0.5f, 0);
                    rectTransform.anchorMax = new Vector2(0.5f, 1);
                    rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    break;
                case TextAlignment.Right:
                    childAlignment = TextAnchor.MiddleRight;
                    rectTransform.anchorMin = new Vector2(1, 0);
                    rectTransform.anchorMax = new Vector2(1, 1);
                    rectTransform.pivot = new Vector2(1, 0.5f);
                    break;
            }
        }
    }
}
