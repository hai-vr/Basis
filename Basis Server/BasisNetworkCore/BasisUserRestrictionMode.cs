using System;

namespace BasisNetworkCore.Security
{
    [Serializable]
    public enum BasisUserRestrictionMode : byte
    {
        Normal,
        BanList,
        AllowList,
        RejoinOnly,
    }
}
