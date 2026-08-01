using System;
using Basis.Scripts.UI;
using UnityEngine;

namespace Basis.BasisUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PanelElementDescriptor))]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(BasisGraphicUIRayCaster))]
    public class BasisMenuPanel : AddressableUIInstanceBase
    {
        [Serializable]
        public struct PanelData
        {
            public string Title;
            public Vector2 PanelSize;
            public Vector3 PanelPosition;

            public static PanelData Standard(string title) => new()
            {
                Title = title,
                PanelSize = new Vector2(1500, 1020),
                PanelPosition = new Vector3(0, 30),
            };

            public static PanelData Toolbar(string title) => new()
            {
                Title = title,
                PanelSize = new Vector2(1500, 200),
                PanelPosition = new Vector3(0, -600),
            };
        }

        public static class PanelStyles
        {
            public static string Default => "Packages/com.basis.sdk/Prefabs/Menu Panel.prefab";
            public static string Page => "Packages/com.basis.sdk/Prefabs/Menu Panel - Page.prefab";
        }

        /// <summary>
        /// Which stack a panel sits in. Every panel is its own root world-space canvas, and canvases
        /// sharing a sorting order fall back to sorting by distance from the camera — which is why a
        /// dialogue could come up behind the page that raised it, or the hotbar disappear behind a
        /// page opened from it. Higher draws later, so higher draws on top.
        /// <para>
        /// The gaps are deliberate: a nested canvas that overrides its sorting can be slotted between
        /// two tiers without renumbering the ones either side of it.
        /// </para>
        /// </summary>
        public enum PanelLayer
        {
            /// <summary>Pages a provider opens — Settings, Library, the player list.</summary>
            Provider = 0,

            /// <summary>Raised over a page and meant to cover it: keyboard, dialogues, prompts, search.</summary>
            Overlay = 100,

            /// <summary>The hotbar. Nothing covers the way back out of the menu.</summary>
            MainMenu = 200,
        }

        public PanelData Data;
        public PanelElementDescriptor Descriptor { get; private set; }

        /// <summary>The stack this panel is currently drawn in.</summary>
        public PanelLayer Layer { get; private set; }

        /// <summary>Moves this panel into a stack. See <see cref="PanelLayer"/>.</summary>
        public void SetLayer(PanelLayer layer)
        {
            Layer = layer;
            if (TryGetComponent(out Canvas canvas))
            {
                canvas.sortingOrder = (int)layer;
            }
        }


        protected override void Awake()
        {
            base.Awake();
            Descriptor = GetComponent<PanelElementDescriptor>();
        }

        /// <summary>
        /// Instantiate a new Panel and load in the corresponding panel data.
        /// </summary>
        public static BasisMenuPanel CreateNew(PanelData data, Component parent) => CreateNew(data, parent, PanelStyles.Default);


        /// <summary>
        /// Instantiate a new Panel and load in the corresponding panel data.
        /// </summary>
        public static BasisMenuPanel CreateNew(PanelData data, Component parent, string referencePath)
        {
            BasisMenuPanel panel = CreateNew<BasisMenuPanel>(referencePath, parent);
            panel.LoadData(data);
            return panel;
        }

        public void LoadData(PanelData data)
        {
            Data = data;

            gameObject.name = data.Title;
            transform.localScale = Vector3.one;
            transform.localPosition = data.PanelPosition;

            rectTransform.sizeDelta = data.PanelSize;
            BasisGraphicUIRayCaster.SetBoxColliderToRectTransform(gameObject);

            // Everything is a provider page until told otherwise, so a panel can never be left
            // sorting by distance because someone forgot to place it.
            SetLayer(PanelLayer.Provider);

            Descriptor.SetTitle(data.Title);
        }
    }
}
