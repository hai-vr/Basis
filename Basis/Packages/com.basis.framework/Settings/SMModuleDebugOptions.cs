using UnityEngine;
public class SMModuleDebugOptions : MonoBehaviour
{
    public void ReceiveOption(bool Selected)
    {
        if (BasisGizmoManager.UseGizmos != Selected)
        {
            BasisGizmoManager.UseGizmos = Selected;
            BasisDebug.Log($"Gizmo State is {BasisGizmoManager.UseGizmos} {Selected}");
            if (BasisGizmoManager.UseGizmos)
            {
                BasisGizmoManager.TryCreateParent();
            }
            BasisGizmoManager.OnUseGizmosChanged?.Invoke(BasisGizmoManager.UseGizmos);
            if (BasisGizmoManager.UseGizmos == false)
            {
                BasisGizmoManager.DestroyParent();
                foreach (BasisGizmos BasisGizmos in BasisGizmoManager.Gizmos.Values)
                {
                    if (BasisGizmos != null)
                    {
                        GameObject.Destroy(BasisGizmos.gameObject);
                    }
                }
                foreach (BasisLineGizmos BasisLineGizmos in BasisGizmoManager.GizmosLine.Values)
                {
                    if (BasisLineGizmos != null)
                    {
                        GameObject.Destroy(BasisLineGizmos.gameObject);
                    }
                }
                BasisGizmoManager.Gizmos.Clear();
                BasisGizmoManager.GizmosLine.Clear();
            }
        }
    }
}
