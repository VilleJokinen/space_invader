// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Unity;
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

public class GeneratedDllPostprocessor : AssetPostprocessor
{
    void OnPreprocessAsset()
    {
        // Set Generated DLL to be excluded from Editor
        if (assetPath == MetaplaySerializerBuilder.GeneratedDllForBuildFilePath)
        {
            PluginImporter importer = (PluginImporter)assetImporter;
            importer.ClearSettings();
            importer.SetExcludeEditorFromAnyPlatform(true);
        }
    }
}

/// <summary>
/// Unity build hooks for generating Metaplay serializer .dll.
/// </summary>
public class MetaplaySerializerBuilder : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IUnityLinkerProcessor
{
    public const string GeneratedDllForBuildFilePath = "Assets/Metaplay.Generated.dll";

    static readonly string[] _buildOutputFilePaths = new string[]
    {
        GeneratedDllForBuildFilePath,
        GeneratedDllForBuildFilePath + ".md5",
    };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            Debug.Log($"MetaplaySerializerBuilder.OnPreprocessBuild(): platform={report.summary.platform}, outputPath={report.summary.outputPath}");
            ScriptingImplementation scriptingBackend = PlayerSettings.GetScriptingBackend(report.summary.platformGroup);
            if (scriptingBackend != ScriptingImplementation.IL2CPP)
            {
                Debug.LogWarning($"[Metaplay] Build with scripting backend {scriptingBackend} uses slow serialization code, consider switching to IL2CPP");
            }

            // Delete generated DLL from old location
            AssetDatabase.DeleteAsset("Assets/Metaplay/Metaplay.Generated.dll");

            BuildDll(scriptingBackend);
        }
        catch (Exception ex)
        {
            throw new BuildFailedException(ex);
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        try
        {
            foreach (string path in _buildOutputFilePaths)
                AssetDatabase.DeleteAsset(path);
        }
        catch (Exception ex)
        {
            throw new BuildFailedException(ex);
        }
    }

    public static void BuildDll(ScriptingImplementation scriptingBackend)
    {
        MetaplayCore.Initialize();

        try
        {
            // Ensure Metaplay.Generated.dll is up-to-date
            RoslynSerializerCompileCache.EnsureDllUpToDate(
                outputDir: Path.GetDirectoryName(GeneratedDllForBuildFilePath),
                dllFileName: Path.GetFileName(GeneratedDllForBuildFilePath),
                errorDir: MetaplaySDK.UnityTempDirectory,
                enableCaching: false,
                forceRoslyn: true,
                useMemberAccessTrampolines: scriptingBackend != ScriptingImplementation.IL2CPP,
                generateRuntimeTypeInfo: true);

            // Inform Unity of the generated .dll
            AssetDatabase.ImportAsset(GeneratedDllForBuildFilePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        catch (Exception)
        {
            foreach (string path in _buildOutputFilePaths)
                File.Delete(path);
            throw;
        }
    }

    [InitializeOnLoadMethod]
    private static void InitForEditor()
    {
        try
        {
            MetaplayCore.Initialize();

            Assembly generatedDll = RoslynSerializerCompileCache.GetOrCompileAssembly(
                outputDir: MetaplaySDK.UnityTempDirectory,
                dllFileName: "Metaplay.Generated.dll",
                errorDir: MetaplaySDK.UnityTempDirectory,
                useMemberAccessTrampolines: true);

            // Init serialization and run lazy initializations immediately to catch any rare runtime issues.
            MetaplaySDK.InitSerializationFromAssembly(generatedDll, forceRunInitEagerly: true);
        }
        catch (MissingIntegrationException)
        {
            // Getting this error in editor means that game integration is incomplete.
            Debug.LogWarning("Metaplay SDK integration incomplete, editor functionality disabled");
            return;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to generate Metaplay.Generated.dll!");
            Debug.LogError(ex);

            // \note: we don't need to unhook this. When the error is fixed (or the code is modified for any reason), the domain is reloaded and the hook is lost.
            EditorApplication.playModeStateChanged += (PlayModeStateChange change) =>
            {
                if (change != PlayModeStateChange.ExitingEditMode)
                    return;

                Debug.LogError("Failed to generate Metaplay.Generated.dll!");
                Debug.LogError(ex);

                bool userAbortedLaunch = EditorUtility.DisplayDialog("Metaplay Build error.", "Build error while building Metaplay. MetaplaySDK is not initialized. See Console for details.", "Abort", "Run anyway");
                if (userAbortedLaunch)
                    EditorApplication.ExitPlaymode();
            };
        }
    }

    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
    {
        // Add link.xml from Metaplay SDK package to the build to preserve Metaplay assemblies.
        // This is required because Unity doesn't automatically scan for link.xml files in packages.
        //
        // This also preserves the user project's shared code assembly (usually named SharedCode)
        // by detecting its name and injecting it to the link.xml.

        // The GUID needs to match the value in link.xml.meta.
        const string linkXmlGuid = "b9afe0407b8f8104b8f0523b1ae0ab49";
        var assetPath = AssetDatabase.GUIDToAssetPath(linkXmlGuid);
        if (string.IsNullOrEmpty(assetPath))
        {
            throw new FileNotFoundException("Could not locate link.xml file in Metaplay SDK package!");
        }

        string linkXmlContent = File.ReadAllText(Path.GetFullPath(assetPath));

        // Inject the name of user's shared code assembly.
        string userSharedCodeAssembly = IntegrationRegistry.GetSingleIntegrationType<IPlayerModelBase>().Assembly.GetName().Name;
        const string PredefinedAssemblyName = "Assembly-CSharp";
        if (userSharedCodeAssembly == PredefinedAssemblyName)
        {
            Debug.LogWarning(
                $"Server-client shared game code appears to be in the predefined client assembly \"{PredefinedAssemblyName}\". " +
                "Shared code is normally added to link.xml to preserve it from Unity's code stripping. " +
                $"However, \"{PredefinedAssemblyName}\" will not be added to link.xml, as it tends to contain lots of types unrelated to Metaplay game logic. " +
                "It is recommended to put shared code into its own assembly.");

            // \note Leaving linkXmlContent as is, without replacing or removing the template {{USER_SHARED_CODE_ASSEMBLY_NAME}}.
            //       A bit hacky, but unknown assembly names in link.xml don't seem to cause problems.
        }
        else
            linkXmlContent = linkXmlContent.Replace("{{USER_SHARED_CODE_ASSEMBLY_NAME}}", userSharedCodeAssembly);

        // Write final content to a temporary file.
        string outputPath = $"{MetaplaySDK.UnityTempDirectory}/metaplay-generated-link.xml";
        File.WriteAllText(outputPath, linkXmlContent);
        return Path.GetFullPath(outputPath);
    }

    public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data)
    {
    }

    public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data)
    {
    }
}
