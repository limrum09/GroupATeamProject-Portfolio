using System;

/// <summary>
/// 런 선택 난이도
/// </summary>
public enum Difficulty
{
    Easy,
    Normal,
    Hard,
    Nightmare,
    Count
}

/// <summary>
/// 적의 최종 능력치와 처치 보상
/// </summary>
public class EnemyDifficultyDefinition
{
    public string EnemyId { get; }
    public CharacterStatValues Stats { get; }
    public float Exp { get; }
    public float Score { get; }
    public float SessionGold { get; }

    static void ValidateReward(float value, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new ArgumentOutOfRangeException(field);
    }

    /// <summary>
    /// 검증된 최종 능력치와 처치 보상 구성
    /// </summary>
    /// <param name="enemyId">적 식별자</param>
    /// <param name="stats">최종 기본 능력치</param>
    /// <param name="exp">처치 경험치</param>
    /// <param name="score">처치 점수</param>
    /// <param name="sessionGold">처치 골드</param>
    public EnemyDifficultyDefinition(
        string enemyId, CharacterStatValues stats, float exp, float score, float sessionGold)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
            throw new ArgumentException("적 식별자 필요", nameof(enemyId));

        stats.Validate();
        if (stats.MaxHealth < 1f || stats.MoveSpeed < 0.1f || stats.AttackSpeed < 0.01f ||
            stats.CriticalChance > 1f || stats.CriticalDamageMultiplier < 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(stats), "전투 능력치 범위 확인 필요");
        }

        ValidateReward(exp, nameof(exp));
        ValidateReward(score, nameof(score));
        ValidateReward(sessionGold, nameof(sessionGold));
        EnemyId = enemyId;
        Stats = stats;
        Exp = exp;
        Score = score;
        SessionGold = sessionGold;
    }
}
