using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class PlayerMovement : MonoBehaviour, ICombatMovement
{
    [Header("----- 연결 -----")]
    [SerializeField] NavMeshAgent _agent;

    [Header("----- 이동 설정 -----")]
    [SerializeField, Min(0.01f)] float _sampleRadius = 0.5f;
    [SerializeField, Min(0.01f)] float _targetSampleRadius = 2f;
    [Tooltip("이동 불가 지점 클릭 시 클릭 지점에서 플레이어 쪽으로 이동 가능 지점을 찾는 간격")]
    [SerializeField, Min(0.05f)] float _blockedProbeStep = 0.5f;
    [SerializeField, Min(0.01f)] float _acceleration = 500f;
    [SerializeField, Min(0.01f)] float _arrivalDistance = 0.5f;

    [Header("----- 배치 설정 -----")]
    [SerializeField, Min(0.01f)] float _placementSampleRadius = 0.5f;

    [Header("----- 회전 -----")]
    [Tooltip("초당 회전 각도, 0이면 즉시 회전")]
    [SerializeField, Min(0f)] float _turnSpeed = 1500f;

    NavMeshPath _path;
    ISkillMovementSettings _skillMoveSettings;
    Vector3 _skillMoveDirection;
    const float SkillProbeStep = 0.2f;
    const float SkillPositionTolerance = 0.02f;
    const float BlockedProbeMaxDistance = 60f;
    float _skillMoveRemaining;
    float _skillMoveSpeed;
    bool _arrivalPending;
    bool _skillMoving;
    bool _hasFacingPoint;
    Vector3 _facingPoint;

    bool AgentReady => _agent.isActiveAndEnabled && _agent.isOnNavMesh;

    public event Action OnArrived;
    public event Action OnMoveFailed;

    public bool HasMoveRequest { get; private set; }
    public bool IsReadyForControl => isActiveAndEnabled && AgentReady;

    /// <summary>
    /// 연결된 NavMeshAgent가 이동시키는 캐릭터 기준 Transform
    /// </summary>
    public Transform ActorTransform
    {
        get
        {
            if (_agent == null)
                throw new MissingReferenceException($"{name}: NavMeshAgent 연결 필요");

            return _agent.transform;
        }
    }

    public float LocomotionSpeed
    {
        get
        {
            if (!isActiveAndEnabled || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh || _agent.isStopped)
                return 0f;

            Vector3 velocity = _agent.velocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }

    // 현재 실제 이동의 수평 방향, 정지 중이면 zero
    public Vector3 LocomotionDirection
    {
        get
        {
            if (LocomotionSpeed <= 0f)
                return Vector3.zero;

            Vector3 velocity = _agent.velocity;
            velocity.y = 0f;
            return velocity.normalized;
        }
    }

    void Awake()
    {
        Transform actorTransform = ActorTransform;

        if (!transform.IsChildOf(actorTransform))
            throw new InvalidOperationException($"{name}: PlayerMovement는 NavMeshAgent가 배치된 캐릭터 계층에 배치 필요");

        _path = new NavMeshPath();

        _agent.updatePosition = true;
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;

        _agent.acceleration = _acceleration;
        _agent.autoBraking = false;
        _agent.autoTraverseOffMeshLink = false;
        _agent.stoppingDistance = _arrivalDistance;
    }

    void OnDisable()
    {
        CancelSkillMovement();
    }

    NavMeshQueryFilter CreateQueryFilter()
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
    }

    // 성공 시 _path에 destination까지의 완전 경로 보관
    bool TryFindReachablePoint(Vector3 position, float sampleRadius, NavMeshQueryFilter filter, out Vector3 destination)
    {
        destination = default;

        // 요청 종류에 따른 위치 보정 반경 적용
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, sampleRadius, filter))
            return false;

        // 이동 가능한 지점을 찾은 뒤 실제 도달 가능한 전체 경로 검사
        if (!_agent.CalculatePath(hit.position, _path) || _path.status != NavMeshPathStatus.PathComplete)
            return false;

        destination = hit.position;
        return true;
    }

    // TryFindReachablePoint 성공 직후의 _path 기준 이동 시작
    bool TryStartPreparedPath(Vector3 destination)
    {
        if (GetPreparedPathLength(destination) <= _arrivalDistance)
        {
            Stop();

            HasMoveRequest = true;
            _arrivalPending = true;

            return true;
        }

        if (!TryAssignPreparedPath())
            return false;

        HasMoveRequest = true;
        _arrivalPending = false;

        return true;
    }

    // 클릭 지점에서 플레이어 쪽으로 직선을 거슬러 오며 처음 만나는 도달 가능 지점 탐색
    bool TryFindReachablePointOnLine(Vector3 position, NavMeshQueryFilter filter, out Vector3 destination)
    {
        Vector3 start = _agent.nextPosition;

        // 화면 밖 먼 지점의 탐색 횟수 제한
        Vector3 far = Vector3.MoveTowards(start, position, BlockedProbeMaxDistance);
        float distance = Vector3.Distance(start, far);

        for (float offset = 0f; offset < distance; offset += _blockedProbeStep)
        {
            Vector3 probe = Vector3.MoveTowards(far, start, offset);

            if (!TryFindReachablePoint(probe, _sampleRadius, filter, out Vector3 reachable))
                continue;

            // 찾은 구간 안에서 클릭 지점에 가장 가까운 NavMesh 경계까지 전진
            if (NavMesh.Raycast(reachable, position, out NavMeshHit edge, filter)
                && TryFindReachablePoint(edge.position, _sampleRadius, filter, out destination))
            {
                return true;
            }

            // 경계 보정 실패 시 탐색 지점의 경로 재계산
            return TryFindReachablePoint(reachable, _sampleRadius, filter, out destination);
        }

        // 직선 전체가 이동 불가인 요청은 현재 위치 유지
        return TryFindReachablePoint(start, _sampleRadius, filter, out destination);
    }

    float GetPreparedPathLength(Vector3 destination)
    {
        Vector3[] corners = _path.corners;
        Vector3 previousPosition = _agent.nextPosition;
        float length = 0f;

        foreach (Vector3 corner in corners)
        {
            length += Vector3.Distance(previousPosition, corner);
            previousPosition = corner;
        }

        length += Vector3.Distance(previousPosition, destination);
        return length;
    }

    bool TryAssignPreparedPath()
    {
        NavMeshPath previousPath = _agent.hasPath ? _agent.path : null;

        bool wasStopped = _agent.isStopped;
        Vector3 previousVelocity = _agent.velocity;

        if (_agent.SetPath(_path))
        {
            _agent.isStopped = wasStopped;
            return true;
        }

        bool restored = previousPath == null || _agent.SetPath(previousPath);

        if (restored)
        {
            _agent.isStopped = wasStopped;
            _agent.velocity = previousVelocity;

            return false;
        }

        // 기존 경로까지 사용할 수 없어진 이동 실패
        FailMove();
        return false;
    }

    bool TryTraceSkillMovement(ISkillMovementSettings settings, Vector3 direction, float distance, out Vector3 destination)
    {
        destination = default;

        if (!isActiveAndEnabled || !AgentReady)
            return false;

        Vector3 start = _agent.nextPosition;
        destination = start;

        Physics.SyncTransforms();

        if (HasSkillBlockingOverlap(start, settings.BlockingMask))
            return false;

        NavMeshQueryFilter filter = CreateQueryFilter();

        float checkedDistance = 0f;

        // 고정된 수평 방향을 따라 짧은 구간 단위로 바닥과 벽 검사
        while (checkedDistance < distance)
        {
            float nextDistance = Mathf.Min(distance, checkedDistance + SkillProbeStep);

            Vector3 requested = start + direction * nextDistance;
            requested.y = destination.y;

            if (!NavMesh.SamplePosition(requested, out NavMeshHit sample, _sampleRadius, filter))
                break;

            Vector3 correction = sample.position - requested;
            correction.y = 0f;

            // 다른 방향의 가까운 NavMesh로 옮겨 붙는 보정 제한
            if (correction.sqrMagnitude > SkillPositionTolerance * SkillPositionTolerance)
                break;

            if (NavMesh.Raycast(destination, sample.position, out _, filter))
                break;

            if (IsSkillSegmentBlocked(destination, sample.position, settings.BlockingMask))
                break;

            destination = sample.position;
            checkedDistance = nextDistance;
        }

        Vector3 displacement = destination - start;
        displacement.y = 0f;
        return displacement.sqrMagnitude > 0.000001f;
    }

    void GetSkillCapsule(Vector3 position, out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = _agent.radius + SkillPositionTolerance;

        bottom = position + Vector3.up * radius;
        top = position + Vector3.up * Mathf.Max(radius, _agent.height - radius);
    }

    bool HasSkillBlockingOverlap(Vector3 position, LayerMask mask)
    {
        GetSkillCapsule(position, out Vector3 bottom, out Vector3 top, out float radius);

        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);

        foreach (Collider candidate in overlaps)
        {
            if (!candidate.transform.IsChildOf(_agent.transform))
                return true;
        }

        return false;
    }

    bool IsSkillSegmentBlocked(Vector3 start, Vector3 end, LayerMask mask)
    {
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;

        if (distance <= 0.0001f)
            return true;

        GetSkillCapsule(start, out Vector3 bottom, out Vector3 top, out float radius);

        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, displacement / distance, distance, mask, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(_agent.transform))
                return true;
        }

        return HasSkillBlockingOverlap(end, mask);
    }

    void CompleteMove()
    {
        Stop();
        OnArrived?.Invoke();
    }

    void FailMove()
    {
        Stop();
        OnMoveFailed?.Invoke();
    }

    static void ValidateMoveSpeed(float moveSpeed)
    {
        if (float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) || moveSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(moveSpeed), "유한한 양수 이동 속도 필요");
    }

    /// <summary>
    /// 이동 상태 정리 후 목적 맵의 NavMesh에 캐릭터 배치
    /// </summary>
    /// <param name="destination">목적 위치와 회전</param>
    /// <returns>배치 성공 여부</returns>
    public bool TryPlace(Pose destination)
    {
        if (!isActiveAndEnabled || !_agent.isActiveAndEnabled || float.IsNaN(_placementSampleRadius) || float.IsInfinity(_placementSampleRadius) || _placementSampleRadius <= 0f)
            throw new InvalidOperationException($"{name}: 활성 이동 구성과 유효한 배치 반경 필요");

        // 이전 맵 비활성화 이후에도 배치 가능하도록 현재 NavMesh 연결 검사는 제외
        CancelSkillMovement();

        NavMeshQueryFilter filter = CreateQueryFilter();

        if (!NavMesh.SamplePosition(destination.position, out NavMeshHit hit, _placementSampleRadius, filter))
        {
            Debug.LogError(
                $"{name}: 플레이어 배치 실패 [NavMesh.SamplePosition] " +
                $"destination={destination.position.ToString("F3")}, sampleRadius={_placementSampleRadius}, " +
                $"agentTypeID={filter.agentTypeID}, areaMask={filter.areaMask}, " +
                $"agentPosition={_agent.transform.position.ToString("F3")}, " +
                $"agentActive={_agent.isActiveAndEnabled}, isOnNavMesh={_agent.isOnNavMesh}", this);
            return false;
        }

        if (!_agent.Warp(hit.position))
        {
            Debug.LogError(
                $"{name}: 플레이어 배치 실패 [NavMeshAgent.Warp] " +
                $"destination={destination.position.ToString("F3")}, hitPosition={hit.position.ToString("F3")}, " +
                $"sampleRadius={_placementSampleRadius}, agentTypeID={filter.agentTypeID}, areaMask={filter.areaMask}, " +
                $"agentPosition={_agent.transform.position.ToString("F3")}, " +
                $"agentActive={_agent.isActiveAndEnabled}, isOnNavMesh={_agent.isOnNavMesh}", this);
            return false;
        }

        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
        _agent.isStopped = true;
        _agent.transform.rotation = destination.rotation;

        Physics.SyncTransforms();
        return true;
    }

    /// <summary>
    /// 전달받은 이동 허용 여부와 속도에 따른 일반 이동 진행
    /// </summary>
    /// <param name="moveSpeed">현재 캐릭터의 일반 이동 속도</param>
    /// <param name="canMove">호출 시점의 일반 이동 허용 여부</param>
    public void Tick(float moveSpeed, bool canMove)
    {
        // 스킬 이동은 Combat의 전용 처리 경로에서 진행
        if (_skillMoving)
            return;

        if (!isActiveAndEnabled || !_agent.isActiveAndEnabled)
            return;

        if (!canMove)
        {
            Stop();
            return;
        }

        if (!AgentReady)
        {
            if (HasMoveRequest)
                FailMove();

            return;
        }

        if (Time.deltaTime <= 0f)
            return;

        ValidateMoveSpeed(moveSpeed);
        _agent.speed = moveSpeed;

        if (!HasMoveRequest)
            return;

        if (_arrivalPending)
        {
            CompleteMove();
            return;
        }

        if (_agent.isStopped || _agent.pathPending)
            return;

        if (_agent.hasPath && !_agent.pathPending && _agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            FailMove();
            return;
        }

        float frameTravel = _agent.velocity.magnitude * Time.deltaTime;
        float arrivalTolerance = Mathf.Max(_arrivalDistance, frameTravel);

        if (_agent.remainingDistance <= arrivalTolerance)
        {
            CompleteMove();
            return;
        }

        // 이동 방향 회전은 TickFacing에서 회전 속도에 맞춰 처리
    }

    /// <summary>
    /// 이동 방향 대신 바라볼 지점 지정
    /// </summary>
    /// <param name="point">바라볼 월드 좌표</param>
    public void SetFacingPoint(Vector3 point)
    {
        _hasFacingPoint = true;
        _facingPoint = point;
    }

    /// <summary>
    /// 바라볼 지점 해제, 이후 이동 경로 방향 기준 회전
    /// </summary>
    public void ClearFacingPoint()
    {
        _hasFacingPoint = false;
    }

    /// <summary>
    /// 지정 지점 또는 이동 경로 방향으로 회전 속도에 맞춰 회전
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    public void TickFacing(float deltaTime)
    {
        // 스킬 이동은 Combat의 전용 처리 경로에서 방향 결정
        if (_skillMoving || !isActiveAndEnabled)
            return;

        Vector3 goal;

        if (_hasFacingPoint)
            goal = _facingPoint;
        else if (HasMoveRequest && AgentReady && _agent.hasPath)
            goal = _agent.steeringTarget;
        else
            return;

        Vector3 direction = goal - _agent.transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(direction);

        _agent.transform.rotation = _turnSpeed <= 0f
            ? target
            : Quaternion.RotateTowards(_agent.transform.rotation, target, _turnSpeed * deltaTime);
    }

    /// <summary>
    /// 위치 변경 없이 이동 시작 가능 여부 검사
    /// </summary>
    /// <param name="settings">이동 설정</param>
    /// <param name="direction">정규화된 수평 이동 방향</param>
    /// <param name="distance">이번 이동의 목표 거리</param>
    /// <returns>이동 가능한 구간 존재 여부</returns>
    public bool CanStartSkillMovement(ISkillMovementSettings settings, Vector3 direction, float distance)
    {
        return !_skillMoving && TryTraceSkillMovement(settings, direction, distance, out _);
    }

    /// <summary>
    /// 대쉬 이동과 진행 구간의 장애물 재검사
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    /// <returns>진행 또는 종료 결과</returns>
    public SkillMovementStepResult TickSkillMovement(float deltaTime)
    {
        if (!_skillMoving || !isActiveAndEnabled || !AgentReady)
        {
            CancelSkillMovement();
            return SkillMovementStepResult.Failed;
        }

        if (_skillMoveSettings.Mode == MovementSkillMode.Teleport)
        {
            CancelSkillMovement();
            return SkillMovementStepResult.Completed;
        }

        if (deltaTime <= 0f)
            return SkillMovementStepResult.Running;

        float step = Mathf.Min(_skillMoveRemaining, _skillMoveSpeed * deltaTime);

        if (!TryTraceSkillMovement(_skillMoveSettings, _skillMoveDirection, step, out Vector3 destination))
        {
            // 시작 이후 장애물에 막힌 이동의 조기 완료
            CancelSkillMovement();
            return SkillMovementStepResult.Completed;
        }

        Vector3 before = _agent.nextPosition;
        _agent.Move(destination - before);

        Vector3 displacement = _agent.nextPosition - before;
        displacement.y = 0f;

        float traveled = displacement.magnitude;
        _skillMoveRemaining = Mathf.Max(0f, _skillMoveRemaining - traveled);

        if (traveled <= 0.001f || _skillMoveRemaining <= 0.001f || traveled + 0.001f < step)
        {
            CancelSkillMovement();
            return SkillMovementStepResult.Completed;
        }

        return SkillMovementStepResult.Running;
    }



    /// <summary> 
    /// 이동 경로 재검사 후 대쉬 또는 순간이동 시작
    /// </summary>
    /// <param name="settings">이동 설정</param>
    /// <param name="direction">정규화된 수평 이동 방향</param>
    /// <param name="distance">이번 이동의 목표 거리</param>
    /// <returns>이동 시작 성공 여부</returns>
    public bool TryBeginSkillMovement(ISkillMovementSettings settings, Vector3 direction, float distance)
    {
        if (_skillMoving || !TryTraceSkillMovement(settings, direction, distance, out Vector3 destination))
            return false;

        Vector3 start = _agent.nextPosition;
        Stop();

        if (settings.Mode == MovementSkillMode.Teleport && !_agent.Warp(destination))
        {
            return false;
        }

        Vector3 displacement = destination - start;
        displacement.y = 0f;

        _skillMoveSettings = settings;
        _skillMoveDirection = direction;
        _skillMoveRemaining = displacement.magnitude;
        // 짧은 이동도 최대 거리 기준의 같은 속도 유지
        _skillMoveSpeed = settings.Mode == MovementSkillMode.Dash ? settings.Distance / settings.DashDuration : 0f;
        _skillMoving = true;

        return true;
    }

    /// <summary>
    /// 이동을 시작하지 않고 요청 위치에서 실제로 이동할 목적지 계산
    /// 이동 불가 지점은 플레이어와 요청 위치를 잇는 직선 위에서 요청 위치에 가장 가까운 도달 가능 지점으로 보정
    /// </summary>
    /// <param name="position">요청한 월드 좌표</param>
    /// <param name="destination">보정된 실제 목적지</param>
    /// <returns>도달 가능한 목적지 존재 여부</returns>
    public bool TryResolveMoveDestination(Vector3 position, out Vector3 destination)
    {
        destination = default;

        if (!isActiveAndEnabled || !AgentReady)
            return false;

        NavMeshQueryFilter filter = CreateQueryFilter();

        if (TryFindReachablePoint(position, _sampleRadius, filter, out destination))
            return true;

        return TryFindReachablePointOnLine(position, filter, out destination);
    }

    /// <summary>
    /// 일반 이동 목적지 보정과 경로 준비
    /// </summary>
    /// <param name="position">이동할 월드 좌표</param>
    /// <param name="destination">보정된 실제 목적지</param>
    /// <returns>경로 준비 성공 여부</returns>
    public bool TryPrepareMove(Vector3 position, out Vector3 destination)
    {
        return TryResolveMoveDestination(position, out destination) && TryStartPreparedPath(destination);
    }

    /// <summary>
    /// 지정 대상 주변의 이동 가능한 위치 검사와 추적 경로 준비
    /// </summary>
    /// <param name="targetPosition">추적 대상의 월드 좌표</param>
    /// <returns>추적 경로 준비 성공 여부</returns>
    public bool TryPrepareTargetMove(Vector3 targetPosition)
    {
        if (!isActiveAndEnabled || !AgentReady)
            return false;

        return TryFindReachablePoint(targetPosition, _targetSampleRadius, CreateQueryFilter(), out Vector3 destination)
            && TryStartPreparedPath(destination);
    }

    /// <summary>
    /// 전달받은 속도로 준비된 일반 이동 재개
    /// </summary>
    /// <param name="moveSpeed">현재 캐릭터의 일반 이동 속도</param>
    /// <returns>이동 재개 성공 여부</returns>
    public bool ResumeMove(float moveSpeed)
    {
        if (!isActiveAndEnabled || !AgentReady)
            return false;

        ValidateMoveSpeed(moveSpeed);

        _agent.speed = moveSpeed;
        _agent.isStopped = false;

        return true;
    }

    public void Face(Vector3 position)
    {
        if (!isActiveAndEnabled)
            return;

        Vector3 direction = position - _agent.transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        _agent.transform.rotation = Quaternion.LookRotation(direction);
    }



    /// <summary>
    /// 스킬 이동 상태와 일반 이동 경로 정리
    /// </summary>
    public void CancelSkillMovement()
    {
        _skillMoving = false;
        _skillMoveSettings = null;
        _skillMoveDirection = Vector3.zero;
        _skillMoveRemaining = 0f;
        _skillMoveSpeed = 0f;

        Stop();
    }

    public void Stop()
    {
        HasMoveRequest = false;
        _arrivalPending = false;

        if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            return;

        _agent.isStopped = true;

        if (_agent.hasPath || _agent.pathPending)
            _agent.ResetPath();

        _agent.velocity = Vector3.zero;
    }
}