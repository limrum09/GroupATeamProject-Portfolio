using UnityEngine;

/// <summary>적 행동 상태 구분</summary>
public enum EnemyState
{
    Idle,
    Roaming,
    Tracking,
    Attack,
    Hit,
    Dead,
    Emergence,
    Confrontation
}

/// <summary>적 상태의 진입과 프레임 처리 및 종료 계약</summary>
public interface IEnemyState
{
    /// <summary>상태 진입 처리</summary>
    void Enter();
    /// <summary>현재 상태의 프레임 처리</summary>
    void Update();
    /// <summary>상태 종료 처리</summary>
    void Exit();
}

/// <summary>소환 위치 복귀와 기본 대기 및 전투 진입 판단</summary>
public class IdleState : IEnemyState
{
    readonly EnemyAI _ai;
    float _elapsed;
    float _retryRemaining;
    bool _waitingForReturnMove;
    bool _waitingForIdleEnd;

    /// <summary>대기 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public IdleState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>대기 시간과 복귀 진행 상태 초기화</summary>
    public void Enter()
    {
        _elapsed = 0f;
        _retryRemaining = 0f;
        _waitingForReturnMove = false;
        _waitingForIdleEnd = false;
    }

    /// <summary>대상 감지와 소환 위치 복귀 및 배회 진입 판단</summary>
    public void Update()
    {
        // 공격 가능한 거리에서는 복귀를 취소하고 공격 상태 진입
        if (_ai.IsWithinAttackRange())
        {
            _ai.ChangeState(EnemyState.Attack);
            return;
        }

        // 추적 가능한 거리에서는 복귀를 취소하고 추적 상태 진입
        if (_ai.IsWithinTrackRange())
        {
            _ai.ChangeState(EnemyState.Tracking);
            return;
        }

        // 소환 위치 복귀 중에는 배회 진입 시간 계산 보류
        if (_ai.ReturnToSpawnRequired)
        {
            UpdateReturn();
            return;
        }

        // 시간 만료 이후 현재 대기 동작의 종료까지 배회 전환 보류
        if (_waitingForIdleEnd)
        {
            if (_ai.IsIdleCycleCompleted)
                _ai.ChangeState(EnemyState.Roaming);
            return;
        }

        // 복귀 완료 이후 대기 시간 계산과 모션 종료 예약
        _elapsed += Time.deltaTime;
        if (_elapsed >= _ai.RoamEnterCycle)
        {
            _waitingForIdleEnd = true;
            _ai.RequestIdleCycleCompletion();
        }
    }

    /// <summary>다른 행동 진입에 따른 대기 완료 예약 취소</summary>
    public void Exit()
    {
        _waitingForIdleEnd = false;
        _ai.CancelIdleCycleCompletion();
    }

    void UpdateReturn()
    {
        // 이전에 요청한 복귀 이동의 진행 여부 확인
        if (_waitingForReturnMove)
        {
            if (_ai.IsMoving)
                return;

            _waitingForReturnMove = false;

            // NavMesh로 보정된 복귀 목적지 도착 확인
            if (_ai.HasArrived)
            {
                _ai.CompleteReturn();
                return;
            }

            // 경로 실패 후 재시도 간격 적용
            _retryRemaining = _ai.TrackRepathInterval;
        }

        // 이미 소환 위치에 있으면 이동 요청 없이 복귀 완료
        if (_ai.IsAtSpawnPosition())
        {
            _ai.CompleteReturn();
            return;
        }

        // 복귀 경로 재요청 시점 확인
        _retryRemaining -= Time.deltaTime;
        if (_retryRemaining > 0f)
            return;

        // 소환 위치까지의 이동 요청
        _waitingForReturnMove = _ai.TryReturnMove();

        // 경로를 확보하지 못하면 일정 간격 이후 재시도
        if (_waitingForReturnMove == false)
            _retryRemaining = _ai.TrackRepathInterval;
    }
}

