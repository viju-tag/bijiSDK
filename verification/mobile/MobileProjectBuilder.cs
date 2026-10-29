using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BijiSDK.MobileVerification.Editor
{
    /// <summary>Creates an isolated sample scene and repeatable mobile verification build.</summary>
    public static class MobileProjectBuilder
    {
        private const string PackageName = "com.biji.sdk";
        private const string ScenePath = "Assets/Scenes/BijiSDKMobileTest.unity";
        private const string ApplicationId = "com.biji.sdk.verification.d20261029";
        private static Task<int> editorChecks;

        // This method must also compile before the sample and runtime checks are imported.
        public static void ImportSample()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .SingleOrDefault(item => item.name == PackageName);
            if (package == null)
                throw new InvalidOperationException("bijiSDK is not registered in Package Manager.");
            var samples = Sample.FindByPackage(PackageName, package.version).ToArray();
            var sample = samples.SingleOrDefault(item => item.displayName == "Offline Config");
            if (string.IsNullOrEmpty(sample.displayName))
                throw new InvalidOperationException("Package Manager did not expose the Offline Config sample.");
            if (!sample.isImported && !sample.Import(Sample.ImportOptions.OverridePreviousImports))
                throw new InvalidOperationException("Package Manager could not import the Offline Config sample.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("BIJISDK_MOBILE_SAMPLE_IMPORTED path=" + sample.importPath);
        }

        public static void Setup()
        {
            ImportSample();
            ConfigurePlayer();
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sampleObject = new GameObject("Offline Config Sample");
            var sample = sampleObject.AddComponent(FindType("BijiSDK.Samples.OfflineConfigSample"));
            var serialized = new SerializedObject(sample);
            SetBoolean(serialized, "useRemoteServer", true);
            SetBoolean(serialized, "useGitHubPages", true);
            SetBoolean(serialized, "allowDevelopmentHttp", false);
            SetBoolean(serialized, "refreshOnStartup", false);
            SetBoolean(serialized, "refreshOnResume", true);
            serialized.FindProperty("pagesBaseUrl").stringValue = "https://viju-tag.github.io/bijiSDK/";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var probe = new GameObject("Mobile Configuration Checks");
            probe.AddComponent(FindType("BijiSDK.MobileVerification.MobileConfigProbe"));
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
            cameraObject.transform.position = new Vector3(0, 0, -10);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save the mobile verification scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("BIJISDK_MOBILE_PROJECT_READY scene=" + ScenePath + " applicationId=" + ApplicationId);
        }

        public static void RunEditorChecks()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            var storageRoot = Path.Combine(OutputRoot(), "editor-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storageRoot);
            var suite = FindType("BijiSDK.MobileVerification.MobileConfigChecks");
            var run = suite.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string) }, null);
            if (run == null)
                throw new MissingMethodException(suite.FullName, "RunAsync(string, string)");
            editorChecks = (Task<int>)run.Invoke(null, new object[] { "editor", storageRoot });
            EditorApplication.update += FinishEditorChecks;
        }

        public static void BuildAndroid()
        {
            ConfigurePlayer();
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Prepare the saved verification scene before building.", ScenePath);
            var outputRoot = OutputRoot();
            Directory.CreateDirectory(outputRoot);
            var apkPath = Path.Combine(outputRoot, "BijiSDKMobileTest-arm64-development.apk");
            Debug.Log("BIJISDK_ANDROID_BUILD_SETTINGS " + JsonUtility.ToJson(CreateSummary(apkPath)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            var summary = CreateSummary(apkPath);
            summary.result = report.summary.result.ToString();
            summary.errors = report.summary.totalErrors;
            summary.warnings = report.summary.totalWarnings;
            summary.totalSize = report.summary.totalSize;
            summary.durationSeconds = report.summary.totalTime.TotalSeconds;
            var json = JsonUtility.ToJson(summary, true);
            File.WriteAllText(Path.Combine(outputRoot, "android-build-summary.json"), json);
            Debug.Log("BIJISDK_ANDROID_BUILD_COMPLETE " + json);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Android verification build " + report.summary.result + ".");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BijiSDKVerification";
            PlayerSettings.productName = "BijiSDKMobileTest20261029";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
            PlayerSettings.runInBackground = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = false;
        }

        private static BuildSummaryData CreateSummary(string apkPath)
        {
            return new BuildSummaryData
            {
                unityVersion = Application.unityVersion,
                applicationId = ApplicationId,
                productName = PlayerSettings.productName,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                architectures = PlayerSettings.Android.targetArchitectures.ToString(),
                strippingLevel = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android).ToString(),
                minimumApi = (int)PlayerSettings.Android.minSdkVersion,
                targetApi = (int)PlayerSettings.Android.targetSdkVersion,
                developmentBuild = true,
                insecureHttp = PlayerSettings.insecureHttpOption.ToString(),
                customKeystore = PlayerSettings.Android.useCustomKeystore,
                artifactPath = apkPath,
                result = "Started"
            };
        }

        private static void FinishEditorChecks()
        {
            if (!editorChecks.IsCompleted)
                return;
            EditorApplication.update -= FinishEditorChecks;
            if (editorChecks.IsFaulted)
            {
                Debug.LogException(editorChecks.Exception);
                EditorApplication.Exit(1);
                return;
            }
            if (editorChecks.IsCanceled)
            {
                Debug.LogError("BIJISDK_MOBILE_EDITOR_COMPLETE cancelled=true");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("BIJISDK_MOBILE_EDITOR_COMPLETE failures=" + editorChecks.Result);
            EditorApplication.Exit(editorChecks.Result == 0 ? 0 : 1);
        }

        private static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null)
                    return type;
            }
            throw new TypeLoadException("Expected imported verification type: " + name);
        }

        private static void SetBoolean(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
                throw new MissingFieldException(serialized.targetObject.GetType().FullName, name);
            property.boolValue = value;
        }

        private static string OutputRoot()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
                if (arguments[index] == "-bijiOutputRoot")
                    return Path.GetFullPath(arguments[index + 1]);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../../mobile-runs/2026-10-29"));
        }

        [Serializable]
        private sealed class BuildSummaryData
        {
            public string unityVersion;
            public string applicationId;
            public string productName;
            public string scriptingBackend;
            public string architectures;
            public string strippingLevel;
            public int minimumApi;
            public int targetApi;
            public bool developmentBuild;
            public string insecureHttp;
            public bool customKeystore;
            public string artifactPath;
            public string result;
            public int errors;
            public int warnings;
            public ulong totalSize;
            public double durationSeconds;
        }
    }
}
