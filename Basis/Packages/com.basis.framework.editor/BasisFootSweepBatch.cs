#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Temporary batch entry point so the foot sweep + GateFoot can be run headless from CI/CLI.
    public static class BasisFootSweepBatch
    {
        public static void Run()
        {
            int exit = 2;
            try
            {
                BasisFootIKSweepConfig cfg = BasisFootIKSweepConfig.Default();
                string path = BasisFootIKSweep.DefaultPath();
                BasisFootIKSweepSummary s = BasisFootIKSweep.Run(cfg, path);
                (bool pass, string reason) = BasisIKTestGates.GateFoot(s);
                Debug.Log("FOOTSWEEP_RESULT " + (pass ? "PASS" : "FAIL") + " :: " + reason);
                Debug.Log("FOOTSWEEP_CSV " + path);
                exit = pass ? 0 : 1;
            }
            catch (System.Exception e)
            {
                Debug.Log("FOOTSWEEP_RESULT EXCEPTION :: " + e.Message + "\n" + e.StackTrace);
            }
            EditorApplication.Exit(exit);
        }
    }
}
#endif
