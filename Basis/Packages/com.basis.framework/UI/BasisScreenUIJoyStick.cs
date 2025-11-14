using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using UnityEngine.Experimental.Rendering;
using Unity.Collections.LowLevel.Unsafe;
namespace UnityEngine.InputSystem.OnScreen
{
    public class BasisScreenUIJoyStick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        private const string kDynamicOriginClickable = "DynamicOriginClickable";

        // Public events instead of sending values into an InputControl
        public Action<Vector2> OnStickDown;    // Fired when interaction begins
        public Action<Vector2> OnStickMove;    // Fired whenever the stick moves (normalized vector)

        /// <summary>
        /// Callback to handle OnPointerDown UI events.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            BeginInteraction(eventData.position, eventData.pressEventCamera);
        }

        /// <summary>
        /// Callback to handle OnDrag UI events.
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            MoveStick(eventData.position, eventData.pressEventCamera);
        }

        /// <summary>
        /// Callback to handle OnPointerUp UI events.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            EndInteraction();
        }

        private void Start()
        {

            // Unable to setup elements according to settings if a RectTransform is not available (ISXB-915, ISXB-916).
            if (!(transform is RectTransform))
                return;

            m_StartPos = ((RectTransform)transform).anchoredPosition;

            if (m_Behaviour != Behaviour.ExactPositionWithDynamicOrigin) return;

            m_PointerDownPos = m_StartPos;

            var dynamicOrigin = new GameObject(kDynamicOriginClickable, typeof(Image));
            dynamicOrigin.transform.SetParent(transform);
            var image = dynamicOrigin.GetComponent<Image>();
            image.color = new Color(1, 1, 1, 0);
            var rectTransform = (RectTransform)dynamicOrigin.transform;
            rectTransform.sizeDelta = new Vector2(m_DynamicOriginRange * 2, m_DynamicOriginRange * 2);
            rectTransform.localScale = new Vector3(1, 1, 0);
            rectTransform.anchoredPosition3D = Vector3.zero;

            image.sprite = CreateCircleSprite(16, new Color32(255, 255, 255, 255));
            image.alphaHitTestMinimumThreshold = 0.5f;
        }

        public static unsafe Sprite CreateCircleSprite(int radius, Color32 colour)
        {
            // cache the diameter
            var d = radius * 2;

            var texture = new Texture2D(d, d, DefaultFormat.LDR, TextureCreationFlags.None);
            var colours = texture.GetRawTextureData<Color32>();
            var coloursPtr = (Color32*)colours.GetUnsafePtr();
            UnsafeUtility.MemSet(coloursPtr, 0, colours.Length * UnsafeUtility.SizeOf<Color32>());

            // pack the colour into a ulong so we can write two pixels at a time to the texture data
            var colorPtr = (uint*)UnsafeUtility.AddressOf(ref colour);
            var colourAsULong = *(ulong*)colorPtr << 32 | *colorPtr;

            float rSquared = radius * radius;

            // loop over the texture memory one column at a time filling in a line between the two x coordinates
            // of the circle at each column
            for (var y = -radius; y < radius; y++)
            {
                // for the current column, calculate what the x coordinate of the circle would be
                // using x^2 + y^2 = r^2, or x^2 = r^2 - y^2. The square root of the value of the
                // x coordinate will equal half the width of the circle at the current y coordinate
                var halfWidth = (int)Mathf.Sqrt(rSquared - y * y);

                // position the pointer so it points at the memory where we should start filling in
                // the current line
                var ptr = coloursPtr
                    + (y + radius) * d  // the position of the memory at the start of the row at the current y coordinate
                    + radius - halfWidth;   // the position along the row where we should start inserting colours

                // fill in two pixels at a time
                for (var x = 0; x < halfWidth; x++)
                {
                    *(ulong*)ptr = colourAsULong;
                    ptr += 2;
                }
            }

            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, d, d), new Vector2(radius, radius), 1, 0, SpriteMeshType.FullRect);
            return sprite;
        }

        public static RectTransform GetCanvasRectTransform(Transform transform)
        {
            var parentTransform = transform.parent;
            return parentTransform != null ? transform.parent.GetComponentInParent<RectTransform>() : null;
        }

        private void BeginInteraction(Vector2 pointerPosition, Camera uiCamera)
        {
            var canvasRectTransform = GetCanvasRectTransform(transform);
            if (canvasRectTransform == null)
            {
                Debug.LogError($"{GetType()} needs to be attached as a child to a UI Canvas and have a RectTransform component to function properly.");
                return;
            }

            switch (m_Behaviour)
            {
                case Behaviour.RelativePositionWithStaticOrigin:
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, pointerPosition, uiCamera, out m_PointerDownPos);
                    break;

                case Behaviour.ExactPositionWithStaticOrigin:
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, pointerPosition, uiCamera, out m_PointerDownPos);
                    break;

                case Behaviour.ExactPositionWithDynamicOrigin:
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, pointerPosition, uiCamera, out var pointerDown);
                    m_PointerDownPos = ((RectTransform)transform).anchoredPosition = pointerDown;
                    break;
            }

            // Stick interaction started: logical value is neutral at this moment.
            OnStickDown?.Invoke(Vector2.zero);

            // For ExactPositionWithStaticOrigin, we want the stick to jump to the press immediately.
            if (m_Behaviour == Behaviour.ExactPositionWithStaticOrigin)
            {
                MoveStick(pointerPosition, uiCamera);
            }
        }

        private void MoveStick(Vector2 pointerPosition, Camera uiCamera)
        {
            var canvasRectTransform = GetCanvasRectTransform(transform);
            if (canvasRectTransform == null)
            {
                Debug.LogError($"{GetType()} needs to be attached as a child to a UI Canvas and have a RectTransform component to function properly.");
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, pointerPosition, uiCamera, out var position);
            var delta = position - m_PointerDownPos;

            switch (m_Behaviour)
            {
                case Behaviour.RelativePositionWithStaticOrigin:
                    delta = Vector2.ClampMagnitude(delta, movementRange);
                    ((RectTransform)transform).anchoredPosition = (Vector2)m_StartPos + delta;
                    break;

                case Behaviour.ExactPositionWithStaticOrigin:
                    delta = position - (Vector2)m_StartPos;
                    delta = Vector2.ClampMagnitude(delta, movementRange);
                    ((RectTransform)transform).anchoredPosition = (Vector2)m_StartPos + delta;
                    break;

                case Behaviour.ExactPositionWithDynamicOrigin:
                    delta = Vector2.ClampMagnitude(delta, movementRange);
                    ((RectTransform)transform).anchoredPosition = m_PointerDownPos + delta;
                    break;
            }

            var newPos = new Vector2(delta.x / movementRange, delta.y / movementRange);

            // Fire movement delegate (normalized -1..1)
            OnStickMove?.Invoke(newPos);
        }

        private void EndInteraction()
        {
            ((RectTransform)transform).anchoredPosition = m_PointerDownPos = m_StartPos;

            // Notify listeners that we returned to neutral
            OnStickMove?.Invoke(Vector2.zero);
        }

        private void OnDrawGizmosSelected()
        {
            // This will not produce meaningful results unless we have a rect transform (ISXB-915, ISXB-916).
            var parentRectTransform = transform.parent as RectTransform;
            if (parentRectTransform == null)
                return;

            Gizmos.matrix = parentRectTransform.localToWorldMatrix;

            var startPos = parentRectTransform.anchoredPosition;
            if (Application.isPlaying)
                startPos = m_StartPos;

            Gizmos.color = new Color32(84, 173, 219, 255);

            var center = startPos;
            if (Application.isPlaying && m_Behaviour == Behaviour.ExactPositionWithDynamicOrigin)
                center = m_PointerDownPos;

            DrawGizmoCircle(center, m_MovementRange);

            if (m_Behaviour != Behaviour.ExactPositionWithDynamicOrigin) return;

            Gizmos.color = new Color32(158, 84, 219, 255);
            DrawGizmoCircle(startPos, m_DynamicOriginRange);
        }

        private void DrawGizmoCircle(Vector2 center, float radius)
        {
            for (var i = 0; i < 32; i++)
            {
                var radians = i / 32f * Mathf.PI * 2;
                var nextRadian = (i + 1) / 32f * Mathf.PI * 2;
                Gizmos.DrawLine(
                    new Vector3(center.x + Mathf.Cos(radians) * radius, center.y + Mathf.Sin(radians) * radius, 0),
                    new Vector3(center.x + Mathf.Cos(nextRadian) * radius, center.y + Mathf.Sin(nextRadian) * radius, 0));
            }
        }

        private void UpdateDynamicOriginClickableArea()
        {
            var dynamicOriginTransform = transform.Find(kDynamicOriginClickable);
            if (dynamicOriginTransform)
            {
                var rectTransform = (RectTransform)dynamicOriginTransform;
                rectTransform.sizeDelta = new Vector2(m_DynamicOriginRange * 2, m_DynamicOriginRange * 2);
            }
        }
        public float movementRange
        {
            get => m_MovementRange;
            set => m_MovementRange = value;
        }
        public float dynamicOriginRange
        {
            get => m_DynamicOriginRange;
            set
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (m_DynamicOriginRange != value)
                {
                    m_DynamicOriginRange = value;
                    UpdateDynamicOriginClickableArea();
                }
            }
        }
        public float m_MovementRange = 50;
        public float m_DynamicOriginRange = 100;
        public Behaviour m_Behaviour;
        public Vector3 m_StartPos;
        public Vector2 m_PointerDownPos;

        [NonSerialized]
        public List<RaycastResult> m_RaycastResults;
        public PointerEventData m_PointerEventData;
        public TouchControl m_TouchControl;

        /// <summary>Defines how the onscreen stick will move relative to it's origin and the press position.</summary>
        public Behaviour behaviour
        {
            get => m_Behaviour;
            set => m_Behaviour = value;
        }

        /// <summary>Defines how the onscreen stick will move relative to it's center of origin and the press position.</summary>
        public enum Behaviour
        {
            /// <summary>The control's center of origin is fixed in the scene.
            /// The control will begin un-actuated at it's centered position and then move relative to the press motion.</summary>
            RelativePositionWithStaticOrigin,

            /// <summary>The control's center of origin is fixed in the scene.
            /// The control may begin from an actuated position to ensure it is always tracking the current press position.</summary>
            ExactPositionWithStaticOrigin,

            /// <summary>The control's center of origin is determined by the initial press position.
            /// The control will begin unactuated at this center position and then track the current press position.</summary>
            ExactPositionWithDynamicOrigin
        }
    }
}
