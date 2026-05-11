using System;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

public class JiggleUpdateExample : MonoBehaviour {
    [Header("OPTIONAL: For debug drawing, import Samples within the Package Manager for URP/HDRP/BuiltIn procedural materials to place here.")]
    [Space(10)]
    [SerializeField] private bool debugDraw;
    [SerializeField] private Material proceduralMaterial;
    [SerializeField] private Mesh sphereMesh;
    [SerializeField] private Mesh capsuleMesh;

    private void LateUpdate() {
        var time = Time.timeAsDouble;

        JigglePhysics.ScheduleSimulate(time, Time.fixedDeltaTime);

        JigglePhysics.SchedulePose(time);
        if (debugDraw) {
            JigglePhysics.ScheduleRender();
        }

        JigglePhysics.CompletePose();
        if (debugDraw) {
            JigglePhysics.CompleteRender(proceduralMaterial, sphereMesh, capsuleMesh);
        }
    }

    void OnApplicationQuit() {
        JigglePhysics.Dispose();
    }

    private void OnDrawGizmos() {
        JigglePhysics.OnDrawGizmos();
    }
}

}
