using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 프로필의 아이콘과 레벨 및 경험치 표시
/// </summary>
[DisallowMultipleComponent]
public class PlayerProfileView : MonoBehaviour
{
    [Header("----- 프로필 화면 -----")]
    [SerializeField] Image _playerImage;
    [SerializeField] TextMeshProUGUI _levelText;
    [SerializeField] Image _experienceGauge;

    void Awake()
    {
        ValidateReferences();
    }

    /// <summary>
    /// 필수 화면 참조와 경험치 채움 설정 검사
    /// </summary>
    public void ValidateReferences()
    {
        if (_playerImage == null || _levelText == null || _experienceGauge == null)
            throw new MissingReferenceException($"{name}: PlayerImage, 레벨 텍스트, 경험치 Gauge 연결 필요");

        if (_experienceGauge.type != Image.Type.Filled || _experienceGauge.sprite == null)
            throw new InvalidOperationException($"{name}: 경험치 Gauge의 Sprite와 Filled 설정 필요");
    }

    /// <summary>
    /// 현재 프로필 정보 표시
    /// </summary>
    /// <param name="icon">캐릭터 프로필 아이콘</param>
    /// <param name="level">현재 런 레벨</param>
    /// <param name="experienceRatio">현재 레벨의 경험치 비율</param>
    public void Show(Sprite icon, int level, float experienceRatio)
    {
        _playerImage.sprite = icon;
        _playerImage.enabled = true;

        _levelText.text = level.ToString();
        _experienceGauge.fillAmount = Mathf.Clamp01(experienceRatio);
    }

    /// <summary>
    /// 연결 해제 시 프로필 표시 정리
    /// </summary>
    public void Clear()
    {
        // 씬 정리 중 먼저 제거된 화면 요소는 제외
        if (_playerImage != null)
        {
            _playerImage.sprite = null;
            _playerImage.enabled = false;
        }

        if (_levelText != null)
            _levelText.text = string.Empty;

        if (_experienceGauge != null)
            _experienceGauge.fillAmount = 0f;
    }
}