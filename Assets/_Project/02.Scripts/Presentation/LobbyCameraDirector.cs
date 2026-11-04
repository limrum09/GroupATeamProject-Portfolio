using System;
using UnityEngine;

/// <summary>
/// 로비 카메라의 기본 구도와 캐릭터 선택 구도 사이 이동
/// </summary>
[DisallowMultipleComponent]
public class LobbyCameraDirector : MonoBehaviour
{
    [Header("----- 카메라와 위치 기준 -----")]
    [SerializeField] Transform _cameraTransform;
    [SerializeField] Transform _mainViewPos;
    [SerializeField] Transform _characterSelectViewPos;
    [Header("----- 스토리 카메라 위치 -----")]
    [SerializeField] Transform _storyViewPos;

    [Header("----- 이동 시간 -----")]
    [SerializeField, Min(0f)] float _moveDuration = 0.6f;

    Vector3 _startPosition;
    Quaternion _startRotation;
    Vector3 _targetPosition;
    Quaternion _targetRotation;

    float _elapsed;
    float _duration;
    bool _isMoving;

    public event Action OnMoveCompleted;

    void Awake()
    {
        if (_storyViewPos == null)
            throw new MissingReferenceException($"{name}: 스토리 카메라 위치 연결 필요");

        if (_storyViewPos.IsChildOf(_cameraTransform))
            throw new InvalidOperationException($"{name}: 스토리 카메라 위치는 카메라 계층 밖에 배치 필요");

        if (_cameraTransform == null || _mainViewPos == null || _characterSelectViewPos == null)
            throw new MissingReferenceException($"{name}: 카메라와 두 위치 기준 연결 필요");

        if (_mainViewPos == _characterSelectViewPos || _mainViewPos.IsChildOf(_cameraTransform) || _characterSelectViewPos.IsChildOf(_cameraTransform))
            throw new InvalidOperationException($"{name}: 서로 다른 위치 기준을 카메라 계층 밖에 배치 필요");

        if (float.IsNaN(_moveDuration) || float.IsInfinity(_moveDuration) || _moveDuration < 0f)
            throw new InvalidOperationException($"{name}: 이동 시간은 유한한 0 이상의 값 필요");
    }

    void Update()
    {
        if (!_isMoving) return;

        _elapsed += Time.unscaledDeltaTime;

        float progress = Mathf.Clamp01(_elapsed / _duration);
        float weight = Mathf.SmoothStep(0f, 1f, progress);

        _cameraTransform.SetPositionAndRotation(Vector3.Lerp(_startPosition, _targetPosition, weight), Quaternion.Slerp(_startRotation, _targetRotation, weight));

        if (progress < 1f) return;

        _cameraTransform.SetPositionAndRotation(_targetPosition, _targetRotation);

        _isMoving = false;
        OnMoveCompleted?.Invoke();
    }

    void OnDisable()
    {
        CancelMove();
    }

    public void MoveTo(Transform target)
    {
        if (!isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 상태에서 카메라 이동 요청 필요");

        _isMoving = false;
        _startPosition = _cameraTransform.position;
        _startRotation = _cameraTransform.rotation;
        _targetPosition = target.position;
        _targetRotation = target.rotation;
        _elapsed = 0f;
        _duration = _moveDuration;

        if (_duration == 0f)
        {
            _cameraTransform.SetPositionAndRotation(_targetPosition, _targetRotation);

            OnMoveCompleted?.Invoke();
            return;
        }

        _isMoving = true;
    }

    /// <summary>
    /// 완료 알림 없이 카메라를 기본 구도로 즉시 배치
    /// </summary>
    public void SnapToMainView()
    {
        CancelMove();

        _cameraTransform.SetPositionAndRotation(_mainViewPos.position, _mainViewPos.rotation);
    }

    /// <summary>
    /// 스토리 화면 위치로 카메라 이동 시작
    /// </summary>
    public void MoveToStoryView()
    {
        MoveTo(_storyViewPos);
    }

    /// <summary>
    /// 캐릭터 선택 구도로 이동 시작
    /// </summary>
    public void MoveToCharacterView()
    {
        MoveTo(_characterSelectViewPos);
    }

    /// <summary>
    /// 기본 로비 구도로 이동 시작
    /// </summary>
    public void MoveToMainView()
    {
        MoveTo(_mainViewPos);
    }

    /// <summary>
    /// 현재 구도를 유지하며 완료 알림 없이 이동 취소
    /// </summary>
    public void CancelMove()
    {
        _isMoving = false;
    }
}