using System;
using UnityEngine;

/// <summary>
/// 접촉 시 정지하는 돌진 공격 설정
/// </summary>
[CreateAssetMenu(
    menuName = "Project/Skills/Charge Attack",
    fileName = "_ChargeAttackSkill")]
public sealed class ChargeAttackSkillDefinition : SkillDefinition
{
    [Header("----- 돌진 이동 -----")]
    [Tooltip("타겟까지 거리가 짧아도 이동할 최소 돌진 거리")]
    [SerializeField, Min(0.1f)] float _distance = 6f;
    [SerializeField, Min(0.01f)] float _duration = 0.5f;

    [Header("----- 접촉 정지 -----")]
    [SerializeField] LayerMask _stopMask;

    [Header("----- 피해 계수 -----")]
    [SerializeField, Min(0f)] float _damageMultiplier = 1.5f;
    [SerializeField, Min(0f)] float _spellPowerMultiplier;

    public float MinimumDistance => _distance;
    public float Duration => _duration;
    public LayerMask StopMask => _stopMask;
    public float DamageMultiplier => _damageMultiplier;
    public float SpellPowerMultiplier => _spellPowerMultiplier;

    static bool IsNonNegativeFinite(float value)
    {
        return value >= 0f && !float.IsInfinity(value);
    }

    /// <summary>
    /// Inspector 입력값의 최소 범위 보정
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();

        _distance = Mathf.Max(0.1f, _distance);
        _duration = Mathf.Max(0.01f, _duration);
        _damageMultiplier = Mathf.Max(0f, _damageMultiplier);
        _spellPowerMultiplier = Mathf.Max(0f, _spellPowerMultiplier);
    }

    /// <summary>
    /// 돌진 이동과 피해 및 조준 설정 검사
    /// </summary>
    public void Validate()
    {
        if (!IsNonNegativeFinite(_distance) || _distance < 0.1f ||
            !IsNonNegativeFinite(_duration) || _duration < 0.01f ||
            !IsNonNegativeFinite(_distance / _duration) ||
            !IsNonNegativeFinite(CastTime) ||
            !IsNonNegativeFinite(Cooldown) ||
            !IsNonNegativeFinite(CastTime + _duration) ||
            !IsNonNegativeFinite(Range) || Range <= 0f ||
            !IsNonNegativeFinite(Range * Range) ||
            !IsNonNegativeFinite(_damageMultiplier) ||
            !IsNonNegativeFinite(_spellPowerMultiplier))
        {
            throw new InvalidOperationException(
                $"{name}: 돌진 거리·시간·사거리·피해 계수 확인 필요");
        }

        if (_stopMask.value == 0 || TargetMask.value == 0)
        {
            throw new InvalidOperationException(
                $"{name}: 정지 대상과 피격 대상 Mask 설정 필요");
        }

        if (TargetMode != SkillTargetMode.None ||
            AimMode != SkillAimMode.RequestedDirection)
        {
            throw new InvalidOperationException(
                $"{name}: Target Mode None과 Aim Mode Requested Direction 필요");
        }

        if (RotationMode != SkillRotationMode.FaceDirection &&
            RotationMode != SkillRotationMode.KeepFacing)
        {
            throw new InvalidOperationException(
                $"{name}: 유효한 회전 방식 필요");
        }
    }
}
