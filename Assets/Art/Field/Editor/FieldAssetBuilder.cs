using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hodba.Field.Editor
{
    [InitializeOnLoad]
    public static class FieldAssetBuilder
    {
        const string Root = FieldImportSettings.Root;
        const string Marker = "ArtSource/Field/run-unity-build.json";
        static double nextPoll;
        static bool building;
        [Serializable] public class MeshResult { public string asset; public int triangles; public Vector3 size; public bool budgetPassed; public bool sizePassed; }
        [Serializable] public class ValidationReport
        {
            public string unityVersion, utc, error;
            public bool passed, humanoidValid, humanoidHuman, rendererPreserved, allShadersSupported;
            public int rendererIndex, modelCount, textureCount, animationCount;
            public float shadowDistance, travelerWalkSpeed = 1.3f;
            public bool animationPoseChanged;
            public float posedHeadHeight, posedMeshHeight;
            public string[] animations, failures, screenshots;
            public MeshResult[] meshes;
        }
        static FieldAssetBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (building || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(Marker)) return;
            File.Delete(Marker);
            EditorApplication.delayCall += Build;
        }
        [MenuItem("Hodba/Field/Build and Validate")]
        public static void Build()
        {
            if (building) return;
            building = true;
            var report = new ValidationReport { unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O") };
            var failures = new List<string>();
            Scene previous = SceneManager.GetActiveScene(); Scene demo = default;
            try
            {
                foreach (var dir in new[] { "Materials", "Prefabs", "Scenes" }) Directory.CreateDirectory(Root + dir);
                Directory.CreateDirectory("ArtSource/Field/Previews");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string[] models = AssetDatabase.FindAssets("t:Model", new[] { Root + "Models" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
                string[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "Textures" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                foreach (string p in textures) { var t = (TextureImporter)AssetImporter.GetAtPath(p); FieldImportSettings.ConfigureTexture(t); t.SaveAndReimport(); }
                foreach (string p in models)
                {
                    var m = (ModelImporter)AssetImporter.GetAtPath(p);
                    if (p.Contains("M3_")) FieldImportSettings.ConfigureClips(m);
                    m.SaveAndReimport();
                }
                report.modelCount = models.Length; report.textureCount = textures.Length;
                if (models.Length != 12) failures.Add("Expected 12 FBX models, got " + models.Length);
                int rendererIndex = ConfigureRenderer(report);
                var small = Lit("RocksSmall"); var boulders = Lit("Boulders"); var traveler = Lit("Traveler");
                var ground = GroundMaterial(); var left = FootprintMaterial("T4_FootprintLeft"); var right = FootprintMaterial("T4_FootprintRight");
                var mats = new[] { small, boulders, traveler, ground, left, right };
                report.allShadersSupported = mats.All(m => m.shader != null && m.shader.isSupported);
                if (!report.allShadersSupported) failures.Add("One or more shaders are unsupported");
                // All transient objects belong to the review scene, never the user's scene.
                demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(demo);
                var meshResults = new List<MeshResult>();
                var prefabs = new List<GameObject>();
                foreach (string p in models)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    bool person = p.Contains("M3_");
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    go.name = Path.GetFileNameWithoutExtension(p);
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterials = Enumerable.Repeat(person ? traveler : p.Contains("M2_") ? boulders : small, r.sharedMaterials.Length).ToArray();
                        if (person) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                        if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                    }
                    if (person) SetupTraveler(go, p, report, failures);
                    MeshResult result = InspectMesh(p, go); meshResults.Add(result);
                    if (!result.budgetPassed) failures.Add(result.asset + " triangle budget failed: " + result.triangles);
                    if (!result.sizePassed) failures.Add(result.asset + " size failed: " + result.size);
                    prefabs.Add(PrefabUtility.SaveAsPrefabAsset(go, Root + "Prefabs/" + go.name + ".prefab"));
                    UnityEngine.Object.DestroyImmediate(go);
                }
                report.meshes = meshResults.ToArray();
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.48f, 0.49f, 0.52f);
                var ambient = new UnityEngine.Rendering.SphericalHarmonicsL2(); ambient.AddAmbientLight(RenderSettings.ambientLight); RenderSettings.ambientProbe = ambient;
                RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.33f, 0.31f, 0.29f); RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.008f;
                var sun = new GameObject("Sun — sunrise 3 degrees behind +Z").AddComponent<Light>(); sun.type = LightType.Directional; sun.color = new Color(1, 0.86f, 0.69f); sun.intensity = 2.1f; sun.shadows = LightShadows.Hard; sun.shadowBias = 0.025f; sun.shadowNormalBias = 0.15f; sun.transform.rotation = Quaternion.Euler(3, 0, 0); RenderSettings.sun = sun;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "T1 + T3 large patches / T2 wind normal"; floor.transform.localScale = new Vector3(16, 1, 16); floor.GetComponent<Renderer>().sharedMaterial = ground;
                var scaleMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                scaleMarker.name = "Scale reference — exactly one metre";
                scaleMarker.transform.position = new Vector3(6, .5f, 5);
                var markerMaterial = Material("ScaleReference", "Universal Render Pipeline/Lit");
                markerMaterial.SetColor("_BaseColor", new Color(.25f, .28f, .30f)); markerMaterial.SetFloat("_Smoothness", 0);
                scaleMarker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                int iSmall = 0, iBig = 0;
                foreach (var prefab in prefabs)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (go.name.StartsWith("M1_")) { go.transform.position = new Vector3(-3.5f + iSmall * 0.9f, 0, 5); iSmall++; }
                    else if (go.name.StartsWith("M2_")) { go.transform.position = new[] { new Vector3(-5, 0, 12), new Vector3(6, 0, 17), new Vector3(-8, 0, 25) }[iBig++]; }
                    else { go.transform.position = new Vector3(0, 0, 0); go.AddComponent<FieldTravelerPreview>(); }
                }
                for (int i = 0; i < 16; i++)
                {
                    var print = GameObject.CreatePrimitive(PrimitiveType.Quad); print.name = (i % 2 == 0 ? "Left" : "Right") + " footprint";
                    print.transform.position = new Vector3((i % 2 == 0 ? -0.10f : 0.10f), 0.006f, -4 + i * 0.48f); print.transform.rotation = Quaternion.Euler(90, 0, 0); print.transform.localScale = new Vector3(0.15f, 0.30f, 1);
                    print.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? left : right; print.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    UnityEngine.Object.DestroyImmediate(print.GetComponent<Collider>());
                }
                var cam = new GameObject("Field first-person preview").AddComponent<Camera>(); cam.tag = "MainCamera"; cam.transform.position = new Vector3(0.55f, 1.7f, -1.5f); cam.transform.rotation = Quaternion.Euler(9, 0, 0); cam.nearClipPlane = 0.05f; cam.farClipPlane = 180; cam.fieldOfView = 65; cam.backgroundColor = RenderSettings.fogColor; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex); cam.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(demo);
                var walking = demo.GetRootGameObjects().Select(g => g.GetComponent<Animator>()).FirstOrDefault(a => a);
                if (walking && report.humanoidValid)
                {
                    walking.Rebind(); walking.Update(0);
                    walking.Play(0, 0, 0); walking.Update(0);
                    report.posedHeadHeight = walking.GetBoneTransform(HumanBodyBones.Head).position.y;
                    var skin = walking.GetComponentInChildren<SkinnedMeshRenderer>();
                    var posed = new Mesh(); skin.BakeMesh(posed, true);
                    var worldVertices = posed.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                    report.posedMeshHeight = worldVertices.Max(v => v.y) - worldVertices.Min(v => v.y);
                    if (report.posedMeshHeight < 1.4f || report.posedMeshHeight > 1.8f) failures.Add("Posed mesh height is outside expected range: " + report.posedMeshHeight);
                    UnityEngine.Object.DestroyImmediate(posed);
                    if (report.posedHeadHeight < 1.2f) failures.Add("Posed traveler head is unexpectedly low: " + report.posedHeadHeight);
                }
                EditorSceneManager.SaveScene(demo, Root + "Scenes/Field_Demo.unity");
                // Several pose captures run inside one editor update; bypass the GPU skinning cache.
                if (walking) foreach (var skin in walking.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.forceMatrixRecalculationPerRender = true;
                // Each capture explicitly renders this scene's camera; the user's open scene remains untouched.
                Capture(cam, "Field_FirstPerson.png");
                cam.transform.position = new Vector3(12, 9, -11); cam.transform.LookAt(new Vector3(0, 0, 8)); Capture(cam, "Field_Gallery.png");
                cam.transform.position = new Vector3(0, 4, -1); cam.transform.rotation = Quaternion.Euler(75, 0, 0); Capture(cam, "Field_GroundFootprints.png");
                cam.orthographic = true; cam.orthographicSize = 20;
                cam.transform.position = new Vector3(6, 20, 0); cam.transform.LookAt(new Vector3(0, 0, 15));
                Capture(cam, "Field_Shadow_Phase0.png");
                if (walking && report.humanoidValid)
                {
                    var foot = walking.GetBoneTransform(HumanBodyBones.LeftFoot);
                    Vector3 first = foot.position;
                    walking.Play(0, 0, 0.25f); walking.Update(0);
                    report.animationPoseChanged = Vector3.Distance(first, foot.position) > 0.02f;
                    if (!report.animationPoseChanged) failures.Add("Walk animation did not move the foot between phases");
                }
                Capture(cam, "Field_Shadow_Phase1.png");
                if (walking)
                {
                    foreach (var skin in walking.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.shadowCastingMode = ShadowCastingMode.On;
                    cam.orthographicSize = 1.35f; cam.transform.position = new Vector3(2.5f, 1.7f, 3.8f); cam.transform.LookAt(new Vector3(0, .8f, 0));
                    Capture(cam, "Field_Traveler_Debug.png");
                }
                foreach (var material in mats)
                    foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) failures.Add(material.shader.name + ": " + message.message);
                report.screenshots = new[] { "Previews/Field_FirstPerson.png", "Previews/Field_Gallery.png", "Previews/Field_GroundFootprints.png", "Previews/Field_Shadow_Phase0.png", "Previews/Field_Shadow_Phase1.png", "Previews/Field_Traveler_Debug.png" };
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { report.error = e.ToString(); failures.Add(e.Message); Debug.LogException(e); }
            finally
            {
                if (!Application.isBatchMode && demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                report.failures = failures.ToArray(); report.passed = failures.Count == 0;
                Directory.CreateDirectory("ArtSource/Field"); File.WriteAllText("ArtSource/Field/unity-validation.json", JsonUtility.ToJson(report, true));
                building = false;
                Debug.Log("Field validation " + (report.passed ? "PASSED" : "FAILED") + ": ArtSource/Field/unity-validation.json");
            }
            if (Application.isBatchMode && !report.passed) throw new InvalidOperationException("Field validation failed; see unity-validation.json");
        }
        static int ConfigureRenderer(ValidationReport report)
        {
            var urp = (QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (!urp) throw new InvalidOperationException("An existing URP asset is required.");
            string path = Root + "Materials/Field_ForwardRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!renderer) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, path); }
            var serialized = new SerializedObject(urp); var list = serialized.FindProperty("m_RendererDataList"); var first = list.GetArrayElementAtIndex(0).objectReferenceValue;
            int originalDefault = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            int index = -1;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) index = i;
            if (index < 0) { index = list.arraySize; list.InsertArrayElementAtIndex(index); list.GetArrayElementAtIndex(index).objectReferenceValue = renderer; }
            serialized.FindProperty("m_ShadowDistance").floatValue = Mathf.Max(80, urp.shadowDistance);
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(urp);
            report.rendererPreserved = first == list.GetArrayElementAtIndex(0).objectReferenceValue && originalDefault == serialized.FindProperty("m_DefaultRendererIndex").intValue;
            report.rendererIndex = index; report.shadowDistance = urp.shadowDistance;
            return index;
        }
        static Texture2D Tex(string prefix, string map) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Textures/" + prefix + "_" + map + ".png");
        static Material Material(string name, string shader)
        {
            string p = Root + "Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(p); var s = Shader.Find(shader);
            if (!s) throw new InvalidOperationException("Shader missing: " + shader);
            if (!mat) { mat = new Material(s); AssetDatabase.CreateAsset(mat, p); } else mat.shader = s;
            EditorUtility.SetDirty(mat); return mat;
        }
        static Material Lit(string prefix)
        {
            var m = Material(prefix, "Universal Render Pipeline/Lit"); m.SetTexture("_BaseMap", Tex(prefix, "Albedo")); m.SetTexture("_BumpMap", Tex(prefix, "Normal")); m.SetTexture("_OcclusionMap", Tex(prefix, "AO")); m.SetTexture("_MetallicGlossMap", Tex(prefix, "MetallicSmoothness")); m.SetFloat("_Smoothness", 1); m.SetFloat("_Metallic", 0); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_OCCLUSIONMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); return m;
        }
        static Material GroundMaterial()
        {
            var m = Material("Field_Ground", "Hodba/Field/Ash Ground"); m.SetTexture("_BaseMap", Tex("T1_Ash", "Albedo")); m.SetTexture("_CrustMap", Tex("T3_Crust", "Albedo"));
            foreach (string map in new[] { "Normal", "Roughness", "AO" }) { m.SetTexture("_Ash" + map, Tex("T1_Ash", map)); m.SetTexture("_Crust" + map, Tex("T3_Crust", map)); }
            m.SetTexture("_RippleNormal", Tex("T2_Ripples", "Normal")); return m;
        }
        static Material FootprintMaterial(string prefix) { var m = Material(prefix, "Hodba/Field/Footprint"); m.SetTexture("_BaseMap", Tex(prefix, "Albedo")); m.SetTexture("_BumpMap", Tex(prefix, "Normal")); return m; }
        static void SetupTraveler(GameObject go, string path, ValidationReport report, List<string> failures)
        {
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(); animator.avatar = avatar;
            report.humanoidValid = avatar && avatar.isValid; report.humanoidHuman = avatar && avatar.isHuman;
            if (!report.humanoidValid || !report.humanoidHuman) failures.Add("Traveler Humanoid avatar is invalid");
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray(); report.animationCount = clips.Length; report.animations = clips.Select(c => c.name + " (" + c.length.ToString("F3") + "s)").ToArray();
            if (clips.Length < 2) failures.Add("Expected Idle and Walk animation clips");
            string controllerPath = Root + "Prefabs/Traveler.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            foreach (var clip in clips) { var state = machine.AddState(clip.name); state.motion = clip; if (clip.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0) machine.defaultState = state; }
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        }
        static MeshResult InspectMesh(string path, GameObject go)
        {
            var meshes = go.GetComponentsInChildren<MeshFilter>().Select(m => m.sharedMesh).Concat(go.GetComponentsInChildren<SkinnedMeshRenderer>().Select(m => m.sharedMesh)).Where(m => m != null).ToArray();
            int triangles = meshes.Sum(m => m.triangles.Length / 3);
            // SkinnedMeshRenderer.bounds includes the full animation envelope, not bind-pose size.
            var positions = go.GetComponentsInChildren<MeshFilter>().SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)))
                .Concat(go.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r => r.sharedMesh.vertices.Select(v => r.transform.TransformPoint(v)))).ToArray();
            Bounds bounds = positions.Length > 0 ? new Bounds(positions[0], Vector3.zero) : new Bounds();
            foreach (var position in positions) bounds.Encapsulate(position);
            bool m1 = path.Contains("M1_"), m2 = path.Contains("M2_");
            int index = m1 || m2 ? int.Parse(Path.GetFileName(path).Substring(3, 2)) - 1 : 0;
            float expected = m1 ? new[] { .05f, .08f, .12f, .16f, .22f, .28f, .34f, .40f }[index] : m2 ? new[] { .6f, 1.2f, 1.8f }[index] : 1.75f;
            float max = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            return new MeshResult { asset = Path.GetFileName(path), triangles = triangles, size = bounds.size, budgetPassed = m1 ? triangles >= 150 && triangles <= 400 : m2 ? triangles >= 600 && triangles <= 1200 : triangles >= 5000 && triangles <= 8000, sizePassed = m1 || m2 ? Mathf.Abs(max - expected) < 0.001f : Mathf.Abs(bounds.size.y - 1.75f) < 0.01f };
        }
        static void Capture(Camera camera, string name)
        {
            var rt = new RenderTexture(1600, 1000, 24); var old = RenderTexture.active; camera.targetTexture = rt;
            try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt; var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); File.WriteAllBytes("ArtSource/Field/Previews/" + name, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
            finally { camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }
    }
}

