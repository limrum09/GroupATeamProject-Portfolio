using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 체력 Slider와 현재·최대 체력 표시
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealthView : MonoBehaviour
{
    [Header("----- 체력 화면 -----")]
    [SerializeField] Slider _healthSlider;
    [SerializeField] TextMeshProUGUI _healthText;

    void Awake()
    {
        ValidateReferences();
    }

    /// <summary>
    /// 필수 화면 참조와 표시 전용 Slider 설정 검사
    /// </summary>
    public void ValidateReferences()
    {
        if (_healthSlider == null || _healthText == null)
            throw new MissingReferenceException($"{name}: 체력 Slider와 체력 텍스트 연결 필요");

        if (_healthSlider.fillRect == null)
            throw new MissingReferenceException($"{name}: 체력 Slider의 Fill Rect 연결 필요");

        if (_healthSlider.wholeNumbers || _healthSlider.interactable || _healthSlider.navigation.mode != Navigation.Mode.None)
            throw new InvalidOperationException($"{name}:Interactable 해제, Navigation None 설정 필요");
    }

    /// <summary>
    /// 현재·최대 체력과 체력 비율 표시
    /// </summary>
    /// <param name="currentHealth">현재 체력</param>
    /// <param name="maxHealth">초기화된 전투 상태의 양수 최대 체력</param>
    public void Show(float currentHealth, float maxHealth)
    {
        float ratio = Mathf.Clamp01(currentHealth / maxHealth);

        float maxSlider = _healthSlider.maxValue;

        _healthSlider.SetValueWithoutNotify(ratio * maxSlider);

        // 양수의 소수 체력이 0으로 표시되는 경우 방지
        _healthText.SetText("{0} / {1}", Mathf.Ceil(currentHealth), Mathf.Ceil(maxHealth));
    }

    /// <summary>
    /// 연결 해제 시 체력 표시 정리
    /// </summary>
    public void Clear()
    {
        // 씬 정리 중 먼저 제거된 화면 요소는 제외
        if (_healthSlider != null)
            _healthSlider.SetValueWithoutNotify(0f);

        if (_healthText != null)
            _healthText.text = string.Empty;
    }
}