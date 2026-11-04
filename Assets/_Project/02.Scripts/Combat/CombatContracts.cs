using System;
using UnityEngine;

public enum CombatTeam
{
    Player,
    Enemy
}

public enum CombatActionKind
{
    None,
    BasicAttack,
    Skill
}

public enum AttackTargetState
{
    Valid,
    Invalid,
    OutOfRange,
    Blocked
}

public enum CombatActionResult
{
    Completed,
    Canceled,
    Replaced
}

public interface IDamageReceiver
{
    bool CanReceiveFrom(CombatTarget source);
    bool ReceiveDamage(DamageRequest request);
    /// <summary>발동 당시 공격자 정보에 따른 피격 가능 여부</summary>
    /// <param name="source">저장된 공격자 정보</param>
    /// <returns>피격 가능 여부</returns>
    bool CanReceiveFrom(CombatSource source);
}

/// <summary>활성화 단위 공격자 식별값과 발동 당시 진영</summary>
public readonly struct CombatSource
{
    public Guid Id { get; }
    public CombatTeam Team { get; }
    public bool IsValid => Id != Guid.Empty;

    internal CombatSource(Guid id, CombatTeam team)
    {
        Id = id;
        Team = team;
    }
}

/// <summary>
/// 공격자 정보와 방어력 적용 전 피해 및 단일 타격의 치명타 결과
/// </summary>
public readonly struct DamageRequest
{
    public CombatSource Source { get; }
    public float Power { get; }
    public bool IsCritical { get; }
    public float CriticalMultiplier { get; }

    /// <summary>단일 타격의 피해 요청 구성</summary>
    /// <param name="source">발동 당시 공격자 정보</param>
    /// <param name="power">방어력 적용 전 피해</param>
    /// <param name="isCritical">이번 타격의 치명타 여부</param>
    /// <param name="criticalMultiplier">치명타 피해 배율</param>
    public DamageRequest(CombatSource source, float power, bool isCritical = false, float criticalMultiplier = 1f)
    {
        Source = source;
        Power = power;
        IsCritical = isCritical;
        CriticalMultiplier = isCritical ? criticalMultiplier : 1f;
    }
}

/// <summary>행동 상태와 입력 시점의 지정 위치를 담은 읽기 전용 정보</summary>
public readonly struct CombatActionInfo
{
    public long Id { get; }
    public CombatActionKind Kind { get; }
    public CombatTarget Target { get; }
    public SkillDefinition Skill { get; }
    public double StartedAt { get; }
    public float Duration { get; }
    public Vector3 Direction { get; }
    /// <summary>입력 당시의 지정 위치, HasWorldPosition이 true일 때 사용</summary>
    public Vector3 WorldPosition { get; }
    public bool HasWorldPosition { get; }

    /// <summary>확정된 행동 정보 구성</summary>
    /// <param name="id">행동 식별 번호</param>
    /// <param name="kind">행동 종류</param>
    /// <param name="target">조준 대상, 없으면 null</param>
    /// <param name="skill">스킬 정의, 기존 기본 공격이면 null</param>
    /// <param name="startedAt">시전 시작 시각</param>
    /// <param name="duration">시전 시간</param>
    /// <param name="direction">확정된 행동 방향</param>
    /// <param name="worldPosition">입력 당시의 지정 위치</param>
    /// <param name="hasWorldPosition">지정 좌표 보유 여부</param>
    public CombatActionInfo(long id, CombatActionKind kind, CombatTarget target, SkillDefinition skill, double startedAt, float duration, Vector3 direction, Vector3 worldPosition, bool hasWorldPosition)
    {
        Id = id;
        Kind = kind;
        Target = target;
        Skill = skill;
        StartedAt = startedAt;
        Duration = duration;
        Direction = direction;
        WorldPosition = hasWorldPosition ? worldPosition : default;
        HasWorldPosition = hasWorldPosition;
    }
}