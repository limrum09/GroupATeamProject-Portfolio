using System;
using UnityEngine;

/// <summary>스킬 피해 계산과 단일 타격 적용</summary>
internal static class SkillDamageResolver
{
    /// <summary>발동 시 저장한 공격 수치와 성장값으로 기본 피해 계산</summary>
    /// <param name="execution">발동 단위 실행 정보</param>
    /// <param name="damageMultiplier">공격력 계수</param>
    /// <param name="spellPowerMultiplier">주문력 계수</param>
    /// <returns>방어력과 치명타 적용 전 피해</returns>
    internal static float CalculatePower(SkillExecutionContext execution, float damageMultiplier, float spellPowerMultiplier)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        float attackMultiplier = damageMultiplier + execution.Growth.AttackMultiplierIncrease;

        float spellMultiplier = spellPowerMultiplier + execution.Growth.SpellMultiplierIncrease;

        float power = execution.AttackPower * attackMultiplier + execution.SpellPower * spellMultiplier;

        if (float.IsNaN(power) || float.IsInfinity(power) || power < 0f)
            throw new InvalidOperationException("유효한 피해 계수와 계산 결과 필요");

        return power;
    }

    /// <summary>유효한 대상에 단일 타격 적용과 적중 통지</summary>
    /// <param name="execution">발동 단위 실행 정보</param>
    /// <param name="target">피격 대상</param>
    /// <param name="power">방어력과 치명타 적용 전 피해</param>
    /// <param name="position">적중 위치</param>
    /// <param name="rotation">적중 연출 기준 회전</param>
    /// <param name="isPrimaryTarget">주 대상 적중 여부</param>
    /// <returns>피해 처리 수락 여부</returns>
    internal static bool TryApply(SkillExecutionContext execution, CombatTarget target, float power, Vector3 position, Quaternion rotation, bool isPrimaryTarget)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        if (!execution.CanContinue || target == null || !target.CanReceiveFrom(execution.Source))
            return false;

        // 유효한 타격마다 독립적인 치명타 판정
        DamageRequest damage = CreateDamage(execution, power);

        if (!target.ReceiveDamage(damage))
            return false;

        SkillImpactInfo impact = new SkillImpactInfo(execution.Action.Id, execution.Action.Skill, target, damage, position, rotation, isPrimaryTarget);

        execution.PublishImpact(impact);
        return true;
    }

    static DamageRequest CreateDamage(SkillExecutionContext execution, float power)
    {
        bool critical = execution.CriticalChance >= 1f || (execution.CriticalChance > 0f && UnityEngine.Random.value < execution.CriticalChance);

        return new DamageRequest(execution.Source, power, critical, execution.CriticalDamageMultiplier);
    }
}