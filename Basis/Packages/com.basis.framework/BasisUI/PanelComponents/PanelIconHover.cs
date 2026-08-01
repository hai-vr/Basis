using Basis.BTween;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Hover motion for a panel element's icon: the icon hops up and pops slightly larger with a
    /// short overshoot, then settles back when the pointer leaves. Scale and anchored position
    /// only, so it reads the same on every icon style and never fights the active theme colours.
    /// </summary>
    public class PanelIconHover
    {
        public const float HoverScale = 1.1f;
        public const float HoverLift = 3f;
        public const float EnterDuration = 0.18f;
        public const float ExitDuration = 0.12f;

        private RectTransform _icon;
        private Vector2 _restPosition;
        private bool _hovered;

        private TweenScale _scaleTween;
        private TweenAnchorPosition _positionTween;

        public void Bind(PanelElementDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.HasIcon)
            {
                _icon = null;
                return;
            }

            _icon = descriptor.IconImage.rectTransform;
            _restPosition = _icon.anchoredPosition;
            _hovered = false;
        }

        public void SetHovered(bool hovered)
        {
            if (!Application.isPlaying || !_icon || _hovered == hovered) return;
            _hovered = hovered;

            KillTweens();

            float duration = hovered ? EnterDuration : ExitDuration;
            Easing ease = hovered ? Easing.OutBack : Easing.OutCubic;
            Vector3 scale = hovered ? Vector3.one * HoverScale : Vector3.one;
            Vector2 position = hovered ? HoveredPosition() : _restPosition;

            _scaleTween = _icon.TweenScale(duration, _icon.localScale, scale).SetEase(ease);
            _positionTween = _icon.TweenAnchorPosition(duration, _icon.anchoredPosition, position).SetEase(ease);
        }

        /// <summary>
        /// Drops the hover and puts the icon back at rest immediately, for a button that is being
        /// hidden or torn down while the pointer is still over it.
        /// </summary>
        public void Cancel()
        {
            _hovered = false;
            if (!_icon) return;

            KillTweens();
            _icon.localScale = Vector3.one;
            _icon.anchoredPosition = _restPosition;
        }

        private Vector2 HoveredPosition()
        {
            Rect rect = _icon.rect;
            Vector2 pivot = _icon.pivot;
            float growth = HoverScale - 1f;

            return new Vector2(
                _restPosition.x + (pivot.x - 0.5f) * rect.width * growth,
                _restPosition.y + (pivot.y - 0.5f) * rect.height * growth + HoverLift);
        }

        private void KillTweens()
        {
            if (_scaleTween != null && _scaleTween.Active && _scaleTween.Target == _icon)
            {
                _scaleTween.Reset();
            }
            _scaleTween = null;

            if (_positionTween != null && _positionTween.Active && _positionTween.Target == _icon)
            {
                _positionTween.Reset();
            }
            _positionTween = null;
        }
    }
}
