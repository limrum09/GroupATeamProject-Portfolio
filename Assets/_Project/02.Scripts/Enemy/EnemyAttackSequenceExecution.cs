using System;

/// <summary>
/// 개별 적의 연속 공격 순서와 승인된 단계의 완료 관리
/// </summary>
public class EnemyAttackSequenceExecution
{
    SkillInstance[] _skills;
    CombatTarget _target;
    int _stepIndex;
    long _actionId;
    bool _actionCompleted;

    public bool IsRunning => _skills != null;

    void RequestCurrentStep(Func<SkillInstance, CombatTarget, bool> request)
    {
        // 즉시 완료되는 스킬의 동기 알림에 앞서 상태 초기화
        _actionId = 0;
        _actionCompleted = false;
        if (!request(_skills[_stepIndex], _target))
            Cancel();
    }

    /// <summary>
    /// 검증된 단계별 인스턴스로 연속 공격 시작
    /// </summary>
    /// <param name="skills">대표 스킬을 포함한 단계별 인스턴스</param>
    /// <param name="target">시작 시 선택한 대상</param>
    /// <param name="request">기존 Combat 요청 경로</param>
    public void Start(SkillInstance[] skills, CombatTarget target,
        Func<SkillInstance, CombatTarget, bool> request)
    {
        if (IsRunning)
            throw new InvalidOperationException("연속 공격 중 중복 시작 불가");

        _skills = skills;
        _target = target;
        _stepIndex = 0;
        RequestCurrentStep(request);
    }

    /// <summary>
    /// 현재 단계의 승인 액션 연결
    /// </summary>
    /// <param name="info">승인된 액션 정보</param>
    public void NotifyStarted(CombatActionInfo info)
    {
        if (!IsRunning)
            return;

        if (_actionId != 0 || info.Skill != _skills[_stepIndex].Definition)
        {
            Cancel();
            return;
        }

        _actionId = info.Id;
    }

    /// <summary>
    /// 현재 단계의 실행 성공 또는 중단 반영
    /// </summary>
    /// <param name="actionId">종료된 액션 식별자</param>
    /// <param name="result">액션 종료 결과</param>
    public void NotifyEnded(long actionId, CombatActionResult result)
    {
        if (!IsRunning || _actionId != actionId)
            return;

        if (result != CombatActionResult.Completed)
        {
            Cancel();
            return;
        }

        _actionCompleted = true;
    }

    /// <summary>
    /// 실행 성공과 모션 종료 후 추가 대기 없이 다음 단계 요청
    /// </summary>
    /// <param name="actionOrMotionPlaying">실행 또는 모션 진행 여부</param>
    /// <param name="request">기존 Combat 요청 경로</param>
    public void Tick(bool actionOrMotionPlaying,
        Func<SkillInstance, CombatTarget, bool> request)
    {
        if (!IsRunning || !_actionCompleted || actionOrMotionPlaying)
            return;

        _stepIndex++;
        if (_stepIndex >= _skills.Length)
        {
            Cancel();
            return;
        }

        RequestCurrentStep(request);
    }

    /// <summary>
    /// 후속 공격 예약과 진행 상태 정리
    /// </summary>
    public void Cancel()
    {
        _skills = null;
        _target = null;
        _stepIndex = 0;
        _actionId = 0;
        _actionCompleted = false;
    }
}
