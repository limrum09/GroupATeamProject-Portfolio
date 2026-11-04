using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>스킬바 하위 View와 Hover 입력의 연결</summary>
[DisallowMultipleComponent]
public sealed class SkillBarView : MonoBehaviour, ISkillBarView
{
    [Header("----- 슬롯 표시 순서 -----")]
    [SerializeField] SkillSlotView[] _slots = Array.Empty<SkillSlotView>();

    [Header("----- 선택 기능: 고정 스킬 표시 -----")]
    [SerializeField] bool _showFixedSkills;
    [SerializeField] SkillSlotView _basicAttackSlot;
    [SerializeField] SkillSlotView _movementSkillSlot;

    [Header("----- 드래그 아이콘 -----")]
    [SerializeField] SkillDragIconView _dragIcon;    

    [Header("----- 공용 툴팁 -----")]
    [SerializeField] SkillTooltipView _tooltip;

    SkillSlotDragInput _dragSource;
    Vector2 _dragPosition;
    int _dragOrigin = -1;
    bool _upgradeBindingsInitialized;
    bool _upgradeMode;

    public event Action<int, bool> OnSlotHoverChanged;
    public event Action<int> OnSlotDragStarted;
    public event Action<int, int> OnSlotDropRequested;
    public event Action OnSlotDragEnded;
    public event Action<int> OnSlotUpgradeRequested;
    public event Action<FixedSkillSlotKind, bool> OnFixedSlotHoverChanged;

    public int SlotCount => _slots.Length;
    public bool ShowsFixedSkills => _showFixedSkills;

    public FixedSkillSlotKind HoveredFixedSlot
    {
        get
        {
            if (!_showFixedSkills)
                return FixedSkillSlotKind.None;

            SkillIconHover basicHover = _basicAttackSlot.HoverTarget;

            if (basicHover.isActiveAndEnabled && basicHover.IsHovered)
                return FixedSkillSlotKind.BasicAttack;

            SkillIconHover movementHover = _movementSkillSlot.HoverTarget;

            if (movementHover.isActiveAndEnabled && movementHover.IsHovered)
                return FixedSkillSlotKind.Movement;

            return FixedSkillSlotKind.None;
        }
    }

    // 현재 활성 Hover 영역의 슬롯 번호, 없으면 -1
    public int HoveredSlot
    {
        get
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                SkillIconHover hover = _slots[i].HoverTarget;

                if (hover.isActiveAndEnabled && hover.IsHovered)
                    return i;
            }

