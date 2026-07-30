using System.Threading.Tasks;
using UnityEditor;

namespace Basis.Scripts.Device_Management.Editor
{
    public static class BasisDeviceManagementEditor
    {
        [MenuItem("Basis/Debug/Device/Force Load XR", false, 640)]
        public static async Task ForceLoadXR()
        {
          await  BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenVRLoader);
        }
        [MenuItem("Basis/Debug/Device/Force Set Desktop", false, 641)]
        public static async Task ForceSetDesktop()
        {
          await  BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.Desktop);
        }
    }
}
