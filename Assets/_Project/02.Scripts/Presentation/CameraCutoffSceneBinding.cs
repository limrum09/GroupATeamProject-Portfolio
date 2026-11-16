using System;
using UnityEngine;

/// <summary>
/// 독립 테스트 씬의 Cutoff 카메라와 대상 연결 및 해제
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CameraCutoffController))]
public class CameraCutoffSceneBinding : MonoBehaviour
{
    [Header("----- 씬 연결 -----")]
    [SerializeField] CameraCutoffController _controller;
    [SerializeField] Camera _camera;
    [SerializeField] Transform _target;

    bool _ownsBinding;

    void OnEnable()
    {
        if (_controller == null || _camera == null || _target == null)
        {
            throw new MissingReferenceException(
                $"{name}: Cutoff Controller, Camera, Target 연결 필요");
        }

        if (_controller.gameObject != gameObject)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 오브젝트의 CameraCutoffController 연결 필요");
        }

        _controller.Bind(_camera, _target);
        _ownsBinding = true;
    }

    void OnDisable()
    {
        if (!_ownsBinding) return;
        _ownsBinding = false;

        // 씬 정리 중 먼저 파괴된 Controller 제외
        if (_controller != null)
            _controller.Unbind();
    }
}
