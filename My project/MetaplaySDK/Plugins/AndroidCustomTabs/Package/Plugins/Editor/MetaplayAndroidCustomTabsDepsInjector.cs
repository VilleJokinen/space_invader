// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_EDITOR

using Metaplay.Core;
using System;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Metaplay.Unity.Android
{
    class MetaplayAndroidCustomTabsDepsInjector : IPostGenerateGradleAndroidProject,  IPreprocessBuildWithReport
    {
        public int callbackOrder => 40;

        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            // Check binary AAR is built.
            string[] aars = AssetDatabase.FindAssets("metaplaycustomtabs-release", new string[] { "Packages/io.metaplay.unitysdk.androidcustomtabs/CompiledArchive" });
            if (aars.Length == 0 || !AssetDatabase.GUIDToAssetPath(aars[0]).EndsWith(".aar", StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    "Metaplay Custom Tabs Android Libary is not built or is not part of the version control.\n"
                    + "You must build the Library for the current Unity Editor version. Follow the instructions in MetaplaySDK/Plugins/AndroidCustomTabs/README.md and try again.");
            }
        }

        void IPostGenerateGradleAndroidProject.OnPostGenerateGradleAndroidProject(string path)
        {
            // Inject intent-filter into the whatever is the UnityActivity. We need to do this at runtime
            // since some plugins override the Activity.
            try
            {
                PatchAndroidManifest(Path.Combine(path, "src/main/AndroidManifest.xml"));
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        static XmlNode TryGetManifestNode(XmlDocument document)
        {
            XmlNode node = document.FirstChild;

            for (;;)
            {
                if (node == null)
                    return null;
                if (node.NodeType == XmlNodeType.XmlDeclaration)
                    break;
                node = node.NextSibling;
            }

            for (;;)
            {
                if (node == null)
                    return null;
                if (node.Name == "manifest")
                    break;
                node = node.NextSibling;
            }

            return node;
        }

        static XmlNode GetAndroidActivity(XmlDocument document)
        {
            XmlNode manifest = TryGetManifestNode(document);
            if (manifest == null)
                throw new MetaAssertException("could not find manifest element.");

            XmlNode androidActivity = null;
            foreach (XmlNode application in manifest)
            {
                if (application.Name != "application")
                    continue;

                foreach (XmlNode activity in application)
                {
                    if (activity.Name != "activity")
                        continue;
                    if (!IsUnityActivity(activity))
                        continue;

                    if (androidActivity != null)
                        throw new MetaAssertException("Multiple Unity Android Activities found. Expected only one.");
                    androidActivity = activity;
                }
            }

            if (androidActivity == null)
                throw new MetaAssertException("Could not find Unity Android Activity.");
            return androidActivity;
        }

        static bool IsUnityActivity(XmlNode activity)
        {
            foreach (XmlNode metaData in activity)
            {
                if (metaData.Name != "meta-data")
                    continue;
                XmlAttribute nameAttr = metaData.Attributes["name", namespaceURI: "http://schemas.android.com/apk/res/android"];
                if (nameAttr == null || nameAttr.Value != "unityplayer.UnityActivity")
                    continue;
                XmlAttribute valueAttr = metaData.Attributes["value", namespaceURI: "http://schemas.android.com/apk/res/android"];
                if (valueAttr == null || valueAttr.Value != "true")
                    continue;
                return true;
            }
            return false;
        }

        static void PatchAndroidManifest(string androidManifest)
        {
            string contents = File.ReadAllText(androidManifest);
            XmlDocument manifest = new XmlDocument();
            manifest.PreserveWhitespace = true;
            manifest.LoadXml(contents);
            XmlNode unityActivity = GetAndroidActivity(manifest);

            // Check if already injected
            foreach (XmlNode elem in unityActivity.ChildNodes)
            {
                if (elem.NodeType == XmlNodeType.Comment && elem.Value == " METAPLAY BEGIN MetaplayAndroidCustomTabsDepsInjector ")
                    return;
            }

            string injected =
                "<root xmlns:android=\"http://schemas.android.com/apk/res/android\">"
                + "<!-- METAPLAY BEGIN MetaplayAndroidCustomTabsDepsInjector -->\n"
                + "<intent-filter>\n"
                + "  <action android:name=\"android.intent.action.VIEW\" />\n"
                + "  <category android:name=\"android.intent.category.DEFAULT\" />\n"
                + "  <category android:name=\"android.intent.category.BROWSABLE\" />\n"
                + $"  <data android:scheme=\"metaplaylogincb\" android:host=\"android.{Application.identifier}\" />\n"
                + "</intent-filter>\n"
                + "<!-- METAPLAY END MetaplayAndroidCustomTabsDepsInjector -->\n"
                + "</root>";

            XmlDocument filter = new XmlDocument();
            filter.PreserveWhitespace = true;
            filter.LoadXml(injected);
            foreach (XmlNode elem in filter.FirstChild)
            {
                XmlNode imported = manifest.ImportNode(elem, true);
                unityActivity.AppendChild(imported);
            }

            using (MemoryStream buffer = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings()
                {
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  ",
                };
                using (XmlWriter writer = XmlWriter.Create(buffer, settings))
                    manifest.Save(writer);

                File.WriteAllBytes(androidManifest, buffer.ToArray());
            }
        }
    }
}

#endif
