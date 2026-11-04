using System;
using UnityEngine;

/// <summary>
/// 공용 돌진 공격의 준비와 접촉 타격 및 종료 관리
/// </summary>
internal sealed class ChargeAttackExecution
{
    enum Phase
    {
        Idle,
        Preparing,
        Charging
    }

    // 기존 Combat 행동의 양수 ID와 구분
    static long _nextActionId;

    readonly CombatController _combat;
    readonly IChargeMovement _movement;
    readonly Collider _contactCollider;

    Phase _phase;
    SkillInstance _skill;
    ChargeAttackSkillDefinition _definition;
    CombatActionInfo _action;
    SkillExecutionContext _execution;

    Guid _targetId;
    double _startsAt;
    float _distance;
    float _remainingDuration;
    float _power;

    internal event Action<CombatActionInfo> OnStarted;
    internal event Action<long, CombatActionResult> OnEnded;
    internal event Action<SkillExecutionContext> OnExecutionCreated;

    internal bool IsRunning => _phase != Phase.Idle;

    void BeginMovement()
    {
        SkillExecutionContext execution =
            new SkillExecutionContext(_combat.Owner, _skill, _action);

        float power = SkillDamageResolver.CalculatePower(
            execution, _definition.DamageMultiplier, _definition.SpellPowerMultiplier);

        if (!_movement.TryBegin(
                _action.Direction, _distance, _definition.Duration))
        {
            Finish(CombatActionResult.Canceled);
            return;
        }

        _execution = execution;
        _power = power;
        _remainingDuration = _definition.Duration;
        _phase = Phase.Charging;

        CombatActionInfo action = _action;

        // 실제 실행 통지 전에 이펙트 구독 연결
        OnExecutionCreated?.Invoke(execution);

        if (_phase != Phase.Charging || _action.Id != action.Id)
            return;

        execution.PublishExecuted(action, _combat.Owner.Position);

        if (_phase == Phase.Charging && _action.Id == action.Id)
            ProbeContact();
    }

    void ProbeContact()
    {
        if (_phase != Phase.Charging)
            return;

        long actionId = _action.Id;

        Physics.SyncTransforms();

        Bounds bounds = _contactCollider.bounds;

        // 기존 Collider의 Bounds로 주변 후보 탐색, 별도 타격 범위 없음
        Collider[] candidates = Physics.OverlapBox(
            bounds.center, bounds.extents, Quaternion.identity,
            _definition.StopMask, QueryTriggerInteraction.Collide);

        foreach (Collider other in candidates)
        {
            if (other.transform.IsChildOf(_combat.Owner.ActorTransform))
                continue;

            // Bounds 후보를 실제 Collider 형상으로 재검사
            bool overlapping = Physics.ComputePenetration(
                _contactCollider,
                _contactCollider.transform.position,
                _contactCollider.transform.rotation,
                other,
                other.transform.position,
                other.transform.rotation,
                out _,
                out _);

            if (!overlapping)
                continue;

            Finish(CombatActionResult.Completed, other);

            // 종료 알림 중 새 돌진이 시작된 경우에도 이전 검사는 종료
            if (_phase != Phase.Charging || _action.Id != actionId)
                return;
        }
    }

    void Finish(CombatActionResult result, Collider contact = null)
    {
        if (!IsRunning)
            return;

        CombatActionInfo action = _action;
        SkillExecutionContext execution = _execution;
        CombatTarget target = action.Target;
        Guid targetId = _targetId;
        float power = _power;
        int targetMask = _definition.TargetMask.value;

        _phase = Phase.Idle;
        _skill = null;
        _definition = null;
        _execution = null;
        _action = default;
        _targetId = Guid.Empty;
        _startsAt = 0d;
        _distance = 0f;
        _remainingDuration = 0f;
        _power = 0f;

        // 피해 적용 여부와 관계없이 먼저 정지
        _movement.Stop();

        if (contact != null && execution != null)
        {
            CombatTarget contactedTarget =
                contact.GetComponentInParent<CombatTarget>();
            bool isTargetLayer =
                (targetMask & (1 << contact.gameObject.layer)) != 0;

            // 지정 대상과 이번 활성화의 개체 ID 확인
            if (isTargetLayer && contactedTarget == target &&
                target != null && target.Source.Id == targetId)
            {
                SkillDamageResolver.TryApply(
                    execution, target, power, target.AimPosition,
                    Quaternion.LookRotation(action.Direction, Vector3.up), true);
            }
        }

        OnEnded?.Invoke(action.Id, result);
    }

