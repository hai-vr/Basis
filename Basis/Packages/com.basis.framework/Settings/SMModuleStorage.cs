using Basis.Scripts.Avatar;

public class SMModuleStorage : BasisSettingsBase
{
    //Avatar Download Size
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (SliderReadOption(optionValue, out float InMB))
        {
            long MaxDownloadInBytes = MiBToBytes((int)InMB);
            BasisDebug.Log($"Avatar Download Size Was in MB {InMB} in Bytes was {MaxDownloadInBytes}", BasisDebug.LogTag.Networking);
            BasisAvatarFactory.MaxDownloadSizeInMBRemote = MaxDownloadInBytes;
        }
        else
        {
            BasisAvatarFactory.MaxDownloadSizeInMBRemote = 4L * 1024 * 1024 * 1024;
        }
    }
    long MiBToBytes(int mb)
    {
        return (long)mb * 1024 * 1024;
    }
    public override void ChangedSettings()
    {
    }
}
