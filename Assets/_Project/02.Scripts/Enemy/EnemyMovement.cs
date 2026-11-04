using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 적의 NavMesh 이동과 도착 및 정지 처리
/// </summary>
[DisallowMultipleComponent]
public class EnemyMovement : MonoBehaviour, IChargeMovement
{
    [Header("----- 컴포넌트 -----")]
    [SerializeField] NavMeshAgent _agent;	// 적 루트를 이동할 Agent
    [SerializeField] CombatController _combat;	// 이동 제한과 현재 이동 속도 제공

    [Header("----- 이동 설정 -----")]
    [SerializeField, Min(0.01f)] float _sampleRadius = 0.5f;	// 목적지의 NavMesh 보정 반경
    [SerializeField, Min(0.01f)] float _arrivalDistance = 0.1f;	// 목적지 도착 허용 거리
    [SerializeField, Min(0.01f)] float _acceleration = 20f;	// 이동 가속도
    [SerializeField, Min(0f)] float _rotationSpeed = 720f;	// 초당 회전 각도

    [Header("----- Roaming 기준 이동 배율 -----")]
    [Tooltip("기존 MoveSpeed 스탯의 절반에 적용할 Roaming 기준 배율, 다른 상태에도 공통 적용")]
    [SerializeField, Min(0.01f)] float _roamingMultiplier = 1f;
    [Tooltip("Roaming 속도 대비 Tracking·공격 접근·복귀 이동 배율")]
    [SerializeField, Min(0.01f)] float _trackingMultiplier = 2f;
    [Tooltip("Roaming 속도 대비 대치 이동 배율")]
    [SerializeField, Min(0.01f)] float _confrontationMultiplier = 0.5f;

    NavMeshPath _path;
    Vector3 _destination;
    bool _initialized;
    EnemyState _speedState = EnemyState.Tracking;

    ChargeAttackExecution _chargeExecution;
    bool _isCharging;
    Vector3 _chargeDirection;
    float _chargeRemainingDistance;
    float _chargeSpeed;

    bool IChargeMovement.IsActiveAndEnabled => isActiveAndEnabled;

    bool AgentReady => _agent.isActiveAndEnabled && _agent.isOnNavMesh;

    // 기존 Tracking 기준 스탯의 실제 속도를 보존하는 Roaming 기준값
    float RoamingSpeed => _combat.Owner.Status.MoveSpeed * 0.5f * _roamingMultiplier;

    /// <summary>
    /// Locomotion 정규화와 추적에 사용할 기준 속도
    /// </summary>
    public float TrackingSpeed => RoamingSpeed * _trackingMultiplier;

    /// <summary>
    /// 현재 이동 상태와 Inspector 배율을 적용한 일반 이동 속도
    /// </summary>
    public float MoveSpeed
    {
        get
        {
            switch (_speedState)
            {
                case EnemyState.Roaming:
                    return RoamingSpeed;
                case EnemyState.Confrontation:
                    return RoamingSpeed * _confrontationMultiplier;
                default:
                    return TrackingSpeed;
            }
        }
    }

    public CombatController Combat => _combat;
    public bool IsMoving { get; private set; }
    public bool HasArrived { get; private set; }
    public float CurrentSpeed => AgentReady && IsMoving
        ? new Vector2(_agent.velocity.x, _agent.velocity.z).magnitude
        : 0f;

    void OnDisable()
    {
        Stop();
    }

    bool CanBeginCharge(Vector3 direction, float distance, float duration)
    {
        if (!_initialized)
            throw new InvalidOperationException($"{name}: 이동 초기화 필요");

        if (!IsPositiveFinite(distance) || !IsPositiveFinite(duration) ||
            !IsPositiveFinite(distance / duration) || !IsPositiveFinite(direction.sqrMagnitude))
        {
            throw new ArgumentException("유효한 돌진 방향·거리·시간 필요");
        }

        if (!isActiveAndEnabled || !AgentReady || !_combat.Owner.IsAlive ||
            _combat.IsBusy || _isCharging || _agent.isOnOffMeshLink)
        {
            return false;
        }

        Vector3 start = _agent.nextPosition;
        Vector3 probe = start + direction * Mathf.Min(distance, _arrivalDistance);
        bool blocked = _agent.Raycast(probe, out NavMeshHit hit);
        return !blocked || (hit.position - start).sqrMagnitude > 0.000001f;
    }

