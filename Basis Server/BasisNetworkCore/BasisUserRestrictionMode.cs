using System;

namespace BasisNetworkCore.Security
{
    [Serializable]
    public enum BasisUserRestrictionMode : byte
    {
        Normal,
        BlackList,
        WhiteList,
        RejoinOnly,
    }
}
