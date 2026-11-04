using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 화면의 정보 표시와 버튼 입력 전달
/// </summary>
[DisallowMultipleComponent]
public class LobbyCharacterSelectView : MonoBehaviour, ILobbyView
{
    [Header("----- 캐릭터 정보 -----")]
    [SerializeField] TextMeshProUGUI _nameText;
    [SerializeField] TextMeshProUGUI _descriptionText;
    [SerializeField] TextMeshProUGUI _attackTypeText;
    [SerializeField] Image _characterIcon;
    [SerializeField] Image[] _startingSkillIcons = Array.Empty<Image>();

    [Header("----- Q W E R 순서의 슬롯 루트 -----")]
    [SerializeField] GameObject[] _startingSkillSlotRoots = new GameObject[4];

    [Header("----- 선택과 닫기 -----")]
    [SerializeField] Button _previousButton;
    [SerializeField] Button _nextButton;
    [SerializeField] Button _closeButton;
    [SerializeField] Button interactionButton;

    [Header("----- 캐릭터 숙련도 -----")]
    [SerializeField] TextMeshProUGUI _masteryLevelText;

    [Header("----- 공격 유형 -----")]
    [SerializeField] RectTransform _attackTypeHandle;

    [Header("----- 막대 전환 -----")]
    [SerializeField] SliderValueAnimator _masteryExperienceAnimator;
    [SerializeField] SegmentedImageValueAnimator _controlDifficultyAnimator;

    [Header("----- 기본 능력치 차트 -----")]
    [SerializeField] PentagonStatGraphic _statChart;

    public event Action<int> SelectionRequested;
    public event Action CloseRequested;

    public int StartingSkillSlotCount => _startingSkillIcons.Length;
    void OnEnable()
    {
        _previousButton.onClick.AddListener(HandlePreviousClicked);
        _nextButton.onClick.AddListener(HandleNextClicked);
        _closeButton.onClick.AddListener(HandleCloseClicked);
    }

    void OnDisable()
    {
        // 씬 종료 중 이미 파괴된 버튼 제외
        if (_previousButton != null)
            _previousButton.onClick.RemoveListener(HandlePreviousClicked);

        if (_nextButton != null)
            _nextButton.onClick.RemoveListener(HandleNextClicked);

        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(HandleCloseClicked);
    }

    void Awake()
    {
        ValidateBindings();
    }

    void ShowAttackType(LobbyAttackType attackType)
    {
        float horizontalAnchor;

        switch (attackType)
        {
            case LobbyAttackType.Melee:
                horizontalAnchor = 0f;
                break;
            case LobbyAttackType.Ranged:
                horizontalAnchor = 1f;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(attackType));
        }

        // 기존 막대의 양 끝 안쪽에 표시점 배치
        Vector2 anchor = new Vector2(horizontalAnchor, 0.5f);

