using System;
using UnityEngine;

/// <summary>스킬 장착 상태와 QWER 슬롯 화면의 연결</summary>
[DisallowMultipleComponent]
public sealed class SkillBarPresenter : IDisposable
{
    static readonly string[] _hotkeys = { "Q", "W", "E", "R" };

    readonly SkillLoadout _loadout;
    readonly CombatController _combat;
    readonly ISkillBarView _view;
    readonly SessionSkillUpgradeService _upgrades;
    readonly SkillUpgradeQuote[] _upgradeQuotes = new SkillUpgradeQuote[_hotkeys.Length];

    SkillInstance _draggedSkill;
    FixedSkillSlotKind _hoveredFixedSlot;

    bool _upgradeMode;
    bool _active;
    bool _disposed;
    bool _refreshRequired;
    int _hoveredSlot = -1;
    int _dragSourceSlot = -1;

    void HandleFixedHoverChanged(FixedSkillSlotKind kind, bool hovered)
    {
        if (!_active || !_view.ShowsFixedSkills)
            return;

        if (kind != FixedSkillSlotKind.BasicAttack && kind != FixedSkillSlotKind.Movement)
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (hovered)
        {
            _hoveredFixedSlot = kind;
            _hoveredSlot = -1;
        }
        else if (_hoveredFixedSlot == kind)
        {
            _hoveredFixedSlot = FixedSkillSlotKind.None;
        }

        _refreshRequired = true;
    }

    void HandleSlotDragStarted(int slot)
    {
        if (!_active)
            return;

        ClearDragSelection();

        if (_loadout == null)
        {
            _view.CancelDrag();
            return;
        }

        SkillInstance skill = _loadout.GetSkill(slot);

        if (skill == null)
        {
            _view.CancelDrag();
            return;
        }

        _dragSourceSlot = slot;
        _draggedSkill = skill;

        _view.HideTooltip();
        _view.ShowDragIcon(skill.Definition.SkillIcon);
    }

    void HandleSlotDropRequested(int source, int destination)
    {
        if (!_active || _upgradeMode || _loadout == null || _draggedSkill == null || source != _dragSourceSlot)
        {
            return;
        }

        SkillInstance expectedSource = _draggedSkill;

        // 모델 변경 알림보다 먼저 선택 상태 정리
        ClearDragSelection();

        _loadout.TrySwapSlots(source, destination, expectedSource);
        _refreshRequired = true;
    }

    void HandleSlotDragEnded()
    {
        if (!_active)
            return;

        ClearDragSelection();
        CaptureHoveredSlot();

        _refreshRequired = true;
    }

    void HandleHoverChanged(int slot, bool hovered)
    {
        if (!_active)
            return;

        if (slot < 0 || slot >= _loadout.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));

        if (hovered)
        {
            _hoveredSlot = slot;
            _hoveredFixedSlot = FixedSkillSlotKind.None;
        }
        else if (_hoveredSlot == slot)
        {
            _hoveredSlot = -1;
        }

