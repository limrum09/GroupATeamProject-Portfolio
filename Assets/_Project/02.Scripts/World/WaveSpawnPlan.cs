using System;

/// <summary>
/// 원본 웨이브와 위치 확보 및 소환에 공통으로 사용하는 확정 수량
/// </summary>
public class WaveSpawnPlan
{
    public WaveSpawnData Source { get; }
    public WaveDifficultyDefinition Counts { get; }
    public int TotalCount => Counts.TotalCount;

    /// <summary>
    /// 원본 웨이브와 확정 수량 연결
    /// </summary>
    /// <param name="source">배치 조건과 이벤트 식별에 사용할 웨이브</param>
    /// <param name="counts">등급별 확정 수량</param>
    public WaveSpawnPlan(WaveSpawnData source, WaveDifficultyDefinition counts)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Counts = counts ?? throw new ArgumentNullException(nameof(counts));
        Source.Validate();
    }
}
