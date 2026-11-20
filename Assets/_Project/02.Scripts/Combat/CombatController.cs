using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CombatController : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] CombatTarget _owner;
    [SerializeField] SkillLoadout _skills;
    [SerializeField] Transform _effectPos;

    [Header("----- 기본 공격 -----")]
    [SerializeField] LayerMask _blockingMask;
    [Tooltip("켜면 기본 공격 중에도 일반 이동 허용")]
    [SerializeField] bool _allowMoveDuringBasicAttack;

    [Header("----- 범위 미리보기 -----")]
    [SerializeField] SkillDefinition _previewSkill;

    [Header("----- 선택 이동 스킬 연결 -----")]
    [SerializeField] MonoBehaviour _movementSource;
    
    Guid _fixedSkillsRunId;
    ICombatMovement _movement;
    CombatActionKind _action;
    CombatTarget _actionTarget;
    BasicAttackCombo _basicAttackCombo;
    SkillInstance _castingSkill;
    SkillInstance _movementSkill;
    Vector3 _actionDirection;
    Vector3 _actionWorldPosition;
    // 이동 단계가 있는 행동의 확정된 이동 거리
    float _actionMoveDistance;
    // 이동 완료 시 도착 효과에 사용할 이동 시작 시점 실행 정보
    SkillExecutionContext _movementExecution;

    long _nextActionId;
    long _actionId;
    double _actionStartedAt;
    double _finishAt;
    double _nextAttackAt;
    double _postCastMoveLockUntil;
    AnimationClip _actionMotionClip;
    bool _actionHasWorldPosition;
    bool _movementExecuting;

    public event Action OnActionChanged;
    public event Action<CombatActionInfo> OnActionStarted;
    public event Action<long, CombatActionResult> OnActionEnded;
    public event Action<SkillExecutionContext> OnExecutionCreated;
    public event Action<CombatActionInfo, Vector3> OnMovementCompleted;

    public CombatTarget Owner => _owner;
    public SkillLoadout Skills => _skills;
    public CombatActionKind Action => _action;
    public BasicAttackComboDefinition BasicAttackComboDefinition => _owner.Status.Definition.BasicAttackCombo;
    public MovementSkillDefinition MovementDefinition => _movementSkill != null ? (MovementSkillDefinition)_movementSkill.Definition : null;
    public int BasicAttackLevel => _basicAttackCombo != null ? _basicAttackCombo.Skill.Level : 0;
    public int MovementSkillLevel => _movementSkill != null ? _movementSkill.Level : 0;
    // 고정 이동 스킬의 실제 남은 쿨타임
    public float MovementSkillRemainingCooldown => _movementSkill != null ? _movementSkill.RemainingCooldown : 0f;
    // 고정 이동 스킬의 실제 남은 쿨타임 비율
    public float MovementSkillCooldownRemainingRatio => _movementSkill != null ? _movementSkill.CooldownRemainingRatio : 0f;
    public float AttackRange => BasicAttackComboDefinition != null ? BasicAttackComboDefinition.Range : _owner.Status.Definition.AttackRange;
    LayerMask BasicBlockingMask => BasicAttackComboDefinition != null ? BasicAttackComboDefinition.BlockingMask : _blockingMask;
    public bool IsBusy => _action != CombatActionKind.None;
    public bool IsCasting => _action == CombatActionKind.Skill;    
    // 현재 시전 중인 FollowCaster 스킬의 일반 이동 허용 여부
    bool AllowsMovementDuringCast => IsCasting && _castingSkill.Definition is AreaDamageSkillDefinition area && area.TrackingMode == AreaTrackingMode.FollowCaster;
    // 이동 허용 설정을 켠 소유자의 기본 공격 중 일반 이동 허용 여부
    bool AllowsMovementDuringBasicAttack => _allowMoveDuringBasicAttack && _action == CombatActionKind.BasicAttack;
    // 게임 시간 기준의 발동 후 이동 제한 여부
    bool IsPostCastMoveLocked => Time.timeAsDouble < _postCastMoveLockUntil;
    // 기본 공격 중 이동 허용 설정 여부
    public bool MovesDuringBasicAttack => _allowMoveDuringBasicAttack;
    // 전투 행동과 발동 후 이동 제한을 반영한 일반 이동 허용 여부
    public bool CanMove => isActiveAndEnabled && _owner.IsAlive && !IsPostCastMoveLocked
        && (!IsBusy || AllowsMovementDuringCast || AllowsMovementDuringBasicAttack);
    // 기본 공격 취소와 발동 후 이동 제한을 반영한 이동 요청 허용 여부
    public bool CanRequestMovement => isActiveAndEnabled && _owner.IsAlive && !IsPostCastMoveLocked && (!IsCasting || AllowsMovementDuringCast);

    public CombatActionInfo CurrentAction
    {
        get
        {
            if (!IsBusy)
                return default;

            bool legacyAttack = _action == CombatActionKind.BasicAttack && _castingSkill == null;

            double endAt = legacyAttack ? _nextAttackAt : _finishAt;

            SkillDefinition skill = _castingSkill != null ? _castingSkill.Definition : null;

            return new CombatActionInfo(_actionId, _action, _actionTarget, skill, _actionStartedAt, (float)(endAt - _actionStartedAt), _actionDirection, _actionWorldPosition, _actionHasWorldPosition, _actionMotionClip);
        }
    }

    void Awake()
    {
        if (_owner == null || _owner.Status == null || _owner.ActorTransform == null)
            throw new MissingReferenceException($"{name}: CombatTarget의 상태와 캐릭터 Transform 연결이 필요합니다.");

        Transform actorTransform = _owner.ActorTransform;

        if (!transform.IsChildOf(actorTransform))
            throw new InvalidOperationException($"{name}: CombatController는 소유 캐릭터 계층에 있어야 합니다.");

        if (_skills != null && (_skills.Combat != this || !_skills.transform.IsChildOf(actorTransform)))
            throw new InvalidOperationException($"{name}: 같은 캐릭터의 SkillLoadout을 서로 연결하세요.");

        if (_effectPos == null || !_effectPos.IsChildOf(actorTransform))
            throw new MissingReferenceException($"{name}: 같은 캐릭터 계층의 EffectPos 연결이 필요합니다.");

        // 플레이어 전용 정의의 고정 스킬은 런 시작 단계에서 초기화
        // 기존 공통 정의와 적의 기본 공격 초기화 경로 유지
        if (!(_owner.Status.Definition is PlayerDefinition))
            _basicAttackCombo = CreateBasicAttackCombo(BasicAttackComboDefinition);
    }

    void OnEnable()
    {
        _owner.Status.OnDied += HandleOwnerDied;
    }

    void OnDisable()
    {
        if (_owner != null && _owner.Status != null)
            _owner.Status.OnDied -= HandleOwnerDied;

        _postCastMoveLockUntil = 0d;
        ResetBasicAttackCombo();
        CancelAction();
    }

    void Update()
    {
        if (_postCastMoveLockUntil > 0d && Time.timeAsDouble >= _postCastMoveLockUntil)
        {
            _postCastMoveLockUntil = 0d;
            OnActionChanged?.Invoke();
        }

        if (!IsBusy || Time.timeScale <= 0f)
            return;

        if (!_owner.IsAlive)
        {
            CancelAction();
            return;
        }

        if (_movementExecuting)
        {
            if (_movementSource == null || !_movementSource.isActiveAndEnabled)
            {
                CancelAction();
                return;
            }

            SkillMovementStepResult result = _movement.TickSkillMovement(Time.deltaTime);

            if (result == SkillMovementStepResult.Completed)
                FinishMovementAction(_actionId);
            else if (result == SkillMovementStepResult.Failed)
                CancelAction();

            return;
        }

        if (Time.timeAsDouble < _finishAt)
            return;

        CompletedAction(_actionId);
    }

    static BasicAttackCombo CreateBasicAttackCombo(BasicAttackComboDefinition definition)
    {
        // 고정 기본 공격 콤보 미지정 시 기존 기본 공격 경로 사용
        if (definition == null)
            return null;

        return new BasicAttackCombo(definition);
    }

    void HandleOwnerDied()
    {
        _postCastMoveLockUntil = 0d;
        ResetBasicAttackCombo();
        CancelAction();
    }

    void ResetBasicAttackCombo()
    {
        if (_basicAttackCombo != null)
            _basicAttackCombo.Reset();
    }

    void CompletedAction(long actionId)
    {
        if (!IsBusy || _actionId != actionId)
            return;

        if (!isActiveAndEnabled || !_owner.IsAlive)
        {
            CancelAction();
            return;
        }

        if (_castingSkill != null && _castingSkill.Definition is ISkillMovementSettings movementSettings)
        {
            BeginMovementExecution(movementSettings);
            return;
        }

        CombatActionInfo completedAction = CurrentAction;
        CombatTarget target = _actionTarget;
        SkillInstance skill = _castingSkill;

        Vector3 origin = _effectPos.position;
        Vector3 effectOrigin = origin;

        float basicRange = AttackRange;
        LayerMask basicBlockingMask = BasicBlockingMask;

        // 시전자 위치 재생을 켠 대상 공격은 이펙트 기준점 유지
        if (skill != null && skill.Definition is TargetDamageSkillDefinition targeted && !targeted.UseProjectile && !targeted.UseEffectAtCaster && target != null)
            effectOrigin = target.Position;

        if (skill != null && skill.Definition is AreaDamageSkillDefinition area && area.DeliveryMode == AreaDeliveryMode.Direct)
            effectOrigin = SkillExecutor.ResolveAreaOrigin(area, origin, completedAction);

        // 이후에는 시전자 객체의 생존 상태와 독립적으로 실행
        SkillExecutionContext execution = new SkillExecutionContext(_owner, skill, completedAction);

        // 실행 확정 시점에 다음 공격 가능 시각 갱신
        if (completedAction.Kind == CombatActionKind.BasicAttack && _basicAttackCombo != null)
        {
            if (_basicAttackCombo.IsCombo)
                _basicAttackCombo.Complete(completedAction.StartedAt, GetBasicAttackSpeedRate());
            else
                _nextAttackAt = Time.timeAsDouble + GetBasicAttackInterval();
        }

        ResetActionState();

        if (skill != null && skill.Definition.PostCastMoveLockDuration > 0f)
        {
            double lockUntil = Time.timeAsDouble + skill.Definition.PostCastMoveLockDuration;

            // 기존 잠금을 단축하지 않고 더 늦은 종료 시각 적용
            _postCastMoveLockUntil = Math.Max(_postCastMoveLockUntil, lockUntil);
        }

        OnExecutionCreated?.Invoke(execution);

        if (skill != null)
        {
            TargetProjectile projectile = SkillExecutor.Execute(origin, execution, _owner);

            if (projectile != null)
            {
                effectOrigin = projectile.transform.position;

                completedAction = new CombatActionInfo(completedAction.Id, completedAction.Kind, completedAction.Target, completedAction.Skill, completedAction.StartedAt, completedAction.Duration, projectile.Direction, completedAction.WorldPosition, completedAction.HasWorldPosition, completedAction.MotionClip);
            }
        }
        else if (completedAction.Kind == CombatActionKind.BasicAttack)
        {
            SkillExecutor.ExecuteBasicAttack(execution, target, basicRange, basicBlockingMask);
        }

        execution.PublishExecuted(completedAction, effectOrigin);

        OnActionEnded?.Invoke(completedAction.Id, CombatActionResult.Completed);

        OnActionChanged?.Invoke();
    }

    bool BeginSkillAction(SkillInstance skill, Vector3 direction, CombatTarget target, Vector3 worldPosition, bool hasWorldPosition, float moveDistance)
    {
        long replacedActionId = _actionId;
        double now = Time.timeAsDouble;

        // 스킬과 이동 스킬 사용 시 기본 공격 콤보 단절
        ResetBasicAttackCombo();

        _action = CombatActionKind.Skill;
        _actionTarget = target;
        _castingSkill = skill;
        _actionDirection = direction;
        _actionWorldPosition = worldPosition;
        _actionHasWorldPosition = hasWorldPosition;
        _actionMoveDistance = moveDistance;

        _actionId = ++_nextActionId;
        _actionStartedAt = now;
        _finishAt = now + skill.Definition.CastTime;

        // 이동 단계가 있는 스킬은 실제 이동 시작 성공 시 쿨타임 적용
        if (!(skill.Definition is ISkillMovementSettings))
            skill.BeginCooldown();

        CombatActionInfo started = CurrentAction;

        if (replacedActionId != 0)
            OnActionEnded?.Invoke(replacedActionId, CombatActionResult.Replaced);

        if (_actionId != started.Id)
            return true;

        OnActionChanged?.Invoke();

        if (_actionId != started.Id)
            return true;

        OnActionStarted?.Invoke(started);

        if (started.Duration <= 0f)
            CompletedAction(started.Id);

        return true;
    }

    void BeginMovementExecution(ISkillMovementSettings settings)
    {
        CombatActionInfo action = CurrentAction;
        SkillInstance skill = _castingSkill;
        Vector3 origin = _owner.Position;

        SkillExecutionContext execution = new SkillExecutionContext(_owner, skill, action);

        if (_movementSource == null || !_movementSource.isActiveAndEnabled || !_movement.TryBeginSkillMovement(settings, action.Direction, _actionMoveDistance))
        {
            CancelAction();
            return;
        }

        _movementExecuting = true;
        _movementExecution = execution;
        skill.BeginCooldown();

        // 기존 사용 이펙트 경로 재사용
        OnExecutionCreated?.Invoke(execution);

        if (_actionId != action.Id)
            return;

        execution.PublishExecuted(action, origin);

        if (_actionId == action.Id && settings.Mode == MovementSkillMode.Teleport)
            FinishMovementAction(action.Id);
    }

    void FinishMovementAction(long actionId)
    {
        if (!IsBusy || !_movementExecuting || _actionId != actionId)
            return;

        CombatActionInfo action = CurrentAction;
        Vector3 position = _owner.Position;
        SkillExecutionContext execution = _movementExecution;

        ResetActionState();

        // 취소·실패가 아닌 이동 완료 시에만 도착 위치 기준 범위 피해
        if (action.Skill is DashStrikeSkillDefinition dashStrike)
            SkillExecutor.ExecuteDashStrikeArrival(dashStrike, position, execution);

        OnMovementCompleted?.Invoke(action, position);
        OnActionEnded?.Invoke(action.Id, CombatActionResult.Completed);
        OnActionChanged?.Invoke();
    }

    Vector3 NormalizeDirection(Vector3 direction)
    {
        direction.y = 0f;
        float lengthSquared = direction.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared))
            throw new ArgumentException("유한한 행동 방향이 필요합니다.", nameof(direction));

        if (lengthSquared <= 0.0001f)
        {
            direction = _owner.ActorTransform.forward;
            direction.y = 0f;
            lengthSquared = direction.sqrMagnitude;
        }

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.0001f)
            throw new InvalidOperationException($"{name}: 캐릭터의 수평 정면 방향을 확인하세요.");

        return direction.normalized;
    }

    float GetBasicAttackInterval()
    {
        float attackSpeed = _owner.Status.AttackSpeed;

        if (float.IsNaN(attackSpeed) || float.IsInfinity(attackSpeed) || attackSpeed <= 0f)
            throw new InvalidOperationException($"{name}: 유효한 공격속도 설정 필요");

        float interval = 1f / attackSpeed;

        if (float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0f)
            throw new InvalidOperationException($"{name}: 유효한 기본 공격 간격 설정 필요");

        return interval;
    }

    float GetBasicAttackSpeedRate()
    {
        float attackSpeed = _owner.Status.AttackSpeed;
        float baseAttackSpeed = _owner.Status.Definition.BaseAttackSpeed;

        // 정의의 기본 공격속도 대비 현재 공격속도 비율
        float rate = attackSpeed / baseAttackSpeed;

        if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
            throw new InvalidOperationException($"{name}: 유효한 공격속도와 기본 공격속도 설정 필요");

        return rate;
    }

    void ResetActionState()
    {
        if (_movementExecuting)
        {
            if (_movementSource != null)
                _movement.CancelSkillMovement();

            _movementExecuting = false;
        }

        _action = CombatActionKind.None;
        _actionTarget = null;
        _castingSkill = null;
        _actionDirection = Vector3.zero;

        _actionId = 0;
        _actionStartedAt = 0d;
        _finishAt = 0d;
        _actionMotionClip = null;

        _actionWorldPosition = default;
        _actionHasWorldPosition = false;
        _actionMoveDistance = 0f;
        _movementExecution = null;
    }

    void CancelAction()
    {
        long actionId = _actionId;

        ResetActionState();

        if(actionId != 0)
            OnActionEnded?.Invoke(actionId, CombatActionResult.Canceled);

        OnActionChanged?.Invoke();
    }

    /// <summary>
    /// 새 런의 고정 기본 공격과 이동 스킬 초기화
    /// </summary>
    /// <param name="definition">선택한 플레이어 정의</param>
    /// <param name="runId">초기화할 런 식별자</param>
    public void InitializeFixedSkillsForRun(PlayerDefinition definition, Guid runId)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (runId == Guid.Empty)
            throw new ArgumentException("유효한 런 식별자 필요", nameof(runId));

        CharacterStatus status = _owner.Status;

        if (!status.IsInitialized || status.Definition != definition || status.RunId != runId)
            throw new InvalidOperationException($"{name}: 같은 플레이어 정의와 런으로 전투 상태 초기화 필요");

        // 같은 런의 반복 초기화로 쿨타임이 사라지는 상황 방지
        if (_fixedSkillsRunId == runId)
            return;

        if (IsBusy)
            throw new InvalidOperationException($"{name}: 진행 중인 전투 행동 종료 후 고정 스킬 초기화 필요");

        if (definition.BasicAttackCombo == null)
            throw new MissingReferenceException($"{definition.name}: 플레이어 기본 공격 콤보 연결 필요");

        BasicAttackCombo basicAttackCombo = CreateBasicAttackCombo(definition.BasicAttackCombo);

        ICombatMovement movement = null;
        SkillInstance movementSkill = null;

        MovementSkillDefinition movementDefinition = definition.MovementSkill;

        // 고정 이동 스킬과 슬롯 돌진 스킬이 함께 사용하는 이동 실행 연결
        if (_movementSource != null)
        {
            if (!(_movementSource is ICombatMovement movementComponent))
                throw new InvalidOperationException($"{name}: Movement Source에 ICombatMovement 구현 필요");

            Transform actorTransform = _owner.ActorTransform;

            if (movementComponent.ActorTransform != actorTransform || !_movementSource.transform.IsChildOf(actorTransform))
                throw new InvalidOperationException($"{name}: 같은 캐릭터 기준 Transform을 사용하는 이동 컴포넌트 연결 필요");

            movement = movementComponent;
        }

        if (movementDefinition != null)
        {
            if (movement == null)
                throw new MissingReferenceException($"{name}: 이동 스킬을 실행할 Movement Source 연결 필요");

            movementDefinition.Validate();

            movementSkill = new SkillInstance(movementDefinition);
        }

        // 필수 설정 검증과 인스턴스 생성 완료 후 상태 교체
        _basicAttackCombo = basicAttackCombo;
        _movement = movement;
        _movementSkill = movementSkill;

        _nextAttackAt = 0d;
        _postCastMoveLockUntil = 0d;
        _fixedSkillsRunId = runId;
    }

    public AttackTargetState CheckBasicAttackTarget(CombatTarget target)
    {
        if (target == null)
            return AttackTargetState.Invalid;

        return target.CheckHitFrom(_owner, AttackRange, BasicBlockingMask);
    }

    /// <summary>고정 스킬 또는 기존 방식의 기본 공격 요청</summary>
    /// <param name="target">명령으로 지정한 공격 대상</param>
    /// <returns>기본 공격 요청 수락 여부</returns>
    public bool TryAttack(CombatTarget target)
    {
        if (!isActiveAndEnabled || !_owner.IsAlive || IsBusy || Time.timeScale <= 0f)
            return false;

        double now = Time.timeAsDouble;
        bool combo = _basicAttackCombo != null && _basicAttackCombo.IsCombo;
        double readyAt = combo ? _basicAttackCombo.NextAttackAt : _nextAttackAt;

        if (now < readyAt)
            return false;

        if (CheckBasicAttackTarget(target) != AttackTargetState.Valid)
            return false;

        SkillInstance step = null;
        AnimationClip motionClip = null;
        float hitDelay;

        if (combo)
        {
            _basicAttackCombo.Begin(now);

            // 콤보 단계는 스킬 CastTime 대신 단계 실행 지점 사용
            // 모션과 실행 시점은 고정, 공격속도는 실행 완료 후 공백에만 반영
            step = _basicAttackCombo.CurrentSkill;
            motionClip = _basicAttackCombo.CurrentMotionClip;
            hitDelay = _basicAttackCombo.CurrentExecuteTime;
        }
        else if (_basicAttackCombo != null)
        {
            // 단계 없는 단일 기본 공격, 다음 공격 시각은 실행 완료 시 공격속도 간격으로 갱신
            step = _basicAttackCombo.Skill;
            hitDelay = step.Definition.CastTime;
        }
        else
        {
            float interval = GetBasicAttackInterval();

            hitDelay = interval * _owner.Status.Definition.AttackPointRatio;

            // 기존 공격의 주기 제한만 유지
            _nextAttackAt = now + interval;
        }

        Vector3 direction = NormalizeDirection(target.Position - _owner.Position);

        _action = CombatActionKind.BasicAttack;
        _actionTarget = target;
        _castingSkill = step;
        _actionDirection = direction;
        _actionMotionClip = motionClip;

        _actionWorldPosition = default;
        _actionHasWorldPosition = false;

        _actionId = ++_nextActionId;
        _actionStartedAt = now;
        _finishAt = now + hitDelay;

        CombatActionInfo started = CurrentAction;
        OnActionChanged?.Invoke();

        if (_actionId != started.Id)
            return true;

        OnActionStarted?.Invoke(started);

        if (hitDelay <= 0f)
            CompletedAction(started.Id);

        return true;
    }

    bool TryStartSkill(SkillInstance skill, SkillAimRequest request)
    {
        if (!isActiveAndEnabled || !_owner.IsAlive || IsCasting || Time.timeScale <= 0f)
            return false;

        if (skill == null || !skill.IsReady || skill.IsBasicAttack || _owner.Status.Definition.IsBasicAttackSkill(skill.Definition))
            return false;

        SkillExecutor.ValidateDefinition(skill.Definition);

        if(_effectPos == null)
            throw new MissingReferenceException($"{name}: EffectPos가 필요합니다.");

        SkillAimRequest acceptedRequest = request;

        if (skill.Definition is AreaDamageSkillDefinition area && area.PlacementMode == AreaPlacementMode.RequestedWorldPosition)
        {
            if (!SkillExecutor.TryResolveRequestedAreaPosition(area, _owner.Position, request, out Vector3 placementPosition))
                return false;

            // 보정된 목적지와 목적지 기준 방향을 수락 시점에 확정
            acceptedRequest = new SkillAimRequest(requestedDirection: placementPosition - _owner.Position, preferredTarget: null, worldPosition: placementPosition, hasWorldPosition: true);
        }

        Vector3 resolvedDirection = SkillAimResolver.Resolve(_owner, skill.Definition, acceptedRequest, out CombatTarget skillTarget);
        Vector3 direction = NormalizeDirection(resolvedDirection);

        float moveDistance = 0f;

        if (skill.Definition is DashStrikeSkillDefinition dashStrike)
        {
            if (_movement == null)
                throw new MissingReferenceException($"{name}: 돌진 스킬을 실행할 Movement Source 연결 필요");

            if (!_movementSource.isActiveAndEnabled)
                return false;

            moveDistance = ResolveDashStrikeDistance(dashStrike, acceptedRequest);

            // 시전과 기존 행동 교체 이전에 돌진 가능 여부 확인
            if (!_movement.CanStartSkillMovement(dashStrike, direction, moveDistance))
                return false;
        }

        return BeginSkillAction(skill, direction, skillTarget, acceptedRequest.WorldPosition, acceptedRequest.HasWorldPosition, moveDistance);
    }

    // 커서까지의 수평 거리를 최소·최대 거리로 제한, 커서 좌표가 없으면 최대 거리
    float ResolveDashStrikeDistance(DashStrikeSkillDefinition definition, SkillAimRequest request)
    {
        if (!request.HasWorldPosition)
            return definition.Distance;

        Vector3 offset = request.WorldPosition - _owner.Position;
        offset.y = 0f;
        float distance = offset.magnitude;

        if (float.IsNaN(distance) || float.IsInfinity(distance))
            throw new ArgumentException("유한한 지정 좌표 필요", nameof(request));

        return Mathf.Clamp(distance, definition.MinDistance, definition.Distance);
    }

    /// <summary>조준 요청을 해석한 뒤 일반 스킬 시전 시작</summary>
    /// <param name="slot">사용할 스킬 슬롯 번호</param>
    /// <param name="request">입력 방향과 우선 대상</param>
    /// <returns>스킬 사용 요청 수락 여부</returns>
    // 플레이어 장착 슬롯에서 스킬을 조회한 뒤 공통 사용 처리로 전달
    public bool TryUseSkill(int slot, SkillAimRequest request)
    {
        if (_skills == null || !_skills.isActiveAndEnabled)
            return false;

        SkillInstance skill = _skills.GetSkill(slot);

        return TryStartSkill(skill, request);
    }

    /// <summary>
    /// 적 스킬 인스턴스의 시전 요청
    /// </summary>
    /// <param name="skill">사용할 적 스킬 인스턴스</param>
    /// <param name="request">입력 방향과 우선 대상</param>
    /// <returns>스킬 사용 요청 수락 여부</returns>
    // 적 스킬을 플레이어 슬롯과 독립적으로 공통 사용 처리에 전달
    public bool TryEnemyAttack(SkillInstance skill, SkillAimRequest request)
    {
        return TryStartSkill(skill, request);
    }



    /// <summary>
    /// 캐릭터 고정 이동 스킬의 시전 요청
    /// </summary>
    /// <param name="requestedDirection">입력 순간의 월드 방향</param>
    /// <returns>시전 요청 수락 여부</returns>
    public bool TryUseMovementSkill(Vector3 requestedDirection)
    {
        if (!isActiveAndEnabled || !_owner.IsAlive || IsCasting || Time.timeScale <= 0f)
            return false;

        if (IsPostCastMoveLocked)
            return false;

        if (_movementSkill == null || !_movementSkill.IsReady)
            return false;

        if (_movementSource == null)
            throw new MissingReferenceException($"{name}: 이동 스킬 실행 연결 필요");

        if (!_movementSource.isActiveAndEnabled)
            return false;

        MovementSkillDefinition definition = (MovementSkillDefinition)_movementSkill.Definition;

        definition.Validate();

        Vector3 direction = NormalizeDirection(requestedDirection);

        // 시전과 기존 행동 교체 이전에 이동 가능 여부 확인
        if (!_movement.CanStartSkillMovement(definition, direction, definition.Distance))
            return false;

        return BeginSkillAction(_movementSkill, direction, null, default, false, definition.Distance);
    }

    /// <summary>
    /// 진행 중인 이동 스킬·돌진 스킬의 시전 또는 이동 취소
    /// </summary>
    public void CancelMovementSkill()
    {
        if (_castingSkill != null && _castingSkill.Definition is ISkillMovementSettings)
            CancelAction();
    }

    /// <summary>
    /// 진행 중인 기본 공격 취소와 콤보 단절
    /// </summary>
    // 이동 요청과 대상 상실 시마다 호출되므로 타격 이후 후딜레이 이동도 콤보 단절로 처리
    public void CancelBasicAttack()
    {
        ResetBasicAttackCombo();

        if (_action == CombatActionKind.BasicAttack)
            CancelAction();
    }

    void OnDrawGizmosSelected()
    {
        if (_owner == null || _owner.ActorTransform == null ||
            _owner.Status == null || _owner.Status.Definition == null)
        {
            return;
        }

        SkillDefinition preview = IsBusy
            ? (_castingSkill != null ? _castingSkill.Definition : null)
            : (_previewSkill != null ? _previewSkill : (BasicAttackComboDefinition != null ? BasicAttackComboDefinition.Skill : null));

        bool basicPreview = IsBusy
            ? _action == CombatActionKind.BasicAttack
            : _previewSkill == null;

        if (basicPreview || preview is TargetDamageSkillDefinition)
        {
            float range = basicPreview
                ? AttackRange
                : ((TargetDamageSkillDefinition)preview).Range;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(_owner.Position, range);

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;
        }

        Vector3 direction = IsBusy
            ? _actionDirection
            : _owner.ActorTransform.forward;

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion rotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);

        if (preview is AreaDamageSkillDefinition area)
        {
            AreaEffectDefinition effect = area.AreaEffect;

            if (_effectPos == null ||
                effect == null ||
                effect.Hitbox == null)
            {
                return;
            }

            // 지정 좌표가 없는 대기 상태의 범위 표시는 생략
            if (area.PlacementMode ==
                    AreaPlacementMode.RequestedWorldPosition &&
                (!IsBusy || !_actionHasWorldPosition))
            {
                return;
            }

            Vector3 areaOrigin = SkillExecutor.ResolveAreaOrigin(
                area,
                _effectPos.position,
                CurrentAction);

            CombatHitQuery.DrawPreview(
                areaOrigin,
                rotation,
                Vector3.zero,
                effect.Hitbox);
        }
        else if (preview is TargetDamageSkillDefinition targeted &&
                 IsBusy &&
                 _actionTarget != null)
        {
            AreaEffectDefinition effect = targeted.OnHitAreaEffect;

            if (effect == null || effect.Hitbox == null)
                return;

            CombatHitQuery.DrawPreview(
                _actionTarget.Position,
                rotation,
                Vector3.zero,
                effect.Hitbox);
        }
    }
}