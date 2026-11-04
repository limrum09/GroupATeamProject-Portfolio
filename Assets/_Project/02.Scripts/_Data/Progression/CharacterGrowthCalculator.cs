using System;

/// <summary>
/// 캐릭터 레벨과 메타 강화에 따른 성장 기여분 계산
/// </summary>
public static class CharacterGrowthCalculator
{
    /// <summary>
    /// 현재 레벨까지 누적된 영구 보너스와 런 성장량 계산
    /// </summary>
    /// <param name="snapshot">이번 런에 확정된 성장 설정과 메타 보너스</param>
    /// <param name="level">계산할 현재 캐릭터 레벨</param>
    /// <returns>기본 스탯을 제외한 전체 성장 기여분</returns>
    public static CharacterStatValues CalculateContribution(RunGrowthSnapshot snapshot, int level)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        CharacterGrowthDefinition definition = snapshot.GrowthDefinition;

        if (level < 1 || level > definition.MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), "캐릭터 성장 설정에 포함된 레벨 필요");

        CharacterStatValues baseGrowth = default;

        for (int reachedLevel = 2; reachedLevel <= level; reachedLevel++)
        {
            CharacterLevelGrowth growth = definition.GetGrowthForLevel(reachedLevel);

            baseGrowth = baseGrowth.Add(growth.StatIncrease);
        }

        CharacterStatValues accumulatedMetaBonus = snapshot.PerLevelBonus.Multiply(level - 1);

        CharacterStatValues total = snapshot.PermanentBonus.Add(baseGrowth);

        total = total.Add(accumulatedMetaBonus);

        return total;
    }
}