    bool IChargeMovement.CanBegin(Vector3 direction, float distance, float duration)
    {
        return CanBeginCharge(direction, distance, duration);
    }

    bool IChargeMovement.TryBegin(Vector3 direction, float distance, float duration)
    {
        if (!CanBeginCharge(direction, distance, duration))
            return false;

        Stop();

        _chargeDirection = direction;
        _chargeRemainingDistance = distance;
        _chargeSpeed = distance / duration;
        _isCharging = true;
        return true;
    }

    SkillMovementStepResult IChargeMovement.Tick(float deltaTime)
    {
        if (!_isCharging)
            return SkillMovementStepResult.Completed;

        if (!isActiveAndEnabled || !AgentReady || !_combat.Owner.IsAlive ||
            _agent.isOnOffMeshLink)
        {
            Stop();
            return SkillMovementStepResult.Failed;
        }

        if (deltaTime <= 0f)
            return SkillMovementStepResult.Running;

        Vector3 start = _agent.nextPosition;
        float step = Mathf.Min(_chargeRemainingDistance, _chargeSpeed * deltaTime);
        Vector3 destination = start + _chargeDirection * step;

        bool blocked = _agent.Raycast(destination, out NavMeshHit hit);
        if (blocked)
            destination = hit.position;

        _agent.Move(destination - start);

        // 같은 프레임의 Content Collider 검사를 위한 Agent 확정 좌표 동기화
        _agent.transform.position = _agent.nextPosition;

        Vector3 displacement = _agent.nextPosition - start;
        displacement.y = 0f;
        float traveled = Mathf.Max(0f, Vector3.Dot(displacement, _chargeDirection));
        _chargeRemainingDistance = Mathf.Max(0f, _chargeRemainingDistance - traveled);

        if (blocked || traveled <= 0.001f || traveled + 0.001f < step ||
            _chargeRemainingDistance <= 0.001f)
        {
            Stop();
            return SkillMovementStepResult.Completed;
        }

        return SkillMovementStepResult.Running;
    }

    internal void BindChargeExecution(ChargeAttackExecution execution)
    {
        if (_chargeExecution != null && _chargeExecution.IsRunning)
            throw new InvalidOperationException($"{name}: 돌진 종료 후 실행 연결 변경 필요");

        _chargeExecution = execution;
    }

    /// <summary>
    /// 비활성 객체의 이동 참조와 설정 초기화
    /// </summary>
    public void Init()
    {
        // 필수 참조와 이동 설정 검사
        ValidateReferences();
        ValidateSettings();

        // 재사용할 경로 객체 최초 생성
        if (_path == null)
            _path = new NavMeshPath();

        // 위치는 Agent가 갱신하고 회전은 이동 코드에서 처리
        _agent.updatePosition = true;
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;

        // 도착 감속과 수동 경로 갱신 정책 설정
        _agent.autoBraking = true;
        _agent.autoRepath = false;
        _agent.autoTraverseOffMeshLink = false;

        // 이동 가속도와 도착 허용 거리 적용
        _agent.acceleration = _acceleration;
        _agent.stoppingDistance = _arrivalDistance;

        // 초기화 완료와 이전 이동 상태 정리
        _speedState = EnemyState.Tracking;
        _initialized = true;
        Stop();
    }

    /// <summary>
    /// AI 상태에 해당하는 일반 이동 속도 선택
    /// </summary>
    /// <param name="state">속도 배율을 적용할 AI 상태</param>
    public void SetSpeedForState(EnemyState state)
    {
        if (!_initialized)
            throw new InvalidOperationException($"{name}: 이동 초기화 필요");

        _speedState = state;
        if (AgentReady)
            _agent.speed = MoveSpeed;
    }

    /// <summary>
    /// 도달 가능한 목적지로 이동 요청
    /// </summary>
    /// <param name="destination">목적지의 월드 좌표</param>
    /// <returns>이동 요청 수락 여부</returns>
    public bool TryMoveTo(Vector3 destination)
    {
        if (TryPreparePath(destination) == false)
            return false;

        return TryStartPath();
    }

