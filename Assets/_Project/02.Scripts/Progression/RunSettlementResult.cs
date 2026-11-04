using System;
using System.Collections.Generic;

/// <summary>
/// 런 종료 시 확정된 점수, 메타 재화와 숙련도 경험치
/// </summary>
public class RunSettlementResult
{
    public Guid RunId { get; }
    public RunEndReason EndReason { get; }
    public decimal RawScore { get; }
    public decimal AppliedMultiplier { get; }
    public decimal FinalScore { get; }
    public long MetaGold { get; }
    public long MetaExperience { get; }
    public IReadOnlyDictionary<EnemyRankType, int> KillsByRank { get; }
    public IReadOnlyDictionary<EnemyRankType, decimal> ScoreByRank { get; }
    public double DamageDealt { get; }
    public double DamageTaken { get; }
    public float MaxSingleDamage { get; }
    public double SessionGoldEarned { get; }
    public int VisitedNodeCount { get; }
    public int VisitedWorldCount { get; }
    public int PlayerLevel { get; }
    public Difficulty SelectedDifficulty { get; }
    public IReadOnlyList<(int Slot, SkillDefinition Definition, int Level)> Skills { get; }
    public bool IsRewardEligible => EndReason == RunEndReason.PlayerDied || EndReason == RunEndReason.Cleared;

    /// <summary>
    /// 확정된 정산 값 보관
    /// </summary>
    /// <param name="runId">중복 지급 판별에 사용할 런 식별자</param>
    /// <param name="endReason">최초 종료 사유</param>
    /// <param name="rawScore">누적 원점수</param>
    /// <param name="appliedMultiplier">적용 배율</param>
    /// <param name="finalScore">배율 적용 점수</param>
    /// <param name="metaGold">잔여 점수를 버린 지급 수량</param>
    /// <param name="metaExperience">최종 점수에서 환산한 숙련도 경험치</param>
    /// <param name="statistics">종료 시점에 복사할 런 통계</param>
    /// <param name="playerLevel">종료 시점 세션 레벨</param>
    /// <param name="skills">종료 시점 보유 스킬의 슬롯, 정의, 레벨</param>
    /// <param name="selectedDifficulty">런 시작 시 선택한 난이도</param>
    public RunSettlementResult(Guid runId, RunEndReason endReason, decimal rawScore,
        decimal appliedMultiplier, decimal finalScore, long metaGold, long metaExperience,
        RunStatistics statistics, int playerLevel, (int Slot, SkillDefinition Definition, int Level)[] skills,
        Difficulty selectedDifficulty)
    {
        if (runId == Guid.Empty) throw new ArgumentException("런 식별자 필요", nameof(runId));
        if (!Enum.IsDefined(typeof(RunEndReason), endReason)) throw new ArgumentOutOfRangeException(nameof(endReason));
        if (rawScore < 0) throw new ArgumentOutOfRangeException(nameof(rawScore));
        if (appliedMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(appliedMultiplier));
        if (finalScore < 0) throw new ArgumentOutOfRangeException(nameof(finalScore));
        if (metaGold < 0) throw new ArgumentOutOfRangeException(nameof(metaGold));
        if (metaExperience < 0) throw new ArgumentOutOfRangeException(nameof(metaExperience));
        if (endReason == RunEndReason.Manual &&
            (appliedMultiplier != 0 || finalScore != 0 || metaGold != 0 || metaExperience != 0))
            throw new ArgumentException("직접 종료의 정산 보상은 0이어야 함");

        RunId = runId;
        EndReason = endReason;
        RawScore = rawScore;
        AppliedMultiplier = appliedMultiplier;
        FinalScore = finalScore;
        MetaGold = metaGold;
        MetaExperience = metaExperience;
        KillsByRank = statistics.CaptureKillsByRank();
        ScoreByRank = statistics.CaptureScoreByRank();
        DamageDealt = statistics.DamageDealt;
        DamageTaken = statistics.DamageTaken;
        MaxSingleDamage = statistics.MaxSingleDamage;
        SessionGoldEarned = statistics.SessionGoldEarned;
        VisitedNodeCount = statistics.VisitedNodeCount;
        VisitedWorldCount = statistics.VisitedWorldCount;
        PlayerLevel = playerLevel;
        SelectedDifficulty = selectedDifficulty;
        Skills = Array.AsReadOnly(((int Slot, SkillDefinition Definition, int Level)[])skills.Clone());
    }
}