        _refreshRequired = true;
    }

    void HandleLoadoutChanged()
    {
        _refreshRequired = true;

        if (_draggedSkill != null && (_loadout == null || _loadout.GetSkill(_dragSourceSlot) != _draggedSkill))
        {
            ClearDragSelection();
            _view.CancelDrag();
        }
    }

    void RefreshUpgrades()
    {
        for (int slot = 0; slot < _loadout.SlotCount; slot++)
        {
            SkillUpgradeQuote quote = _upgrades.GetQuote(slot);
            _upgradeQuotes[slot] = quote;
            _view.ShowUpgrade(slot, quote);
        }
    }

    void RefreshSlots()
    {
        for (int i = 0; i < _loadout.SlotCount; i++)
        {
            SkillInstance skill = _loadout.GetSkill(i);

            if (skill != null && string.IsNullOrWhiteSpace(skill.Definition.SkillName))
                throw new InvalidOperationException($"{skill.Definition.name}: Skill Name 입력 필요");

            _view.ShowSlot(i, _hotkeys[i], skill != null ? skill.Definition.SkillIcon : null);
        }

        if (!_view.ShowsFixedSkills)
            return;

        SkillDefinition basicAttack = _combat.BasicAttackDefinition;
        SkillDefinition movement = _combat.MovementDefinition;

        _view.ShowFixedSlot(FixedSkillSlotKind.BasicAttack, basicAttack != null ? basicAttack.SkillIcon : null);
        _view.ShowFixedSlot(FixedSkillSlotKind.Movement, movement != null ? movement.SkillIcon : null);
    }

    void RefreshCooldowns()
    {
        for (int i = 0; i < _loadout.SlotCount; i++)
        {
            SkillInstance skill = _loadout.GetSkill(i);

            _view.ShowCooldown(i, skill != null ? skill.RemainingCooldown : 0f, skill != null ? skill.CooldownRemainingRatio : 0f);
        }

        if (_view.ShowsFixedSkills)
        {
            _view.ShowMovementCooldown(_combat.MovementSkillRemainingCooldown, _combat.MovementSkillCooldownRemainingRatio);
        }
    }

    void RefreshTooltip()
    {
        if (_upgradeMode || _draggedSkill != null)
        {
            _view.HideTooltip();
            return;
        }

        SkillDefinition definition;
        int level;

        if (_hoveredFixedSlot == FixedSkillSlotKind.BasicAttack)
        {
            definition = _combat.BasicAttackDefinition;
            level = _combat.BasicAttackLevel;
        }
        else if (_hoveredFixedSlot == FixedSkillSlotKind.Movement)
        {
            definition = _combat.MovementDefinition;
            level = _combat.MovementSkillLevel;
        }
        else
        {
            if (_hoveredSlot < 0)
            {
                _view.HideTooltip();
                return;
            }

            SkillInstance skill = _loadout.GetSkill(_hoveredSlot);

            if (skill == null)
            {
                _view.HideTooltip();
                return;
            }

            definition = skill.Definition;
            level = skill.Level;
        }

        if (definition == null)
        {
            _view.HideTooltip();
            return;
        }

        _view.ShowTooltip(definition.SkillName, level, definition.SkillDescription);
    }

    void CaptureHoveredSlot()
    {
        _hoveredFixedSlot = _view.HoveredFixedSlot;

        _hoveredSlot = _hoveredFixedSlot == FixedSkillSlotKind.None ? _view.HoveredSlot : -1;
    }

    void ClearDragSelection()
    {
        _dragSourceSlot = -1;
        _draggedSkill = null;
    }

    /// <summary>
    /// 스킬 모델과 화면 및 선택적인 강화 서비스 연결
    /// </summary>
    /// <param name="loadout">표시할 스킬 장착 상태</param>
    /// <param name="view">스킬바 화면 계약</param>
    /// <param name="upgrades">강화 서비스, 강화 기능 미사용 시 null</param>
    public SkillBarPresenter(SkillLoadout loadout, ISkillBarView view, SessionSkillUpgradeService upgrades = null)
    {
        if (loadout == null)
            throw new ArgumentNullException(nameof(loadout));

        _loadout = loadout;
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _upgrades = upgrades;

        _combat = loadout.Combat;

        if (_view.ShowsFixedSkills && _combat == null)
            throw new MissingReferenceException("고정 스킬을 표시할 CombatController 연결 필요");

        if (_loadout.SlotCount != _hotkeys.Length || _view.SlotCount != _hotkeys.Length)
            throw new InvalidOperationException("QWER 모델과 화면의 슬롯 네 개 연결 필요");

        if (_upgrades != null && _upgrades.Loadout != _loadout)
            throw new InvalidOperationException("스킬바와 강화 서비스의 동일 SkillLoadout 연결 필요");
    }

    /// <summary>
    /// 이벤트 구독과 현재 상태 표시 준비
    /// </summary>
    public void Activate()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SkillBarPresenter));

        if (_active)
            return;

        _view.CancelDrag();

        _loadout.OnChanged += HandleLoadoutChanged;

        _view.OnSlotHoverChanged += HandleHoverChanged;
        _view.OnFixedSlotHoverChanged += HandleFixedHoverChanged;

        _view.OnSlotDragStarted += HandleSlotDragStarted;
        _view.OnSlotDropRequested += HandleSlotDropRequested;
        _view.OnSlotDragEnded += HandleSlotDragEnded;

        CaptureHoveredSlot();

        _refreshRequired = true;
        _active = true;
    }

    /// <summary>이벤트 구독 해제와 화면 상태 정리</summary>
    public void Deactivate()
    {
        if (!_active)
            return;

        _active = false;

        _upgradeMode = false;
        Array.Clear(_upgradeQuotes, 0, _upgradeQuotes.Length);

        _loadout.OnChanged -= HandleLoadoutChanged;
        _view.OnSlotHoverChanged -= HandleHoverChanged;
        _view.OnFixedSlotHoverChanged -= HandleFixedHoverChanged;

        _view.OnSlotDragStarted -= HandleSlotDragStarted;
        _view.OnSlotDropRequested -= HandleSlotDropRequested;
        _view.OnSlotDragEnded -= HandleSlotDragEnded;

        ClearDragSelection();

        _hoveredSlot = -1;
        _hoveredFixedSlot = FixedSkillSlotKind.None;

        _refreshRequired = true;
        _view.Clear();
    }

    /// <summary>활성 화면의 변경 내용과 남은 쿨타임 갱신</summary>
    public void Tick()
    {
        if (!_active)
            return;

        if (_refreshRequired)
        {
            RefreshSlots();
            RefreshTooltip();
            _refreshRequired = false;
        }

        RefreshCooldowns();

        if (_upgradeMode)
            RefreshUpgrades();
    }

    /// <summary>Presenter 사용 종료와 구독 해제</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Deactivate();
        _disposed = true;
    }

    /// <summary>
    /// 강화 표시와 드래그 입력 모드 전환
    /// </summary>
    /// <param name="enabled">강화 모드 여부</param>
    public void SetUpgradeMode(bool enabled)
    {
        if (!_active)
            return;

        if (_upgrades == null)
        {
            if (enabled)
                throw new InvalidOperationException("강화 서비스가 없는 스킬바의 강화 모드 진입 불가");

            return;
        }

        if (_upgradeMode == enabled)
            return;

        _upgradeMode = enabled;

        ClearDragSelection();

        _view.CancelDrag();
        _view.SetUpgradeMode(enabled);

        CaptureHoveredSlot();

        _refreshRequired = true;

        if (enabled)
            RefreshUpgrades();
    }

    /// <summary>
    /// 화면에 표시한 조건으로 선택 슬롯의 강화 요청
    /// </summary>
    /// <param name="slot">클릭한 슬롯 번호</param>
    public void RequestUpgrade(int slot)
    {
        if (!_active || !_upgradeMode)
            return;

        if (slot < 0 || slot >= _upgradeQuotes.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        _upgrades.RequestUpgrade(_upgradeQuotes[slot]);

        // 강화 알림 중 HUD가 닫힌 경우 갱신 중단
        if (!_active || !_upgradeMode)
            return;

        RefreshUpgrades();
        _refreshRequired = true;
    }
}