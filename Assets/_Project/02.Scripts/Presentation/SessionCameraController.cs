using System;
using UnityEngine;

/// <summary>
/// 세션 카메라의 대상 추적과 맵 배치 이후 즉시 보정
/// </summary>
[DisallowMultipleComponent]
public class SessionCameraController : MonoBehaviour
{
    [Header("----- 카메라 연결 -----")]
    [SerializeField] Camera _camera;
    [SerializeField] Vector3 _offset = new Vector3(0f, 12f, -8f);
    [SerializeField, Min(0f)] float _followSpeed = 12f;

    Transform _target;

    void LateUpdate()
    {
        if (_target == null) return;
        float weight = 1f - Mathf.Exp(-_followSpeed * Time.deltaTime);
        _camera.transform.position = Vector3.Lerp(
            _camera.transform.position, _target.position + _offset, weight);
    }

    /// <summary>
    /// 준비된 플레이어의 카메라 추적 연결
    /// </summary>
    /// <param name="target">추적할 플레이어 Transform</param>
    public void Bind(Transform target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (_camera == null)
            throw new MissingReferenceException($"{name}: 세션 Camera 연결 필요");
        if (_target != null || !isActiveAndEnabled || !_camera.isActiveAndEnabled ||
            target.gameObject.scene != gameObject.scene || _camera.gameObject.scene != gameObject.scene ||
            float.IsNaN(_followSpeed) || float.IsInfinity(_followSpeed) || _followSpeed <= 0f)
            throw new InvalidOperationException("같은 씬의 활성 카메라와 미연결 추적 담당 및 양수 추적 속도 필요");

        _target = target;
        Snap();
    }

    /// <summary>
    /// 현재 플레이어 위치로 추적 지연 없이 카메라 보정
    /// </summary>
    public void Snap()
    {
        if (_target == null)
            throw new InvalidOperationException("카메라 추적 대상 연결 필요");
        _camera.transform.position = _target.position + _offset;
    }

    /// <summary>
    /// 세션 카메라 추적 해제
    /// </summary>
    public void Unbind()
    {
        _target = null;
    }
}
