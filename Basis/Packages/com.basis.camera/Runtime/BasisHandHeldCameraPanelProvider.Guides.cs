using Basis.Cinematics;
using UnityEngine;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// Draws the live shot's dead zone, soft zone and target point over the panel preview. The
    /// composition numbers are meaningless without this — a dead zone is a region you place by eye,
    /// and until it is visible the sliders are guesswork.
    /// <para>
    /// These are ordinary panel UI parented to the preview image, so they cannot reach the capture
    /// camera and can never end up in a shot.
    /// </para>
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        private static readonly Color DeadZoneColor = new Color(0.35f, 0.9f, 1f, 0.85f);
        private static readonly Color SoftZoneColor = new Color(1f, 0.75f, 0.25f, 0.6f);
        private static readonly Color TargetColor = new Color(1f, 1f, 1f, 0.9f);

        private const float GuideThickness = 2f;
        private const float TargetArmLength = 10f;

        private GuideRect _deadZoneGuide;
        private GuideRect _softZoneGuide;
        private GuideCross _targetGuide;
        private bool _guidesBuilt;

        /// <summary>
        /// An outline drawn as four thin strips anchored in the parent's normalised space, so it
        /// tracks the preview through any resize without needing a sprite or a layout pass.
        /// </summary>
        private sealed class GuideRect
        {
            private readonly RectTransform[] edges = new RectTransform[4];

            public GuideRect(RectTransform parent, string name, Color color)
            {
                for (int Index = 0; Index < edges.Length; Index++)
                {
                    edges[Index] = CreateStrip(parent, $"{name}{Index}", color);
                }
            }

            public void Set(Vector2 centre, Vector2 size)
            {
                Vector2 half = size * 0.5f;
                float low = Mathf.Clamp01(centre.x - half.x);
                float high = Mathf.Clamp01(centre.x + half.x);
                float bottom = Mathf.Clamp01(centre.y - half.y);
                float top = Mathf.Clamp01(centre.y + half.y);

                Span(edges[0], new Vector2(low, top), new Vector2(high, top), true);
                Span(edges[1], new Vector2(low, bottom), new Vector2(high, bottom), true);
                Span(edges[2], new Vector2(low, bottom), new Vector2(low, top), false);
                Span(edges[3], new Vector2(high, bottom), new Vector2(high, top), false);
            }

            public void SetActive(bool active)
            {
                for (int Index = 0; Index < edges.Length; Index++)
                {
                    if (edges[Index] != null)
                    {
                        edges[Index].gameObject.SetActive(active);
                    }
                }
            }

            public void Destroy()
            {
                for (int Index = 0; Index < edges.Length; Index++)
                {
                    if (edges[Index] != null)
                    {
                        Object.Destroy(edges[Index].gameObject);
                        edges[Index] = null;
                    }
                }
            }

            private static void Span(RectTransform strip, Vector2 from, Vector2 to, bool horizontal)
            {
                if (strip == null)
                {
                    return;
                }
                strip.anchorMin = from;
                strip.anchorMax = to;
                strip.anchoredPosition = Vector2.zero;
                strip.sizeDelta = horizontal
                    ? new Vector2(0f, GuideThickness)
                    : new Vector2(GuideThickness, 0f);
            }
        }

        /// <summary>The crosshair marking where the subject is meant to sit.</summary>
        private sealed class GuideCross
        {
            private readonly RectTransform horizontal;
            private readonly RectTransform vertical;

            public GuideCross(RectTransform parent, Color color)
            {
                horizontal = CreateStrip(parent, "GuideTargetH", color);
                vertical = CreateStrip(parent, "GuideTargetV", color);
            }

            public void Set(Vector2 point)
            {
                point = new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));

                Place(horizontal, point, new Vector2(TargetArmLength * 2f, GuideThickness));
                Place(vertical, point, new Vector2(GuideThickness, TargetArmLength * 2f));
            }

            public void SetActive(bool active)
            {
                if (horizontal != null) horizontal.gameObject.SetActive(active);
                if (vertical != null) vertical.gameObject.SetActive(active);
            }

            public void Destroy()
            {
                if (horizontal != null) Object.Destroy(horizontal.gameObject);
                if (vertical != null) Object.Destroy(vertical.gameObject);
            }

            private static void Place(RectTransform strip, Vector2 point, Vector2 size)
            {
                if (strip == null)
                {
                    return;
                }
                strip.anchorMin = point;
                strip.anchorMax = point;
                strip.anchoredPosition = Vector2.zero;
                strip.sizeDelta = size;
            }
        }

        private static RectTransform CreateStrip(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;

            return rect;
        }

        /// <summary>
        /// Builds the guides on first use. Deferred rather than built with the section because the
        /// preview image is only swapped in once the preview group exists.
        /// </summary>
        private void EnsureGuidesBuilt()
        {
            if (_guidesBuilt || _previewImage == null)
            {
                return;
            }

            RectTransform parent = _previewImage.rectTransform;
            _softZoneGuide = new GuideRect(parent, "GuideSoft", SoftZoneColor);
            _deadZoneGuide = new GuideRect(parent, "GuideDead", DeadZoneColor);
            _targetGuide = new GuideCross(parent, TargetColor);
            _guidesBuilt = true;
        }

        /// <summary>
        /// Pushes the live shot's composition onto the overlay. Hidden unless the shot actually
        /// composes — every other aim mode ignores these regions, so drawing them would be a lie.
        /// </summary>
        private void RefreshCompositionGuides()
        {
            if (!_showGuides || _activeCamera == null || !_activeCamera.cinematicEnabled)
            {
                SetGuidesActive(false);
                return;
            }

            BasisCameraShot shot = EditedShot;
            if (shot == null || shot.aimMode != BasisCameraAimMode.Composer)
            {
                SetGuidesActive(false);
                return;
            }

            EnsureGuidesBuilt();
            if (!_guidesBuilt)
            {
                return;
            }

            BasisComposerSettings composer = shot.composer;

            Vector2 deadCentre = new Vector2(composer.screenX, composer.screenY);
            Vector2 deadSize = new Vector2(Mathf.Max(0f, composer.deadZoneWidth), Mathf.Max(0f, composer.deadZoneHeight));

            // Asks the solver for the limits it will actually clamp to, rather than re-deriving
            // them here where they could drift out of step and draw a guide that lies.
            BasisCameraComposer.GetEffectiveLimits(
                composer.screenX, deadSize.x * 0.5f,
                composer.screenX + composer.biasX, Mathf.Max(0f, composer.softZoneWidth) * 0.5f,
                out float softLow, out float softHigh);

            BasisCameraComposer.GetEffectiveLimits(
                composer.screenY, deadSize.y * 0.5f,
                composer.screenY + composer.biasY, Mathf.Max(0f, composer.softZoneHeight) * 0.5f,
                out float softBottom, out float softTop);

            _softZoneGuide.Set(
                new Vector2((softLow + softHigh) * 0.5f, (softBottom + softTop) * 0.5f),
                new Vector2(softHigh - softLow, softTop - softBottom));
            _softZoneGuide.SetActive(true);

            _deadZoneGuide.Set(deadCentre, deadSize);
            _deadZoneGuide.SetActive(deadSize.x > 0f || deadSize.y > 0f);

            _targetGuide.Set(deadCentre);
            _targetGuide.SetActive(true);
        }

        private void SetGuidesActive(bool active)
        {
            if (!_guidesBuilt)
            {
                return;
            }
            _softZoneGuide?.SetActive(active);
            _deadZoneGuide?.SetActive(active);
            _targetGuide?.SetActive(active);
        }

        private void DestroyCompositionGuides()
        {
            _softZoneGuide?.Destroy();
            _deadZoneGuide?.Destroy();
            _targetGuide?.Destroy();
            _softZoneGuide = null;
            _deadZoneGuide = null;
            _targetGuide = null;
            _guidesBuilt = false;
        }
    }
}
