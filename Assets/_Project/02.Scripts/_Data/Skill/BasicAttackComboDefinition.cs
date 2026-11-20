using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기본 공격 스킬과 순서대로 반복하는 타격 모션 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Skills/Basic Attack Combo", fileName = "_BasicAttackCombo")]
public class BasicAttackComboDefinition : ScriptableObject
{
    [Header("----- 기본 공격 스킬 -----")]
    [Tooltip("모든 단계의 기본 피해와 이펙트, UI 표시 기준")]
    [SerializeField] TargetDamageSkillDefinition _skill;

    [Header("----- 콤보 단계 -----")]
    [Tooltip("비우면 콤보 없이 스킬 Cast Time과 공격속도 간격으로 반복")]
    [SerializeField] BasicAttackComboStep[] _steps = Array.Empty<BasicAttackComboStep>();

    [Header("----- 콤보 유지 -----")]
    [Tooltip("다음 단계 연결 가능 시각 이후 1단계로 돌아가기까지의 유예 시간(초)")]
    [SerializeField, Min(0.01f)] float _resetDelay = 0.5f;

    public TargetDamageSkillDefinition Skill => _skill;
    public IReadOnlyList<BasicAttackComboStep> Steps => _steps;
    public float ResetDelay => _resetDelay;
    // 모든 단계 공통 사거리, Validate에서 단계별 스킬과 일치 검증
    public float Range => _skill.Range;
    // 모든 단계 공통 장애물 마스크, Validate에서 단계별 스킬과 일치 검증
    public LayerMask BlockingMask => _skill.BlockingMask;

    /// <summary>
    /// 지정 스킬의 기본 공격 사용 여부
    /// </summary>
    /// <param name="skill">확인할 스킬 정의</param>
    /// <returns>기본 공격 스킬 또는 단계별 지정 스킬이면 true</returns>
    public bool Contains(SkillDefinition skill)
    {
        if (skill == null)
            return false;

        if (skill == _skill)
            return true;

        foreach (BasicAttackComboStep step in _steps)
        {
            if (step.SkillOverride == skill)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 지정 단계에서 실제로 사용할 스킬 조회
    /// </summary>
    /// <param name="index">단계 번호, 0부터</param>
    /// <returns>단계별 지정 스킬, 없으면 기본 공격 스킬</returns>
    public TargetDamageSkillDefinition GetStepSkill(int index)
    {
        TargetDamageSkillDefinition skillOverride = _steps[index].SkillOverride;

        return skillOverride != null ? skillOverride : _skill;
    }

    /// <summary>
    /// 지정 단계에서 실제로 재생할 모션 조회
    /// </summary>
    /// <param name="index">단계 번호, 0부터</param>
    /// <returns>단계 모션, 없으면 사용 스킬의 MotionClip</returns>
    public AnimationClip GetStepMotion(int index)
    {
        AnimationClip motion = _steps[index].MotionClip;

        return motion != null ? motion : GetStepSkill(index).MotionClip;
    }

    /// <summary>
    /// 기본 공격 스킬과 단계 구성 검증
    /// </summary>
    public void Validate()
    {
        if (_skill == null)
            throw new MissingReferenceException($"{name}: 기본 공격 스킬 연결 필요");

        SkillExecutor.ValidateDefinition(_skill);

        if (_resetDelay <= 0f || float.IsInfinity(_resetDelay))
            throw new InvalidOperationException($"{name}: 콤보 유예 시간은 0보다 큰 유한한 값 필요");

        // 단계 없음은 콤보 없는 단일 기본 공격
        for (int i = 0; i < _steps.Length; i++)
        {
            BasicAttackComboStep step = _steps[i];

            if (step.SkillOverride != null)
            {
                SkillExecutor.ValidateDefinition(step.SkillOverride);

                // 단계마다 사거리가 다르면 콤보 도중 대상 판정이 바뀜
                if (!Mathf.Approximately(step.SkillOverride.Range, _skill.Range) || step.SkillOverride.BlockingMask != _skill.BlockingMask)
                    throw new InvalidOperationException($"{name}: {i}번 단계 지정 스킬의 사거리와 장애물 마스크는 기본 공격 스킬과 같아야 함");
            }

            // 실행 지점과 연결 지점을 시간으로 바꾸기 위해 모션 길이 필요
            AnimationClip motion = GetStepMotion(i);

            if (motion == null)
                throw new MissingReferenceException($"{name}: {i}번 단계 모션 또는 사용 스킬의 MotionClip 필요");

            if (motion.length <= 0f || motion.isLooping)
                throw new InvalidOperationException($"{name}: {i}번 단계 모션은 길이가 양수인 비반복 클립 필요");

            if (step.ExecutePoint < 0f || step.ExecutePoint > 1f || step.LinkPoint <= 0f || step.LinkPoint > 1f)
                throw new InvalidOperationException($"{name}: {i}번 단계 실행 지점은 0~1, 연결 지점은 0 초과 1 이하 필요");

            // 실행 이전의 다음 단계 시작 방지
            if (step.LinkPoint < step.ExecutePoint)
                throw new InvalidOperationException($"{name}: {i}번 단계 연결 지점은 실행 지점 이상 필요");

            if (step.RecoveryTime < 0f || float.IsNaN(step.RecoveryTime) || float.IsInfinity(step.RecoveryTime))
                throw new InvalidOperationException($"{name}: {i}번 단계 공백은 0 이상의 유한한 값 필요");
        }
    }
}
