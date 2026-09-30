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
    /// Глаз поверх готовой картинки: веки, ресницы, свет сквозь веки. Проход добавляется из кода,
    /// в ассет рендера ничего не прописывается; пока веки не видны, прохода нет вовсе.
    /// </summary>
    public sealed class EyeRender : IDisposable
    {
        const float RestTop = 1.1f, RestBottom = -0.1f;
        /// <summary>Нижнее веко ходит меньше верхнего: доля щели, которую оно может закрыть.</summary>
        const float LowerTravel = 0.875f;

        static readonly int LidsId = Shader.PropertyToID("_Lids");
        static readonly int LookId = Shader.PropertyToID("_LidLook");
        static readonly int GlowId = Shader.PropertyToID("_GlowColor");

        readonly Camera _camera;
        readonly Material _material;
        readonly EyePass _pass;
        bool _active;

        public EyeRender(Camera camera, Shader shader)
        {
            _camera = camera;
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("Hodba: нет шейдера Hidden/Hodba/Eye — веки не видны. Запусти Hodba ▸ Setup Field.");
                return;
            }
            _material = CoreUtils.CreateEngineMaterial(shader);
            _material.SetColor(GlowId, new Color(1f, 0.32f, 0.12f));
            _pass = new EyePass(_material);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <param name="sunStimulus">0..1 — насколько солнце бьёт в глаз: лучи ресниц в щели прищура.</param>
        public void Apply(in EyelidState lids, float sunStimulus)
        {
            if (_material == null) return;
            _active = !lids.FullyOpen;
            if (!_active) return;

            float lower = RestBottom + lids.Lower * LowerTravel;
            float upper = RestTop - lids.Upper * (RestTop - lower);
            _material.SetVector(LidsId, new Vector4(upper, lower, 0.12f * lids.Upper, 0.06f * lids.Lower));
            _material.SetVector(LookId, new Vector4(
                0.07f + 0.05f * lids.Upper,
                0.35f * lids.Upper,
                lids.Squint * sunStimulus * 0.6f,
                lids.Glow * 0.5f));
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_active || camera != _camera) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            CoreUtils.Destroy(_material);
        }

        sealed class EyePass : ScriptableRenderPass
        {
            readonly Material _material;

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

                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "Hodba Eye";
                desc.clearBuffer = false;
                var target = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, target, _material, 0), "Hodba Eye");
                resources.cameraColor = target;
            }
        }
    }
}
