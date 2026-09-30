using System;
using Hodba.Client.Body;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Hodba.Client
{
    /// <summary>
    /// Глаз поверх готовой картинки: периферия, веки, ресницы, свет сквозь веки. Проход добавляется из кода,
    /// в ассет рендера ничего не прописывается. Если ни периферии, ни век не видно — прохода нет вовсе.
    /// Периферия: картинка уменьшается вчетверо, размывается в два прохода и подмешивается к краям.
    /// </summary>
    public sealed class EyeRender : IDisposable
    {
        const float RestTop = 1.1f, RestBottom = -0.1f;
        /// <summary>Нижнее веко ходит меньше верхнего: доля щели, которую оно может закрыть.</summary>
        const float LowerTravel = 0.875f;

        static readonly int LidsId = Shader.PropertyToID("_Lids");
        static readonly int LookId = Shader.PropertyToID("_LidLook");
        static readonly int GlowId = Shader.PropertyToID("_GlowColor");
        static readonly int PeripheryId = Shader.PropertyToID("_Periphery");
        static readonly int BlurStepId = Shader.PropertyToID("_BlurStep");
        static readonly int BlurTexId = Shader.PropertyToID("_PeripheryBlur");

        readonly Camera _camera;
        readonly Material _material;
        readonly EyePass _pass;
        bool _active;

        /// <summary>Проход глаза действительно отработал в последних кадрах — для отладки.</summary>
        public bool Working => _pass != null && Time.frameCount - _pass.RecordedFrame <= 2;

        public EyeRender(Camera camera, Shader shader)
        {
            _camera = camera;
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("Hodba: нет шейдера Hidden/Hodba/Eye — глаз не виден. Запусти Hodba ▸ Setup Field.");
                return;
            }
            _material = CoreUtils.CreateEngineMaterial(shader);
            _material.SetColor(GlowId, new Color(1f, 0.32f, 0.12f));
            _pass = new EyePass(_material);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <param name="sunStimulus">0..1 — насколько солнце бьёт в глаз: лучи ресниц в щели прищура.</param>
        public void Apply(in EyelidState lids, float sunStimulus, in PeripheryState periphery)
        {
            if (_material == null) return;
            _active = !lids.FullyOpen || periphery.Visible;
            if (!_active) return;

            float lower = RestBottom + lids.Lower * LowerTravel;
            float upper = RestTop - lids.Upper * (RestTop - lower);
            _material.SetVector(LidsId, new Vector4(upper, lower, 0.12f * lids.Upper, 0.06f * lids.Lower));
            _material.SetVector(LookId, new Vector4(
                0.07f + 0.05f * lids.Upper,
                0.35f * lids.Upper,
                lids.Squint * sunStimulus * 0.6f,
                lids.Glow * 0.5f));
            _material.SetVector(PeripheryId, new Vector4(periphery.Start, periphery.Blur, periphery.Desaturate, periphery.Darken));
            _pass.BlurRadius = periphery.Radius;
            _pass.Periphery = periphery.Visible;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_active || camera != _camera) return;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            CoreUtils.Destroy(_material);
        }

        sealed class EyePass : ScriptableRenderPass
        {
            sealed class ComposeData
            {
                public TextureHandle Source, Blur;
                public Material Material;
                public bool HasBlur;
            }

            readonly Material _material;
            public float BlurRadius = 1.5f;
            public bool Periphery;
            public int RecordedFrame = -100;

            public EyePass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                RecordedFrame = Time.frameCount;

                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);

                TextureHandle blur = TextureHandle.nullHandle;
                if (Periphery)
                {
                    // Вдвое и ещё вдвое: билинейное уменьшение по шагу не мерцает так, как сразу вчетверо.
                    var half = Small(desc, 2, "Hodba Periphery 1/2");
                    var a = Small(desc, 4, "Hodba Periphery A");
                    var b = Small(desc, 4, "Hodba Periphery B");
                    var h = renderGraph.CreateTexture(half);
                    var ta = renderGraph.CreateTexture(a);
                    var tb = renderGraph.CreateTexture(b);
                    renderGraph.AddBlitPass(source, h, Vector2.one, Vector2.zero, passName: "Hodba Periphery Down 1/2");
                    renderGraph.AddBlitPass(h, ta, Vector2.one, Vector2.zero, passName: "Hodba Periphery Down 1/4");
                    _material.SetVector(BlurStepId, new Vector4(BlurRadius / a.width, BlurRadius / a.height, 0f, 0f));
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(ta, tb, _material, 1), "Hodba Periphery Blur H");
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(tb, ta, _material, 2), "Hodba Periphery Blur V");
                    blur = ta;
                }

                desc.name = "Hodba Eye";
                desc.clearBuffer = false;
                var target = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<ComposeData>("Hodba Eye", out var data))
                {
                    data.Source = source;
                    data.Blur = blur;
                    data.HasBlur = blur.IsValid();
                    data.Material = _material;
                    builder.UseTexture(source);
                    if (data.HasBlur) builder.UseTexture(blur);
                    builder.SetRenderAttachment(target, 0);
                    builder.SetRenderFunc((ComposeData d, RasterGraphContext ctx) =>
                    {
                        if (d.HasBlur) d.Material.SetTexture(BlurTexId, d.Blur);
                        Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0);
                    });
                }
                resources.cameraColor = target;
            }

            static TextureDesc Small(TextureDesc full, int divisor, string name)
            {
                var d = full;
                d.sizeMode = TextureSizeMode.Explicit;
                d.width = Mathf.Max(1, full.width / divisor);
                d.height = Mathf.Max(1, full.height / divisor);
                d.name = name;
                d.clearBuffer = false;
                d.msaaSamples = MSAASamples.None;
                return d;
            }
        }
    }
}
