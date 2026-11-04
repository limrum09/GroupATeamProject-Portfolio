using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 메타 강화 화면의 표시와 포인터 입력 연결
/// </summary>
[DisallowMultipleComponent]
public class MetaUpgradeView : MonoBehaviour, IMetaUpgradeView, ILobbyView
{
    [Header("----- 왼쪽 슬롯 순서 -----")]
    [SerializeField]
    MetaUpgradeSlotView[] _slots = Array.Empty<MetaUpgradeSlotView>();

    [Header("----- 오른쪽 데이터 표시 순서 -----")]
    [SerializeField]
    MetaUpgradeSlotView[] _catalog = Array.Empty<MetaUpgradeSlotView>();

    [Header("----- 캐릭터와 재화 -----")]
    [SerializeField] TextMeshProUGUI _characterName;
    [SerializeField] TextMeshProUGUI _currencyText;

    [Header("----- 숙련도 표시 -----")]
    [SerializeField] TextMeshProUGUI _integratedLevelText;
    [SerializeField] TextMeshProUGUI _characterMasteryLevelText;
    [SerializeField] TextMeshProUGUI _currentMasteryExperienceText;
    [SerializeField] Slider _masteryExperienceSlider;

    [Header("----- 상세 정보 -----")]
    [SerializeField] Image _detailIcon;
    [SerializeField] TextMeshProUGUI _detailName;
    [SerializeField] TextMeshProUGUI _detailRank;
    [SerializeField] TextMeshProUGUI _detailDescription;
    [SerializeField] TextMeshProUGUI _priceText;
    [SerializeField] TextMeshProUGUI _messageText;

    [Header("----- 구매와 닫기 -----")]
    [SerializeField] Button _purchaseButton;
    [SerializeField] TextMeshProUGUI _purchaseLabel;
    [SerializeField] Button _closeButton;

    [Header("----- 공용 드래그 표시 -----")]
    [SerializeField] SkillDragIconView _dragIcon;

    Sprite[] _catalogIcons;
    SkillSlotDragInput _dragSource;
    Vector2 _dragPosition;
    bool _inputEnabled;

    public event Action<bool, int, bool> Clicked;
    public event Action<bool, int> DragStarted;
    public event Action<int> Dropped;
    public event Action DragCanceled;
    public event Action PurchaseRequested;
    public event Action CloseRequested;

    public int CatalogCount => _catalog.Length;

    void Awake()
    {
        _catalogIcons = new Sprite[_catalog.Length];

        for (int i = 0; i < _catalog.Length; i++)
            _catalogIcons[i] = _catalog[i].Icon;

        Clear();
    }

    void OnEnable()
    {
        foreach (MetaUpgradeSlotView slot in AllSlots())
        {
            slot.Clicked += HandleClicked;

            SkillSlotDragInput input = slot.DragInput;
            input.OnDragStarted += HandleDragStarted;
            input.OnDragged += HandleDragged;
            input.OnDragReleased += HandleDragReleased;
            input.OnDragCanceled += HandleDragCanceled;
        }

        _purchaseButton.onClick.AddListener(HandlePurchase);
        _closeButton.onClick.AddListener(HandleClose);
    }

    void OnDisable()
    {
        bool hadDrag = _dragSource != null;
        CancelDrag();

        if (hadDrag)
            DragCanceled?.Invoke();

        foreach (MetaUpgradeSlotView slot in AllSlots())
        {
            // 씬 종료 중 이미 파괴된 오브젝트 제외
            if (slot == null) continue;

            slot.Clicked -= HandleClicked;

            SkillSlotDragInput input = slot.DragInput;
            if (input == null) continue;

            input.OnDragStarted -= HandleDragStarted;
            input.OnDragged -= HandleDragged;
            input.OnDragReleased -= HandleDragReleased;
            input.OnDragCanceled -= HandleDragCanceled;
        }

        if (_purchaseButton != null)
            _purchaseButton.onClick.RemoveListener(HandlePurchase);

        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(HandleClose);
    }

    IEnumerable<MetaUpgradeSlotView> AllSlots()
    {
        foreach (MetaUpgradeSlotView slot in _slots)
            yield return slot;

        foreach (MetaUpgradeSlotView slot in _catalog)
            yield return slot;
    }

    void HandleClicked(MetaUpgradeSlotView source, bool doubleClick)
    {
        if (!_inputEnabled) return;

        int index = Array.IndexOf(_slots, source);

        if (index >= 0)
        {
            Clicked?.Invoke(true, index, doubleClick);
            return;
        }

        index = Array.IndexOf(_catalog, source);
        Clicked?.Invoke(false, index, doubleClick);
    }

