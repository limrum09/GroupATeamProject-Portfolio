using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Forward 계열 렌더러의 정적 불투명 장애물 깊이 마스크와 원본 표면 렌더링 구성
/// </summary>
public class CameraCutoffRendererFeature : ScriptableRendererFeature
{
    [Header("----- 렌더러 연결 -----")]
    [SerializeField] UniversalRendererData _rendererData;
    [SerializeField] Material _depthMaterial;
    [SerializeField] LayerMask _obstacleLayers;

    CutoffPass _normalsPass;
    CutoffPass _colorPass;

    void ValidateConfiguration()
    {
        if (_rendererData == null || _depthMaterial == null)
            throw new MissingReferenceException($"{name}: Renderer Data와 깊이 머티리얼 연결 필요");

        if (_rendererData.renderingMode != RenderingMode.Forward &&
            _rendererData.renderingMode != RenderingMode.ForwardPlus)
            throw new InvalidOperationException($"{name}: Forward 또는 Forward+ 렌더러 필요");

        if (_rendererData.depthPrimingMode != DepthPrimingMode.Disabled)
            throw new InvalidOperationException($"{name}: Depth Priming Disabled 설정 필요");

        if (_depthMaterial.shader == null ||
            _depthMaterial.shader.name != "Project/Presentation/CameraCutoffDepth")
            throw new InvalidOperationException($"{name}: CameraCutoffDepth 셰이더 사용 필요");

        if (_obstacleLayers.value == 0 ||
            (_rendererData.opaqueLayerMask.value & _obstacleLayers.value) != 0)
            throw new InvalidOperationException($"{name}: 대상 레이어 지정과 기본 Opaque Layer Mask에서 제외 필요");

        RenderGraphSettings settings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
        if (settings == null || settings.enableRenderCompatibilityMode)
            throw new InvalidOperationException($"{name}: Render Graph 활성화 필요");
    }

    /// <summary>
    /// SSAO 이전 노멀 보완과 기본 불투명 렌더링 이전 색상 패스 생성
    /// </summary>
    public override void Create()
    {
        _normalsPass = new CutoffPass
        {
            WriteNormals = true,
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses
        };
        _normalsPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);

        _colorPass = new CutoffPass
        {
            WriteNormals = false,
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
        };
    }

    /// <summary>
    /// Base 카메라에 장애물 노멀과 색상 렌더링 패스 등록
    /// </summary>
    /// <param name="renderer">현재 카메라의 렌더러</param>
    /// <param name="renderingData">현재 카메라 렌더링 데이터</param>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.camera.cameraType == CameraType.Preview ||
            renderingData.cameraData.renderType != CameraRenderType.Base)
            return;

        ValidateConfiguration();
        _normalsPass.DepthMaterial = _depthMaterial;
        _normalsPass.Layers = _obstacleLayers.value;
        _colorPass.DepthMaterial = _depthMaterial;
        _colorPass.Layers = _obstacleLayers.value;
        renderer.EnqueuePass(_normalsPass);
        renderer.EnqueuePass(_colorPass);
    }

    class CutoffPass : ScriptableRenderPass
    {
        public Material DepthMaterial;
        public int Layers;
        public bool WriteNormals;

        static readonly List<ShaderTagId> NormalTags = new List<ShaderTagId>
        {
            new ShaderTagId("DepthNormals"),
            new ShaderTagId("DepthNormalsOnly")
        };

        static readonly List<ShaderTagId> ColorTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        static readonly ShaderTagId[] StateTags = { new ShaderTagId() };
        static readonly RenderStateBlock[] SurfaceStates =
        {
            new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(false, CompareFunction.Equal)
            }
        };

        class PassData
        {
            public RendererListHandle RendererList;
        }

        RendererListHandle CreateRendererList(
            RenderGraph graph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, bool depthOnly)
        {
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                WriteNormals ? NormalTags : ColorTags, renderingData, cameraData,
                lightData, cameraData.defaultOpaqueSortFlags);
            FilteringSettings filtering = new FilteringSettings(RenderQueueRange.opaque, Layers);

            if (depthOnly)
            {
                drawing.overrideMaterial = DepthMaterial;
                drawing.overrideMaterialPassIndex = 0;
                return graph.CreateRendererList(new RendererListParams(
                    renderingData.cullResults, drawing, filtering));
            }

            // 원본 셰이더의 표면 계산을 유지하며 마스크가 허용한 깊이만 통과
            using (NativeArray<ShaderTagId> tags = new NativeArray<ShaderTagId>(StateTags, Allocator.Temp))
            using (NativeArray<RenderStateBlock> states = new NativeArray<RenderStateBlock>(SurfaceStates, Allocator.Temp))
            {
                RendererListParams parameters = new RendererListParams(renderingData.cullResults, drawing, filtering)
                {
                    tagValues = tags,
                    stateBlocks = states,
                    isPassTagName = false
                };
                return graph.CreateRendererList(parameters);
            }
        }

        static void Execute(PassData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.RendererList);
        }

        /// <summary>
        /// 해당 렌더 타깃의 마스크 깊이와 원본 노멀 또는 색상 기록
        /// </summary>
        /// <param name="renderGraph">현재 프레임 렌더 그래프</param>
        /// <param name="frameData">현재 카메라 프레임 데이터</param>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            // Depth Priming이 꺼진 Forward의 프리패스 깊이와 최종 카메라 깊이는 별도 타깃
            TextureHandle depth = WriteNormals ? resources.cameraDepthTexture : resources.activeDepthTexture;
            TextureHandle color = WriteNormals ? resources.cameraNormalsTexture : resources.activeColorTexture;
            if (!depth.IsValid() || !color.IsValid())
                throw new InvalidOperationException("Cutoff의 깊이와 표면 렌더 타깃 준비 필요");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                WriteNormals ? "Camera Cutoff Prepass Depth" : "Camera Cutoff Camera Depth", out var passData))
            {
                passData.RendererList = CreateRendererList(
                    renderGraph, renderingData, cameraData, lightData, true);
                builder.UseRendererList(passData.RendererList);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                builder.SetRenderFunc<PassData>(Execute);
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                WriteNormals ? "Camera Cutoff Original Normals" : "Camera Cutoff Original Color", out var passData))
            {
                passData.RendererList = CreateRendererList(
                    renderGraph, renderingData, cameraData, lightData, false);
                builder.UseRendererList(passData.RendererList);
                builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);

                if (!WriteNormals)
                {
                    if (resources.mainShadowsTexture.IsValid())
                        builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid())
                        builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    if (resources.ssaoTexture.IsValid())
                        builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);

                    TextureHandle[] dBuffer = resources.dBuffer;
                    for (int i = 0; i < dBuffer.Length; i++)
                    {
                        if (dBuffer[i].IsValid())
                            builder.UseTexture(dBuffer[i], AccessFlags.Read);
                    }
                }

                builder.SetRenderFunc<PassData>(Execute);
            }
        }
    }
}
