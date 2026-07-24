using Basis.Scripts.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Component-level guards for the panel's reachability by the Basis pointer. A panel is only
    /// hit-testable if two things hold: it carries a collider matching its visible size, and it
    /// sits on a layer the raycast mask includes. Either one silently makes the panel unclickable
    /// while still rendering perfectly, which is a confusing failure to debug in a headset.
    /// </summary>
    public class BasisUIToolkitPanelComponentTests
    {
        private GameObject Host;

        [SetUp]
        public void SetUp()
        {
            // A UIDocument with no PanelSettings is fine for geometry, but may complain.
            LogAssert.ignoreFailingMessages = true;
            Host = new GameObject("BasisUIToolkitPanelTestHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (Host != null)
            {
                Object.DestroyImmediate(Host);
                Host = null;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private BasisUIToolkitPanel CreatePanel(Vector2 worldSize)
        {
            UIDocument document = Host.AddComponent<UIDocument>();
            document.worldSpaceSize = worldSize;
            return Host.AddComponent<BasisUIToolkitPanel>();
        }

        [Test]
        public void RefreshCollider_MatchesWorldSpaceSize()
        {
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1.5f, 0.75f));
            panel.ColliderDepth = 0.03f;
            panel.RefreshCollider();

            Assert.That(Host.TryGetComponent(out BoxCollider collider), Is.True,
                "Without a collider the physics raycast never reaches the panel.");
            Assert.That(collider.size.x, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(collider.size.y, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(collider.size.z, Is.EqualTo(0.03f).Within(1e-4f));
        }

        /// <summary>
        /// Centre pivot is assumed throughout the mapping, so the collider must be centred too —
        /// an offset box would shift every hit relative to the rendered panel.
        /// </summary>
        [Test]
        public void RefreshCollider_IsCentredOnTheDocument()
        {
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(2f, 1f));
            panel.RefreshCollider();

            Host.TryGetComponent(out BoxCollider collider);
            Assert.That(collider.center, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void RefreshCollider_ResizesWhenTheDocumentResizes()
        {
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1f, 1f));
            panel.RefreshCollider();

            panel.Document.worldSpaceSize = new Vector2(3f, 2f);
            panel.RefreshCollider();

            Host.TryGetComponent(out BoxCollider collider);
            Assert.That(collider.size.x, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(collider.size.y, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void DegenerateWorldSize_DoesNotProduceACollider()
        {
            BasisUIToolkitPanel panel = CreatePanel(Vector2.zero);
            panel.RefreshCollider();

            Assert.That(Host.TryGetComponent(out BoxCollider _), Is.False,
                "A zero-sized panel should be left alone rather than given a degenerate collider.");
        }

        // EnsureUILayer is invoked explicitly below rather than relied on via OnEnable: EditMode
        // does not run MonoBehaviour lifecycle callbacks without [ExecuteAlways], so asserting on
        // AddComponent alone would pass vacuously and guard nothing.

        /// <summary>
        /// The physics query runs against the UI mask; a panel left on Default is never hit.
        /// </summary>
        [Test]
        public void EnsureUILayer_MovesPanelOffDefault()
        {
            Host.layer = 0;
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1f, 1f));
            panel.EnsureUILayer();

            Assert.That(BasisUIRaycast.IsUILayer(Host.layer), Is.True,
                "Panel stayed on a layer the pointer's physics mask does not include.");
        }

        [Test]
        public void EnsureUILayer_PreservesAnExistingOverlayUILayer()
        {
            int overlay = LayerMask.NameToLayer("OverlayUI");
            Host.layer = overlay;
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1f, 1f));
            panel.EnsureUILayer();

            Assert.That(Host.layer, Is.EqualTo(overlay),
                "A panel deliberately placed on OverlayUI must not be demoted to UI.");
        }

        [Test]
        public void EnsureUILayer_RespectsTheOptOut()
        {
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1f, 1f));
            panel.AssignUILayer = false;
            Host.layer = 0;
            panel.EnsureUILayer();

            Assert.That(Host.layer, Is.EqualTo(0));
        }

        [Test]
        public void IsUILayer_AcceptsUIAndOverlayUI_AndRejectsDefault()
        {
            Assert.That(BasisUIRaycast.IsUILayer(LayerMask.NameToLayer("UI")), Is.True);
            Assert.That(BasisUIRaycast.IsUILayer(LayerMask.NameToLayer("OverlayUI")), Is.True);
            Assert.That(BasisUIRaycast.IsUILayer(0), Is.False);
        }

        /// <summary>
        /// The front normal drives the fingertip's approach-side test; if it stopped tracking the
        /// transform a poke from the front would read as a back-side pierce and be rejected.
        /// </summary>
        [Test]
        public void FrontNormal_TracksTheTransformForward()
        {
            BasisUIToolkitPanel panel = CreatePanel(new Vector2(1f, 1f));

            Host.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            Assert.That(Vector3.Dot(panel.FrontNormal, Host.transform.forward),
                Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
