using System;
using UnityEngine;

/// <summary>
/// 스킬 강화 조회와 실행 결과
/// </summary>
public enum SkillUpgradeStatus
{
    Ready,
    Empty,
    MaxLevel,
    InsufficientGold,
    Unavailable,
    StaleSelection,
    Busy,
    Upgraded
}

/// <summary>
/// 화면에 표시한 슬롯과 스킬 및 강화 조건
/// </summary>
public readonly struct SkillUpgradeQuote
{
    public int Slot { get; }
    public SkillInstance Skill { get; }
    public int Level { get; }
    public float Cost { get; }
    public SkillUpgradeStatus Status { get; }

    /// <summary>
    /// 조회 시점의 강화 정보 구성
    /// </summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="skill">현재 스킬, 빈 슬롯은 null</param>
    /// <param name="level">현재 레벨</param>
    /// <param name="cost">다음 단계의 강화 비용</param>
    /// <param name="status">현재 강화 가능 상태</param>
    public SkillUpgradeQuote(int slot, SkillInstance skill, int level, float cost, SkillUpgradeStatus status)
    {
        Slot = slot;
        Skill = skill;
        Level = level;
        Cost = cost;
        Status = status;
    }
}

/// <summary>
/// 세션 골드 결제와 장착 스킬 강화 조율
/// </summary>
[DisallowMultipleComponent]
public class SessionSkillUpgradeService : MonoBehaviour
{
    SkillLoadout _loadout;
    SessionEconomyManager _economy;
    bool _processing;

    public SkillLoadout Loadout => _loadout;
    public SessionEconomyManager Economy => _economy;

    void PublishCommittedChange(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception exception)
        {
            // 확정된 거래를 유지하고 표시 알림 실패 보고
            Debug.LogException(exception, this);
        }
    }

    /// <summary>
    /// 현재 런의 스킬 로드아웃과 강화 결제 지갑 연결
    /// </summary>
    /// <param name="loadout">강화할 플레이어의 스킬 로드아웃</param>
    /// <param name="economy">현재 세션의 골드 지갑</param>
    public void Bind(SkillLoadout loadout, SessionEconomyManager economy)
    {
        if (loadout == null)
            throw new ArgumentNullException(nameof(loadout));

        if (economy == null)
            throw new ArgumentNullException(nameof(economy));

        if (loadout.gameObject.scene != gameObject.scene || economy.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 로드아웃과 지갑 연결 필요");

        if (_loadout != null || _economy != null)
            throw new InvalidOperationException($"{name}: 이전 강화 서비스 연결 해제 필요");

        _loadout = loadout;
        _economy = economy;
    }

    /// <summary>
    /// 현재 런의 스킬 로드아웃과 지갑 연결 해제
    /// </summary>
    public void Unbind()
    {
        _loadout = null;
        _economy = null;
    }

    /// <summary>
    /// 현재 슬롯의 강화 비용과 실행 가능 상태 조회
    /// </summary>
    /// <param name="slot">조회할 슬롯 번호</param>
    /// <returns>현재 스킬과 레벨 및 강화 조건</returns>
    public SkillUpgradeQuote GetQuote(int slot)
    {
        if (_loadout == null || _economy == null)
            throw new InvalidOperationException($"{name}: 강화 조회 전 Bind 필요");

        if (slot < 0 || slot >= _loadout.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));

        SkillInstance skill = _loadout.GetSkill(slot);

        if (skill == null)
            return new SkillUpgradeQuote(slot, null, 0, 0f, SkillUpgradeStatus.Empty);

        int level = skill.Level;

        if (!skill.CanLevelUp)
            return new SkillUpgradeQuote(slot, skill, level, 0f, SkillUpgradeStatus.MaxLevel);

        float cost = skill.Definition.GetUpgradeCost(level);
        SkillUpgradeStatus status;

        if (!isActiveAndEnabled || !_economy.isActiveAndEnabled || !_loadout.CanUpgradeSkill(slot))
        {
            status = SkillUpgradeStatus.Unavailable;
        }
        else if (_economy.Gold < cost)
        {
            status = SkillUpgradeStatus.InsufficientGold;
        }
        else
        {
            status = SkillUpgradeStatus.Ready;
        }

        return new SkillUpgradeQuote(slot, skill, level, cost, status);
    }

    /// <summary>
    /// 표시 당시의 강화 조건 재검사와 한 단계 결제 및 강화
    /// </summary>
    /// <param name="expected">화면에서 확인한 강화 정보</param>
    /// <returns>강화 성공 또는 거절 사유</returns>
    public SkillUpgradeStatus RequestUpgrade(SkillUpgradeQuote expected)
    {
        if (_processing)
            return SkillUpgradeStatus.Busy;

        SkillUpgradeQuote current = GetQuote(expected.Slot);

        if (current.Skill != expected.Skill || current.Level != expected.Level || current.Cost != expected.Cost)
            return SkillUpgradeStatus.StaleSelection;

        if (current.Status != SkillUpgradeStatus.Ready)
            return current.Status;

        bool debited = false;
        bool committed = false;
        _processing = true;

        try
        {
            if (!_economy.TryDebit(current.Cost))
                return SkillUpgradeStatus.InsufficientGold;

            debited = true;

            if (!_loadout.TryUpgradeSkill(current.Slot, current.Skill, current.Level))
                return SkillUpgradeStatus.Unavailable;

            committed = true;

            SessionEconomyManager economy = _economy;
            // 골드와 레벨 변경 완료 이후 외부 알림
            PublishCommittedChange(_loadout.PublishChanged);
            PublishCommittedChange(economy.PublishGoldChanged);

            return SkillUpgradeStatus.Upgraded;
        }
        finally
        {
            // 강화 적용 전 거절 또는 예외 발생 시 결제 복원
            if (debited && !committed)
                _economy.RefundDebit(current.Cost);

            _processing = false;
        }
    }
}