            return -1;
        }
    }

    void Awake()
    {
        if (_tooltip == null)
            throw new MissingReferenceException($"{name}: SkillTooltipView 연결 필요");

        if (_dragIcon == null)
            throw new MissingReferenceException($"{name}: SkillDragIconView 연결 필요");

        if (_slots == null || _slots.Length == 0)
            throw new InvalidOperationException($"{name}: 하나 이상의 슬롯 연결 필요");

        for (int i = 0; i < _slots.Length; i++)
        {
            SkillSlotView slot = _slots[i];

            if (slot == null)
                throw new MissingReferenceException($"{name}: {i}번 슬롯 연결 필요");

            if (!slot.transform.IsChildOf(transform))
                throw new InvalidOperationException($"{name}: 스킬바 하위 슬롯 연결 필요");

            // 슬롯의 Awake 실행 순서와 무관하게 입력 참조 준비
            slot.InitializeBindings();

            for (int j = 0; j < i; j++)
            {
                if (slot == _slots[j] || slot.HoverTarget == _slots[j].HoverTarget)
                    throw new InvalidOperationException($"{name}: 슬롯 또는 Hover 영역의 중복 연결");
            }
        }

        if (_showFixedSkills)
        {
            InitializeFixedSlot(_basicAttackSlot);
            InitializeFixedSlot(_movementSkillSlot);

            if (_basicAttackSlot == _movementSkillSlot || _basicAttackSlot.HoverTarget == _movementSkillSlot.HoverTarget)
                throw new InvalidOperationException($"{name}: 서로 다른 기본 공격과 이동 스킬 슬롯 연결 필요");
        }
    }

    void OnEnable()
    {
        foreach (SkillSlotView slot in _slots)
        {
            slot.HoverTarget.OnHoverChanged += HandleHoverChanged;
            slot.OnUpgradeRequested += HandleUpgradeRequested;

            SkillSlotDragInput input = slot.DragInput;

            input.OnDragStarted += HandleDragStarted;
            input.OnDragged += HandleDragged;
            input.OnDragReleased += HandleDragReleased;
            input.OnDragCanceled += HandleDragCanceled;

            if (_showFixedSkills)
            {
                _basicAttackSlot.HoverTarget.OnHoverChanged += HandleFixedHoverChanged;
                _movementSkillSlot.HoverTarget.OnHoverChanged += HandleFixedHoverChanged;
            }
        }
    }

    void OnDisable()
    {
        foreach (SkillSlotView slot in _slots)
        {
            // 계층 제거 중 이미 파괴된 슬롯 제외
            if (slot == null)
                continue;

            slot.OnUpgradeRequested -= HandleUpgradeRequested;

            SkillIconHover hover = slot.HoverTarget;

            if (hover != null)
                hover.OnHoverChanged -= HandleHoverChanged;

            SkillSlotDragInput input = slot.DragInput;

            if (input != null)
            {
                input.OnDragStarted -= HandleDragStarted;
                input.OnDragged -= HandleDragged;
                input.OnDragReleased -= HandleDragReleased;
                input.OnDragCanceled -= HandleDragCanceled;
            }
        }

        if (_basicAttackSlot != null && _basicAttackSlot.HoverTarget != null)
            _basicAttackSlot.HoverTarget.OnHoverChanged -= HandleFixedHoverChanged;

        if (_movementSkillSlot != null && _movementSkillSlot.HoverTarget != null)
            _movementSkillSlot.HoverTarget.OnHoverChanged -= HandleFixedHoverChanged;

        Clear();
    }

    void HandleUpgradeRequested(SkillSlotView source)
    {
        if (!_upgradeMode)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != source)
                continue;

            OnSlotUpgradeRequested?.Invoke(i);
            return;
        }

        throw new InvalidOperationException($"{name}: 등록되지 않은 강화 슬롯");
    }

    void HandleHoverChanged(SkillIconHover source, bool hovered)
    {
        for(int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].HoverTarget == source)
            {
                OnSlotHoverChanged?.Invoke(i, hovered);
                return;
            }
        }

        throw new InvalidOperationException($"{name}: 등록되지 않은 Hover 영역");
    }

    void HandleDragStarted(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_upgradeMode)
        {
            source.Cancel();
            return;
        }

        if (_dragOrigin >= 0)
            return;

        for(int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].DragInput != source)
                continue;

            _dragSource = source;
            _dragOrigin = i;
            _dragPosition = eventData.position;

            HideTooltip();
            OnSlotDragStarted?.Invoke(i);

            return;
        }

        throw new InvalidOperationException($"{name}: 등록되지 않은 드래그 입력");
    }

    void HandleDragged(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_dragSource != source)
            return;

        _dragPosition = eventData.position;
        _dragIcon.Move(_dragPosition);
    }

    void HandleDragReleased(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_dragSource != source)
            return;

        int origin = _dragOrigin;
        int destination = FindDropSlot(eventData.pointerCurrentRaycast.gameObject);

        // 콜백 전에 UI 드래그 상태 정리
        ResetDrag();

        if (destination >= 0)
            OnSlotDropRequested?.Invoke(origin, destination);

        OnSlotDragEnded?.Invoke();
    }

    void HandleDragCanceled(SkillSlotDragInput source)
    {
        if (_dragSource == source)
            CancelDrag();
    }

    void HandleFixedHoverChanged(SkillIconHover source, bool hovered)
    {
        if (source == _basicAttackSlot.HoverTarget)
        {
            OnFixedSlotHoverChanged?.Invoke(FixedSkillSlotKind.BasicAttack, hovered);
            return;
        }

        if (source == _movementSkillSlot.HoverTarget)
        {
            OnFixedSlotHoverChanged?.Invoke(FixedSkillSlotKind.Movement, hovered);
            return;
        }

        throw new InvalidOperationException($"{name}: 등록되지 않은 고정 슬롯 Hover 영역");
    }

    void InitializeFixedSlot(SkillSlotView fixedSlot)
    {
        if (fixedSlot == null)
            throw new MissingReferenceException($"{name}: 고정 스킬 슬롯 연결 필요");

        if (fixedSlot.transform == transform || !fixedSlot.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 스킬바 하위의 고정 슬롯 연결 필요");

        fixedSlot.InitializeBindings();

        foreach (SkillSlotView slot in _slots)
        {
            if (slot == fixedSlot || slot.HoverTarget == fixedSlot.HoverTarget)
                throw new InvalidOperationException($"{name}: QWER 슬롯과 고정 슬롯의 독립된 구성 필요");
        }

        // 고정 슬롯은 드래그 시작 대상으로 사용하지 않음
        fixedSlot.DragInput.enabled = false;
    }

    SkillSlotView GetFixedSlot(FixedSkillSlotKind kind)
    {
        switch (kind)
        {
            case FixedSkillSlotKind.BasicAttack:
                return _basicAttackSlot;

            case FixedSkillSlotKind.Movement:
                return _movementSkillSlot;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    int FindDropSlot(GameObject hitObject)
    {
        if (hitObject == null)
            return -1;

        Transform hitTransform = hitObject.transform;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (hitTransform.IsChildOf(_slots[i].transform))
                return i;
        }

        return -1;
    }

    /// <summary>진행 중인 UI 드래그의 아이콘 표시</summary>
    /// <param name="icon">표시할 스킬 아이콘</param>
    public void ShowDragIcon(Sprite icon)
    {
        if (_dragSource == null)
            throw new InvalidOperationException($"{name}: 시작된 드래그 입력 필요");

        _dragIcon.Show(icon, _dragPosition);
    }

    /// <summary>현재 드래그 입력과 표시 취소</summary>
    public void CancelDrag()
    {
        if (ResetDrag())
            OnSlotDragEnded?.Invoke();
    }

    bool ResetDrag()
    {
        bool hadDrag = _dragOrigin >= 0;
        SkillSlotDragInput source = _dragSource;

        _dragSource = null;
        _dragOrigin = -1;

        if (_dragIcon != null)
            _dragIcon.Hide();

        if (source != null)
            source.Cancel();

        return hadDrag;
    }

    /// <summary>슬롯 단축키와 아이콘 표시</summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="hotkey">단축키 문자열</param>
    /// <param name="icon">아이콘, 미지정 시 숨김</param>
    public void ShowSlot(int slot, string hotkey, Sprite icon)
    {
        _slots[slot].SetHotkey(hotkey);
        _slots[slot].ShowIcon(icon);
    }

    /// <summary>슬롯 쿨타임 표시</summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="remainingSeconds">남은 시간</param>
    /// <param name="remainingRatio">남은 비율</param>
    public void ShowCooldown(int slot, float remainingSeconds, float remainingRatio)
    {
        _slots[slot].ShowCooldown(remainingSeconds, remainingRatio);
    }

    /// <summary>
    /// 고정 스킬 슬롯의 아이콘 표시
    /// </summary>
    /// <param name="kind">표시할 고정 슬롯 종류</param>
    /// <param name="icon">표시할 아이콘, 미지정 시 숨김</param>
    public void ShowFixedSlot(FixedSkillSlotKind kind, Sprite icon)
    {
        if (!_showFixedSkills)
            return;

        SkillSlotView slot = GetFixedSlot(kind);
        slot.ShowIcon(icon);

        // 기본 공격은 일반 스킬 쿨타임 표시에서 제외
        if (kind == FixedSkillSlotKind.BasicAttack)
            slot.ShowCooldown(0f, 0f);
    }

    /// <summary>
    /// 고정 이동 스킬의 실제 쿨타임 표시
    /// </summary>
    /// <param name="remainingSeconds">남은 쿨타임</param>
    /// <param name="remainingRatio">남은 쿨타임 비율</param>
    public void ShowMovementCooldown(float remainingSeconds, float remainingRatio)
    {
        if (!_showFixedSkills)
            return;

        _movementSkillSlot.ShowCooldown(remainingSeconds, remainingRatio);
    }

    /// <summary>공용 툴팁 표시</summary>
    /// <param name="skillName">스킬 이름</param>
    /// <param name="level">현재 레벨</param>
    /// <param name="description">설명</param>
    public void ShowTooltip(string skillName, int level, string description)
    {
        _tooltip.Show(skillName, level, description);
    }

    /// <summary>공용 툴팁 숨김</summary>
    public void HideTooltip()
    {
        _tooltip.Hide();
    }

    /// <summary>
    /// 강화 기능을 사용하는 스킬바의 추가 슬롯 참조 준비
    /// </summary>
    public void InitializeUpgradeBindings()
    {
        if (_upgradeBindingsInitialized)
            return;

        foreach (SkillSlotView slot in _slots)
            slot.InitializeUpgradeBindings();

        _upgradeBindingsInitialized = true;
    }

    /// <summary>
    /// 드래그 취소와 전체 슬롯의 강화 모드 변경
    /// </summary>
    /// <param name="enabled">강화 모드 여부</param>
    public void SetUpgradeMode(bool enabled)
    {
        if (!_upgradeBindingsInitialized)
            throw new InvalidOperationException($"{name}: 강화 슬롯 초기화 이후 모드 변경 필요");

        if (_upgradeMode == enabled)
            return;

        _upgradeMode = enabled;

        if (enabled)
        {
            CancelDrag();
            HideTooltip();
        }

        foreach (SkillSlotView slot in _slots)
        {
            // 씬 종료 중 먼저 제거된 슬롯 제외
            if (slot != null)
                slot.SetUpgradeMode(enabled);
        }
    }

    /// <summary>
    /// 지정 슬롯의 강화 정보 표시
    /// </summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="quote">현재 강화 정보</param>
    public void ShowUpgrade(int slot, SkillUpgradeQuote quote)
    {
        _slots[slot].ShowUpgrade(quote);
    }

    /// <summary>단축키를 유지한 채 장착 표시와 툴팁 정리</summary>
    public void Clear()
    {
        CancelDrag();

        if (_upgradeBindingsInitialized)
            SetUpgradeMode(false);

        foreach (SkillSlotView slot in _slots)
        {
            // 계층 제거 중 이미 파괴된 View 제외
            if (slot != null)
                slot.ShowEmpty();
        }

        if (_showFixedSkills)
        {
            if (_basicAttackSlot != null)
                _basicAttackSlot.ShowEmpty();

            if (_movementSkillSlot != null)
                _movementSkillSlot.ShowEmpty();
        }

        if (_tooltip != null)
            _tooltip.Hide();
    }
}