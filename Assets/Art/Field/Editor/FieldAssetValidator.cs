using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Hodba.Field.Editor
{
    /// <summary>
    /// Проверка импорта арт-пака: настройки импорта, бюджеты и размеры мешей, Humanoid-аватар путника и его шаг.
    /// Материалы, сцены и префабы — не здесь: игра собирает их сама (Hodba ▸ Refresh Field Assets).
    /// build.ps1 оставляет маркер — проверка запускается сама, отчёт в ArtSource/Field/unity-validation.json.
    /// </summary>
    [InitializeOnLoad]
    public static class FieldAssetValidator
    {
        const string Root = FieldImportSettings.Root;
        const string Marker = "ArtSource/Field/run-unity-build.json";
        const string ReportPath = "ArtSource/Field/unity-validation.json";
        const int ExpectedModels = 12;

        static double _nextPoll;
        static bool _running;

        [Serializable]
        public class MeshResult
        {
            public string asset;
            public int triangles;
            public Vector3 size;
            public bool budgetPassed, sizePassed;
        }

        [Serializable]
        public class ValidationReport
        {
            public string unityVersion, utc, error;
            public bool passed, humanoidValid, humanoidHuman, animationPoseChanged;
            public int modelCount, textureCount, animationCount;
            public float posedHeadHeight, posedMeshHeight;
            public string[] animations, failures;
            public MeshResult[] meshes;
        }

        static FieldAssetValidator() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (_running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(Marker)) return;
            File.Delete(Marker);
            EditorApplication.delayCall += Validate;
        }

        [MenuItem("Hodba/Field/Validate Import")]
        public static void Validate()
        {
            if (_running) return;
            _running = true;
            var report = new ValidationReport { unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O") };
            var failures = new List<string>();
            Scene previous = SceneManager.GetActiveScene();
            Scene scratch = default;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string[] models = AssetDatabase.FindAssets("t:Model", new[] { Root + "Models" }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
                string[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "Textures" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                foreach (string p in textures)
                {
                    var t = (TextureImporter)AssetImporter.GetAtPath(p);
                    FieldImportSettings.ConfigureTexture(t);
                    t.SaveAndReimport();
                }
                // Настройки моделей и клипов ставит FieldImportSettings при импорте — переимпортируем.
                foreach (string p in models) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                report.modelCount = models.Length;
                report.textureCount = textures.Length;
                if (models.Length != ExpectedModels) failures.Add($"Expected {ExpectedModels} FBX models, got {models.Length}");

                // Экземпляры живут во временной сцене, которая не сохраняется; рабочая сцена не трогается.
                scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(scratch);
                var meshes = new List<MeshResult>();
                foreach (string p in models)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p), scratch);
                    var result = InspectMesh(p, go);
                    meshes.Add(result);
                    if (!result.budgetPassed) failures.Add($"{result.asset} triangle budget failed: {result.triangles}");
                    if (!result.sizePassed) failures.Add($"{result.asset} size failed: {result.size}");
                    if (IsTraveler(p)) InspectTraveler(go, p, report, failures);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                report.meshes = meshes.ToArray();
            }
            catch (Exception e)
            {
                report.error = e.ToString();
                failures.Add(e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if (!Application.isBatchMode && scratch.IsValid() && scratch.isLoaded) EditorSceneManager.CloseScene(scratch, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                report.failures = failures.ToArray();
                report.passed = failures.Count == 0;
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
                _running = false;
                Debug.Log($"Field validation {(report.passed ? "PASSED" : "FAILED")}: {ReportPath}");
            }
            if (Application.isBatchMode && !report.passed) throw new InvalidOperationException($"Field validation failed; see {ReportPath}");
        }

        static bool IsTraveler(string path) => Path.GetFileName(path).StartsWith("M3_", StringComparison.Ordinal);

        /// <summary>Аватар, клипы и шаг: в начале клипа ходьбы путник стоит в рост, к четверти клипа стопа сдвинулась.</summary>
        static void InspectTraveler(GameObject go, string path, ValidationReport report, List<string> failures)
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            report.humanoidValid = avatar != null && avatar.isValid;
            report.humanoidHuman = avatar != null && avatar.isHuman;
            if (!report.humanoidValid || !report.humanoidHuman) failures.Add("Traveler Humanoid avatar is invalid");

            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            report.animationCount = clips.Length;
            report.animations = clips.Select(c => $"{c.name} ({c.length:F3}s)").ToArray();
            if (clips.Length < 2) failures.Add("Expected Idle and Walk animation clips");

            var walk = clips.FirstOrDefault(c => c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0);
            if (walk == null || !report.humanoidValid)
            {
                if (walk == null) failures.Add("Walk animation clip is missing");
                return;
            }

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.avatar = avatar;
            var playable = AnimationPlayableUtilities.PlayClip(animator, walk, out var graph);
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable.SetTime(0);
                graph.Evaluate();
                report.posedHeadHeight = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var posed = new Mesh();
                skin.BakeMesh(posed, true);
                var heights = posed.vertices.Select(v => skin.transform.TransformPoint(v).y).ToArray();
                UnityEngine.Object.DestroyImmediate(posed);
                report.posedMeshHeight = heights.Max() - heights.Min();
                if (report.posedMeshHeight < 1.4f || report.posedMeshHeight > 1.8f) failures.Add($"Posed mesh height is outside expected range: {report.posedMeshHeight}");
                if (report.posedHeadHeight < 1.2f) failures.Add($"Posed traveler head is unexpectedly low: {report.posedHeadHeight}");

                var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Vector3 first = foot.position;
                playable.SetTime(walk.length * 0.25f);
                graph.Evaluate();
                report.animationPoseChanged = Vector3.Distance(first, foot.position) > 0.02f;
                if (!report.animationPoseChanged) failures.Add("Walk animation did not move the foot between phases");
            }
            finally
            {
                graph.Destroy();
            }
        }

        static MeshResult InspectMesh(string path, GameObject go)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).ToArray();
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null).ToArray();
            int triangles = filters.Sum(f => f.sharedMesh.triangles.Length / 3) + skins.Sum(r => r.sharedMesh.triangles.Length / 3);
            // SkinnedMeshRenderer.bounds охватывает всю анимацию, а нужен размер в позе привязки.
            var positions = filters.SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)))
                .Concat(skins.SelectMany(r => r.sharedMesh.vertices.Select(v => r.transform.TransformPoint(v)))).ToArray();
            var bounds = positions.Length > 0 ? new Bounds(positions[0], Vector3.zero) : new Bounds();
            foreach (var position in positions) bounds.Encapsulate(position);

            bool m1 = path.Contains("M1_"), m2 = path.Contains("M2_");
            int index = m1 || m2 ? int.Parse(Path.GetFileName(path).Substring(3, 2)) - 1 : 0;
            float expected = m1 ? new[] { .05f, .08f, .12f, .16f, .22f, .28f, .34f, .40f }[index] : m2 ? new[] { .6f, 1.2f, 1.8f }[index] : 1.75f;
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            return new MeshResult
            {
                asset = Path.GetFileName(path),
                triangles = triangles,
                size = bounds.size,
                budgetPassed = m1 ? triangles >= 150 && triangles <= 400 : m2 ? triangles >= 600 && triangles <= 1200 : triangles >= 5000 && triangles <= 8000,
                sizePassed = m1 || m2 ? Mathf.Abs(longest - expected) < 0.001f : Mathf.Abs(bounds.size.y - 1.75f) < 0.01f,
            };
        }
    }
}
