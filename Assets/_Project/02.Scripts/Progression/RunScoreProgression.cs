using System;

/// <summary>
/// 한 런의 점수 누적과 최초 종료 결과 확정
/// </summary>
public class RunScoreProgression
{
    const decimal ScorePerMetaGold = 100m;
    const decimal ScorePerMetaExperience = 10m;
    readonly decimal _difficultyMultiplier;
    readonly Difficulty _difficulty;
    readonly RunStatistics _statistics;
    decimal _score;
    bool _started;
    RunSettlementResult _result;

    public event Action OnChanged;

    public Guid RunId { get; }
    public decimal Score => _score;
    public bool IsActive => _started && _result == null;
    public RunSettlementResult Result => _result;

    /// <summary>
    /// 런 식별자와 선택 난이도의 정산 배율 설정
    /// </summary>
    /// <param name="runId">저장 재시도에도 유지할 런 식별자</param>
    /// <param name="statistics">같은 런의 결산 통계</param>
    /// <param name="difficulty">런 시작 시 선택한 난이도</param>
    public RunScoreProgression(Guid runId, RunStatistics statistics, Difficulty difficulty)
    {
        if (runId == Guid.Empty) throw new ArgumentException("런 식별자 필요", nameof(runId));
        RunId = runId;
        _difficulty = difficulty;
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _difficultyMultiplier = difficulty switch
        {
            Difficulty.Easy => 1.0m,
            Difficulty.Normal => 1.1m,
            Difficulty.Hard => 1.2m,
            Difficulty.Nightmare => 1.3m,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
    }

    /// <summary>
    /// 점수 수집 시작, 동일 객체의 재시작 금지
    /// </summary>
    public void Start()
    {
        if (_started) throw new InvalidOperationException("이미 시작한 런");
        _started = true;
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 활성 런에 적의 정의 점수와 등급별 처치 누적
    /// </summary>
    /// <param name="rank">처치한 적의 등급</param>
    /// <param name="amount">0 이상의 처치 점수</param>
    /// <returns>활성 런의 점수 수락 여부</returns>
    public bool TryAddKill(EnemyRankType rank, float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!IsActive) return false;
        decimal score = (decimal)amount;
        _score = checked(_score + score);
        _statistics.RecordKill(rank, score);
        if (amount > 0)
            OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 최초 종료 사유로 정산 확정, 반복 호출 시 기존 결과 반환
    /// </summary>
    /// <param name="reason">사망, 클리어 또는 무보상 직접 종료</param>
    /// <param name="playerLevel">종료 시점 세션 레벨</param>
    /// <param name="skills">종료 시점 보유 스킬</param>
    /// <returns>런마다 잔여 점수를 버린 불변 정산 결과</returns>
    public RunSettlementResult Finish(RunEndReason reason, int playerLevel,
        (int Slot, SkillDefinition Definition, int Level)[] skills)
    {
        if (!Enum.IsDefined(typeof(RunEndReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (_result != null) return _result;
        if (!_started) throw new InvalidOperationException("런 시작 전 정산 불가");

        decimal multiplier = reason == RunEndReason.Cleared || reason == RunEndReason.PlayerDied
            ? _difficultyMultiplier
            : 0m;
        decimal finalScore = checked(_score * multiplier);
        long metaGold = checked((long)decimal.Floor(finalScore / ScorePerMetaGold));
        long metaExperience = checked((long)decimal.Floor(finalScore / ScorePerMetaExperience));
        _result = new RunSettlementResult(
            RunId, reason, _score, multiplier, finalScore, metaGold, metaExperience,
            _statistics, playerLevel, skills, _difficulty);
        OnChanged?.Invoke();
        return _result;
    }
}
