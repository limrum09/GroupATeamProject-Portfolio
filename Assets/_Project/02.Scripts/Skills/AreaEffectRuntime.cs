using UnityEngine;

/// <summary>
/// 범위 효과의 위치와 반복 타격 및 종료 관리
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class AreaEffectRuntime : MonoBehaviour
{
    SkillExecutionContext _execution;
    AreaEffectDefinition _definition;

    CombatTarget _followCaster;
    Vector3 _followOffset;

    Vector3 _origin;
    Quaternion _rotation;
    float _power;

    double _endsAt;
    double _nextPulseAt;
    int _pulseCount;
    bool _finished;

    // 파괴된 추적 대상이 고정형으로 전환되는 상황 방지
    bool FollowsCaster => !object.ReferenceEquals(_followCaster, null);

    void Update()
    {
        if (_finished || FollowsCaster)
            return;

        Tick();
    }

    void LateUpdate()
    {
        if (_finished || !FollowsCaster)
            return;

        Tick();
    }

    void OnDisable()
    {
        Finish();
    }

    bool CanRemainActive()
    {
        return _execution.CanContinue && (!FollowsCaster || IsFollowCasterValid(_execution, _followCaster));
    }

    void Tick()
    {
        if (_finished)
            return;

        if (!CanRemainActive())
        {
            Finish();
            return;
        }

        if (Time.timeScale <= 0f)
            return;

        double now = Time.timeAsDouble;

        // 종료 시각과 같은 시점의 타격은 제외
        if (_definition.EndMode == AreaEffectEndMode.Duration && now >= _endsAt)
        {
            Finish();
            return;
        }

        if (FollowsCaster)
        {
            // 발동 기준점의 오프셋과 발동 당시 회전 유지
            _origin = _followCaster.Position + _followOffset;
            transform.SetPositionAndRotation(_origin, _rotation);
        }

        if (now < _nextPulseAt)
            return;

        // 지연된 회차를 한 프레임에 몰아서 적용하지 않는 방식
        _nextPulseAt = now + _definition.PulseInterval;
        _pulseCount++;

        AreaEffectExecutor.ExecutePulse(_execution, _origin, _rotation, Vector3.zero, _definition.Hitbox, _definition.TargetMask, _definition.BlockingMask, _power);

        // 시작한 한 회차는 완료하고 이후 회차의 지속 여부 검사
        if (!CanRemainActive() || (_definition.EndMode == AreaEffectEndMode.PulseCount && _pulseCount >= _definition.PulseCount))
            Finish();
    }

    void Finish()
    {
        if (_finished)
            return;

        _finished = true;

        try
        {
            // 초기화 전 정리 가능성을 포함한 종료 처리
            if (_execution != null)
                _execution.PublishAreaEnded();
        }
        finally
        {
            Destroy(gameObject);
        }
    }

    public void Initialize(SkillExecutionContext execution, AreaEffectDefinition definition, Vector3 origin, Quaternion rotation, float power, double startedAt, int initialPulseCount, CombatTarget followCaster)
    {
        _execution = execution;
        _definition = definition;

        _followCaster = followCaster;
        _followOffset = FollowsCaster
            ? origin - execution.SourcePosition
            : Vector3.zero;

        _origin = origin;
        _rotation = rotation;
        _power = power;

        _endsAt = startedAt + definition.Duration;
        _nextPulseAt = startedAt + definition.PulseInterval;
        _pulseCount = initialPulseCount;
    }

    /// <summary>
    /// 발동 당시 시전자의 생존과 활성화 식별값 및 씬 검사
    /// </summary>
    /// <param name="execution">발동 당시 실행 정보</param>
    /// <param name="caster">추적할 시전자</param>
    /// <returns>동일한 시전자를 계속 추적할 수 있는지 여부</returns>
    public static bool IsFollowCasterValid(SkillExecutionContext execution, CombatTarget caster)
    {
        return caster != null && caster.IsAlive && caster.Source.Id == execution.Source.Id && caster.gameObject.scene == execution.SourceScene;
    }
}