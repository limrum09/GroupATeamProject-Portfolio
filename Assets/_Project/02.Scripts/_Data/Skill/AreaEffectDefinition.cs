using System;
using UnityEngine;

/// <summary>
/// 범위 효과의 피해 실행 방식
/// </summary>
public enum AreaEffectDamageMode
{
    Once = 0,
    Repeat = 1
}

/// <summary>
/// 반복 범위 효과의 종료 기준
/// </summary>
public enum AreaEffectEndMode
{
    Duration = 0,
    PulseCount = 1
}

/// <summary>
/// 반복 범위 효과의 첫 타격 시점
/// </summary>
public enum AreaEffectFirstPulse
{
    Immediate = 0,
    AfterInterval = 1
}

/// <summary>
/// 공통 범위 효과의 타격 범위와 피해 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Skills/Area Effect", fileName = "AreaEffect")]
public sealed class AreaEffectDefinition : ScriptableObject
{
    [Header("----- 타격 범위 -----")]
    [SerializeField] CombatHitbox _hitbox = new CombatHitbox();

    [Header("----- 피해 실행 방식 -----")]
    [SerializeField] AreaEffectDamageMode _damageMode = AreaEffectDamageMode.Once;

    [Header("----- 반복형 설정 -----")]
    [SerializeField] AreaEffectEndMode _endMode = AreaEffectEndMode.Duration;
    [SerializeField] AreaEffectFirstPulse _firstPulse = AreaEffectFirstPulse.Immediate;
    [SerializeField, Min(0.01f)] float _pulseInterval = 0.5f;
    [SerializeField, Min(0.01f)] float _duration = 3f;
    [SerializeField, Min(1)] int _pulseCount = 5;

    [Header("----- 대상과 차단 -----")]
    [SerializeField] LayerMask _targetMask = ~0;
    [SerializeField] LayerMask _blockingMask;

    [Header("----- 피해 계수 -----")]
    [SerializeField, Min(0f)] float _damageMultiplier = 2f;
    [SerializeField, Min(0f)] float _spellPowerMultiplier;

    public CombatHitbox Hitbox => _hitbox;
    public AreaEffectDamageMode DamageMode => _damageMode;
    public AreaEffectEndMode EndMode => _endMode;
    public AreaEffectFirstPulse FirstPulse => _firstPulse;
    public float PulseInterval => _pulseInterval;
    public float Duration => _duration;
    public int PulseCount => _pulseCount;
    /// 생성 시점의 범위 타격 여부, 1회형은 항상 즉시 타격
    public bool HasImmediatePulse => _damageMode == AreaEffectDamageMode.Once || _firstPulse == AreaEffectFirstPulse.Immediate;
    public LayerMask TargetMask => _targetMask;
    public LayerMask BlockingMask => _blockingMask;
    public float DamageMultiplier => _damageMultiplier;
    public float SpellPowerMultiplier => _spellPowerMultiplier;

    void OnValidate()
    {
        if (_hitbox != null)
            _hitbox.ClampValues();

        _damageMultiplier = Mathf.Max(0f, _damageMultiplier);
        _spellPowerMultiplier = Mathf.Max(0f, _spellPowerMultiplier);
        _pulseInterval = Mathf.Max(0.01f, _pulseInterval);
        _duration = Mathf.Max(0.01f, _duration);
        _pulseCount = Mathf.Max(1, _pulseCount);

        ValidateTiming();
    }

    void ValidateTiming()
    {
        if (_damageMode == AreaEffectDamageMode.Once)
            return;

        if (_damageMode != AreaEffectDamageMode.Repeat)
            throw new InvalidOperationException($"{name}: 지원하지 않는 피해 실행 방식");

        if (_firstPulse != AreaEffectFirstPulse.Immediate && _firstPulse != AreaEffectFirstPulse.AfterInterval)
            throw new InvalidOperationException($"{name}: 지원하지 않는 첫 타격 시점");

        if (!IsPositiveFinite(_pulseInterval))
            throw new InvalidOperationException($"{name}: 유한한 양수의 타격 간격 필요");

        switch (_endMode)
        {
            case AreaEffectEndMode.Duration:
                if (!IsPositiveFinite(_duration))
                    throw new InvalidOperationException($"{name}: 유한한 양수의 지속시간 필요");

                break;

            case AreaEffectEndMode.PulseCount:
                if (_pulseCount < 1)
                    throw new InvalidOperationException($"{name}: 1 이상의 타격 횟수 필요");

                break;

            default:
                throw new InvalidOperationException($"{name}: 지원하지 않는 종료 기준");
        }
    }

    /// <summary>
    /// 실행에 필요한 범위와 피해 설정 검사
    /// </summary>
    public void Validate()
    {
        if (_hitbox == null)
            throw new MissingReferenceException($"{name}: Hitbox 설정 필요");

        if (!IsFinite(_hitbox.Offset))
            throw new InvalidOperationException($"{name}: 유한한 Hitbox Offset 필요");

        switch (_hitbox.HitShape)
        {
            case CombatHitShape.Box:
                Vector3 size = _hitbox.BoxSize;

                if (!IsPositiveFinite(size.x) || !IsPositiveFinite(size.y) || !IsPositiveFinite(size.z))
                    throw new InvalidOperationException($"{name}: 유한한 양수 Box Size 필요");

                break;

            case CombatHitShape.Sphere:
                if (!IsPositiveFinite(_hitbox.Radius))
                    throw new InvalidOperationException($"{name}: 유한한 양수 Radius 필요");

                break;

            default:
                throw new InvalidOperationException($"{name}: 지원하지 않는 Hitbox 형태");
        }

        if (_targetMask.value == 0)
            throw new InvalidOperationException($"{name}: Target Mask 설정 필요");

        if (!IsNonNegativeFinite(_damageMultiplier) || !IsNonNegativeFinite(_spellPowerMultiplier))
            throw new InvalidOperationException($"{name}: 유한한 0 이상의 피해 계수 필요");
    }

    static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    static bool IsNonNegativeFinite(float value)
    {
        return value >= 0f && !float.IsInfinity(value);
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}