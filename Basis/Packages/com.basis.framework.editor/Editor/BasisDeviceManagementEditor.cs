using UnityEditor;

namespace Basis.Scripts.Device_Management.Editor
{
    public static class BasisDeviceManagementEditor
    {
        [MenuItem("Basis/ForceLoadXR")]
        public static void ForceLoadXR()
        {
            BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenVRLoader);
        }
        [MenuItem("Basis/ForceSetDesktop")]
        public static void ForceSetDesktop()
        {
            BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.Desktop);
        }
    }
}
