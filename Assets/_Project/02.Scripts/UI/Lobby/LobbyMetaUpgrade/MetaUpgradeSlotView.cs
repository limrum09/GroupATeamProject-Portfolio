using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 메타 강화 항목의 표시와 클릭 입력 전달
/// </summary>
[DisallowMultipleComponent]
public class MetaUpgradeSlotView : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    [Header("----- 필수 연결 -----")]
    [SerializeField] Image _icon;
    [SerializeField] SkillSlotDragInput _dragInput;

    [Header("----- 왼쪽 장착 슬롯 전용 -----")]
    [SerializeField] MetaLoadoutSlotVisual _loadoutVisual;
    [SerializeField] SkillIconHover _loadoutHover;

    [Header("----- 오른쪽 목록 표시, 왼쪽에서는 생략 가능 -----")]
    [SerializeField] TextMeshProUGUI _rankText;
    [SerializeField] GameObject _equippedText;
    [SerializeField] GameObject _selectionFrame;
    [SerializeField] GameObject _catalogLock;
    [SerializeField] TextMeshProUGUI _catalogUnlockLevel;

    bool _inputEnabled;
    bool _ignoreClick;
    bool _clickPrimed;

    public event Action<MetaUpgradeSlotView, bool> Clicked;

    public SkillSlotDragInput DragInput => _dragInput;
    public SkillIconHover LoadoutHover => _loadoutHover;
    void OnEnable()
    {
        _dragInput.OnDragStarted += HandleDragStarted;
    }

    void OnDisable()
    {
        if (_dragInput != null)
            _dragInput.OnDragStarted -= HandleDragStarted;

        _ignoreClick = false;
        _clickPrimed = false;
    }

    void SetIcon(Sprite icon)
    {
        _icon.sprite = icon;
        _icon.enabled = icon != null;
    }

    void HandleDragStarted(SkillSlotDragInput source, PointerEventData eventData)
    {
        // 드래그 종료 후 클릭이 추가로 처리되는 상황 방지
        _ignoreClick = true;
        _clickPrimed = false;
    }

    /// <summary>
    /// 새 포인터 입력의 드래그 여부 초기화
    /// </summary>
    /// <param name="eventData">포인터 입력 정보</param>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _ignoreClick = false;
    }

    /// <summary>
    /// 단일 클릭 또는 더블클릭 전달
    /// </summary>
    /// <param name="eventData">포인터 입력 정보</param>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_inputEnabled || _ignoreClick || eventData.button != PointerEventData.InputButton.Left)
            return;

        bool doubleClick = _clickPrimed && eventData.clickCount == 2;
        _clickPrimed = !doubleClick;

        Clicked?.Invoke(this, doubleClick);
    }

    /// <summary>
    /// 오른쪽 강화 항목의 아이콘, 구매 단계, 선택 및 잠금 표시
    /// </summary>
    /// <param name="icon">표시할 상태별 아이콘, 초기화 시 null</param>
    /// <param name="rank">구매 단계 문자열</param>
    /// <param name="equipped">장착 표시 여부</param>
    /// <param name="selected">선택 테두리 표시 여부</param>
    /// <param name="lockedUntilLevel">잠긴 항목의 요구 통합 레벨, 해금 시 0</param>
    public void Show(Sprite icon, string rank, bool equipped, bool selected, int lockedUntilLevel)
    {
        bool locked = lockedUntilLevel > 0;

        SetIcon(icon);
        _rankText.text = rank;
        _equippedText.SetActive(equipped && !locked);
        _selectionFrame.SetActive(selected);
        _catalogLock.SetActive(locked);

        if (locked)
            _catalogUnlockLevel.SetText("{0}", lockedUntilLevel);
    }

    /// <summary>
    /// 왼쪽 슬롯의 강화 아이콘과 상태별 프레임 표시
    /// </summary>
    /// <param name="visibleIcon">표시할 강화 아이콘, 빈칸 또는 임시 숨김은 null</param>
    /// <param name="unlocked">슬롯 해금 여부</param>
    /// <param name="hasUpgrade">슬롯에 실제 강화 값이 있는지 여부</param>
    public void ShowLoadout(Sprite visibleIcon, bool unlocked, bool hasUpgrade)
    {
        SetIcon(unlocked ? visibleIcon : null);
        _loadoutVisual.Show(unlocked, hasUpgrade);
    }

    /// <summary>
    /// 클릭 입력 허용 상태 변경
    /// </summary>
    /// <param name="enabled">입력 허용 여부</param>
    public void SetInput(bool enabled)
    {
        _inputEnabled = enabled;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 연결 검사에서만 사용하는 슬롯 참조 확인
    /// </summary>
    /// <param name="catalog">오른쪽 강화 목록 항목 여부</param>
    public void ValidateBindings(bool catalog)
    {
        if (_icon == null || _dragInput == null)
            throw new MissingReferenceException($"{name}: Icon과 SkillSlotDragInput 연결 필요");

        if (_dragInput.gameObject != gameObject || !_dragInput.enabled)
            throw new InvalidOperationException($"{name}: 같은 오브젝트의 활성 SkillSlotDragInput 연결 필요");

        if (catalog && (_rankText == null || _equippedText == null || _selectionFrame == null || _catalogLock == null || _catalogUnlockLevel == null))
            throw new MissingReferenceException($"{name}: 오른쪽 강화 표시와 잠금 UI 연결 필요");
    }
#endif
}