using System;

[Serializable]
public class BasisPlayerSettingsData
{
    public string UUID;
    public float VolumeLevel;
    public bool AvatarVisible;
    public int Version = 1;

    public static readonly BasisPlayerSettingsData Default = new BasisPlayerSettingsData("", 1.0f, true);

    public BasisPlayerSettingsData(string uuid, float volumeLevel, bool avatarVisible)
    {
        UUID = uuid;
        VolumeLevel = volumeLevel;
        AvatarVisible = avatarVisible;
    }
}
