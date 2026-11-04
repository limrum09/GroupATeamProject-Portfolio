using System;
using UnityEngine;

/// <summary>
/// 장착 슬롯과 구분되는 고정 스킬 표시 종류
/// </summary>
public enum FixedSkillSlotKind
{
    None,
    BasicAttack,
    Movement
}

/// <summary>스킬바 화면 표시와 Hover 전달 계약</summary>
public interface ISkillBarView
{
    // 현재 Hover 중인 고정 슬롯, 표시 기능이 꺼져 있거나 대상이 없으면 None
    FixedSkillSlotKind HoveredFixedSlot { get; }
    int SlotCount { get; }
    // 현재 Hover 슬롯 번호, 해당 슬롯이 없으면 -1
    int HoveredSlot { get; }
    bool ShowsFixedSkills { get; }

    event Action<int, bool> OnSlotHoverChanged;
    event Action<int> OnSlotDragStarted;
    event Action<int, int> OnSlotDropRequested;
    event Action OnSlotDragEnded;
    event Action<int> OnSlotUpgradeRequested;
    event Action<FixedSkillSlotKind, bool> OnFixedSlotHoverChanged;    

    /// <summary>슬롯 단축키와 아이콘 표시</summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="hotkey">단축키 문자열</param>
    /// <param name="icon">아이콘, 미지정 시 숨김</param>
    void ShowSlot(int slot, string hotkey, Sprite icon);

    /// <summary>
    /// 고정 스킬 슬롯의 아이콘 표시
    /// </summary>
    /// <param name="kind">표시할 고정 슬롯 종류</param>
    /// <param name="icon">표시할 아이콘, 미지정 시 숨김</param>
    void ShowFixedSlot(FixedSkillSlotKind kind, Sprite icon);

    /// <summary>
    /// 고정 이동 스킬의 실제 쿨타임 표시
    /// </summary>
    /// <param name="remainingSeconds">남은 쿨타임</param>
    /// <param name="remainingRatio">남은 쿨타임 비율</param>
    void ShowMovementCooldown(float remainingSeconds, float remainingRatio);

    /// <summary>슬롯 쿨타임 표시</summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="remainingSeconds">남은 시간</param>
    /// <param name="remainingRatio">남은 비율</param>
    void ShowCooldown(int slot, float remainingSeconds, float remainingRatio);

    /// <summary>공용 툴팁 표시</summary>
    /// <param name="skillName">스킬 이름</param>
    /// <param name="level">현재 레벨</param>
    /// <param name="description">설명</param>
    void ShowTooltip(string skillName, int level, string description);

    /// <summary>공용 툴팁 숨김</summary>
    void HideTooltip();
    /// <summary>현재 포인터 위치에 드래그 아이콘 표시</summary>
    /// <param name="icon">표시할 스킬 아이콘</param>
    void ShowDragIcon(Sprite icon);

    /// <summary>현재 드래그 입력과 표시 취소</summary>
    void CancelDrag();

    /// <summary>
    /// 전체 슬롯의 강화 입력 모드 변경
    /// </summary>
    /// <param name="enabled">강화 모드 여부</param>
    void SetUpgradeMode(bool enabled);

    /// <summary>
    /// 지정 슬롯의 강화 정보 표시
    /// </summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="quote">표시할 강화 정보</param>
    void ShowUpgrade(int slot, SkillUpgradeQuote quote);

    /// <summary>단축키를 유지한 채 장착 표시와 툴팁 정리</summary>
    void Clear();
}   