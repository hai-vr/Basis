using System.IO;
using System.Xml;
using Basis.Scripts.Networking;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Basis.Editor
{
    public static class BasisDeepLinkPostProcess
    {
        private static string GetScheme() => BasisDeepLinkProvider.DeepLinkScheme;

        [PostProcessBuild(50)]
        public static void OnPostprocessBuild(BuildTarget target, string buildPath)
        {
            if (target == BuildTarget.iOS)
                PatchInfoPlist(Path.Combine(buildPath, "Info.plist"), GetScheme());
            else if (target == BuildTarget.StandaloneOSX)
                PatchInfoPlist(Path.Combine(buildPath, "Contents", "Info.plist"), GetScheme());
        }

        private static void PatchInfoPlist(string plistPath, string scheme)
        {
            string bundleUrlName = BasisDeepLinkProvider.BundleUrlName;
            if (!File.Exists(plistPath)) return;

            var doc = new XmlDocument();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
            using (var reader = XmlReader.Create(plistPath, settings))
                doc.Load(reader);

            XmlElement rootDict = (XmlElement)doc.SelectSingleNode("plist/dict");
            if (rootDict == null) return;

            // Find existing CFBundleURLTypes array if any other plugin already added it.
            XmlElement existingArray = null;
            XmlNodeList children = rootDict.ChildNodes;
            for (int i = 0; i < children.Count - 1; i++)
            {
                if (children[i].NodeType == XmlNodeType.Element
                    && children[i].Name == "key"
                    && children[i].InnerText == "CFBundleURLTypes"
                    && children[i + 1].NodeType == XmlNodeType.Element
                    && children[i + 1].Name == "array")
                {
                    existingArray = (XmlElement)children[i + 1];
                    break;
                }
            }

            if (existingArray != null)
            {
                // Check if scheme is already registered inside the existing array.
                foreach (XmlNode dictNode in existingArray.ChildNodes)
                {
                    if (dictNode.Name != "dict") continue;
                    XmlNodeList dictChildren = dictNode.ChildNodes;
                    for (int i = 0; i < dictChildren.Count - 1; i++)
                    {
                        if (dictChildren[i].Name == "key"
                            && dictChildren[i].InnerText == "CFBundleURLSchemes"
                            && dictChildren[i + 1].Name == "array")
                        {
                            foreach (XmlNode schemeNode in dictChildren[i + 1].ChildNodes)
                            {
                                if (schemeNode.InnerText == scheme)
                                {
                                    Debug.Log($"[BasisDeepLink] {scheme}:// already registered in {plistPath}.");
                                    return;
                                }
                            }
                        }
                    }
                }
                AppendUrlTypeEntry(doc, existingArray, scheme, bundleUrlName);
            }
            else
            {
                // No CFBundleURLTypes yet — create the key and array.
                Append(doc, rootDict, "key").InnerText = "CFBundleURLTypes";
                AppendUrlTypeEntry(doc, Append(doc, rootDict, "array"), scheme, bundleUrlName);
            }

            doc.Save(plistPath);
            Debug.Log($"[BasisDeepLink] Added {scheme}:// URL scheme to {plistPath}");
        }

        private static void AppendUrlTypeEntry(XmlDocument doc, XmlElement array, string scheme, string bundleUrlName)
        {
            XmlElement dict = Append(doc, array, "dict");
            Append(doc, dict, "key").InnerText = "CFBundleURLName";
            Append(doc, dict, "string").InnerText = bundleUrlName;
            Append(doc, dict, "key").InnerText = "CFBundleURLSchemes";
            Append(doc, Append(doc, dict, "array"), "string").InnerText = scheme;
        }

        private static XmlElement Append(XmlDocument doc, XmlElement parent, string tag)
        {
            var el = doc.CreateElement(tag);
            parent.AppendChild(el);
            return el;
        }
    }

    public class BasisDeepLinkAndroidPostProcess : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 50;

        public void OnPostGenerateGradleAndroidProject(string gradlePath)
        {
            string scheme = BasisDeepLinkProvider.DeepLinkScheme;
            PatchAndroidManifest(
                Path.Combine(gradlePath, "src", "main", "AndroidManifest.xml"), scheme);
        }

        private static void PatchAndroidManifest(string manifestPath, string scheme)
        {
            if (!File.Exists(manifestPath)) return;

            const string androidNs = "http://schemas.android.com/apk/res/android";
            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var nsm = new XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("android", androidNs);

            if (doc.SelectSingleNode($"//intent-filter/data[@android:scheme='{scheme}']", nsm) != null)
            {
                Debug.Log($"[BasisDeepLink] {scheme}:// already registered in {manifestPath}.");
                return;
            }

            XmlNode mainActivity = doc.SelectSingleNode(
                "//activity[intent-filter/action[@android:name='android.intent.action.MAIN']]", nsm);
            if (mainActivity == null)
            {
                Debug.LogWarning("[BasisDeepLink] Could not find main activity in AndroidManifest.xml.");
                return;
            }

            XmlElement filter = doc.CreateElement("intent-filter");

            XmlElement action = doc.CreateElement("action");
            action.SetAttribute("name", androidNs, "android.intent.action.VIEW");
            filter.AppendChild(action);

            XmlElement catDefault = doc.CreateElement("category");
            catDefault.SetAttribute("name", androidNs, "android.intent.category.DEFAULT");
            filter.AppendChild(catDefault);

            XmlElement catBrowsable = doc.CreateElement("category");
            catBrowsable.SetAttribute("name", androidNs, "android.intent.category.BROWSABLE");
            filter.AppendChild(catBrowsable);

            XmlElement data = doc.CreateElement("data");
            data.SetAttribute("scheme", androidNs, scheme);
            filter.AppendChild(data);

            mainActivity.AppendChild(filter);
            doc.Save(manifestPath);
            Debug.Log($"[BasisDeepLink] Added {scheme}:// deep link to {manifestPath}");
        }
    }
}
