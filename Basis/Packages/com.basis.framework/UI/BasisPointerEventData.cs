using UnityEngine;
using UnityEngine.EventSystems;

namespace Basis.Scripts.UI
{
public class BasisPointerEventData : PointerEventData
{
    public bool WasLastDown = false;
    public Transform DragPlaneTransform;
    public Camera DragEventCamera;
    public GameObject HapticHoverTarget;
    public BasisPointerEventData(EventSystem eventSystem) : base(eventSystem)
    {
    }
}
}