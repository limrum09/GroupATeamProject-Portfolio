using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 선택 캐릭터 이름과 계정 통합 성장 정보 표시
/// </summary>
[DisallowMultipleComponent]
public class LobbyPlayerInfoView : MonoBehaviour
{
    [Header("----- 캐릭터 이름 -----")]
    [SerializeField] TextMeshProUGUI _nameText;

    [Header("----- 통합 성장 -----")]
    [SerializeField] TextMeshProUGUI _levelText;
    [SerializeField] Slider _experienceBar;

    /// <summary>
    /// 표시 요소와 경험치 바 설정 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_nameText == null || _levelText == null || _experienceBar == null)
            throw new MissingReferenceException($"{name}: 이름, 레벨, 경험치 바 연결 필요");

        if (_experienceBar.wholeNumbers || _experienceBar.interactable)
            throw new System.InvalidOperationException($"{name}: 경험치 바는 범위 0~1, 소수 사용, 조작 비활성 설정 필요");
    }

    /// <summary>
    /// 현재 선택한 캐릭터 이름 표시
    /// </summary>
    /// <param name="characterName">로비 캐릭터의 표시 이름</param>
    public void ShowCharacterName(string characterName)
    {
        _nameText.text = characterName;
    }

    /// <summary>
    /// 통합 레벨과 현재 경험치 구간의 진행률 표시
    /// </summary>
    /// <param name="level">통합 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨 필요 경험치, 최대 레벨은 0</param>
    public void ShowProgress(int level, long currentExperience, long requiredExperience)
    {
        _levelText.text = $"{level:N0}";

        float progress = requiredExperience == 0 ? 1f : (float)((double)currentExperience / requiredExperience);

        _experienceBar.SetValueWithoutNotify(Mathf.Clamp01(progress));
    }

    /// <summary>
    /// 메타 성장 정보를 사용할 수 없는 상태 표시
    /// </summary>
    public void ShowProgressUnavailable()
    {
        _levelText.text = "Lv. —";
        _experienceBar.SetValueWithoutNotify(0f);
    }
}