/// <summary>등장 모션 완료 대기와 행동 판단 보류</summary>
public class EmergenceState : IEnemyState
{
    readonly EnemyAI _ai;

    /// <summary>등장 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public EmergenceState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>등장 모션 시작</summary>
    public void Enter()
    {
        _ai.BeginEmergence();
    }

    /// <summary>완료 또는 피격과 사망 이벤트 대기</summary>
    public void Update()
    {
    }

    /// <summary>중단된 등장 완료 요청 취소</summary>
    public void Exit()
    {
        _ai.CancelEmergence();
    }
}

/// <summary>배회 범위 안의 반복 이동과 대기 복귀 판단</summary>
public class RoamingState : IEnemyState
{
    readonly EnemyAI _ai;

    Vector3 _origin;
    int _remainingMoveCount;
    float _elapsed;
    float _retryRemaining;
    bool _waitingForMove;

    /// <summary>배회 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public RoamingState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>배회 기준점과 랜덤 이동 횟수 및 시간 초기화</summary>
    public void Enter()
    {
        // 이번 배회의 기준 위치와 이동 횟수 결정
        _origin = _ai.Position;
        _remainingMoveCount = Random.Range(_ai.MinRoamMoveCount, _ai.MaxRoamMoveCount + 1);

        // 이전 배회의 시간과 이동 대기 상태 초기화
        _elapsed = 0f;
        _retryRemaining = 0f;
        _waitingForMove = false;
    }

    /// <summary>대상 감지와 배회 이동 및 종료 조건 처리</summary>
    public void Update()
    {
        // 공격 가능한 거리에서는 배회 종료 후 공격 상태 진입
        if (_ai.IsWithinAttackRange())
        {
            _ai.ChangeState(EnemyState.Attack);
            return;
        }

        // 추적 가능한 거리에서는 배회 종료 후 추적 상태 진입
        if (_ai.IsWithinTrackRange())
        {
            _ai.ChangeState(EnemyState.Tracking);
            return;
        }

        // 배회 경과 시간과 이동 실패 재시도 시간 갱신
        _elapsed += Time.deltaTime;
        _retryRemaining = Mathf.Max(0f, _retryRemaining - Time.deltaTime);

        // 최대 배회 시간에 도달하면 진행 중인 이동을 취소하고 대기
        if (_elapsed >= _ai.RoamExitCycle)
        {
            _ai.ChangeState(EnemyState.Idle);
            return;
        }

        // 이전에 요청한 이동의 진행 여부와 종료 결과 확인
        if (_waitingForMove)
        {
            if (_ai.IsMoving)
                return;

            _waitingForMove = false;

            // 정상 도착한 이동만 횟수에서 차감
            if (_ai.HasArrived)
            {
                _remainingMoveCount--;
                _retryRemaining = 0f;
            }
            else
            {
                // 경로 실패로 종료된 경우 재시도 간격 적용
                _retryRemaining = _ai.RoamRetryInterval;
            }
        }

        // 지정된 랜덤 이동 횟수를 모두 완료하면 대기 상태 진입
        if (_remainingMoveCount <= 0)
        {
            _ai.ChangeState(EnemyState.Idle);
            return;
        }

        // 이동 실패 후 재시도 시점까지 대기
        if (_retryRemaining > 0f)
            return;

        // 다음 목적지 탐색과 이동 요청
        _waitingForMove = TryStartMove();

        // 모든 목적지 후보가 실패하면 다음 탐색까지 간격 적용
        if (_waitingForMove == false)
            _retryRemaining = _ai.RoamRetryInterval;
    }

    /// <summary>배회 종료 처리 없음, 다음 진입에서 진행 정보 초기화</summary>
    public void Exit()
    {
    }

    bool TryStartMove()
    {
        // 설정한 횟수 안에서만 목적지 후보 탐색
        for (int i = 0; i < _ai.RoamDestinationAttempts; i++)
        {
            // 배회 기준 위치의 수평 원형 범위 안에서 후보 선택
            Vector2 offset = Random.insideUnitCircle * _ai.RoamRadius;
            Vector3 destination = _origin + new Vector3(offset.x, 0f, offset.y);

            // 경로 검사에 통과한 첫 목적지로 이동 시작
            if (_ai.TryRoamMove(destination, _origin))
                return true;
        }

        return false;
    }
}