    /// <summary>
    /// 배회 범위 안의 경로로 이동 요청
    /// </summary>
    /// <param name="destination">목적지 후보의 월드 좌표</param>
    /// <param name="origin">배회 기준 위치</param>
    /// <param name="radius">수평 배회 반경</param>
    /// <returns>범위 안의 이동 요청 수락 여부</returns>
    public bool TryMoveWithin(Vector3 destination, Vector3 origin, float radius)
    {
        // 목적지 보정과 도달 가능한 경로 준비
        if (TryPreparePath(destination) == false)
            return false;

        // 출발 위치와 보정된 목적지의 배회 범위 검사
        float radiusSquared = radius * radius;
        if (IsWithinRange(_agent.nextPosition, origin, radiusSquared) == false ||
            IsWithinRange(_destination, origin, radiusSquared) == false)
        {
            return false;
        }

        // 우회 경로를 포함한 모든 경로 꼭짓점의 배회 범위 검사
        foreach (Vector3 corner in _path.corners)
        {
            if (IsWithinRange(corner, origin, radiusSquared) == false)
                return false;
        }

        // 검사를 통과한 경로로 이동 시작
        return TryStartPath();
    }

    /// <summary>
    /// 이동 제한과 도착 및 경로 실패 처리
    /// </summary>
    public void Tick()
    {
        // 이동 기능 초기화 여부 확인
        if (_initialized == false)
            throw new InvalidOperationException($"{name}: 이동 Init이 필요합니다.");

        // 준비 시간을 포함한 돌진 이동은 공용 실행에서만 갱신
        if (_chargeExecution != null && _chargeExecution.IsRunning)
            return;

        // 진행 중인 이동 요청 여부 확인
        if (IsMoving == false)
            return;

        // 이동 컴포넌트와 Agent 및 전투 상태의 이동 가능 여부 확인
        if (isActiveAndEnabled == false || AgentReady == false || _combat.CanMove == false)
        {
            Stop();
            return;
        }

        // 현재 전투 스탯의 이동 속도 반영
        _agent.speed = MoveSpeed;

        // 시간 정지 또는 경로 계산 중에는 진행 판정 보류
        if (Time.deltaTime <= 0f || _agent.pathPending)
            return;

        // 미지원 링크 진입 또는 기존 경로 무효화 확인
        if (_agent.isOnOffMeshLink || _agent.isPathStale)
        {
            Stop();
            return;
        }

        // Agent에 남아 있는 경로의 유효성 검사
        if (_agent.hasPath)
        {
            if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Stop();
                return;
            }

            // 남은 경로 거리를 기준으로 도착 판정
            if (_agent.remainingDistance <= _arrivalDistance)
            {
                CompleteMove();
                return;
            }

            // 현재 경로의 진행 방향으로 회전
            Face(_agent.steeringTarget);
            return;
        }

