using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 분할된 Image 막대의 순차 채움과 부드러운 표시값 전환
/// </summary>
[DisallowMultipleComponent]
public class SegmentedImageValueAnimator : MonoBehaviour
{
    [Header("----- 왼쪽부터 순서대로 연결 -----")]
    [SerializeField]
    Image[] _fillImages = new Image[3];

    [Header("----- 전환 시간 -----")]
    [SerializeField, Min(0f)] float _duration = 0.5f;

    float _currentValue;
    float _startValue;
    float _targetValue;
    float _elapsed;

    bool _isAnimating;

    void Awake()
    {
        ValidateBindings();

        // 최초 표시값 0, 비활성 상태에서 먼저 전달된 값은 유지
        ApplyValue(_currentValue);
    }

    void OnValidate()
    {
        ValidateDuration();
    }

    void Update()
    {
        if (!_isAnimating)
            return;

        _elapsed += Time.unscaledDeltaTime;

        float progress = _duration == 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);

        if (progress >= 1f)
        {
            SetImmediate(_targetValue);
            return;
        }

        float weight = Mathf.SmoothStep(0f, 1f, progress);
        float value = Mathf.Lerp(_startValue, _targetValue, weight);

        ApplyValue(value);
    }

    void OnDisable()
    {
        if (!_isAnimating)
            return;

        _isAnimating = false;

        // 화면 종료 시 목표값 확정, 씬 종료 중 파괴된 Image 제외
        ApplyValue(_targetValue, true);
    }

    void ValidateDuration()
    {
        if (float.IsNaN(_duration) || float.IsInfinity(_duration) || _duration < 0f)
            throw new InvalidOperationException($"{name}: 전환 시간은 유한한 0 이상의 값 필요");
    }

    float NormalizeValue(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "유한한 표시 비율 필요");

        return Mathf.Clamp01(value);
    }

    void ApplyValue(float value, bool skipDestroyedImages = false)
    {
        _currentValue = value;

        float filledSegments = _currentValue * _fillImages.Length;

        for (int i = 0; i < _fillImages.Length; i++)
        {
            Image fillImage = _fillImages[i];

            // 비활성화 정리 중 이미 파괴된 참조만 제외
            if (skipDestroyedImages && fillImage == null)
                continue;

            fillImage.fillAmount = Mathf.Clamp01(filledSegments - i);
        }
    }

    /// <summary>
    /// Fill Image 3개의 연결과 채움 설정 및 전환 시간 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_fillImages == null || _fillImages.Length != 3)
            throw new InvalidOperationException($"{name}: 왼쪽부터 순서대로 Fill Image 3개 연결 필요");

        ValidateDuration();
    }

    /// <summary>
    /// 현재 표시값에서 새 목표 비율까지 부드러운 전환 시작
    /// 비활성 상태 또는 전환 시간 0에서는 즉시 반영
    /// </summary>
    /// <param name="value">0부터 1 사이로 제한할 목표 비율</param>
    public void SetTarget(float value)
    {
        float target = NormalizeValue(value);

        if (!isActiveAndEnabled || _duration == 0f)
        {
            SetImmediate(target);
            return;
        }

        // 동일 목표의 반복 요청으로 전환 시간이 늘어나는 상황 방지
        if (_isAnimating && _targetValue == target)
            return;

        if (_currentValue == target)
        {
            SetImmediate(target);
            return;
        }

        // 전환 도중 변경된 목표도 현재 표시값에서 이어서 처리
        _startValue = _currentValue;
        _targetValue = target;
        _elapsed = 0f;
        _isAnimating = true;
    }

    /// <summary>
    /// 진행 중인 전환을 종료하고 표시 비율 즉시 반영
    /// </summary>
    /// <param name="value">0부터 1 사이로 제한할 표시 비율</param>
    public void SetImmediate(float value)
    {
        float target = NormalizeValue(value);

        _isAnimating = false;
        _startValue = target;
        _targetValue = target;
        _elapsed = 0f;

        ApplyValue(target);
    }
}