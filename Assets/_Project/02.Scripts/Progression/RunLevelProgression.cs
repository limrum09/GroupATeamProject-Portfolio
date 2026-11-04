using System;

/// <summary>
/// 한 런의 경험치와 레벨 및 레벨업 결과 전달 관리
/// </summary>
public sealed class RunLevelProgression
{
    readonly RunLevelDefinition _levelDefinition;
    readonly RunGrowthSnapshot _growthSnapshot;

    Action<RunLevelUpResult> _applyLevelUp;

    int _level = 1;
    float _experience;

    bool _started;
    bool _stopped;
    bool _processingExperience;

    public event Action OnChanged;
    public event Action<RunLevelUpResult> OnLevelUp;

    public Guid RunId { get; }

    public int Level => _level;
    public float Experience => _experience;
    public int MaxLevel => _levelDefinition.MaxLevel;
    public bool IsMaxLevel => _level >= MaxLevel;
    public bool IsActive => _started && !_stopped;

    // 다음 레벨까지의 필요 경험치, 최대 레벨에서는 0
    public float RequiredExperience => IsMaxLevel ? 0f : _levelDefinition.GetRequiredExperience(_level);
    // 현재 레벨과 확정된 메타 보너스로 계산한 전체 성장 기여분
    public CharacterStatValues GrowthContribution => CharacterGrowthCalculator.CalculateContribution(_growthSnapshot, _level);



    static void ValidateExperienceAmount(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(nameof(amount), "유한한 0 이상의 경험치 필요");
    }

    /// <summary>
    /// 새 런의 성장 상태와 필수 레벨업 적용 대상 구성
    /// </summary>
    /// <param name="levelDefinition">캐릭터 공통 경험치 설정</param>
    /// <param name="growthSnapshot">이번 런에 확정한 성장 조건</param>
    /// <param name="applyLevelUp">스탯과 체력을 동기적으로 적용할 필수 콜백</param>
    public RunLevelProgression(RunLevelDefinition levelDefinition, RunGrowthSnapshot growthSnapshot, Action<RunLevelUpResult> applyLevelUp)
    {
        if (levelDefinition == null)
            throw new ArgumentNullException(nameof(levelDefinition));

        if (growthSnapshot == null)
            throw new ArgumentNullException(nameof(growthSnapshot));

        if (applyLevelUp == null)
            throw new ArgumentNullException(nameof(applyLevelUp));

        growthSnapshot.GrowthDefinition.ValidateFor(levelDefinition);

        _levelDefinition = levelDefinition;
        _growthSnapshot = growthSnapshot;
        _applyLevelUp = applyLevelUp;

        RunId = Guid.NewGuid();
    }

    /// <summary>
    /// 플레이어 초기화와 필수 연결 완료 후 경험치 수신 시작
    /// </summary>
    public void Start()
    {
        if (_started || _stopped)
            throw new InvalidOperationException("새로 생성한 런 성장 상태에서 한 번만 시작 가능");

        _started = true;

        OnChanged?.Invoke();
    }

    /// <summary>
    /// 추가 경험치 수신 중단과 필수 적용 대상 참조 해제
    /// </summary>
    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _applyLevelUp = null;
    }

    /// <summary>
    /// 확정된 경험치 보상 반영과 연속 레벨업 처리
    /// </summary>
    /// <param name="amount">지급할 유한한 0 이상의 경험치</param>
    /// <returns>활성 런에서 보상을 접수했는지 여부</returns>
    public bool TryAddExperience(float amount)
    {
        ValidateExperienceAmount(amount);

        if (!IsActive)
            return false;

        if (_processingExperience)
            throw new InvalidOperationException("경험치 처리 중 경험치 지급 재진입 불가");

        if (amount == 0f || IsMaxLevel)
            return true;

        float nextExperience = _experience + amount;

        if (float.IsInfinity(nextExperience))
            throw new OverflowException("런 경험치 범위 초과");

        _processingExperience = true;

        try
        {
            _experience = nextExperience;

            while (IsActive && !IsMaxLevel)
            {
                float requiredExperience = _levelDefinition.GetRequiredExperience(_level);

                if (_experience < requiredExperience)
                    break;

                int nextLevel = _level + 1;

                CharacterLevelGrowth growth = _growthSnapshot.GrowthDefinition.GetGrowthForLevel(nextLevel);

                CharacterStatValues contribution = CharacterGrowthCalculator.CalculateContribution(_growthSnapshot, nextLevel);

                RunLevelUpResult result = new RunLevelUpResult(RunId, _level, nextLevel, contribution, growth.AdditionalHealing);

                // 필수 적용 콜백에서 조회할 새 레벨과 잔여 경험치 선반영
                _experience -= requiredExperience;
                _level = nextLevel;

                if (IsMaxLevel)
                    _experience = 0f;

                // 필수 스탯·회복 적용 이후 UI와 연출 알림
                _applyLevelUp(result);
                OnLevelUp?.Invoke(result);
            }

            OnChanged?.Invoke();
            return true;
        }
        finally
        {
            _processingExperience = false;
        }
    }
}