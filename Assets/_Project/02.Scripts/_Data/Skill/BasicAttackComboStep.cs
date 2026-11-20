using System;
using UnityEngine;

/// <summary>
/// 기본 공격 콤보 한 단계의 모션과 실행 지점
/// </summary>
[Serializable]
public struct BasicAttackComboStep
{
    [Tooltip("이 단계의 모션, 비우면 사용 스킬의 MotionClip")]
    [SerializeField] AnimationClip _motionClip;
    [Tooltip("모션 원래 속도 기준 길이 대비 스킬 실행 지점, 근접은 타격, 투사체는 발사 시점")]
    [SerializeField, Range(0f, 1f)] float _executePoint;
    [Tooltip("모션 원래 속도 기준 길이 대비 공백 시작 지점")]
    [SerializeField, Range(0f, 1f)] float _linkPoint;
    [Tooltip("연결 지점 이후 다음 공격까지의 공백(초), 기본 공격속도 기준, 공격속도 비율만큼 단축")]
    [SerializeField, Min(0f)] float _recoveryTime;
    [Tooltip("이 단계에만 사용할 스킬, 비우면 콤보의 기본 공격 스킬")]
    [SerializeField] TargetDamageSkillDefinition _skillOverride;

    public AnimationClip MotionClip => _motionClip;
    public float ExecutePoint => _executePoint;
    public float LinkPoint => _linkPoint;
    public float RecoveryTime => _recoveryTime;
    public TargetDamageSkillDefinition SkillOverride => _skillOverride;
}
