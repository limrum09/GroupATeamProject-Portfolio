using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 게임 카메라의 캐릭터 가림 제거용 셰이더 입력 관리
/// </summary>
[DisallowMultipleComponent]
public class CameraCutoffController : MonoBehaviour
{
    [Header("----- 마스크 설정 -----")]
    [SerializeField, Min(0.001f)] float _radius = 0.12f;
    [SerializeField] float _targetHeight = 1f;
    [SerializeField, Min(0f)] float _depthBias = 0.1f;

    static readonly int EnabledId = Shader.PropertyToID("_CameraCutoffEnabled");
    static readonly int TargetPositionId = Shader.PropertyToID("_CameraCutoffTargetWS");
    static readonly int ParametersId = Shader.PropertyToID("_CameraCutoffParameters");

    Camera _camera;
    Transform _target;

    public bool IsBound => _camera != null;

    void OnEnable()
    {
        Shader.SetGlobalFloat(EnabledId, 0f);

        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;

        Unbind();
    }

    void HandleBeginCameraRendering(
        ScriptableRenderContext context, Camera renderingCamera)
    {
        // 다른 카메라로의 마스크 상태 전파 방지
        Shader.SetGlobalFloat(EnabledId, 0f);

        if (renderingCamera != _camera) return;

        if (_target == null)
        {
            Unbind();
            return;
        }

        Vector3 center = _target.position + Vector3.up * _targetHeight;
        Vector3 viewportPosition = _camera.WorldToViewportPoint(center);

        // 카메라 뒤쪽이나 근거리 절단면 안쪽 대상 제외
        if (viewportPosition.z <= _camera.nearClipPlane) return;

        Shader.SetGlobalVector(TargetPositionId, new Vector4(center.x, center.y, center.z, 1f));
        // 반지름, 깊이 여유, 화면 비율
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

    /// <summary>
    /// 같은 씬의 게임 카메라와 준비된 캐릭터 연결
    /// </summary>
    /// <param name="camera">Cutoff를 적용할 게임 카메라</param>
    /// <param name="target">마스크 중심의 기준 Transform</param>
    public void Bind(Camera camera, Transform target)
    {
        if (camera == null)
            throw new ArgumentNullException(nameof(camera));

        if (target == null)
            throw new ArgumentNullException(nameof(target));

        if (IsBound || !isActiveAndEnabled || !camera.isActiveAndEnabled)
            throw new InvalidOperationException(
                "활성 카메라와 미연결 Cutoff 컴포넌트 필요");

        if (camera.gameObject.scene != gameObject.scene || target.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬의 카메라와 캐릭터 연결 필요");

        if (!IsFinite(_radius) || _radius <= 0f || !IsFinite(_targetHeight) || !IsFinite(_depthBias) || _depthBias < 0f)
            throw new InvalidOperationException("유한한 마스크 설정과 양수 반지름 및 0 이상의 깊이 여유 필요");

        _camera = camera;
        _target = target;
    }

    /// <summary>
    /// 대상 연결 해제와 마스크 비활성화
    /// </summary>
    public void Unbind()
    {
        _camera = null;
        _target = null;

        Shader.SetGlobalFloat(EnabledId, 0f);
    }
}