    int FindDestination(GameObject hitObject)
    {
        if (hitObject == null)
            return -1;

        Transform hit = hitObject.transform;

        for (int i = 0; i < _slots.Length; i++)
        {
            Transform slot = _slots[i].transform;

            if (hit == slot || hit.IsChildOf(slot))
                return i;
        }

        return -1;
    }

    void HandleDragStarted(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (!_inputEnabled || _dragSource != null)
        {
            source.Cancel();
            return;
        }

        int index = Array.FindIndex(_slots, slot => slot.DragInput == source);

        bool fromLoadout = index >= 0;

        if (!fromLoadout)
            index = Array.FindIndex(_catalog, slot => slot.DragInput == source);

        _dragSource = source;
        _dragPosition = eventData.position;

        DragStarted?.Invoke(fromLoadout, index);
    }

    void HandleDragged(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_dragSource == source)
            _dragIcon.Move(eventData.position);
    }

    void HandleDragReleased(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_dragSource != source) return;

        int destination = FindDestination(eventData.pointerCurrentRaycast.gameObject);

        // Presenter 요청 전에 입력 상태 정리
        CancelDrag();
        Dropped?.Invoke(destination);
    }

    void HandleDragCanceled(SkillSlotDragInput source)
    {
        if (_dragSource != source) return;

        CancelDrag();
        DragCanceled?.Invoke();
    }

    void HandlePurchase()
    {
        PurchaseRequested?.Invoke();
    }

    void HandleClose()
    {
        CloseRequested?.Invoke();
    }

