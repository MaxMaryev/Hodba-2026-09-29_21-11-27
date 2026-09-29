using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hodba.Editor
{
    /// <summary>
    /// Собирает debug APK. Можно вызвать из меню или через Temp/BuildApk.request
    /// (файл-запрос подхватывается после domain reload).
    /// </summary>
    public static class AndroidApkBuilder
    {
        const string RequestPath = "Temp/BuildApk.request";
        const string ResultPath = "Temp/BuildApk.result";
        const string OutputPath = "Builds/Hodba.apk";
        const string PackageName = "com.DefaultCompany.Hodba";

        [InitializeOnLoadMethod]
        static void WatchRequest()
        {
            EditorApplication.delayCall += TryBuildFromRequest;
        }

        static void TryBuildFromRequest()
        {
            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            Build();
        }

        [MenuItem("Hodba/Build Android APK", priority = 100)]
        public static void Build()
        {
            Directory.CreateDirectory("Builds");
            Directory.CreateDirectory("Temp");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[AndroidApkBuilder] Switching build target to Android...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Android, BuildTarget.Android))
                {
                    WriteResult("FAILED: could not switch to Android build target");
                    return;
                }
            }

            if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android))
                || PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
                    .StartsWith("com.UnityCompany"))
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            }

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Debug;

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                WriteResult("FAILED: no enabled scenes in EditorBuildSettings");
                return;
            }

            Debug.Log($"[AndroidApkBuilder] Building {OutputPath} from {scenes.Length} scene(s)...");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            if (report.summary.result == BuildResult.Succeeded)
            {
                var absolute = Path.GetFullPath(OutputPath);
                WriteResult($"OK: {absolute} ({report.summary.totalSize} bytes)");
                Debug.Log($"[AndroidApkBuilder] Success: {absolute}");
            }
            else
            {
                WriteResult($"FAILED: {report.summary.result} errors={report.summary.totalErrors}");
                Debug.LogError($"[AndroidApkBuilder] Build failed: {report.summary.result}");
            }
        }

        static void WriteResult(string text)
        {
            File.WriteAllText(ResultPath, text);
        }
    }
}
