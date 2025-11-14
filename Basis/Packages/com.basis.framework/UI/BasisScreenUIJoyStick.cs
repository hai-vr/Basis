using System;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class BasisScreenUIJoyStick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public float movementRange = 50f;
    public Action<Vector2> OnStickMove;

    RectTransform rect;
    Canvas canvas;
    RectTransform canvasRect;
    Vector2 startPos;

    void Awake()
    {
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            canvasRect = canvas.GetComponent<RectTransform>();

        startPos = rect.anchoredPosition;
    }

    Camera GetUICamera()
    {
        if (canvas == null) return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Snap immediately
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, GetUICamera(), out Vector2 localPos);

        var delta = localPos - startPos;
        delta = Vector2.ClampMagnitude(delta, movementRange);

        rect.anchoredPosition = startPos + delta;

        var norm = delta / movementRange;
        OnStickMove?.Invoke(norm);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        rect.anchoredPosition = startPos;
        OnStickMove?.Invoke(Vector2.zero);
    }
}
