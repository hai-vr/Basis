using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.UI
{
    /// <summary>
    /// A popup opened over a page is a separate root canvas stacked on top of it, but the pointer
    /// reaches both through their colliders, which sit at the same depth. Distance alone cannot
    /// separate them and the physics query returns the tie in no particular order, so a dialogue
    /// used to hand roughly half its presses to the page behind it.
    /// </summary>
    [TestFixture]
    public class BasisUIPanelStackingTests
    {
        private const int OverlayLayer = 9;
        private const int WorldUILayer = 5;
        private const int PageOrder = (int)BasisMenuPanel.PanelLayer.Provider;
        private const int PopupOrder = (int)BasisMenuPanel.PanelLayer.Overlay;

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots)
            {
                if (root)
                {
                    Object.DestroyImmediate(root);
                }
            }
            _roots.Clear();
        }

        [Test]
        public void PopupBeatsThePageItCoversWhateverOrderTheRayFindsThem()
        {
            Assert.That(BasisUIRaycast.CompareStackedPanels(OverlayLayer, PopupOrder, OverlayLayer, PageOrder), Is.LessThan(0));
            Assert.That(BasisUIRaycast.CompareStackedPanels(OverlayLayer, PageOrder, OverlayLayer, PopupOrder), Is.GreaterThan(0));
        }

        [Test]
        public void PanelsSharingAStackDeferToDistance()
        {
            Assert.That(BasisUIRaycast.CompareStackedPanels(OverlayLayer, PopupOrder, OverlayLayer, PopupOrder), Is.Zero);
        }

        [Test]
        public void UnrelatedLayersDeferToDistance()
        {
            Assert.That(BasisUIRaycast.CompareStackedPanels(OverlayLayer, PageOrder, WorldUILayer, PopupOrder), Is.Zero);
            Assert.That(BasisUIRaycast.CompareStackedPanels(WorldUILayer, PopupOrder, OverlayLayer, PageOrder), Is.Zero);
        }

        [Test]
        public void FingerPokesThePopupWhenBothPanelsAreUnderTheFingertip()
        {
            Canvas page = BuildCanvas(OverlayLayer, PageOrder);
            Canvas popup = BuildCanvas(OverlayLayer, PopupOrder);

            const float tied = 0.012f;
            Assert.That(BasisDirectTouch.IsBetterTouchTarget(popup, tied, page, tied), Is.True);
            Assert.That(BasisDirectTouch.IsBetterTouchTarget(page, tied, popup, tied), Is.False);
        }

        [Test]
        public void FingerStillPokesWhateverItIsActuallyTouching()
        {
            Canvas page = BuildCanvas(OverlayLayer, PageOrder);
            Canvas popup = BuildCanvas(OverlayLayer, PopupOrder);

            // A panel the finger is nowhere near does not get the touch just for being on top.
            Assert.That(BasisDirectTouch.IsBetterTouchTarget(popup, 0.03f, page, 0.001f), Is.False);
            Assert.That(BasisDirectTouch.IsBetterTouchTarget(page, 0.001f, popup, 0.03f), Is.True);
        }

        [Test]
        public void TheDialogueSitsInFrontOfTheStandardPage()
        {
            // The other popups' panel data goes through localization to build a title, which is more
            // than this wants to reach for; the dialogue is the one that went coplanar in any case.
            float pageDepth = BasisMenuPanel.PanelData.Standard(string.Empty).PanelPosition.z;

            // Coplanar with the page is what exposed the stacking bug in the first place, and it is
            // also what leaves a fingertip with no real answer about which panel it reached.
            Assert.That(BasisMenuDialoguePanel.DialoguePanelData.PanelPosition.z, Is.LessThan(pageDepth));
        }

        private Canvas BuildCanvas(int layer, int sortingOrder)
        {
            GameObject go = new GameObject("Panel", typeof(Canvas));
            _roots.Add(go);
            go.layer = layer;
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;
            return canvas;
        }
    }
}
