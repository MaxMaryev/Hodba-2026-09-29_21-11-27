using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hodba.Field.Editor
{
    public sealed class FieldImportSettings : AssetPostprocessor
    {
        internal const string Root = "Assets/Art/Field/";
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root + "Textures/", StringComparison.Ordinal)) return;
            ConfigureTexture((TextureImporter)assetImporter);
        }
        internal static void ConfigureTexture(TextureImporter t)
        {
            string n = Path.GetFileNameWithoutExtension(t.assetPath);
            bool normal = n.EndsWith("_Normal", StringComparison.Ordinal);
            bool albedo = n.EndsWith("_Albedo", StringComparison.Ordinal);
            bool footprint = n.StartsWith("T4_", StringComparison.Ordinal);
            // Альфа несёт данные: у следа — маску, у земли — микротени впадин, у MetallicSmoothness — гладкость.
            bool ground = n.StartsWith("T1_", StringComparison.Ordinal) || n.StartsWith("T3_", StringComparison.Ordinal) || n.StartsWith("T5_", StringComparison.Ordinal);
            bool packed = n.EndsWith("_MetallicSmoothness", StringComparison.Ordinal);
            t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            t.sRGBTexture = albedo;
            t.alphaSource = (albedo && (footprint || ground)) || packed ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            t.alphaIsTransparency = albedo && footprint;
            t.wrapMode = footprint ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            t.mipmapEnabled = true;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.maxTextureSize = n.StartsWith("T1_") || n.StartsWith("T2_") || n.StartsWith("T3_") ? 2048 : 1024;
            foreach (string platform in new[] { "Android", "iPhone" })
            {
                var p = t.GetPlatformTextureSettings(platform);
                p.name = platform; p.overridden = true; p.maxTextureSize = 1024;
                p.format = TextureImporterFormat.ASTC_6x6;
                t.SetPlatformTextureSettings(p);
            }
        }
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root + "Audio/", StringComparison.Ordinal)) return;
            ConfigureAudio((AudioImporter)assetImporter);
        }
        internal static void ConfigureAudio(AudioImporter a)
        {
            // Steps are short one-shots played constantly; wind loops are long and always playing.
            bool step = Path.GetFileName(a.assetPath).StartsWith("Step_", StringComparison.Ordinal);
            a.forceToMono = step;
            a.loadInBackground = !step;
            var s = a.defaultSampleSettings;
            s.loadType = step ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = step ? 0.7f : 0.5f;
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            a.defaultSampleSettings = s;
        }
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root + "Models/", StringComparison.Ordinal)) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importCameras = false; m.importLights = false;
            m.globalScale = 1; m.useFileScale = true;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.isReadable = true;
            bool traveler = Path.GetFileName(m.assetPath).StartsWith("M3_");
            m.importAnimation = traveler;
            m.animationType = traveler ? ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
            if (traveler)
            {
                m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                var h = m.humanDescription;
                h.human = HumanTrait.BoneName.Where(x => !x.Contains("Thumb") && !x.Contains("Index") && !x.Contains("Middle") && !x.Contains("Ring") && !x.Contains("Little") && !x.Contains("Eye") && x != "Jaw" && x != "UpperChest")
                    .Select(x => new HumanBone { humanName = x, boneName = x.Replace(" ", ""), limit = new HumanLimit { useDefaultValues = true } }).ToArray();
                h.upperArmTwist = 0.5f; h.lowerArmTwist = 0.5f; h.upperLegTwist = 0.5f; h.lowerLegTwist = 0.5f; h.armStretch = 0.05f; h.legStretch = 0.05f; h.feetSpacing = 0;
                m.humanDescription = h;
            }
        }
        // Клипы путника зациклены и стоят на месте: шаг двигает игра, а не корень анимации.
        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root + "Models/", StringComparison.Ordinal)) return;
            if (!Path.GetFileName(assetPath).StartsWith("M3_", StringComparison.Ordinal)) return;
            var m = (ModelImporter)assetImporter;
            var clips = m.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true; clip.loopPose = true;
                clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionY = true; clip.keepOriginalPositionXZ = true;
            }
            m.clipAnimations = clips;
        }
    }
}
