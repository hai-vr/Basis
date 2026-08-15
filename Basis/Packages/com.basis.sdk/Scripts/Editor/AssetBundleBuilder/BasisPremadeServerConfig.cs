using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Basis.Scripts.BasisSdk;
using UnityEngine;

public static class BasisPremadeServerConfig
{
    public const string PremadeFolderName = "premade";
    public const string ServerFolderName = "initialresources";

    private const string ModeComment =
        " Mode: 0 = Prop (spawned at the transform below), 1 = Scene (transform ignored), 2 = Avatar (transform ignored) ";

    public static string Write(string buildOutDir, BasisContentBase content, string beeFilePath, string password)
    {
        if (content == null || string.IsNullOrEmpty(buildOutDir) || string.IsNullOrEmpty(beeFilePath))
        {
            return null;
        }

        if (!TryGetMode(content, out int mode, out string kind))
        {
            return null;
        }

        string bundleName = content.BasisBundleDescription.AssetBundleName;
        string premadeDirectory = Path.Combine(buildOutDir, PremadeFolderName);
        Directory.CreateDirectory(premadeDirectory);

        string filePath = Path.Combine(premadeDirectory, BasisBundleBuild.MakeSafeFolderName(bundleName) + ".xml");

        bool spawnsAtTransform = mode == 0;
        Transform transform = content.transform;
        Vector3 position = spawnsAtTransform ? transform.position : Vector3.zero;
        Quaternion rotation = spawnsAtTransform ? transform.rotation : Quaternion.identity;
        Vector3 scale = spawnsAtTransform ? transform.localScale : Vector3.one;

        CultureInfo inv = CultureInfo.InvariantCulture;

        XDocument document =
            new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("BasisLoadableConfiguration",
                    new XComment($" {kind} \"{bundleName}\" built by the Basis SDK on {DateTime.UtcNow.ToString("u", inv)}. "),
                    new XComment($" Copy this file into the server's \"{ServerFolderName}\" folder and the server loads it on startup. "),

                    new XComment(ModeComment),
                    new XElement("Mode", mode),

                    new XComment(" Network ID - leave this blank and the server generates one when it loads. "),
                    new XElement("LoadedNetID", string.Empty),

                    new XComment(" Unlock password for the .BEE, copied from this build. Rebuilding makes a new one unless you set a custom password. "),
                    new XElement("UnlockPassword", password ?? string.Empty),

                    new XComment(" CombinedURL is where every client downloads the .BEE from. "),
                    new XComment(" YOU NEED TO REPLACE THIS URL with the https:// address you uploaded the .BEE to. "),
                    new XComment(" Leave it alone only if you are loading locally: the path below is on the machine that built this, so it only resolves for clients that can read that exact file. "),
                    new XElement("CombinedURL", ToLoadableUrl(beeFilePath)),

                    new XComment(" Position values "),
                    new XElement("PositionX", position.x.ToString(inv)),
                    new XElement("PositionY", position.y.ToString(inv)),
                    new XElement("PositionZ", position.z.ToString(inv)),

                    new XComment(" Quaternion values "),
                    new XElement("QuaternionX", rotation.x.ToString(inv)),
                    new XElement("QuaternionY", rotation.y.ToString(inv)),
                    new XElement("QuaternionZ", rotation.z.ToString(inv)),
                    new XElement("QuaternionW", rotation.w.ToString(inv)),

                    new XComment(" Scale values, only applied when ModifyScale is true "),
                    new XElement("ScaleX", scale.x.ToString(inv)),
                    new XElement("ScaleY", scale.y.ToString(inv)),
                    new XElement("ScaleZ", scale.z.ToString(inv)),

                    new XComment(" Persist flag - true keeps this loaded when the last player leaves. "),
                    new XElement("Persist", "false"),

                    new XComment(" Apply the scale above instead of the scale the content was built with. "),
                    new XElement("ModifyScale", "false")
                )
            );

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            document.Save(stream);
        }

        return filePath;
    }

    private static bool TryGetMode(BasisContentBase content, out int mode, out string kind)
    {
        if (content is BasisScene)
        {
            mode = 1;
            kind = "World";
            return true;
        }

        if (content is BasisProp)
        {
            mode = 0;
            kind = "Prop";
            return true;
        }

        mode = 0;
        kind = null;
        return false;
    }

    private static string ToLoadableUrl(string beeFilePath)
    {
        try
        {
            return new Uri(Path.GetFullPath(beeFilePath)).AbsoluteUri;
        }
        catch (Exception ex)
        {
            BasisDebug.LogWarning($"Could not turn {beeFilePath} into a file url: {ex.Message}");
            return beeFilePath;
        }
    }
}
