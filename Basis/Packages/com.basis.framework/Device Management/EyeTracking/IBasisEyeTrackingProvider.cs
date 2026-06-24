namespace Basis.Scripts.Device_Management.EyeTracking
{
    public interface IBasisEyeTrackingProvider
    {
        BasisEyeSource Source { get; }
        bool IsActive { get; }
        bool TryGetEyeData(ref BasisEyeTrackingData data);
    }
}
