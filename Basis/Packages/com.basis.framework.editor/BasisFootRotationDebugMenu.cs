using Basis.Scripts.Drivers;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    /// <summary>
    /// Menu hooks for the runtime foot-IK rotation recorder. Enter play, Start, move around (walk so foot
    /// IK disengages = correct-foot reference, then stand so it engages), then Stop + Dump to get the CSV
    /// and a PASS/FAIL summary naming which rotation source foot IK should feed.
    /// </summary>
    public static class BasisFootRotationDebugMenu
    {
        [MenuItem("Basis/Debug/IK/Foot Rotation Debug - Start (record)")]
        static void StartRec()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[FootRotDebug] enter Play mode first."); return; }
            BasisFootRotationDebug.Start();
            Debug.Log("[FootRotDebug] recording. Walk around AND stand still, then run 'Stop + Dump CSV'.");
        }

        [MenuItem("Basis/Debug/IK/Foot Rotation Debug - Stop + Dump CSV")]
        static void StopRec() => BasisFootRotationDebug.StopAndDump();

        [MenuItem("Basis/Debug/IK/Foot Rotation Debug - Start (record)", true)]
        static bool StartValidate() => !BasisFootRotationDebug.Enabled;

        [MenuItem("Basis/Debug/IK/Foot Rotation Debug - Stop + Dump CSV", true)]
        static bool StopValidate() => BasisFootRotationDebug.Enabled;
    }
}
