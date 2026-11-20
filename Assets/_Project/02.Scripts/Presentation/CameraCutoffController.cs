using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 지정한 CinemachineCamera의 추적 대상을 사용하는 단일 출력 카메라 Cutoff
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraCutoffController : MonoBehaviour
{
    [Header("----- 시네머신 연결 -----")]
    [SerializeField] CinemachineCamera _sourceCamera;

    [Header("----- 마스크 설정 -----")]
    [SerializeField, Min(0.001f)] float _radius = 0.12f;
    [SerializeField] float _targetHeight = 1f;
    [SerializeField, Min(0f)] float _depthBias = 0.1f;

    static readonly int EnabledId = Shader.PropertyToID("_CameraCutoffEnabled");
    static readonly int TargetPositionId = Shader.PropertyToID("_CameraCutoffTargetWS");
    static readonly int ParametersId = Shader.PropertyToID("_CameraCutoffParameters");

    Camera _camera;

    void OnEnable()
    {
        // 같은 오브젝트의 필수 Camera만 사용
        _camera = GetComponent<Camera>();
        ValidateConfiguration();
        Shader.SetGlobalFloat(EnabledId, 0f);

        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        Shader.SetGlobalFloat(EnabledId, 0f);
        _camera = null;
    }

    void ValidateConfiguration()
    {
        if (_camera == null || _sourceCamera == null)
            throw new MissingReferenceException($"{name}: 같은 오브젝트의 Camera와 Source Camera 연결 필요");

        if (!IsFinite(_radius) || _radius <= 0f ||
            !IsFinite(_targetHeight) || !IsFinite(_depthBias) || _depthBias < 0f)
        {
            throw new InvalidOperationException(
                $"{name}: 유한한 마스크 설정과 양수 반지름 및 0 이상의 깊이 여유 필요");
        }
    }

    void HandleBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        // 미니맵 등 다른 카메라로의 마스크 상태 전파 방지
        Shader.SetGlobalFloat(EnabledId, 0f);
        if (renderingCamera != _camera) return;

        // 런 준비 전 미지정 상태와 대상 교체를 매 렌더 시점에 반영
        Transform target = _sourceCamera.Follow;
        if (target == null || !target.gameObject.activeInHierarchy) return;

        Vector3 center = target.position + Vector3.up * _targetHeight;
        Vector3 viewportPosition = _camera.WorldToViewportPoint(center);
        if (viewportPosition.z <= _camera.nearClipPlane) return;

        Shader.SetGlobalVector(TargetPositionId, new Vector4(center.x, center.y, center.z, 1f));
        Shader.SetGlobalVector(ParametersId, new Vector4(_radius, _depthBias, _camera.aspect, 0f));
        Shader.SetGlobalFloat(EnabledId, 1f);
    }

    void HandleEndCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera == _camera)
            Shader.SetGlobalFloat(EnabledId, 0f);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
