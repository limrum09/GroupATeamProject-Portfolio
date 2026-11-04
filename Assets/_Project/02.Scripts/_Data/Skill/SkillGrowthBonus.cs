using System;

/// <summary>
/// 현재 스킬 레벨까지 누적된 성장 효과
/// </summary>
public readonly struct SkillGrowthBonus
{
    public float AttackMultiplierIncrease { get; }
    public float SpellMultiplierIncrease { get; }
    public float CooldownReduction { get; }
    internal SkillGrowthBonus(float attackMultiplierIncrease, float spellMultiplierIncrease, float cooldownReduction)
    {
        AttackMultiplierIncrease = attackMultiplierIncrease;
        SpellMultiplierIncrease = spellMultiplierIncrease;
        CooldownReduction = cooldownReduction;
    }
}