#if UNITY_EDITOR
    void ValidateSlot(MetaUpgradeSlotView slot, bool catalog, HashSet<MetaUpgradeSlotView> unique)
    {
        if (slot == null || !unique.Add(slot))
            throw new InvalidOperationException($"{name}: 슬롯 누락 또는 중복 연결 확인 필요");

        if (!slot.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: MetaUpgrade 하위 슬롯 연결 필요");

        slot.ValidateBindings(catalog);
    }
#endif

    /// <summary>
    /// 캐릭터 이름과 보유 재화 표시
    /// </summary>
    /// <param name="characterName">캐릭터 이름</param>
    /// <param name="balance">보유 재화</param>
    public void ShowHeader(string characterName, long balance)
    {
        _characterName.text = characterName;
        _currencyText.text = balance.ToString("N0");
    }

    /// <summary>
    /// 오른쪽 강화 항목 표시
    /// </summary>
    /// <param name="index">목록 인덱스</param>
    /// <param name="rank">구매 단계 문자열</param>
    /// <param name="equipped">장착 여부</param>
    /// <param name="selected">선택 여부</param>
    public void ShowCatalog(int index, string rank, bool equipped, bool selected)
    {
        _catalog[index].Show(_catalogIcons[index], rank, equipped, selected);
    }

    /// <summary>
    /// 왼쪽 장착 슬롯 표시
    /// </summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="catalogIndex">목록 인덱스, 빈 슬롯은 -1</param>
    /// <param name="selected">선택 여부</param>
    public void ShowSlot(int slot, int catalogIndex, bool selected)
    {
        Sprite icon = catalogIndex < 0 ? null : _catalogIcons[catalogIndex];

        _slots[slot].Show(icon, string.Empty, false, selected);
    }

    /// <summary>
    /// 선택한 강화의 상세 정보 표시
    /// </summary>
    /// <param name="catalogIndex">목록 인덱스, 미선택은 -1</param>
    /// <param name="upgradeName">강화 이름</param>
    /// <param name="rank">구매 단계 문자열</param>
    /// <param name="description">효과 설명</param>
    /// <param name="price">구매 비용 문자열</param>
    public void ShowDetails(int catalogIndex, string upgradeName, string rank, string description, string price)
    {
        _detailIcon.sprite = catalogIndex < 0 ? null : _catalogIcons[catalogIndex];

        _detailIcon.enabled = catalogIndex >= 0;
        _detailName.text = upgradeName;
        _detailRank.text = rank;
        _detailDescription.text = description;
        _priceText.text = price;
    }

    /// <summary>
    /// 통합 레벨과 선택 캐릭터의 숙련도 진행 표시
    /// </summary>
    /// <param name="integratedLevel">계정 공통 통합 레벨</param>
    /// <param name="characterLevel">선택 캐릭터의 숙련도 레벨</param>
    /// <param name="currentExperience">현재 캐릭터 레벨 구간의 획득 숙련도</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 숙련도, 최대 레벨은 0</param>
    public void ShowMastery(int integratedLevel, int characterLevel, long currentExperience, long requiredExperience)
    {
        _integratedLevelText.text = integratedLevel.ToString("N0");
        _characterMasteryLevelText.text = characterLevel.ToString("N0");

        if (requiredExperience == 0)
        {
            _currentMasteryExperienceText.text = "MAX";
            _masteryExperienceSlider.SetValueWithoutNotify(1f);
            return;
        }

        _currentMasteryExperienceText.text = $"{currentExperience:N0} / {requiredExperience:N0}";

        float progress = (float)((double)currentExperience / requiredExperience);

        _masteryExperienceSlider.SetValueWithoutNotify(progress);
    }

    /// <summary>
    /// 슬롯과 버튼의 입력 허용 상태 갱신
    /// </summary>
    /// <param name="inspect">항목 입력 허용 여부</param>
    /// <param name="purchase">구매 입력 허용 여부</param>
    /// <param name="close">닫기 입력 허용 여부</param>
    /// <param name="purchaseLabel">구매 버튼 문구</param>
    public void SetInteraction(bool inspect, bool purchase, bool close, string purchaseLabel)
    {
        _inputEnabled = inspect;

        foreach (MetaUpgradeSlotView slot in AllSlots())
            slot.SetInput(inspect);

        _purchaseButton.interactable = purchase;
        _closeButton.interactable = close;
        _purchaseLabel.text = purchaseLabel;
    }

    /// <summary>
    /// 강화 이용 제한 조건 표시
    /// </summary>
    /// <param name="restriction">제한 문구, 제한이 없으면 빈 문자열</param>
    public void ShowRestriction(string restriction)
    {
        _messageText.text = restriction;
    }

    /// <summary>
    /// 계정 공통 통합 숙련도 레벨 표시
    /// </summary>
    /// <param name="level">1부터 시작하는 통합 레벨</param>
    public void ShowIntegratedLevel(long level)
    {
        _integratedLevelText.text = $"{level:N0}";
    }

    /// <summary>
    /// 현재 드래그 위치에 강화 아이콘 표시
    /// </summary>
    /// <param name="catalogIndex">목록 인덱스</param>
    public void ShowDragIcon(int catalogIndex)
    {
        _dragIcon.Show(_catalogIcons[catalogIndex], _dragPosition);
    }

    /// <summary>
    /// 취소 이벤트를 재발행하지 않고 드래그 정리
    /// </summary>
    public void CancelDrag()
    {
        SkillSlotDragInput source = _dragSource;
        _dragSource = null;

        // 씬 종료 중 호출도 허용하는 정리 처리
        if (_dragIcon != null)
            _dragIcon.Hide();

        if (source != null)
            source.Cancel();
    }

    /// <summary>
    /// 준비 전 표시 초기화
    /// </summary>
    public void Clear()
    {
        CancelDrag();

        _characterName.text = "메타 강화";
        _currencyText.text = "—";

        _integratedLevelText.text = "—";
        _characterMasteryLevelText.text = "—";
        _currentMasteryExperienceText.text = "—";
        _masteryExperienceSlider.SetValueWithoutNotify(0f);

        for (int i = 0; i < _catalog.Length; i++)
            ShowCatalog(i, "—", false, false);

        for (int i = 0; i < _slots.Length; i++)
            ShowSlot(i, -1, false);

        ShowDetails(-1, string.Empty, string.Empty, "강화를 선택하세요.", "—");

        SetInteraction(false, false, true, "강화");
        ShowRestriction(string.Empty);
    }

    /// <summary>
    /// 메타 강화 화면 열기
    /// </summary>
    public void Open()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 메타 강화 화면 숨김
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 연결 검사에서만 사용하는 화면 참조 확인
    /// </summary>
    public void ValidateBindings()
    {
        if (_slots == null || _slots.Length != MetaProgression.EquipSlotCount || _catalog == null || _catalog.Length == 0)
            throw new InvalidOperationException($"{name}: 왼쪽 6슬롯과 오른쪽 강화 목록 연결 필요");

        if (_characterName == null || _currencyText == null || _detailIcon == null || _detailName == null || _detailRank == null || _detailDescription == null || _priceText == null || _messageText == null ||
            _purchaseButton == null || _purchaseLabel == null || _closeButton == null || _dragIcon == null)
        {
            throw new MissingReferenceException($"{name}: 메타 화면의 표시와 버튼 참조 연결 필요");
        }

        if (_integratedLevelText == null || _characterMasteryLevelText == null || _currentMasteryExperienceText == null || _masteryExperienceSlider == null)
            throw new MissingReferenceException($"{name}: 통합 레벨, 캐릭터 레벨, 숙련도 텍스트와 Slider 연결 필요");

        HashSet<MetaUpgradeSlotView> unique = new HashSet<MetaUpgradeSlotView>();

        foreach (MetaUpgradeSlotView slot in _slots)
            ValidateSlot(slot, false, unique);

        foreach (MetaUpgradeSlotView slot in _catalog)
            ValidateSlot(slot, true, unique);
    }
#endif
}