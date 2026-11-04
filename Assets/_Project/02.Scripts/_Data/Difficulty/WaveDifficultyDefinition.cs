using System;

/// <summary>
/// 웨이브의 등급별 확정 소환 수량
/// </summary>
public class WaveDifficultyDefinition
{
    public int NormalCount { get; }
    public int EpicCount { get; }
    public int MidBossCount { get; }
    public int BossCount { get; }
    public int TotalCount { get; }

    /// <summary>
    /// 등급별 수량 검증과 합계 확정
    /// </summary>
    /// <param name="normalCount">일반 적 수량</param>
    /// <param name="epicCount">정예 적 수량</param>
    /// <param name="midBossCount">중간 보스 수량</param>
    /// <param name="bossCount">보스 수량</param>
    public WaveDifficultyDefinition(int normalCount, int epicCount, int midBossCount, int bossCount)
    {
        if (normalCount < 0 || epicCount < 0 || midBossCount < 0 || bossCount < 0)
            throw new ArgumentException("등급별 소환 수량은 0 이상 필요");

        NormalCount = normalCount;
        EpicCount = epicCount;
        MidBossCount = midBossCount;
        BossCount = bossCount;
        TotalCount = checked(normalCount + epicCount + midBossCount + bossCount);
    }
}
