using System;
using UnityEngine;

/// <summary>
/// 커서 방향 돌진 후 도착 지점 범위 피해 슬롯 스킬 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Skills/Dash Strike", fileName = "_DashStrikeSkill")]
public sealed class DashStrikeSkillDefinition : SkillDefinition, ISkillMovementSettings
{
    [Header("----- 돌진 이동 -----")]
    [SerializeField, Min(0.1f)] float _minDistance = 1f;
    [SerializeField, Min(0.1f)] float _distance = 6f;
    [SerializeField, Min(0.01f)] float _dashDuration = 0.2f;
    [SerializeField] LayerMask _blockingMask;

    [Header("----- 도착 범위 피해 -----")]
    [SerializeField] AreaEffectDefinition _arrivalAreaEffect;

    [Header("----- 선택 도착 이펙트 -----")]
    [SerializeField] ParticleSystem _arrivalEffectPrefab;

    // 돌진 공격은 항상 대쉬 이동
    public MovementSkillMode Mode => MovementSkillMode.Dash;
    public float MinDistance => _minDistance;
    public float Distance => _distance;
    public float DashDuration => _dashDuration;
    public LayerMask BlockingMask => _blockingMask;
    public AreaEffectDefinition ArrivalAreaEffect => _arrivalAreaEffect;
    public ParticleSystem ArrivalEffectPrefab => _arrivalEffectPrefab;

    protected override void OnValidate()
    {
        base.OnValidate();
        _distance = Mathf.Max(0.1f, _distance);
        _minDistance = Mathf.Clamp(_minDistance, 0.1f, _distance);
        _dashDuration = Mathf.Max(0.01f, _dashDuration);
    }

    static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    /// <summary>
    /// 돌진 거리·시간, 도착 범위, 조준 설정 검사
    /// </summary>
    public void Validate()
    {
        if (!IsPositiveFinite(_minDistance) || !IsPositiveFinite(_distance) || _minDistance > _distance ||
            !IsPositiveFinite(_dashDuration) || !IsPositiveFinite(_distance / _dashDuration))
        {
            throw new InvalidOperationException($"{name}: 유효한 돌진 최소·최대 거리와 시간 필요");
        }

        if (_blockingMask.value == 0)
            throw new InvalidOperationException($"{name}: 벽 판정 Mask 설정 필요");

        if (_arrivalAreaEffect == null)
            throw new MissingReferenceException($"{name}: 도착 Area Effect 연결 필요");

        _arrivalAreaEffect.Validate();

        if (TargetMode != SkillTargetMode.None || AimMode != SkillAimMode.RequestedDirection)
            throw new InvalidOperationException($"{name}: Target Mode None과 Aim Mode Requested Direction 필요");
    }
}
