using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Basis.Editor
{
    public static class BasisDeepLinkPostProcess
    {
        private const string Scheme = "basisvr";
        private const string BundleUrlName = "org.basisvr.deeplink";

        [PostProcessBuild(50)]
        public static void OnPostprocessBuild(BuildTarget target, string buildPath)
        {
            if (target == BuildTarget.iOS)
                PatchInfoPlist(Path.Combine(buildPath, "Info.plist"));
            else if (target == BuildTarget.StandaloneOSX)
                PatchInfoPlist(Path.Combine(buildPath, "Contents", "Info.plist"));
        }

        private static void PatchInfoPlist(string plistPath)
        {
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
                // Check if basisvr scheme is already registered inside the existing array.
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
                            foreach (XmlNode scheme in dictChildren[i + 1].ChildNodes)
                            {
                                if (scheme.InnerText == Scheme)
                                {
                                    Debug.Log($"[BasisDeepLink] {Scheme}:// already registered in {plistPath}.");
                                    return;
                                }
                            }
                        }
                    }
                }
                // Add basisvr entry to the existing array.
                AppendUrlTypeEntry(doc, existingArray);
            }
            else
            {
                // No CFBundleURLTypes yet — create the key and array.
                Append(doc, rootDict, "key").InnerText = "CFBundleURLTypes";
                AppendUrlTypeEntry(doc, Append(doc, rootDict, "array"));
            }

            doc.Save(plistPath);
            Debug.Log($"[BasisDeepLink] Added {Scheme}:// URL scheme to {plistPath}");
        }

        private static void AppendUrlTypeEntry(XmlDocument doc, XmlElement array)
        {
            XmlElement dict = Append(doc, array, "dict");
            Append(doc, dict, "key").InnerText = "CFBundleURLName";
            Append(doc, dict, "string").InnerText = BundleUrlName;
            Append(doc, dict, "key").InnerText = "CFBundleURLSchemes";
            Append(doc, Append(doc, dict, "array"), "string").InnerText = Scheme;
        }

        private static XmlElement Append(XmlDocument doc, XmlElement parent, string tag)
        {
            var el = doc.CreateElement(tag);
            parent.AppendChild(el);
            return el;
        }
    }
}