/// <summary>추적 중 공격 진입과 추적 해제 판단</summary>
public class TrackingState : IEnemyState
{
    readonly EnemyAI _ai;
    float _repathRemaining;

    /// <summary>추적 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public TrackingState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>추적 목적지 갱신 시간 초기화</summary>
    public void Enter()
    {
        _repathRemaining = 0f;
    }

    /// <summary>추적 해제와 공격 진입 거리 판정</summary>
    public void Update()
    {
        // 대상 부재 또는 추적 해제 거리 초과 시 소환 위치 복귀 요청
        if (_ai.IsOutsideTrackRange())
        {
            _ai.ReturnToSpawn();
            return;
        }

        // 스킬 사거리·쿨타임과 별개인 AttackState 진입 거리
        if (_ai.IsWithinAttackRange())
        {
            _ai.ChangeState(EnemyState.Attack);
            return;
        }

        // 추적 목적지 갱신 시점 확인
        _repathRemaining -= Time.deltaTime;
        if (_repathRemaining > 0f)
            return;

        // 다음 갱신 시간 설정과 대상의 현재 위치로 이동 요청
        _repathRemaining = _ai.TrackRepathInterval;
        _ai.TryTrackMove();
    }

    /// <summary>추적 종료 처리 없음</summary>
    public void Exit()
    {
    }
}

/// <summary>공격 영역 안의 스킬 요청과 접근 및 대치 전환</summary>
public class AttackState : IEnemyState
{
    readonly EnemyAI _ai;
    float _repathRemaining;

    /// <summary>공격 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public AttackState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>접근 목적지 갱신 시간 초기화</summary>
    public void Enter()
    {
        _repathRemaining = 0f;
    }

    /// <summary>
    /// State 이탈과 스킬 공격·접근·대치 판단
    /// </summary>
    public void Update()
    {
        EnemyAttackDecision decision = _ai.GetAttackDecision();
        if (decision == EnemyAttackDecision.NoTarget)
        {
            _ai.ReturnToSpawn();
            return;
        }

        // 승인된 실행과 모션이 끝난 뒤 State 이탈 판단
        if (decision == EnemyAttackDecision.Busy)
            return;

        if (_ai.IsOutsideAttackRange())
        {
            _ai.ChangeState(EnemyState.Tracking);
            return;
        }

        switch (decision)
        {
            case EnemyAttackDecision.Wait:
                _ai.ChangeState(EnemyState.Confrontation);
                return;
            case EnemyAttackDecision.Approach:
                // 스킬 사거리 밖에서도 AttackState를 유지하며 접근
                _repathRemaining -= Time.deltaTime;
                if (Time.deltaTime <= 0f || _repathRemaining > 0f)
                    return;

                _repathRemaining = _ai.TrackRepathInterval;
                _ai.TryTrackMove();
                return;
            case EnemyAttackDecision.Ready:
                _ai.StopAttackApproach();
                _repathRemaining = 0f;
                _ai.TickAttack();
                return;
        }
    }

    /// <summary>
    /// 진행 중인 공격과 남은 모션 취소
    /// </summary>
    public void Exit()
    {
        _ai.EndAttack();
    }
}

/// <summary>
/// 스킬 쿨타임 동안 대상 주변을 이동하는 대치 상태
/// </summary>
public sealed class ConfrontationState : IEnemyState
{
    readonly EnemyAI _ai;
    float _repathRemaining;
    int _orbitDirection;
    float _orbitRadius;

