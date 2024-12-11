using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Basis.Scripts.BasisSdk
{
    [DisallowMultipleComponent]
    public class BasisConditionalLoad : MonoBehaviour
    {
        public bool useNetworkCondition = true;
        public BasisConditionalLoadNetwork networkAllow = BasisConditionalLoadNetwork.Local;
    }

    [Flags]
    public enum BasisConditionalLoadNetwork
    {
        Local = 1,
        Remote = 2
    }
}