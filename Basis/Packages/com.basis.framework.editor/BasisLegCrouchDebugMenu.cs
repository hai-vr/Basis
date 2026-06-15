using Basis.Scripts.Drivers;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    /// <summary>
    /// Menu hooks for the runtime leg-crouch recorder. Enter Play, Start, crouch up/down a few times
    /// (especially with feet a bit behind you), then Stop + Dump to get the CSV + a PASS/FAIL naming any
    /// frame where the knee shot up. The log includes the live foot-driver knee hint at the failing frame.
    /// </summary>
    public static class BasisLegCrouchDebugMenu
    {
        [MenuItem("Basis/IK Tests/Leg Crouch Debug - Start (record)")]
        static void StartRec()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegCrouchDebug] enter Play mode first."); return; }
            BasisLegCrouchDebug.Start();
            Debug.Log("[LegCrouchDebug] recording -- crouch up/down (try feet behind you), then 'Stop + Dump CSV'.");
        }

        [MenuItem("Basis/IK Tests/Leg Crouch Debug - Stop + Dump CSV")]
        static void StopRec() => BasisLegCrouchDebug.StopAndDump();

        [MenuItem("Basis/IK Tests/Leg Crouch Debug - Start (record)", true)]
        static bool V1() => !BasisLegCrouchDebug.Enabled;

        [MenuItem("Basis/IK Tests/Leg Crouch Debug - Stop + Dump CSV", true)]
        static bool V2() => BasisLegCrouchDebug.Enabled;
    }
}