    /// <summary>
    /// 대치 상태 생성
    /// </summary>
    /// <param name="ai">상태 소유 AI</param>
    public ConfrontationState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>
    /// 진입 거리와 원호 방향 및 첫 이동 대기 시간 초기화
    /// </summary>
    public void Enter()
    {
        _orbitDirection = Random.value < 0.5f ? -1 : 1;
        _repathRemaining = _ai.ConfrontationMoveDelay;
        _orbitRadius = 0f;

        if (!_ai.TryGetTargetPosition(out Vector3 targetPosition))
            return;

        Vector3 radial = _ai.Position - targetPosition;
        radial.y = 0f;
        _orbitRadius = radial.magnitude;
    }

    /// <summary>
    /// 공격 재개 판단과 대상 주변 이동 요청
    /// </summary>
    public void Update()
    {
        EnemyAttackDecision decision = _ai.GetAttackDecision();
        if (decision == EnemyAttackDecision.NoTarget)
        {
            _ai.ReturnToSpawn();
            return;
        }

        if (decision == EnemyAttackDecision.Busy)
        {
            _ai.ChangeState(EnemyState.Attack);
            return;
        }

        // 대치도 동일한 공격 영역의 이탈 거리 적용
        if (_ai.IsOutsideAttackRange())
        {
            _ai.ChangeState(EnemyState.Tracking);
            return;
        }

        if (decision == EnemyAttackDecision.Ready || decision == EnemyAttackDecision.Approach)
        {
            _ai.ChangeState(EnemyState.Attack);
            return;
        }

        _repathRemaining -= Time.deltaTime;
        if (Time.deltaTime <= 0f || _repathRemaining > 0f)
            return;

        _repathRemaining = _ai.ConfrontationRepathInterval;

        if (!_ai.TryGetTargetPosition(out Vector3 targetPosition))
        {
            _ai.ReturnToSpawn();
            return;
        }

        Vector3 radial = _ai.Position - targetPosition;
        radial.y = 0f;

        // 겹친 위치에서는 임의 반경으로 이동하지 않고 대기
        if (_orbitRadius <= 0.001f || radial.sqrMagnitude <= 0.000001f)
            return;

        float travelDistance = _ai.MovementSpeed * _ai.ConfrontationRepathInterval;
        float stepAngle = travelDistance / _orbitRadius * Mathf.Rad2Deg;
        stepAngle = Mathf.Min(stepAngle, _ai.ConfrontationStepAngle);

        Vector3 nextDirection = Quaternion.AngleAxis(
            stepAngle * _orbitDirection, Vector3.up) * radial.normalized;
        Vector3 destination = targetPosition + nextDirection * _orbitRadius;

        if (!_ai.TryConfrontationMove(destination))
            _orbitDirection = -_orbitDirection;
    }

    /// <summary>
    /// 다음 상태 전환에서 공통 이동 정지 처리
    /// </summary>
    public void Exit()
    {
    }
}

/// <summary>피격 애니메이션 종료 대기</summary>
public class HitState : IEnemyState
{
    readonly EnemyAI _ai;

    /// <summary>피격 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public HitState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>피격 애니메이션 시작 요청</summary>
    public void Enter()
    {
        _ai.BeginHit();
    }

    /// <summary>피격 애니메이션 종료 알림 대기</summary>
    public void Update()
    {
    }

    /// <summary>피격 애니메이션 종료 알림 대기 취소</summary>
    public void Exit()
    {
        _ai.EndHit();
    }
}

/// <summary>사망 애니메이션 종료와 풀 반환 대기</summary>
public class DeadState : IEnemyState
{
    readonly EnemyAI _ai;

    /// <summary>사망 상태 생성</summary>
    /// <param name="ai">상태 소유 AI</param>
    public DeadState(EnemyAI ai)
    {
        _ai = ai;
    }

    /// <summary>사망 애니메이션 시작 요청</summary>
    public void Enter()
    {
        _ai.BeginDeath();
    }

    /// <summary>사망 애니메이션 종료와 풀 반환 대기</summary>
    public void Update()
    {
    }

    /// <summary>사망 애니메이션 종료 알림 대기 취소</summary>
    public void Exit()
    {
        _ai.EndDeath();
    }
}
