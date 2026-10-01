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

        /// <summary>Только то, что влияет на свет: камни на Hodba/Rock и альбедо земли с микротенями.</summary>
        public static void AssignLook(FieldConfig config, Material genericStone)
        {
            if (!AssetDatabase.IsValidFolder(Art)) return;
            Directory.CreateDirectory(Out);

            AssignRocks(config, genericStone);
            AssignGround(config);
        }

        // ——— камни ———

        static void AssignRocks(FieldConfig config, Material genericStone)
        {
            var small = Models("M1_").Select(Bake).Where(m => m != null).ToList();
            var big = Models("M2_").Select(Bake).Where(m => m != null).ToList();

            if (small.Count > 0 && config.stoneMeshes.All(m => m == null)) config.stoneMeshes = small;
            if (big.Count > 0 && config.boulderMeshes.All(m => m == null)) config.boulderMeshes = big;

            var rocks = Rock("M_RocksSmall", "RocksSmall");
            var boulders = Rock("M_Boulders", "Boulders");
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
            config.ashAlbedo = WithMicroShadows(config.ashAlbedo, "T1_Ash");
            if (config.ashNormal == null) config.ashNormal = Tex("T1_Ash", "Normal");
            if (config.rippleNormal == null) config.rippleNormal = Tex("T2_Ripples", "Normal");
            config.packedAlbedo = WithMicroShadows(config.packedAlbedo, "T3_Crust");
        }

        /// <summary>
        /// Альбедо пака с микротенями в альфе — вместо пустого поля или исходного альбедо; выбранное руками не трогаем.
        /// </summary>
        static Texture2D WithMicroShadows(Texture2D current, string prefix)
        {
            var source = Tex(prefix, "Albedo");
            if (current != null && current != source) return current;
            return PackMicroShadows(prefix) ?? source;
        }

        /// <summary>
        /// Альфа = затенённость пака × впадины по карте высот (высота ниже своей округи ~1 см).
        /// AO пака у пепла почти пустой (0,95–1), а зерно и трещины видны только в высотах.
        /// Настройки импорта — как у исходного альбедо, чтобы сжатие и мипы на телефонах совпали.
        /// </summary>
        public static Texture2D PackMicroShadows(string prefix)
        {
            string albedoPath = $"{Art}/Textures/{prefix}_Albedo.png";
            string heightPath = $"{Art}/Textures/{prefix}_Height.png";
            string aoPath = $"{Art}/Textures/{prefix}_AO.png";
            string outPath = $"{Out}/{prefix}_AlbedoAO.png";
            if (!File.Exists(albedoPath) || !File.Exists(heightPath)) return null;
            // AO от прежнего пака не совпадает с новыми высотами (старые трещины впечатались бы в новую корку) — берём только свежий.
            if (File.Exists(aoPath) && File.GetLastWriteTimeUtc(aoPath) < File.GetLastWriteTimeUtc(heightPath)) aoPath = null;

            if (!File.Exists(outPath) || File.GetLastWriteTimeUtc(outPath) < Newest(albedoPath, heightPath, aoPath))
            {
                var albedo = Load(albedoPath);
                var height = Load(heightPath);
                var ao = File.Exists(aoPath) ? Load(aoPath) : null;
                try
                {
                    int w = albedo.width, h = albedo.height;
                    if (height.width != w || height.height != h || (ao != null && (ao.width != w || ao.height != h)))
                    {
                        Debug.LogWarning($"Hodba: {prefix} — размеры альбедо, высот и AO не совпадают, микротеней не будет.");
                        return null;
                    }
                    var color = albedo.GetPixels32();
                    var cavity = Cavity(height.GetPixels32(), w, h, Mathf.Max(2, w / 160));
                    var occlusion = ao != null ? ao.GetPixels32() : null;
                    for (int i = 0; i < color.Length; i++)
                    {
                        float a = cavity[i] * (occlusion != null ? occlusion[i].r / 255f : 1f);
                        color[i].a = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                    }
                    var packed = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    packed.SetPixels32(color);
                    File.WriteAllBytes(outPath, packed.EncodeToPNG());
                    Object.DestroyImmediate(packed);
                }
                finally
                {
                    Object.DestroyImmediate(albedo);
                    Object.DestroyImmediate(height);
                    if (ao != null) Object.DestroyImmediate(ao);
                }
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
                CopyImport(albedoPath, outPath);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static System.DateTime Newest(params string[] paths) =>
            paths.Where(File.Exists).Select(File.GetLastWriteTimeUtc).Max();

        static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        /// <summary>1 на ровном и буграх, до 0,4 в самых глубоких впадинах (98-й процентиль) относительно размытой округи.</summary>
        static float[] Cavity(Color32[] height, int w, int h, int radius)
        {
            var hv = new float[w * h];
            for (int i = 0; i < hv.Length; i++) hv[i] = height[i].r / 255f;
            var blurred = BoxBlur(BoxBlur(hv, w, h, radius, true), w, h, radius, false);

            var depth = new float[hv.Length];
            for (int i = 0; i < hv.Length; i++) depth[i] = Mathf.Max(0f, blurred[i] - hv[i]);
            var sorted = depth.Where((_, i) => i % 7 == 0).OrderBy(d => d).ToArray();
            float p98 = Mathf.Max(1e-4f, sorted[(int)(sorted.Length * 0.98f)]);

            for (int i = 0; i < depth.Length; i++) depth[i] = 1f - 0.6f * Mathf.Clamp01(depth[i] / p98);
            return depth;
        }

        /// <summary>Бесшовное размытие по строкам или столбцам: тайл повторяется, края заворачиваются.</summary>
        static float[] BoxBlur(float[] src, int w, int h, int r, bool horizontal)
        {
            var dst = new float[src.Length];
            int n = horizontal ? w : h, lines = horizontal ? h : w;
            float inv = 1f / (2 * r + 1);
            for (int line = 0; line < lines; line++)
            {
                int Index(int k)
                {
                    k = ((k % n) + n) % n;
                    return horizontal ? line * w + k : k * w + line;
                }
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += src[Index(k)];
                for (int k = 0; k < n; k++)
                {
                    dst[Index(k)] = sum * inv;
                    sum += src[Index(k + r + 1)] - src[Index(k - r)];
                }
            }
            return dst;
        }

        static void CopyImport(string sourcePath, string targetPath)
        {
            var source = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            var target = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (source == null || target == null) return;

            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            settings.alphaSource = TextureImporterAlphaSource.FromInput;
            settings.alphaIsTransparency = false;
            target.SetTextureSettings(settings);
            target.textureCompression = source.textureCompression;
            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
                target.SetPlatformTextureSettings(source.GetPlatformTextureSettings(platform));
            target.SaveAndReimport();
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

        /// <summary>Hodba/Rock из атласа пака: Albedo, Normal, AO. Свет и дымка — как у земли.</summary>
        static Material Rock(string name, string prefix)
        {
            var albedo = Tex(prefix, "Albedo");
            if (albedo == null) return null;
            var m = Material(name, "Hodba/Rock");
            if (m == null) return null;

            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", Tex(prefix, "Normal"));
            m.SetTexture("_OcclusionMap", Tex(prefix, "AO"));
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
