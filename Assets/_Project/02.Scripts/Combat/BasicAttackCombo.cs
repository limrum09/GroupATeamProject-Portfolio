using System;
using UnityEngine;

/// <summary>
/// 기본 공격 콤보의 현재 단계와 연결 시각 관리
/// </summary>
public class BasicAttackCombo
{
    readonly SkillInstance _skill;
    readonly SkillInstance[] _stepSkills;
    readonly AnimationClip[] _motionClips;
    readonly float[] _executeTimes;
    readonly float[] _linkTimes;
    readonly float[] _recoveryTimes;
    readonly float _resetDelay;

    int _index;
    double _nextAttackAt;
    double _expiresAt;

    public BasicAttackComboDefinition Definition { get; }
    // UI와 레벨 표시 기준인 기본 공격 스킬 인스턴스
    public SkillInstance Skill => _skill;
    // 단계 설정 여부, false면 기본 공격 스킬만 사용하는 단일 공격
    public bool IsCombo => _stepSkills.Length > 0;
    public SkillInstance CurrentSkill => IsCombo ? _stepSkills[_index] : _skill;
    // 현재 단계 모션, 단일 공격이면 null로 스킬 MotionClip 사용
    public AnimationClip CurrentMotionClip => IsCombo ? _motionClips[_index] : null;
    // 모션 원래 속도 기준 현재 단계 실행 시각(초), 공격속도 영향 없음
    public float CurrentExecuteTime => _executeTimes[_index];
    public double NextAttackAt => _nextAttackAt;

    /// <summary>
    /// 콤보 설정 검증과 단계별 기본 공격 인스턴스 준비
    /// </summary>
    /// <param name="definition">사용할 콤보 정의</param>
    public BasicAttackCombo(BasicAttackComboDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        definition.Validate();

        int count = definition.Steps.Count;

        Definition = definition;
        _skill = new SkillInstance(definition.Skill, 1, true);
        _stepSkills = new SkillInstance[count];
        _motionClips = new AnimationClip[count];
        _executeTimes = new float[count];
        _linkTimes = new float[count];
        _recoveryTimes = new float[count];
        _resetDelay = definition.ResetDelay;

        for (int i = 0; i < count; i++)
        {
            BasicAttackComboStep step = definition.Steps[i];
            AnimationClip motion = definition.GetStepMotion(i);

            // 단계별 지정 스킬이 없으면 기본 공격 인스턴스 공유
            _stepSkills[i] = step.SkillOverride != null ? new SkillInstance(step.SkillOverride, 1, true) : _skill;
            _motionClips[i] = motion;

            // 모션 길이 대비 비율을 모션 원래 속도 기준 시간으로 변환
            _executeTimes[i] = step.ExecutePoint * motion.length;
            _linkTimes[i] = step.LinkPoint * motion.length;
            _recoveryTimes[i] = step.RecoveryTime;
        }
    }

    void RequireCombo()
    {
        if (!IsCombo)
            throw new InvalidOperationException($"{Definition.name}: 콤보 단계가 없는 단일 기본 공격");
    }

    /// <summary>
    /// 이번 공격의 단계 확정, 유예 시간 초과 시 1단계 복귀
    /// </summary>
    /// <param name="now">공격 시작 시각</param>
    public void Begin(double now)
    {
        RequireCombo();

        if (now > _expiresAt)
            _index = 0;
    }

    /// <summary>
    /// 현재 단계 실행 완료 후 다음 단계로 진행
    /// </summary>
    /// <param name="startedAt">완료한 단계의 시작 시각</param>
    /// <param name="attackSpeedRate">기본 공격속도 대비 현재 공격속도 비율, 공백에만 적용</param>
    public void Complete(double startedAt, float attackSpeedRate)
    {
        RequireCombo();

        if (attackSpeedRate <= 0f || float.IsNaN(attackSpeedRate) || float.IsInfinity(attackSpeedRate))
            throw new ArgumentOutOfRangeException(nameof(attackSpeedRate), "0보다 큰 유한한 공격속도 비율 필요");

        // 모션 구간은 고정, 연결 지점 이후 공백만 공격속도 비율로 단축
        double linkAt = startedAt + _linkTimes[_index] + _recoveryTimes[_index] / attackSpeedRate;

        _nextAttackAt = linkAt;
        _expiresAt = linkAt + _resetDelay;
        _index = (_index + 1) % _stepSkills.Length;
    }

    /// <summary>
    /// 다음 공격을 1단계부터 시작하도록 초기화
    /// </summary>
    public void Reset()
    {
        // 이동 취소 직후 재공격으로 연결 시각을 건너뛰지 않도록 NextAttackAt 유지
        _index = 0;
        _expiresAt = 0d;
    }
}
