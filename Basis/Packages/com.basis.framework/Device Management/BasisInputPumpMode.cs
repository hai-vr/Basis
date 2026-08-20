namespace Basis.Scripts.Device_Management
{
    public enum BasisInputPumpMode
    {
        /// <summary>
        /// Full rate while keyboard/mouse/controller events are flowing, slow heartbeat when idle.
        /// Only applies in OpenVR mode; Desktop and OpenXR always pump full rate.
        /// </summary>
        Adaptive,
        /// <summary>
        /// Pump every frame in every mode.
        /// </summary>
        AllInputs,
        /// <summary>
        /// Desktop devices are effectively off while in OpenVR mode; events only drain on a slow
        /// heartbeat so nothing accumulates. Desktop and OpenXR still pump full rate.
        /// </summary>
        VRDesktopInputOff,
    }
}
