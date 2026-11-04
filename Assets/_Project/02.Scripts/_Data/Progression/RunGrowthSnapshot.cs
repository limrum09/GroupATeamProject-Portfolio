using System;

/// <summary>
/// 런 시작 시 확정한 캐릭터 성장 설정과 메타 보너스
/// </summary>
public sealed class RunGrowthSnapshot
{
    public string CharacterId { get; }
    public CharacterGrowthDefinition GrowthDefinition { get; }
    public CharacterStatValues PermanentBonus { get; }
    public CharacterStatValues PerLevelBonus { get; }

    /// <summary>
    /// 이번 런에 사용할 성장 설정과 보너스 확정
    /// </summary>
    /// <param name="growthDefinition">선택한 캐릭터의 성장 설정</param>
    /// <param name="permanentBonus">구매 기록에서 계산한 영구 고정 보너스</param>
    /// <param name="perLevelBonus">구매 기록에서 계산한 레벨업당 보너스</param>
    public RunGrowthSnapshot(CharacterGrowthDefinition growthDefinition, CharacterStatValues permanentBonus, CharacterStatValues perLevelBonus)
    {
        if (growthDefinition == null)
            throw new ArgumentNullException(nameof(growthDefinition));

        growthDefinition.Validate();
        permanentBonus.Validate();
        perLevelBonus.Validate();

        CharacterId = growthDefinition.CharacterId;
        GrowthDefinition = growthDefinition;
        PermanentBonus = permanentBonus;
        PerLevelBonus = perLevelBonus;
    }
}