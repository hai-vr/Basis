using System;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [Serializable]
    public struct HVRAddressSelector
    {
        public string path;
        public HVRAddress asset;

        public bool TryResolvePath(out string result)
        {
            if (asset != null)
            {
                result = asset.AsPath();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                result = path;
                return true;
            }

            result = "";
            return false;
        }

        /// Resolves a path or generates it from the path to that component's avatar.
        /// If that component is not on an avatar, this may throw an exception when this address selector is empty.
        public (string, int) ResolvePathOrDefaultToAvatar<T>(T discriminatorComponent) where T : Component
        {
            if (!TryResolvePath(out var result))
            {
                result = HVRAddress.GenerateAddressFromPath(discriminatorComponent, HVRCommsUtil.GetAvatar(discriminatorComponent).transform);
            }

            return (result, HVRAddress.AddressToId(result));
        }
    }
}
