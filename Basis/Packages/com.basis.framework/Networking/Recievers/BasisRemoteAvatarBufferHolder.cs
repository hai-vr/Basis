using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using UnityEngine;

namespace Basis.Scripts.Networking.Receivers
{
    [Serializable]
    public class BasisRemoteAvatarBufferHolder
    {
        public bool HasFirst = false;
        public bool HasLast = false;

        [SerializeField] public BasisAvatarBuffer First = new BasisAvatarBuffer() { Scale = Vector3.one };
        [SerializeField] public BasisAvatarBuffer Last = new BasisAvatarBuffer() { Scale = Vector3.one };

        public void ClearAndRelease()
        {
            if (HasFirst)
            {
                BasisAvatarBufferPool.Release(ref First);
                HasFirst = false;
            }
            if (HasLast)
            {
                BasisAvatarBufferPool.Release(ref Last);
                HasLast = false;
            }
        }
    }
}
