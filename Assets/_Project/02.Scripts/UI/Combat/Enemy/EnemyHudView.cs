using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 적 체력과 피해 잔상의 화면 표현
/// </summary>
[DisallowMultipleComponent]
public class EnemyHudView : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] RectTransform _rect;
    [SerializeField] CanvasGroup _group;
    [SerializeField] Image _healthFill;
    [SerializeField] Image _damageFill;

    [Header("----- 피해 잔상 -----")]
    [SerializeField, Min(0f)] float _damageDelay = 0.2f;
    [SerializeField, Min(0.01f)] float _damageSpeed = 0.8f;

    float _delayRemaining;

    void Awake()
    {
        ValidateConfiguration();
    }

    /// <summary>
    /// 필수 화면 참조와 수평 채움 설정 검사
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_rect == null || _group == null || _healthFill == null || _damageFill == null)
            throw new MissingReferenceException($"{name}: HUD RectTransform, CanvasGroup, 체력 및 잔상 Image 연결 필요");

        if (_rect != transform || _group.gameObject != gameObject ||
            !_healthFill.transform.IsChildOf(transform) || !_damageFill.transform.IsChildOf(transform) ||
            _healthFill == _damageFill || _healthFill.sprite == null || _damageFill.sprite == null ||
            _healthFill.type != Image.Type.Filled || _damageFill.type != Image.Type.Filled ||
            _healthFill.fillMethod != Image.FillMethod.Horizontal ||
            _damageFill.fillMethod != Image.FillMethod.Horizontal ||
            _healthFill.fillOrigin != 0 || _damageFill.fillOrigin != 0 ||
            _rect.anchorMin != Vector2.one * 0.5f || _rect.anchorMax != Vector2.one * 0.5f ||
            _healthFill.raycastTarget || _damageFill.raycastTarget ||
            _group.interactable || _group.blocksRaycasts ||
            float.IsNaN(_damageDelay) || float.IsInfinity(_damageDelay) || _damageDelay < 0f ||
            float.IsNaN(_damageSpeed) || float.IsInfinity(_damageSpeed) || _damageSpeed <= 0f)
        {
            throw new InvalidOperationException($"{name}: HUD 내부 참조, 중앙 Anchor, 비상호작용 및 왼쪽 수평 채움 설정 필요");
        }
    }

    /// <summary>
    /// 대여 및 반환 시 체력과 잔상 초기화
    /// </summary>
    /// <param name="current">현재 체력</param>
    /// <param name="maximum">최대 체력</param>
    public void ResetHealth(float current, float maximum)
    {
        float ratio = Mathf.Clamp01(current / maximum);
        _healthFill.fillAmount = ratio;
        _damageFill.fillAmount = ratio;
        _delayRemaining = 0f;
    }

    /// <summary>
    /// 체력 변경과 피해 잔상 대기 반영
    /// </summary>
    /// <param name="current">현재 체력</param>
    /// <param name="maximum">최대 체력</param>
    public void SetHealth(float current, float maximum)
    {
        float ratio = Mathf.Clamp01(current / maximum);
        float previous = _healthFill.fillAmount;
        _healthFill.fillAmount = ratio;

        if (ratio < previous)
        {
            _delayRemaining = _damageDelay;
        }
        else if (ratio > previous)
        {
            _damageFill.fillAmount = ratio;
            _delayRemaining = 0f;
        }
    }

    /// <summary>
    /// 피해 잔상 시간 진행
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    public void Tick(float deltaTime)
    {
        float remaining = Mathf.Max(0f, deltaTime - _delayRemaining);
        _delayRemaining = Mathf.Max(0f, _delayRemaining - deltaTime);
        _damageFill.fillAmount = Mathf.MoveTowards(
            _damageFill.fillAmount, _healthFill.fillAmount, _damageSpeed * remaining);
    }

    /// <summary>
    /// 화면 위치와 가시성 반영
    /// </summary>
    /// <param name="position">부모 기준 UI 좌표</param>
    /// <param name="visible">표시 여부</param>
    public void SetPresentation(Vector2 position, bool visible)
    {
        _rect.anchoredPosition = position;
        _group.alpha = visible ? 1f : 0f;
    }
}
