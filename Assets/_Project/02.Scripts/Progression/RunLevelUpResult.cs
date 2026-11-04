using System;

/// <summary>
/// 한 단계 런 레벨업의 성장 적용 결과
/// </summary>
public readonly struct RunLevelUpResult
{
    public Guid RunId { get; }
    public int PreviousLevel { get; }
    public int NewLevel { get; }

    public CharacterStatValues GrowthContribution { get; }
    public float AdditionalHealing { get; }

    /// <summary>
    /// 레벨업 한 단계에 대응하는 성장 결과 구성
    /// </summary>
    /// <param name="runId">결과를 발생시킨 런 식별자</param>
    /// <param name="previousLevel">레벨업 이전 레벨</param>
    /// <param name="newLevel">한 단계 증가한 새 레벨</param>
    /// <param name="growthContribution">새 레벨까지의 전체 성장 기여분</param>
    /// <param name="additionalHealing">최대 체력 증가분 외 추가 회복량</param>
    internal RunLevelUpResult(Guid runId, int previousLevel, int newLevel, CharacterStatValues growthContribution, float additionalHealing)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("유효한 런 식별자 필요", nameof(runId));

        if (previousLevel < 1 || newLevel <= previousLevel || newLevel - previousLevel != 1)
            throw new ArgumentOutOfRangeException(nameof(newLevel), "이전 레벨보다 한 단계 높은 새 레벨 필요");

        growthContribution.Validate();

        if (float.IsNaN(additionalHealing) || float.IsInfinity(additionalHealing) || additionalHealing < 0f)
            throw new ArgumentOutOfRangeException(nameof(additionalHealing), "유한한 0 이상의 추가 회복량 필요");

        RunId = runId;
        PreviousLevel = previousLevel;
        NewLevel = newLevel;
        GrowthContribution = growthContribution;
        AdditionalHealing = additionalHealing;
    }
}