        // 경로가 제거된 경우 저장한 목적지와 실제 위치로 도착 판정
        if (Vector3.Distance(_agent.nextPosition, _destination) <= _arrivalDistance)
            CompleteMove();
        else
            Stop();
    }

    /// <summary>지정 위치의 도착 허용 거리 충족 여부 확인</summary>
    /// <param name="position">확인할 월드 좌표</param>
    /// <returns>Agent 위치가 도착 허용 거리 이내이면 true</returns>
    public bool IsWithinArrivalDistance(Vector3 position)
    {
        return Vector3.Distance(_agent.transform.position, position) <= _arrivalDistance;
    }

    /// <summary>
    /// 이동 요청과 도착 결과 및 경로 제거
    /// </summary>
    public void Stop()
    {
        _isCharging = false;
        _chargeDirection = Vector3.zero;
        _chargeRemainingDistance = 0f;
        _chargeSpeed = 0f;

        // 이동 요청과 이전 도착 결과 초기화
        IsMoving = false;
        HasArrived = false;

        // Agent를 조작할 수 없는 경우 논리 상태 정리만 수행
        if (_agent == null || AgentReady == false)
            return;

        // Agent 정지와 기존 경로 제거
        _agent.isStopped = true;
        if (_agent.hasPath || _agent.pathPending)
            _agent.ResetPath();

        // 남아 있는 이동 속도 제거
        _agent.velocity = Vector3.zero;
    }

    bool TryPreparePath(Vector3 destination)
    {
        // 이동 기능 초기화 여부 확인
        if (_initialized == false)
            throw new InvalidOperationException($"{name}: 이동 Init이 필요합니다.");

        // 돌진 준비·진행 중에는 기존 돌진을 정지하지 않고 일반 요청만 거절
        if (_chargeExecution != null && _chargeExecution.IsRunning)
            return false;

        // 현재 이동 요청을 실행할 수 있는 상태인지 확인
        if (isActiveAndEnabled == false || AgentReady == false || _combat.CanMove == false)
        {
            Stop();
            return false;
        }

        // Agent 종류와 통과 가능한 영역을 기준으로 탐색 조건 구성
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };

        // 요청 목적지를 주변 NavMesh 위치로 보정
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, _sampleRadius, filter) == false)
            return false;

        // 보정된 목적지까지 도달 가능한 전체 경로 검사
        if (_agent.CalculatePath(hit.position, _path) == false ||
            _path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        // 도착 판정에 사용할 보정된 목적지 저장
        _destination = hit.position;

        // 제자리 이동으로 배회 횟수가 소진되는 상황 방지
        return GetPathLength() > _arrivalDistance;
    }

    bool TryStartPath()
    {
        // 준비한 경로를 Agent에 적용
        if (_agent.SetPath(_path) == false)
        {
            Stop();
            return false;
        }

        // 현재 이동 속도 반영과 Agent 정지 해제
        _agent.speed = MoveSpeed;
        _agent.isStopped = false;

        // 새 이동 요청의 진행 상태와 도착 결과 설정
        IsMoving = true;
        HasArrived = false;
        return true;
    }

    float GetPathLength()
    {
        // 현재 Agent 위치를 경로 길이 계산의 시작점으로 사용
        Vector3 previous = _agent.nextPosition;
        float length = 0f;

        // 경로 꼭짓점 사이의 이동 거리 누적
        foreach (Vector3 corner in _path.corners)
        {
            length += Vector3.Distance(previous, corner);
            previous = corner;
        }

        // 마지막 꼭짓점부터 목적지까지의 거리 포함
        return length + Vector3.Distance(previous, _destination);
    }

    void CompleteMove()
    {
        Stop();
        HasArrived = true;
    }

    void Face(Vector3 position)
    {
        // 높이 차이를 제외한 수평 진행 방향 계산
        Vector3 direction = position - _agent.transform.position;
        direction.y = 0f;

        // 회전 방향을 결정할 수 없는 거리에서는 현재 회전 유지
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        // 설정한 회전 속도로 진행 방향에 접근
        _agent.transform.rotation = Quaternion.RotateTowards(
            _agent.transform.rotation,
            Quaternion.LookRotation(direction),
            _rotationSpeed * Time.deltaTime);
    }

    void ValidateReferences()
    {
        // 이동 실행에 필요한 컴포넌트 연결 검사
        if (_agent == null || _combat == null)
            throw new MissingReferenceException($"{name}: NavMeshAgent와 CombatController가 필요합니다.");

        // 이동 속도와 캐릭터 기준 위치를 제공할 전투 소유자 검사
        CombatTarget owner = _combat.Owner;
        if (owner == null || owner.Status == null || owner.ActorTransform == null)
            throw new MissingReferenceException($"{name}: 전투 소유자와 상태 및 캐릭터 Transform이 필요합니다.");

        // Agent의 이동 대상과 이동 및 전투 컴포넌트의 소유 계층 검사
        Transform actor = owner.ActorTransform;
        if (_agent.transform != actor || transform.IsChildOf(actor) == false ||
            _combat.transform.IsChildOf(actor) == false)
        {
            throw new InvalidOperationException($"{name}: Agent는 캐릭터 루트에, 이동과 전투는 같은 캐릭터 계층에 연결하세요.");
        }
    }

    void ValidateSettings()
    {
        if (IsPositiveFinite(_sampleRadius) == false ||
            IsPositiveFinite(_arrivalDistance) == false ||
            IsPositiveFinite(_acceleration) == false ||
            !IsPositiveFinite(_roamingMultiplier) ||
            !IsPositiveFinite(_trackingMultiplier) ||
            !IsPositiveFinite(_confrontationMultiplier) ||
            !IsPositiveFinite(TrackingSpeed) ||
            !IsPositiveFinite(RoamingSpeed * _confrontationMultiplier) ||
            float.IsNaN(_rotationSpeed) || float.IsInfinity(_rotationSpeed) || _rotationSpeed < 0f)
        {
            throw new InvalidOperationException($"{name}: 이동 설정 값의 범위를 확인하세요.");
        }
    }

    static bool IsWithinRange(Vector3 position, Vector3 origin, float radiusSquared)
    {
        Vector3 offset = position - origin;
        offset.y = 0f;
        return offset.sqrMagnitude <= radiusSquared;
    }

    static bool IsPositiveFinite(float value)
    {
        return float.IsNaN(value) == false && float.IsInfinity(value) == false && value > 0f;
    }
}