    internal ChargeAttackExecution(
        CombatController combat,
        IChargeMovement movement,
        Collider contactCollider)
    {
        if (combat == null || combat.Owner == null)
            throw new ArgumentNullException(nameof(combat));
        if (movement == null)
            throw new ArgumentNullException(nameof(movement));
        if (contactCollider == null)
            throw new ArgumentNullException(nameof(contactCollider));

        if (movement.Combat != combat ||
            !contactCollider.transform.IsChildOf(combat.Owner.ActorTransform))
        {
            throw new InvalidOperationException(
                "같은 캐릭터의 Combat·이동·접촉 Collider 연결 필요");
        }

        bool supported =
            contactCollider is BoxCollider ||
            contactCollider is SphereCollider ||
            contactCollider is CapsuleCollider ||
            contactCollider is MeshCollider mesh && mesh.convex;
        if (!supported)
        {
            throw new InvalidOperationException(
                "Box·Sphere·Capsule 또는 Convex Mesh 접촉 Collider 필요");
        }

        _combat = combat;
        _movement = movement;
        _contactCollider = contactCollider;
    }

    internal bool TryStart(SkillInstance skill, SkillAimRequest request)
    {
        if (IsRunning || !_combat.isActiveAndEnabled || !_combat.Owner.IsAlive ||
            _combat.IsBusy || !_movement.IsActiveAndEnabled ||
            !_contactCollider.enabled || !_contactCollider.gameObject.activeInHierarchy ||
            Time.timeScale <= 0f)
        {
            return false;
        }

        if (skill == null || !skill.IsReady || skill.IsBasicAttack ||
            !(skill.Definition is ChargeAttackSkillDefinition definition))
        {
            return false;
        }

        definition.Validate();

        CombatTarget target = request.PreferredTarget;
        if (target == null ||
            target.CheckHitFrom(_combat.Owner, definition.Range, 0) != AttackTargetState.Valid)
        {
            return false;
        }

        // 승인 시점의 타겟 위치를 기준으로 방향과 최소 이동 거리 확정
        Vector3 direction = target.Position - _combat.Owner.Position;
        direction.y = 0f;
        float lengthSquared = direction.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) ||
            lengthSquared <= 0.0001f)
        {
            return false;
        }

        float targetDistance = Mathf.Sqrt(lengthSquared);
        direction /= targetDistance;

        float distance = Mathf.Max(targetDistance, definition.MinimumDistance);
        if (float.IsNaN(distance / definition.Duration) ||
            float.IsInfinity(distance / definition.Duration))
        {
            return false;
        }

        if (!_movement.CanBegin(direction, distance, definition.Duration))
            return false;

        _movement.Stop();

        _skill = skill;
        _definition = definition;
        _distance = distance;
        _targetId = target.Source.Id;
        _action = new CombatActionInfo(
            --_nextActionId, CombatActionKind.Skill, target, definition,
            Time.timeAsDouble, definition.CastTime + definition.Duration,
            direction, default, false);
        _startsAt = Time.timeAsDouble + definition.CastTime;
        _phase = Phase.Preparing;

        // 승인 후 시작 실패 또는 취소 시 쿨타임 유지
        skill.BeginCooldown();

        long actionId = _action.Id;
        OnStarted?.Invoke(_action);

        if (!IsRunning || _action.Id != actionId)
            return true;

        if (definition.CastTime <= 0f)
            BeginMovement();

        return true;
    }

    internal void Tick(float deltaTime)
    {
        if (!IsRunning)
            return;

        if (!_combat.isActiveAndEnabled || !_combat.Owner.IsAlive ||
            _combat.IsBusy || !_movement.IsActiveAndEnabled ||
            !_contactCollider.enabled || !_contactCollider.gameObject.activeInHierarchy)
        {
            Cancel();
            return;
        }

        if (deltaTime <= 0f)
            return;

        if (_phase == Phase.Preparing)
        {
            if (Time.timeAsDouble >= _startsAt)
                BeginMovement();
            return;
        }

        long actionId = _action.Id;

        ProbeContact();
        if (_phase != Phase.Charging || _action.Id != actionId)
            return;

        float stepTime = Mathf.Min(deltaTime, _remainingDuration);
        _remainingDuration -= stepTime;

        SkillMovementStepResult result = _movement.Tick(stepTime);
        if (_phase != Phase.Charging || _action.Id != actionId)
            return;

        if (result == SkillMovementStepResult.Failed)
        {
            Cancel();
            return;
        }

        ProbeContact();
        if (_phase != Phase.Charging || _action.Id != actionId)
            return;

        if (result == SkillMovementStepResult.Completed || _remainingDuration <= 0f)
            Finish(CombatActionResult.Completed);
    }

    internal void Cancel()
    {
        Finish(CombatActionResult.Canceled);
    }
}