        _attackTypeHandle.anchorMin = anchor;
        _attackTypeHandle.anchorMax = anchor;
        _attackTypeHandle.pivot = anchor;
        _attackTypeHandle.anchoredPosition = Vector2.zero;
    }
    void HandlePreviousClicked()
    {
        SelectionRequested?.Invoke(-1);
    }

    void HandleNextClicked()
    {
        SelectionRequested?.Invoke(1);
    }

    void HandleCloseClicked()
    {
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// 초기 비활성 상태에서도 사용할 화면 참조 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_nameText == null || _descriptionText == null || _previousButton == null || _nextButton == null || _closeButton == null)
            throw new MissingReferenceException($"{name}: 이름, 설명, 이전, 다음, 닫기 연결 필요");

        if (_previousButton == _nextButton || _previousButton == _closeButton || _nextButton == _closeButton)
            throw new InvalidOperationException($"{name}: 서로 다른 버튼 연결 필요");

        if (_startingSkillIcons == null || _startingSkillIcons.Length == 0)
            throw new InvalidOperationException($"{name}: 시작 스킬 아이콘 표시 영역 연결 필요");

        if (_startingSkillIcons == null || _startingSkillIcons.Length != 4 || _startingSkillSlotRoots == null || _startingSkillSlotRoots.Length != 4)
            throw new InvalidOperationException($"{name}: QWER 순서의 슬롯 루트와 아이콘 각각 4개 연결 필요");

        for (int slot = 0; slot < 4; slot++)
        {
            GameObject root = _startingSkillSlotRoots[slot];
            Image icon = _startingSkillIcons[slot];

            if (root == null || icon == null)
                throw new MissingReferenceException($"{name}: {slot}번 슬롯 루트와 아이콘 연결 필요");

            if (root == gameObject || !root.transform.IsChildOf(transform) || !icon.transform.IsChildOf(root.transform))
                throw new InvalidOperationException($"{name}: 선택 화면 아래의 개별 슬롯과 그 안의 아이콘 연결 필요");
        }

        if (_masteryLevelText == null)
            throw new MissingReferenceException($"{name}: 캐릭터 숙련도 레벨과 경험치 막대 연결 필요");

        if (_attackTypeHandle == null)
            throw new MissingReferenceException($"{name}: 공격 유형 표시점 연결 필요");

        if (_statChart == null)
            throw new MissingReferenceException($"{name}: 오각형 능력치 차트 연결 필요");

        if (_masteryExperienceAnimator == null || _controlDifficultyAnimator == null)
            throw new MissingReferenceException($"{name}: 숙련도와 조작 난이도 Slider 애니메이션 연결 필요");

        _masteryExperienceAnimator.ValidateBindings();
        _controlDifficultyAnimator.ValidateBindings();
    }

    /// <summary>
    /// 전달받은 캐릭터 이름과 설명 및 유형 정보 표시
    /// </summary>
    /// <param name="characterIcon">캐릭터 표시 아이콘</param>
    /// <param name="displayName">캐릭터 표시 이름</param>
    /// <param name="description">캐릭터 설명</param>
    /// <param name="attackType">공격 거리 유형</param>
    /// <param name="controlDifficulty">1부터 6 사이의 조작 난이도</param>
    public void ShowCharacter(Sprite characterIcon, string displayName, string description, string CharacterTypeName, LobbyAttackType attackType, int controlDifficulty)
    {
        _characterIcon.sprite = characterIcon;
        _nameText.text = displayName;
        _descriptionText.text = description;
        _attackTypeText.text = $"피해유형 : {CharacterTypeName}";

        ShowAttackType(attackType);

        _controlDifficultyAnimator.SetTarget((float)controlDifficulty / LobbyCharacterDefinition.MaxControlDifficulty);
    }

    /// <summary>
    /// 지정 시작 스킬 슬롯의 아이콘과 표시 여부 반영
    /// </summary>
    /// <param name="slot">QWER 순서의 슬롯 번호</param>
    /// <param name="icon">표시할 아이콘, 빈 슬롯은 null</param>
    public void ShowStartingSkill(int slot, Sprite icon)
    {
        _startingSkillIcons[slot].sprite = icon;
        _startingSkillSlotRoots[slot].SetActive(icon != null);
    }

    /// <summary>
    /// 계산이 완료된 능력치 비율을 오각형 차트에 전달
    /// </summary>
    /// <param name="attack">공격력 비율</param>
    /// <param name="spell">주문력 비율</param>
    /// <param name="attackSpeed">공격속도 비율</param>
    /// <param name="health">최대 체력 비율</param>
    /// <param name="defense">방어력 비율</param>
    public void ShowStatChart(float attack, float spell, float attackSpeed, float health, float defense)
    {
        _statChart.SetValues(attack, spell, attackSpeed, health, defense);
    }

    /// <summary>
    /// 선택 캐릭터의 숙련도 레벨과 현재 구간 진행률 표시
    /// </summary>
    /// <param name="level">현재 캐릭터 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 숙련도</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 숙련도, 최대 레벨은 0</param>
    public void ShowMastery(int level, long currentExperience, long requiredExperience)
    {
        _masteryLevelText.text = $"Lv. {level:N0}";

        float progress = requiredExperience == 0 ? 1f : (float)((double)currentExperience / requiredExperience);

        _masteryExperienceAnimator.SetTarget(progress);
    }

    /// <summary>
    /// 프로필 준비 전 또는 독립 테스트의 숙련도 미표시 상태
    /// </summary>
    public void ShowMasteryUnavailable()
    {
        _masteryLevelText.text = "Lv. —";
        _masteryExperienceAnimator.SetTarget(0f);
    }

    /// <summary>
    /// 캐릭터 선택 화면 표시 여부 변경
    /// </summary>
    /// <param name="visible">화면 표시 여부</param>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        interactionButton.interactable = !visible;
    }

    /// <summary>
    /// 캐릭터 순환과 닫기 버튼의 입력 허용 변경
    /// </summary>
    /// <param name="canSelect">이전과 다음 버튼 사용 가능 여부</param>
    /// <param name="canClose">닫기 버튼 사용 가능 여부</param>
    public void SetInteraction(bool canSelect, bool canClose)
    {
        _previousButton.interactable = canSelect;
        _nextButton.interactable = canSelect;
        _closeButton.interactable = canClose;
    }

    /// <summary>
    /// 캐릭터 선택 화면 열기
    /// </summary>
    public void Open()
    {
        SetVisible(true);
    }

    /// <summary>
    /// 캐릭터 선택 화면 닫기
    /// </summary>
    public void Close()
    {
        SetVisible(false);
    }
}