using System;
using UnityEngine;


/// <summary>
/// 직선·유도 탄환의 이동과 최초 적중 처리
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetProjectile : MonoBehaviour
{
    CombatSource _source;
    CombatTarget _homingTarget;
    ProjectileFlightMode _flightMode;
    LayerMask _targetMask;

    Vector3 _direction;
    Vector3 _destination;


    Action<CombatTarget, Vector3, Vector3> _onHit;
    Action<Vector3> _onArrived;

    float _speed;
    float _radius;
    float _remainingLifetime;
    bool _fliesToPosition;
    bool _initialized;
    bool _finished;

    public Vector3 Direction => _direction;

    void Start()
    {
        if (!_initialized)
            throw new InvalidOperationException($"{name}: 탄환 초기화 필요");
    }

    void FixedUpdate()
    {
        if (_finished)
            return;

        float deltaTime = Mathf.Min(Time.fixedDeltaTime, _remainingLifetime);

        if (deltaTime <= 0f)
        {
            Cancel();
            return;
        }

        if (_fliesToPosition)
            MoveToPosition(deltaTime);
        else
            MoveToTarget(deltaTime);

        if (_finished)
            return;

        _remainingLifetime -= deltaTime;

        if (_remainingLifetime <= 0f)
            Cancel();
    }

    void MoveToPosition(float deltaTime)
    {
        Vector3 position = transform.position;
        Vector3 towardDestination = _destination - position;
        float distance = towardDestination.magnitude;
        float travel = _speed * deltaTime;

        if (distance > 0f)
        {
            _direction = towardDestination / distance;
            transform.rotation =
                Quaternion.LookRotation(_direction, Vector3.up);
        }

        // 목적지 초과 이동 방지와 정확한 도착 좌표 확정
        if (distance <= travel)
        {
            transform.position = _destination;
            Arrive();
            return;
        }

        transform.position = position + _direction * travel;
    }

    void MoveToTarget(float deltaTime)
    {
        Physics.SyncTransforms();

        Vector3 position = transform.position;
        int mask = _targetMask.value;

        CombatTarget overlappingTarget = null;
        Vector3 overlappingPoint = default;
        float nearestOverlapDistanceSquared = float.PositiveInfinity;

        Collider[] overlaps = Physics.OverlapSphere(position, _radius, mask, QueryTriggerInteraction.Collide);

        foreach (Collider overlap in overlaps)
        {
            if (!TryGetTarget(overlap, out CombatTarget candidate))
                continue;

            Vector3 point = overlap.ClosestPoint(position);
            float distanceSquared = (point - position).sqrMagnitude;

            if (distanceSquared < nearestOverlapDistanceSquared)
            {
                overlappingTarget = candidate;
                overlappingPoint = point;
                nearestOverlapDistanceSquared = distanceSquared;
            }
        }

        if (overlappingTarget != null)
        {
            Hit(overlappingTarget, overlappingPoint);
            return;
        }

        UpdateDirection(position);

        float travel = _speed * deltaTime;

        RaycastHit[] hits = Physics.SphereCastAll(position, _radius, _direction, travel, mask, QueryTriggerInteraction.Collide);

        CombatTarget nearestTarget = null;
        RaycastHit nearestHit = default;
        float nearestDistance = float.PositiveInfinity;

        // 반환 배열 순서와 무관한 최초 충돌 선택
        foreach (RaycastHit hit in hits)
        {
            if (!TryGetTarget(hit.collider, out CombatTarget candidate))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestTarget = candidate;
                nearestHit = hit;
                nearestDistance = hit.distance;
            }
        }

        if (nearestTarget != null)
        {
            transform.position = position + _direction * nearestDistance;
            Hit(nearestTarget, nearestHit.point);
            return;
        }

        transform.position = position + _direction * travel;
    }

    void UpdateDirection(Vector3 position)
    {
        if (_flightMode != ProjectileFlightMode.Homing)
            return;

        // 대상 상실 후에는 재탐색 없이 마지막 진행 방향 유지
        if (_homingTarget == null || !_homingTarget.CanReceiveFrom(_source))
        {
            _homingTarget = null;
            return;
        }

        Vector3 towardTarget = _homingTarget.AimPosition - position;
        float lengthSquared = towardTarget.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared))
            throw new InvalidOperationException($"{name}: 유효한 유도 대상 위치 필요");

        if (lengthSquared <= 0.0001f)
            return;

        _direction = towardTarget.normalized;
        transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
    }

    bool TryGetTarget(Collider collider, out CombatTarget target)
    {
        target = collider.GetComponentInParent<CombatTarget>();

        return target != null && target.CanReceiveFrom(_source);
    }

    void Hit(CombatTarget target, Vector3 position)
    {
        if (_finished)
            return;

        Action<CombatTarget, Vector3, Vector3> onHit = _onHit;
        Vector3 hitDirection = _direction;

        // 콜백 전에 종료하여 재진입과 중복 적중 방지
        Cancel();
        onHit(target, position, hitDirection);
    }

    void Arrive()
    {
        if (_finished)
            return;

        Action<Vector3> onArrived = _onArrived;
        Vector3 destination = _destination;

        // 도착 콜백 이전 종료 확정
        Cancel();
        onArrived(destination);
    }

    void InitializeMotion(Vector3 direction, float speed, float lifetime)
    {
        if (_initialized)
            throw new InvalidOperationException($"{name}: 탄환 중복 초기화");

        float lengthSquared = direction.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.0001f)
        {
            throw new ArgumentException("유효한 탄환 진행 방향 필요", nameof(direction));
        }

        // 속도·수명 설정 검사는 SkillExecutor의 공통 검증 담당
        _speed = speed;
        _remainingLifetime = lifetime;
        _direction = direction.normalized;

        transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);

        _initialized = true;
    }

    /// <summary>탄환 설정과 저장된 공격자 정보 및 적중 콜백 연결</summary>
    /// <param name="definition">탄환 설정</param>
    /// <param name="source">발동 당시 공격자 정보</param>
    /// <param name="target">유도 대상, 없으면 null</param>
    /// <param name="direction">초기 진행 방향</param>
    /// <param name="onHit">실제 피격 대상·위치·방향 전달</param>
    internal void Initialize(TargetDamageSkillDefinition definition, CombatSource source, CombatTarget target, Vector3 direction, Action<CombatTarget, Vector3, Vector3> onHit)
    {
        if (_initialized)
            throw new InvalidOperationException($"{name}: 탄환 중복 초기화");

        float lengthSquared = direction.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.0001f)
            throw new ArgumentException("유효한 탄환 진행 방향 필요", nameof(direction));

        InitializeMotion(direction, definition.ProjectileSpeed, definition.ProjectileLifetime); 

        _fliesToPosition = false;
        _source = source;
        _flightMode = definition.FlightMode;
        _targetMask = definition.TargetMask;
        _radius = definition.ProjectileRadius;

        _homingTarget = _flightMode == ProjectileFlightMode.Homing ? target : null;

        _onHit = onHit;
    }

    internal void InitializeToPosition(Vector3 destination, Vector3 direction, float speed, float lifetime, Action<Vector3> onArrived)
    {
        if (onArrived == null)
            throw new ArgumentNullException(nameof(onArrived));

        InitializeMotion(direction, speed, lifetime);

        // 배치 검증이 끝난 고정 목적지 사용
        _fliesToPosition = true;
        _destination = destination;
        _onArrived = onArrived;
    }

    internal void Cancel()
    {
        if (_finished)
            return;

        _finished = true;
        _onHit = null;
        Destroy(gameObject);
    }

    void OnDisable()
    {
        if (_initialized)
            Cancel();
    }
}