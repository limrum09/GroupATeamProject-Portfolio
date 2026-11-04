using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 같은 오브젝트의 Slider 표시값을 지정 시간 동안 부드럽게 변경
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Slider))]
public class SliderValueAnimator : MonoBehaviour
{
    [Header("----- 표시 대상 -----")]
    [SerializeField] Slider _slider;

    [Header("----- 전환 시간 -----")]
    [SerializeField, Min(0f)] float _duration = 0.5f;

    float _startValue;
    float _targetValue;
    float _elapsed;

    bool _isAnimating;

    void Reset()
    {
        // 에디터에서 컴포넌트 추가 시 같은 오브젝트의 Slider 연결
        _slider = GetComponent<Slider>();
    }

    void Awake()
    {
        ValidateBindings();
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

        _slider.SetValueWithoutNotify(value);
    }

    void OnDisable()
    {
        if (!_isAnimating)
            return;

        _isAnimating = false;

        // 화면 종료 시 마지막 목표로 확정, 파괴 중인 Slider는 제외
        if (_slider != null)
            _slider.SetValueWithoutNotify(_targetValue);
    }

    void ValidateDuration()
    {
        if (float.IsNaN(_duration) || float.IsInfinity(_duration) || _duration < 0f)
            throw new InvalidOperationException($"{name}: 전환 시간은 유한한 0 이상의 값 필요");
    }

    float NormalizeValue(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "유한한 Slider 값 필요");

        float normalized = Mathf.Clamp(value, _slider.minValue, _slider.maxValue);

        return _slider.wholeNumbers ? Mathf.Round(normalized) : normalized;
    }

    /// <summary>
    /// 같은 오브젝트의 Slider 연결과 전환 시간 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_slider == null)
            throw new MissingReferenceException($"{name}: Slider 연결 필요");

        if (_slider.gameObject != gameObject)
            throw new InvalidOperationException($"{name}: 같은 오브젝트의 Slider 연결 필요");

        ValidateDuration();
    }

    /// <summary>
    /// 현재 표시값에서 새 목표값으로 부드러운 전환 시작
    /// 비활성 상태 또는 전환 시간 0에서는 즉시 반영
    /// </summary>
    /// <param name="value">Slider 범위로 보정할 목표값</param>
    public void SetTarget(float value)
    {
        float target = NormalizeValue(value);

        if (!isActiveAndEnabled || _duration == 0f)
        {
            SetImmediate(target);
            return;
        }

        // 같은 목표의 반복 요청으로 전환 시간이 연장되는 상황 방지
        if (_isAnimating && _targetValue == target)
            return;

        if (_slider.value == target)
        {
            SetImmediate(target);
            return;
        }

        // 전환 도중 새 요청도 현재 보이는 위치에서 시작
        _startValue = _slider.value;
        _targetValue = target;
        _elapsed = 0f;
        _isAnimating = true;
    }

    /// <summary>
    /// 진행 중인 전환을 종료하고 표시값을 즉시 반영
    /// 값 변경 이벤트는 발생하지 않음
    /// </summary>
    /// <param name="value">Slider 범위로 보정할 표시값</param>
    public void SetImmediate(float value)
    {
        float target = NormalizeValue(value);

        _isAnimating = false;
        _startValue = target;
        _targetValue = target;
        _elapsed = 0f;

        _slider.SetValueWithoutNotify(target);
    }
}