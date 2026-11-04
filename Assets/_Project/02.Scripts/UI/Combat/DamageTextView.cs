using System;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 피해 숫자의 내용과 확대 및 상승과 소멸 표현
/// </summary>
[DisallowMultipleComponent]
public class DamageTextView : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] RectTransform _rect;
    [SerializeField] CanvasGroup _group;
    [SerializeField] TMP_Text _text;

    [Header("----- 출력 시간 -----")]
    [SerializeField, Min(0.01f)] float _duration = 0.8f;
    [SerializeField, Min(0.01f)] float _popDuration = 0.12f;
    [SerializeField, Range(0f, 0.99f)] float _fadeStart = 0.55f;

    [Header("----- 이동 -----")]
    [SerializeField, Min(0f)] float _riseDistance = 70f;
    [SerializeField, Min(0f)] float _horizontalDistance = 24f;
    [SerializeField] Ease _movementEase = Ease.OutQuint;

    [Header("----- 크기 -----")]
    [SerializeField, Min(1f)] float _criticalScale = 1.3f;

    float _elapsed;
    float _baseScale;
    float _opacity;
    Vector2 _offset;
    Tween _movementTween;

    void Awake()
    {
        ValidateConfiguration();
    }

    void OnDisable()
    {
        StopMovement();
    }

    void OnDestroy()
    {
        StopMovement();
    }

    void StopMovement()
    {
        if (_movementTween == null) return;

        _movementTween.Kill();
        _movementTween = null;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 화면 참조와 비상호작용 및 애니메이션 설정 검사
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_rect == null || _group == null || _text == null)
            throw new MissingReferenceException($"{name}: RectTransform, CanvasGroup과 TMP Text 연결 필요");

        if (_rect != transform || _group.gameObject != gameObject ||
            !_text.transform.IsChildOf(transform) || _text.font == null ||
            _rect.anchorMin != Vector2.one * 0.5f || _rect.anchorMax != Vector2.one * 0.5f ||
            _rect.pivot != Vector2.one * 0.5f || _group.interactable ||
            _group.blocksRaycasts || _text.raycastTarget)
        {
            throw new InvalidOperationException(
                $"{name}: 내부 화면 참조, TMP Font, 중앙 Anchor와 Pivot 및 비상호작용 설정 필요");
        }

        if (!IsFinite(_duration) || _duration <= 0f ||
            !IsFinite(_popDuration) || _popDuration <= 0f || _popDuration > _duration ||
            !IsFinite(_fadeStart) || _fadeStart < 0f || _fadeStart >= 1f ||
            !IsFinite(_riseDistance) || _riseDistance < 0f ||
            !IsFinite(_horizontalDistance) || _horizontalDistance < 0f ||
            !IsFinite(_criticalScale) || _criticalScale < 1f)
        {
            throw new InvalidOperationException($"{name}: 유효한 출력 시간, 이동 거리와 크기 설정 필요");
        }
    }

    /// <summary>
    /// 새 피해 숫자의 내용과 출력 상태 초기화 및 방향별 이동 시작
    /// </summary>
    /// <param name="damage">실제 감소한 체력</param>
    /// <param name="isCritical">치명타 여부</param>
    /// <param name="color">숫자 표시 색상</param>
    /// <param name="horizontalDirection">왼쪽 -1부터 오른쪽 1까지의 수평 이동 비율</param>
    public void Begin(float damage, bool isCritical, Color color, float horizontalDirection)
    {
        StopMovement();

        _elapsed = 0f;
        _offset = Vector2.zero;
        _opacity = 1f;
        _baseScale = isCritical ? _criticalScale : 1f;

        // 소수 피해의 0 표시 방지를 위한 정수 올림
        _text.text = Mathf.Ceil(damage).ToString("0", CultureInfo.InvariantCulture);
        _text.color = color;
        _group.alpha = 1f;
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale = Vector3.one * (_baseScale * 0.7f);

        Vector2 destination = new Vector2(_horizontalDistance * horizontalDirection, _riseDistance);

        // 카메라 투영 좌표와 독립적인 애니메이션 변위
        _movementTween = DOTween.To(
                () => _offset,
                value => _offset = value,
                destination,
                _duration)
            .SetEase(_movementEase)
            .SetUpdate(UpdateType.Normal, false)
            .OnKill(() => _movementTween = null);
    }

    /// <summary>
    /// 경과 시간에 따른 확대와 투명도 갱신
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    /// <returns>출력 종료 여부</returns>
    public bool Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        float progress = Mathf.Clamp01(_elapsed / _duration);
        float pop = Mathf.Clamp01(_elapsed / _popDuration);
        float scale = pop < 0.5f
            ? Mathf.Lerp(0.7f, 1.2f, pop * 2f)
            : Mathf.Lerp(1.2f, 1f, (pop - 0.5f) * 2f);

        _rect.localScale = Vector3.one * (_baseScale * scale);

        _opacity = 1f - Mathf.InverseLerp(_fadeStart, 1f, progress);
        return progress >= 1f;
    }

    /// <summary>
    /// 피격 위치에 애니메이션 변위와 가시성 반영
    /// </summary>
    /// <param name="position">부모 기준 피격 위치</param>
    /// <param name="visible">카메라 내 표시 여부</param>
    public void SetPresentation(Vector2 position, bool visible)
    {
        _rect.anchoredPosition = position + _offset;
        _group.alpha = visible ? _opacity : 0f;
    }
}
