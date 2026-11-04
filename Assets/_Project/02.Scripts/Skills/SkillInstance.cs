using System;
using UnityEngine;

public sealed class SkillInstance
{
    double _readyAt;
    float _cooldownDuration;

    public SkillDefinition Definition { get; }
    public int Level { get; private set; }
    public bool IsBasicAttack { get; }

    public bool IsReady => IsBasicAttack || Time.timeAsDouble >= _readyAt;
    public float RemainingCooldown => IsBasicAttack ? 0f : (float)Math.Max(0d, _readyAt - Time.timeAsDouble);
    public int MaxLevel => IsBasicAttack ? 1 : Definition.MaxLevel;
    public bool CanLevelUp => !IsBasicAttack && Level < MaxLevel;

    /// <summary>현재 레벨로 다음 사용 시 적용할 쿨타임</summary>
    public float Cooldown => IsBasicAttack ? 0f : Mathf.Max(0f, Definition.Cooldown - Definition.GetGrowth(Level).CooldownReduction);

    /// <summary>진행 중인 쿨타임의 남은 비율, 준비 완료 시 0</summary>
    public float CooldownRemainingRatio => _cooldownDuration > 0f ? Mathf.Clamp01(RemainingCooldown / _cooldownDuration) : 0f;

    internal SkillInstance(SkillDefinition definition, int level = 1, bool isBasicAttack = false)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (level < 1 || level > definition.MaxLevel || (isBasicAttack && level != 1))
            throw new ArgumentOutOfRangeException(nameof(level), "스킬 성장 데이터 범위와 기본 공격 레벨 확인 필요");

        Definition = definition;
        Level = level;
        IsBasicAttack = isBasicAttack;
    }

    internal void BeginCooldown()
    {
        if (IsBasicAttack)
            return;

        _cooldownDuration = Cooldown;
        _readyAt = Time.timeAsDouble + _cooldownDuration;
    }

    internal bool TryLevelUp()
    {
        if (!CanLevelUp)
            return false;

        // 다음 레벨의 누적 성장값 검증 완료 후 상태 변경
        // 설정에 문제가 있으면 여기서 실패함
        Definition.GetGrowth(Level + 1);

        Level++;
        return true;
    }
}