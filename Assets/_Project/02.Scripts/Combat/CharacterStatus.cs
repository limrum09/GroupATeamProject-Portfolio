using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CharacterStatus : MonoBehaviour
{
    CharacterDefinition _definition;

    Guid _runId;
    CharacterStatValues _baseStats;

    // 마지막으로 소비한 성장 결과의 레벨
    int _lastAppliedGrowthLevel;
    float _currentHealth;
    bool _initialized;

    public event Action OnStatsChanged;
    public event Action<float, float> OnHealthChanged;
    public event Action<float> OnHealed;
    public event Action OnDied;

    public CharacterDefinition Definition => _definition;
    public Guid RunId => _runId;
    public bool IsInitialized => _initialized;

    public float MaxHealth { get; private set; }
    public float AttackPower { get; private set; }
    public float Defense { get; private set; }
    public float SpellPower { get; private set; }
    public float MoveSpeed { get; private set; }
    public float AttackSpeed { get; private set; }
    public float CriticalChance { get; private set; }
    public float CriticalDamageMultiplier { get; private set; }

    public float CurrentHealth => _currentHealth;
    public bool IsAlive => _initialized && _currentHealth > 0f;

    void Awake()
    {
        // 외부 소유자의 선행 초기화 요구
        RequireInitialized();

        if (_definition == null)
            throw new MissingReferenceException($"{name}: 외부 초기화에서 전달한 CharacterDefinition 필요");
    }

    void InitializeState(CharacterDefinition definition, CharacterStatValues baseStats, Guid runId, CharacterStatValues growth, int appliedGrowthLevel)
    {
        if (definition == null)
            throw new MissingReferenceException($"{name}: CharacterDefinition 연결 필요");

        CharacterStatValues totalStats = baseStats.Add(growth);

        // 계산과 유효성 검사 완료 후 상태 교체
        _definition = definition;
        _baseStats = baseStats;
        _runId = runId;
        _lastAppliedGrowthLevel = appliedGrowthLevel;

        ApplyStats(totalStats);

        _currentHealth = MaxHealth;
        _initialized = true;

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    static CharacterStatValues ReadBaseStats(CharacterDefinition definition)
    {
        return new CharacterStatValues(definition.MaxHealth, definition.AttackPower, definition.Defense, definition.SpellPower, definition.MoveSpeed, definition.BaseAttackSpeed, definition.CriticalChance, definition.CriticalDamageMultiplier);
    }

    void ApplyStats(CharacterStatValues values)
    {
        MaxHealth = Mathf.Max(1f, values.MaxHealth);
        AttackPower = values.AttackPower;
        Defense = values.Defense;
        SpellPower = values.SpellPower;
        MoveSpeed = Mathf.Max(0.1f, values.MoveSpeed);
        AttackSpeed = Mathf.Max(0.01f, values.AttackSpeed);
        CriticalChance = Mathf.Clamp01(values.CriticalChance);
        CriticalDamageMultiplier = Mathf.Max(1f, values.CriticalDamageMultiplier);
    }

    void RequireInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException($"{name}: 전투 상태 초기화 완료 필요");
        }
    }

    /// <summary>
    /// 계산이 끝난 최종 피해를 적용하고 실제 감소한 체력을 반환한다.
    /// </summary>
    internal float ApplyDamage(float amount)
    {
        if (!IsAlive || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
            return 0f;

        float applied = Mathf.Min(_currentHealth, amount);
        _currentHealth -= applied;

        // 이벤트 구독자가 다른 피해를 발생시켜도 이번 결과는 유지한다.
        bool killed = _currentHealth <= 0f;

        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);

        if (killed)
            OnDied?.Invoke();

        return applied;
    }

    /// <summary>
    /// 일반 액터 생성 또는 풀 대여 시 기본 스탯과 체력 초기화
    /// </summary>
    /// <param name="definition">이번 액터에 적용할 기본 정의</param>
    public void InitializeActor(CharacterDefinition definition)
    {
        if (_runId != Guid.Empty)
        {
            throw new InvalidOperationException($"{name}: 런 플레이어는 InitializeForRun으로 초기화 필요");
        }

        if (definition == null)
            throw new MissingReferenceException($"{name}: CharacterDefinition 연결 필요");

        InitializeState(definition, ReadBaseStats(definition), Guid.Empty, default, 0);
    }

    /// <summary>
    /// 전달받은 최종 기본 능력치로 일반 액터 초기화
    /// </summary>
    /// <param name="definition">공격 설정에 사용할 원본 정의</param>
    /// <param name="baseStats">이번 소환의 최종 기본 능력치</param>
    public void InitializeActor(CharacterDefinition definition, CharacterStatValues baseStats)
    {
        if (_runId != Guid.Empty)
            throw new InvalidOperationException($"{name}: 런 플레이어는 InitializeForRun으로 초기화 필요");

        InitializeState(definition, baseStats, Guid.Empty, default, 0);
    }

    /// <summary>
    /// 새 런의 성장 기여분을 적용하고 최대 체력으로 시작</summary>
    /// <param name="definition">선택한 캐릭터의 기본 정
    /// 의</param>
    /// <param name="runId">이번 런의 식별자</param>
    /// <param name="initialGrowth">1레벨에서 적용할 전체 성장 기여분</param>
    public void InitializeForRun(CharacterDefinition definition, Guid runId, CharacterStatValues initialGrowth)
    {
        if (definition == null)
            throw new MissingReferenceException($"{name}: CharacterDefinition 연결 필요");

        if (runId == Guid.Empty)
            throw new ArgumentException("유효한 런 식별자 필요", nameof(runId));

        initialGrowth.Validate();

        if (_initialized && _runId == runId)
        {
            if (_definition != definition)
                throw new InvalidOperationException($"{name}: 동일 런에서 기본 캐릭터 정의 변경 불가");

            // 동일 런의 재연결에 따른 체력 회복과 성장 초기화 방지
            return;
        }

        InitializeState(definition, ReadBaseStats(definition), runId, initialGrowth, 1);
    }

    /// <summary>
    /// 미적용 레벨업 결과의 성장 교체와 체력 회복을 한 번 적용
    /// </summary>
    /// <param name="result">현재 런의 다음 단계 레벨업 결과</param>
    public void ApplyLevelUp(RunLevelUpResult result)
    {
        RequireInitialized();

        if (_runId == Guid.Empty)
            throw new InvalidOperationException($"{name}: 런 성장 적용 전 InitializeForRun 호출 필요");

        if (result.RunId != _runId)
            throw new InvalidOperationException($"{name}: 다른 런의 레벨업 결과 적용 불가");

        // 이미 소비한 결과의 재전달 무시
        if (result.NewLevel <= _lastAppliedGrowthLevel)
            return;

        if (result.PreviousLevel != _lastAppliedGrowthLevel || result.NewLevel - _lastAppliedGrowthLevel != 1)
            throw new InvalidOperationException($"{name}: 레벨업 결과의 순차 적용 필요");

        // 기존 최종 스탯에 더하지 않고 기본값부터 다시 계산
        CharacterStatValues nextStats = _baseStats.Add(result.GrowthContribution);

        float nextMaxHealth = Mathf.Max(1f, nextStats.MaxHealth);
        float nextHealth = 0f;

        if (IsAlive)
        {
            float maxHealthIncrease = Mathf.Max(0f, nextMaxHealth - MaxHealth);

            // 큰 값의 합산에서도 float 범위 초과 전 체력 상한 적용
            double restoredHealth = (double)_currentHealth + maxHealthIncrease + result.AdditionalHealing;

            nextHealth = (float)Math.Min(nextMaxHealth, restoredHealth);
        }

        ApplyStats(nextStats);
        _currentHealth = nextHealth;

        // 이벤트에서 같은 결과를 다시 전달해도 재적용되지 않도록 선반영
        _lastAppliedGrowthLevel = result.NewLevel;

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);
    }


    public float Heal(float amount)
    {
        if (!IsAlive || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
            return 0f;

        float restored = Mathf.Min(amount, MaxHealth - _currentHealth);

        if (restored <= 0f)
            return 0f;

        _currentHealth += restored;
        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);

        OnHealed?.Invoke(restored);

        return restored;
    }
}