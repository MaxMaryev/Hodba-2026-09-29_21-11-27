using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hodba.Client;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Editor
{
    /// <summary>
    /// Подключает арт-пак Assets/Art/Field (ArtSource/Field/README.md) к игре: камни М1, валуны М2, путник М3,
    /// земля Т1–Т3, следы Т4. Конвейер владеет этими полями конфига и материалами и каждый раз выставляет их одинаково.
    /// Арт лежит в репозитории — если чего-то нет, это ошибка сборки ассетов, а не повод для заглушки.
    /// </summary>
    public static class FieldArt
    {
        const string Art = "Assets/Art/Field";
        const string Out = "Assets/Hodba/Generated/Art";
        const string Settings = "Assets/Hodba/Settings";

        public static void Assign(FieldConfig config)
        {
            if (!AssetDatabase.IsValidFolder(Art)) throw new System.InvalidOperationException($"Нет арт-пака {Art}.");
            Directory.CreateDirectory(Out);

            AssignRocks(config);
            AssignGround(config.groundMaterial);
            AssignFootprints(config);
            AssignTraveler(config);
        }

        // ——— камни ———

        static void AssignRocks(FieldConfig config)
        {
            config.stoneMeshes = Models("M1_").Select(Bake).ToList();
            config.boulderMeshes = Models("M2_").Select(Bake).ToList();
            config.stoneMaterial = Rock("M_RocksSmall", "RocksSmall");
            config.boulderMaterial = Rock("M_Boulders", "Boulders");
            Debug.Log($"Hodba: камней {config.stoneMeshes.Count}, валунов {config.boulderMeshes.Count}.");
        }

        static IEnumerable<string> Models(string prefix) =>
            AssetDatabase.FindAssets("t:Model", new[] { Art + "/Models" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith(prefix))
                .OrderBy(p => p);

        /// <summary>
        /// Меш из FBX с запечённым положением узла и pivot внизу по центру —
        /// камни рисуются инстансингом, трансформ узла там не участвует.
        /// </summary>
        static Mesh Bake(string fbx)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var filter = root != null ? root.GetComponentInChildren<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) throw new System.InvalidOperationException($"{fbx}: нет меша.");
            var src = filter.sharedMesh;
            if (!src.isReadable) throw new System.InvalidOperationException($"{fbx}: меш не читается — импорт должен включать Read/Write (FieldImportSettings).");

            var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var verts = src.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
            var normals = src.normals.Select(n => matrix.MultiplyVector(n).normalized).ToArray();
            bool mirrored = matrix.determinant < 0f;
            var tangents = src.tangents.Select(t =>
            {
                var d = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                return new Vector4(d.x, d.y, d.z, mirrored ? -t.w : t.w);
            }).ToArray();

            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            var shift = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] += shift;

            var indices = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++) indices.AddRange(src.GetTriangles(s));
            if (mirrored)
                for (int i = 0; i < indices.Count; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(fbx) };
            mesh.SetVertices(verts);
            if (normals.Length == verts.Length) mesh.SetNormals(normals);
            if (tangents.Length == verts.Length) mesh.SetTangents(tangents);
            mesh.SetUVs(0, src.uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            if (normals.Length != verts.Length) mesh.RecalculateNormals();

            string path = $"{Out}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ——— земля и следы ———

        /// <summary>Т1 — пепел, Т3 — корка. В альфе альбедо — микротени, их запекает генератор текстур. Рябь — процедурная.</summary>
        static void AssignGround(Material ground)
        {
            ground.SetTexture("_AshAlbedo", Require("T1_Ash", "Albedo"));
            ground.SetTexture("_AshNormal", Require("T1_Ash", "Normal"));
            ground.SetTexture("_PackedAlbedo", Require("T3_Crust", "Albedo"));
            ground.SetTexture("_PackedNormal", Require("T3_Crust", "Normal"));
            EditorUtility.SetDirty(ground);
        }

        /// <summary>Левый след Т4; правый — его отражение (Footprints).</summary>
        static void AssignFootprints(FieldConfig config)
        {
            var mat = Material("M_FootprintLit", "Hodba/FootprintLit");
            mat.SetTexture("_BaseMap", Require("T4_FootprintLeft", "Albedo"));
            mat.SetTexture("_BumpMap", Require("T4_FootprintLeft", "Normal"));
            EditorUtility.SetDirty(mat);
            config.footprintMaterial = mat;
        }

        // ——— путник ———

        /// <summary>Префаб путника: модель М3, аватар, переход стойка ↔ ходьба. Виден только его тень.</summary>
        static void AssignTraveler(FieldConfig config)
        {
            string fbx = Models("M3_").FirstOrDefault() ?? throw new System.InvalidOperationException("Нет модели путника М3.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var walk = clips.FirstOrDefault(c => c.name.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new System.InvalidOperationException($"{fbx}: нет клипа ходьбы.");
            var idle = clips.FirstOrDefault(c => c.name.IndexOf("Idle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new System.InvalidOperationException($"{fbx}: нет клипа стойки.");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = Controller(walk, idle);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var mat = Traveler();
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }

                config.walkerPrefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Out}/Traveler.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Стоит ↔ идёт по параметру Speed (м/с).</summary>
        static AnimatorController Controller(AnimationClip walk, AnimationClip idle)
        {
            string path = $"{Out}/Traveler.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            var walkState = machine.AddState("Walk");
            walkState.motion = walk;
            machine.defaultState = idleState;

            var go = idleState.AddTransition(walkState);
            go.hasExitTime = false;
            go.duration = 0.3f;
            go.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

            var stop = walkState.AddTransition(idleState);
            stop.hasExitTime = false;
            stop.duration = 0.4f;
            stop.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ——— материалы ———

        /// <summary>URP Lit путника: Albedo, Normal, MetallicSmoothness.</summary>
        static Material Traveler()
        {
            var m = Material("M_Traveler", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", Require("Traveler", "Albedo"));
            m.SetTexture("_BumpMap", Require("Traveler", "Normal"));
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Require("Traveler", "MetallicSmoothness"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", null);
            m.DisableKeyword("_OCCLUSIONMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 1f); // множитель к альфе MetallicSmoothness
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Hodba/Rock из атласа пака: Albedo, Normal, AO. Свет и дымка — как у земли.</summary>
        static Material Rock(string name, string prefix)
        {
            var m = Material(name, "Hodba/Rock");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", Require(prefix, "Albedo"));
            m.SetTexture("_BumpMap", Require(prefix, "Normal"));
            m.SetTexture("_OcclusionMap", Require(prefix, "AO"));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Require(string prefix, string map)
        {
            string path = $"{Art}/Textures/{prefix}_{map}.png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? throw new System.InvalidOperationException($"Нет текстуры {path}.");
        }

        static Material Material(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName) ?? throw new System.InvalidOperationException($"Не найден шейдер {shaderName}.");
            string path = $"{Settings}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            return m;
        }
    }
}
