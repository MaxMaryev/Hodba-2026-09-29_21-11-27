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
    /// Подключает пак ассетов Assets/Art/Field (ArtSource/Field/README.md) к игре:
    /// камни М1, валуны М2, путник М3, текстуры Т1–Т4. Чего нет — остаётся заглушкой.
    /// Поля конфига, которые уже заполнены руками, не трогаются.
    /// </summary>
    public static class FieldArt
    {
        const string Art = "Assets/Art/Field";
        const string Out = "Assets/Hodba/Generated/Art";
        const string Settings = "Assets/Hodba/Settings";

        public static void Assign(FieldConfig config, Material genericStone)
        {
            if (!AssetDatabase.IsValidFolder(Art)) return;
            Directory.CreateDirectory(Out);

            AssignRocks(config, genericStone);
            AssignGround(config);
            AssignFootprints(config);
            AssignTraveler(config);
        }

        // ——— камни ———

        static void AssignRocks(FieldConfig config, Material genericStone)
        {
            var small = Models("M1_").Select(Bake).Where(m => m != null).ToList();
            var big = Models("M2_").Select(Bake).Where(m => m != null).ToList();

            if (small.Count > 0 && config.stoneMeshes.All(m => m == null)) config.stoneMeshes = small;
            if (big.Count > 0 && config.boulderMeshes.All(m => m == null)) config.boulderMeshes = big;

            var rocks = Lit("M_RocksSmall", "RocksSmall");
            var boulders = Lit("M_Boulders", "Boulders");
            if (rocks != null && (config.stoneMaterial == null || config.stoneMaterial == genericStone)) config.stoneMaterial = rocks;
            if (boulders != null && config.boulderMaterial == null) config.boulderMaterial = boulders;
            Debug.Log($"Hodba: камней {small.Count}, валунов {big.Count}.");
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
            if (filter == null || filter.sharedMesh == null) return null;
            var src = filter.sharedMesh;
            if (!src.isReadable)
            {
                Debug.LogWarning($"Hodba: {fbx} не читается (Read/Write выключен) — пропускаю.");
                return null;
            }

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

        static void AssignGround(FieldConfig config)
        {
            if (config.ashAlbedo == null) config.ashAlbedo = Tex("T1_Ash", "Albedo");
            if (config.ashNormal == null) config.ashNormal = Tex("T1_Ash", "Normal");
            if (config.rippleNormal == null) config.rippleNormal = Tex("T2_Ripples", "Normal");
            if (config.packedAlbedo == null) config.packedAlbedo = Tex("T3_Crust", "Albedo");
        }

        static void AssignFootprints(FieldConfig config)
        {
            var albedo = Tex("T4_FootprintLeft", "Albedo");
            var normal = Tex("T4_FootprintLeft", "Normal");
            if (albedo == null) return;

            var mat = Material("M_FootprintLit", "Hodba/FootprintLit");
            if (mat == null) return;
            mat.SetTexture("_BaseMap", albedo);
            if (normal != null) mat.SetTexture("_BumpMap", normal);
            EditorUtility.SetDirty(mat);

            // Сгенерированный след заменяем, выбранный руками — нет.
            if (config.footprintMaterial == null || config.footprintMaterial.shader.name == "Hodba/Footprint")
                config.footprintMaterial = mat;
            if (config.footprintSize == new Vector2(0.13f, 0.30f))
                config.footprintSize = new Vector2(0.14f, 0.32f); // холст Т4: 14 × 32 см
        }

        // ——— путник ———

        static void AssignTraveler(FieldConfig config)
        {
            string fbx = Models("M3_").FirstOrDefault();
            if (fbx == null || config.walkerPrefab != null) return;

            LoopClips(fbx);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var walk = clips.FirstOrDefault(c => c.name.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0);
            var idle = clips.FirstOrDefault(c => c.name.IndexOf("Idle", System.StringComparison.OrdinalIgnoreCase) >= 0);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = Controller(walk, idle);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var mat = Lit("M_Traveler", "Traveler");
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }

                config.walkerPrefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Out}/Traveler.prefab");
                Debug.Log($"Hodba: путник подключён. Ходьба: {walk?.name ?? "нет"}, стойка: {idle?.name ?? "нет"}.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void LoopClips(string fbx)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer.clipAnimations.Length > 0) return;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true;
                c.loopPose = true;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
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
            idleState.motion = idle != null ? idle : walk;
            var walkState = machine.AddState("Walk");
            walkState.motion = walk != null ? walk : idle;
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

        // ——— общее ———

        static Texture2D Tex(string prefix, string map) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/Textures/{prefix}_{map}.png");

        /// <summary>URP Lit из атласа пака: Albedo, Normal, AO, MetallicSmoothness.</summary>
        static Material Lit(string name, string prefix)
        {
            var albedo = Tex(prefix, "Albedo");
            if (albedo == null) return null;
            var m = Material(name, "Universal Render Pipeline/Lit");
            if (m == null) return null;

            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", albedo);
            SetMap(m, "_BumpMap", Tex(prefix, "Normal"), "_NORMALMAP");
            SetMap(m, "_OcclusionMap", Tex(prefix, "AO"), "_OCCLUSIONMAP");
            SetMap(m, "_MetallicGlossMap", Tex(prefix, "MetallicSmoothness"), "_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 1f); // множитель к альфе MetallicSmoothness (1 − шероховатость)
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetMap(Material m, string prop, Texture2D tex, string keyword)
        {
            m.SetTexture(prop, tex);
            if (tex != null) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        static Material Material(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Hodba: не найден шейдер {shaderName}");
                return null;
            }
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
