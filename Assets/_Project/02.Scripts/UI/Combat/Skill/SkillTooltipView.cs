using TMPro;
using UnityEngine;

/// <summary>스킬 이름, 레벨 및 설명의 툴팁 표시</summary>
[DisallowMultipleComponent]
public sealed class SkillTooltipView : MonoBehaviour
{
    [Header("----- 툴팁 표시 -----")]
    [SerializeField] CanvasGroup _group;
    [SerializeField] TMP_Text _nameText;
    [SerializeField] TMP_Text _levelText;
    [SerializeField] TMP_Text _descriptionText;

    void Awake()
    {
        if (_group == null || _nameText == null || _levelText == null || _descriptionText == null)
            throw new MissingReferenceException($"{name}: 툴팁 UI 참조 연결 필요");

        // 툴팁이 Hover 대상의 마우스 입력을 가로채지 않도록 설정
        _group.interactable = false;
        _group.blocksRaycasts = false;

        Hide();
    }

    /// <summary>전달받은 스킬 설명 표시</summary>
    /// <param name="skillName">표시할 이름</param>
    /// <param name="level">현재 스킬 레벨</param>
    /// <param name="description">설명, 미지정 시 설명 없음 표시</param>
    public void Show(string skillName, int level, string description)
    {
        _nameText.text = skillName;
        _levelText.SetText("Lv.{0}", level);
        _descriptionText.text = string.IsNullOrWhiteSpace(description) ? "None" : description;

        _group.alpha = 1f;
    }

    /// <summary>툴팁 숨김</summary>
    public void Hide()
    {
        _group.alpha = 0f;
    }
}