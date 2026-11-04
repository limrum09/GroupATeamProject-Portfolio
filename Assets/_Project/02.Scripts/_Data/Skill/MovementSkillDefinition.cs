using System;
using UnityEngine;

/// <summary>
/// 이동 스킬의 이동 방식
/// </summary>
public enum MovementSkillMode
{
    Dash,
    Teleport
}

/// <summary>
/// 캐릭터 고정 이동 스킬의 설정 데이터
/// </summary>
[CreateAssetMenu(menuName = "Project/Skills/Movement Skill", fileName = "_MovementSkill")]
public sealed class MovementSkillDefinition : SkillDefinition
{
    [Header("----- 이동 설정 -----")]
    [SerializeField] MovementSkillMode _mode;
    [SerializeField, Min(0.1f)] float _distance = 5f;
    [SerializeField, Min(0.01f)] float _dashDuration = 0.15f;
    [SerializeField] LayerMask _blockingMask;

    [Header("----- 선택 도착 이펙트 -----")]
    [SerializeField] ParticleSystem _arrivalEffectPrefab;

    public MovementSkillMode Mode => _mode;
    public float Distance => _distance;
    public float DashDuration => _dashDuration;
    public LayerMask BlockingMask => _blockingMask;
    public ParticleSystem ArrivalEffectPrefab => _arrivalEffectPrefab;

    protected override void OnValidate()
    {
        base.OnValidate();
        _distance = Mathf.Max(0.1f, _distance);
        _dashDuration = Mathf.Max(0.01f, _dashDuration);
    }

    static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    static bool IsNonNegativeFinite(float value)
    {
        return value >= 0f && !float.IsInfinity(value);
    }

    /// <summary>
    /// 이동 스킬의 시간과 이동 및 조준 설정 검사
    /// </summary>
    public void Validate()
    {
        if (_mode != MovementSkillMode.Dash && _mode != MovementSkillMode.Teleport)
            throw new InvalidOperationException($"{name}: 지원하지 않는 이동 방식");

        if (!IsPositiveFinite(_distance) || !IsNonNegativeFinite(CastTime) || !IsNonNegativeFinite(Cooldown))
            throw new InvalidOperationException($"{name}: 유효한 이동 거리와 시전 시간 및 쿨타임 필요");

        if (_mode == MovementSkillMode.Dash && (!IsPositiveFinite(_dashDuration) || !IsPositiveFinite(_distance / _dashDuration)))
            throw new InvalidOperationException($"{name}: 유효한 대쉬 시간과 속도 필요");

        if (_blockingMask.value == 0)
            throw new InvalidOperationException($"{name}: 벽 차단 Mask 설정 필요");

        if (TargetMode != SkillTargetMode.None || AimMode != SkillAimMode.RequestedDirection || (RotationMode != SkillRotationMode.FaceDirection && RotationMode != SkillRotationMode.KeepFacing))
            throw new InvalidOperationException($"{name}: 대상 없음과 요청 방향 및 유효한 회전 방식 설정 필요");
    }
}