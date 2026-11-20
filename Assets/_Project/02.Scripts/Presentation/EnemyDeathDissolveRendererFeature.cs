using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 사망 모델의 원본 표면과 공용 효과 마스크를 카메라에 합성
/// </summary>
public class EnemyDeathDissolveRendererFeature : ScriptableRendererFeature
{
    [Header("----- 사망 연출 -----")]
    [SerializeField] Shader _shader;

    Material _material;
    DissolvePass _pass;

    /// <summary>
    /// 마스크와 합성 패스의 공용 머티리얼 생성
    /// </summary>
    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = _shader == null ? null : CoreUtils.CreateEngineMaterial(_shader);
        _pass = new DissolvePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox
        };
    }

    /// <summary>
    /// 사망 모델이 재생 중인 Base 카메라에 합성 패스 등록
    /// </summary>
    /// <param name="renderer">카메라의 현재 렌더러</param>
    /// <param name="renderingData">카메라 렌더링 데이터</param>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!EnemyDeathDissolve.HasActivePlaybacks || renderingData.cameraData.renderType != CameraRenderType.Base ||
            renderingData.cameraData.camera.cameraType == CameraType.Preview)
        {
            return;
        }

        int layer = LayerMask.NameToLayer(EnemyDeathDissolve.LayerName);
        if (layer < 0 || _material == null || _material.passCount != 2)
            throw new InvalidOperationException($"{name}: 사망 연출 레이어와 마스크·합성 셰이더 연결 필요");

        int layerMask = 1 << layer;
        if ((renderingData.cameraData.camera.cullingMask & layerMask) == 0) return;

        _pass.Configure(_material, layerMask);
        renderer.EnqueuePass(_pass);
    }

    /// <summary>
    /// 공용 사망 연출 머티리얼 해제
    /// </summary>
    /// <param name="disposing">렌더러 기능 해제 여부</param>
    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    class DissolvePass : ScriptableRenderPass
    {
        static readonly List<ShaderTagId> SurfaceTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        static readonly int ColorId = Shader.PropertyToID("_EnemyDeathColor");
        static readonly int DepthId = Shader.PropertyToID("_EnemyDeathDepth");
        static readonly int MaskId = Shader.PropertyToID("_EnemyDeathMask");

        Material _material;
        int _layerMask;

        class DrawData
        {
            public RendererListHandle Renderers;
        }

        class CompositeData
        {
            public Material Material;
            public TextureHandle Color;
            public TextureHandle Depth;
            public TextureHandle Mask;
        }

        RendererListHandle CreateList(RenderGraph graph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, bool mask)
        {
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                SurfaceTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            if (mask)
            {
                drawing.overrideMaterial = _material;
                drawing.overrideMaterialPassIndex = 0;
                drawing.enableInstancing = false;
                drawing.enableDynamicBatching = false;
            }

            FilteringSettings filtering = new FilteringSettings(RenderQueueRange.opaque, _layerMask);
            return graph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
        }

        static void UseLighting(IRasterRenderGraphBuilder builder, UniversalResourceData resources)
        {
            if (resources.mainShadowsTexture.IsValid())
                builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
            if (resources.additionalShadowsTexture.IsValid())
                builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
            if (resources.ssaoTexture.IsValid())
                builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);

            foreach (TextureHandle texture in resources.dBuffer)
            {
                if (texture.IsValid())
                    builder.UseTexture(texture, AccessFlags.Read);
            }
        }

        /// <summary>
        /// 공용 마스크 및 합성 머티리얼과 사망 연출 레이어 연결
        /// </summary>
        /// <param name="material">공용 사망 연출 머티리얼</param>
        /// <param name="layerMask">사망 모델 레이어 비트</param>
        public void Configure(Material material, int layerMask)
        {
            _material = material;
            _layerMask = layerMask;
        }

        /// <summary>
        /// 원본 표면 기록, 깊이 일치 마스크 기록, 카메라 합성 패스 등록
        /// </summary>
        /// <param name="renderGraph">현재 카메라의 렌더 그래프</param>
        /// <param name="frameData">현재 카메라의 프레임 리소스</param>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();

            TextureDesc desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            desc.discardBuffer = false;
            desc.name = "Enemy Death Original Color";
            TextureHandle originalColor = renderGraph.CreateTexture(desc);

            desc.name = "Enemy Death Original Depth";
            desc.depthBufferBits = DepthBits.Depth32;
            TextureHandle originalDepth = renderGraph.CreateTexture(desc);

            desc.name = "Enemy Death Effect Mask";
            desc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            TextureHandle mask = renderGraph.CreateTexture(desc);

            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<DrawData>("Enemy Death Original Surface", out DrawData data))
            {
                data.Renderers = CreateList(renderGraph, rendering, camera, lights, false);
                builder.UseRendererList(data.Renderers);
                builder.SetRenderAttachment(originalColor, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(originalDepth, AccessFlags.Write);
                UseLighting(builder, resources);
                builder.SetRenderFunc((DrawData pass, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(pass.Renderers));
            }

            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<DrawData>("Enemy Death Mask", out DrawData data))
            {
                data.Renderers = CreateList(renderGraph, rendering, camera, lights, true);
                builder.UseRendererList(data.Renderers);
                // 원본 알파 컷아웃을 통과해 기록된 표면 깊이에만 마스크 적용
                builder.SetRenderAttachmentDepth(originalDepth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderFunc((DrawData pass, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(pass.Renderers));
            }

            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<CompositeData>("Enemy Death Composite", out CompositeData data))
            {
                data.Material = _material;
                data.Color = originalColor;
                data.Depth = originalDepth;
                data.Mask = mask;
                builder.UseTexture(originalColor, AccessFlags.Read);
                builder.UseTexture(originalDepth, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((CompositeData pass, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(ColorId, pass.Color);
                    context.cmd.SetGlobalTexture(DepthId, pass.Depth, RenderTextureSubElement.Depth);
                    context.cmd.SetGlobalTexture(MaskId, pass.Mask);
                    context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 1, MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}
