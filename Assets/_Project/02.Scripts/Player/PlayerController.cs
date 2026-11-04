using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerController : MonoBehaviour
{
    enum CommandKind
    {
        None,
        Move,
        AttackMove,
        AttackTarget,
        StationaryAttack
    }

    [Header("----- 연결 -----")]
    [SerializeField] CombatController _combat;
    [SerializeField] PlayerMovement _movement;
    

    [Header("----- 대상 탐색 -----")]
    [SerializeField] LayerMask _targetMask;
    [SerializeField, Min(0.05f)] float _repathInterval = 0.2f;

    CommandKind _command;
    Vector3 _destination;
    CombatTarget _target;
    PlayerAnimation _animation;
    double _nextPathAt;
    bool _subscribed;

    public bool CanAcceptCommands => isActiveAndEnabled && _subscribed && HasPreparedAnimation && Time.timeScale > 0f && _combat.isActiveAndEnabled && _combat.Owner.IsAlive;
    public Vector3 Position => _combat.Owner.Position;
    public Vector3 Forward => _combat.Owner.ActorTransform.forward;
    public bool HasPreparedAnimation => _animation != null && _animation.IsPrepared;

    void Awake()
    {
        ValidateCoreReferences();
    }

    void OnEnable()
    {
        try
        {
            ValidateCoreReferences();
            ValidateAnimationReference(_animation);

            // 기존 Inspector 연결과 PlayerActor 연결이 같은 준비 경로 사용
            _animation.Synchronize(_combat.CurrentAction);

            if (!isActiveAndEnabled)
                throw new OperationCanceledException( $"{name}: 조작 준비 중 PlayerController 비활성화");

            Subscribe();
        }
        catch
        {
            // 준비 실패 상태에서는 입력 요청과 Update 실행 차단
            enabled = false;
            throw;
        }
    }

    void OnDisable()
    {
        bool wasSubscribed = _subscribed;

        // 취소 과정에서 발생하는 전투 이벤트를 받지 않도록 먼저 해제
        Unsubscribe();
        ResetCommand();

        if (wasSubscribed)
        {
            if (_movement != null)
                _movement.Stop();

            if (_combat != null)
            {
                _combat.CancelBasicAttack();
                _combat.CancelMovementSkill();
            }
        }

        if (_animation != null)
            _animation.ClearAction();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Update()
    {
        UpdateCommand();

        CharacterStatus status = _combat.Owner.Status;

        _movement.Tick(status.MoveSpeed, _combat.CanMove);

        _animation.Present(status.IsAlive, _movement.LocomotionSpeed, status.MoveSpeed);
    }

    void Subscribe()
    {
        if (_subscribed)
            return;

        // 일부 구독 이후 실패하더라도 해제할 수 있도록 선기록
        _subscribed = true;

        _movement.OnMoveFailed += ClearCommand;
        _movement.OnArrived += HandleArrived;

        _combat.OnActionChanged += HandleActionChanged;
        _combat.OnActionStarted += HandleActionStarted;
        _combat.OnActionEnded += HandleActionEnded;

        _combat.Owner.Status.OnDied += HandleDeath;
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _subscribed = false;

        if (_movement != null)
        {
            _movement.OnMoveFailed -= ClearCommand;
            _movement.OnArrived -= HandleArrived;
        }

        if (_combat != null)
        {
            _combat.OnActionChanged -= HandleActionChanged;
            _combat.OnActionStarted -= HandleActionStarted;
            _combat.OnActionEnded -= HandleActionEnded;

            if (_combat.Owner != null && _combat.Owner.Status != null)
                _combat.Owner.Status.OnDied -= HandleDeath;
        }
    }



    void ValidateCoreReferences()
    {
        if (_movement == null || _combat == null)
            throw new MissingReferenceException($"{name}: 이동과 전투 컴포넌트 연결 필요");

        CombatTarget owner = _combat.Owner;

        if (owner == null || owner.Status == null || owner.ActorTransform == null)
            throw new MissingReferenceException($"{name}: 전투 소유자의 상태와 캐릭터 Transform 연결 필요");

        Transform actorTransform = owner.ActorTransform;

        if (owner.Team != CombatTeam.Player || !transform.IsChildOf(actorTransform) || !_combat.transform.IsChildOf(actorTransform) || !_movement.transform.IsChildOf(actorTransform) || _movement.ActorTransform != actorTransform)
            throw new InvalidOperationException($"{name}: 같은 플레이어의 입력, 이동, 전투 연결 필요");
    }

    void ValidateAnimationReference(PlayerAnimation animation)
    {
        if (animation == null)
            throw new MissingReferenceException($"{name}: PlayerAnimation 연결 필요");

        if (!animation.transform.IsChildOf(_combat.Owner.ActorTransform))
            throw new InvalidOperationException($"{name}: 같은 플레이어 계층의 외형 애니메이션 연결 필요");
    }

    void HandleActionChanged()
    {
        if (!_combat.CanMove)
            _movement.Stop();

        if (!_combat.isActiveAndEnabled || !_combat.Owner.IsAlive)
            _animation.ClearAction();
    }

    void HandleActionStarted(CombatActionInfo info)
    {
        if (info.Kind == CombatActionKind.Skill)
        {
            bool preserveMovement = _combat.CanMove && _movement.HasMoveRequest;

            if (!preserveMovement)
            {
                ClearCommand();

                if (info.Skill.RotationMode == SkillRotationMode.FaceDirection)
                {
                    _movement.Face(_combat.Owner.Position + info.Direction);
                }
            }

            // 이동 유지 시 기존 경로와 이동 방향을 그대로 사용
        }
        else if (info.Target != null)
        {
            _movement.Face(info.Target.Position);
        }

        _animation.BeginAction(info);
    }

    void HandleActionEnded(long actionId, CombatActionResult result)
    {
        _animation.EndAction(actionId, result);
    }

    void HandleDeath()
    {
        ClearCommand();
        _animation.Present(false, 0f, 0f);
    }

    void HandleArrived()
    {
        if (_command == CommandKind.Move || _command == CommandKind.AttackMove)
            ClearCommand();
    }

    void UpdateCommand()
    {
        if (!_combat.isActiveAndEnabled || !_combat.Owner.IsAlive)
        {
            ClearCommand();
            return;
        }

        if (_command == CommandKind.None || Time.timeScale <= 0f)
            return;

        if(_command == CommandKind.StationaryAttack)
        {
            UpdateStationaryAttack();
            return;
        }

        if (_combat.IsBusy)
            return;

        switch (_command)
        {
            case CommandKind.AttackMove:
                UpdateAttackMove();
                break;

            case CommandKind.AttackTarget:
                UpdateTargetAttack();
                break;
        }
    }

    void UpdateStationaryAttack()
    {
        if (_combat.CheckBasicAttackTarget(_target) != AttackTargetState.Valid)
        {
            // 기존 대상에 대한 미완료 기본 공격 취소
            _combat.CancelBasicAttack();

            // 현재 위치에서 공격 가능한 가장 가까운 대상으로 교체
            _target = FindNearestTarget();
        }

        if (_target == null)
        {
            ClearCommand();
            return;
        }

        if (_combat.IsBusy)
            return;

        TryAttackOnce(_target);
    }

    void UpdateAttackMove()
    {
        CombatTarget target = FindNearestTarget();

        if(target != null)
        {
            TryAttackOnce(target);
            return;
        }

        if (_movement.HasMoveRequest)
            return;

        if (!TryMoveTo(_destination))
            ClearCommand();
    }

    void UpdateTargetAttack()
    {
        AttackTargetState state = _combat.CheckBasicAttackTarget(_target);

        if (state == AttackTargetState.Invalid)
        {
            ClearCommand();
            return;
        }

        if (state == AttackTargetState.Valid)
        {
            TryAttackOnce(_target);
            return;
        }

        if (Time.timeAsDouble < _nextPathAt)
            return;

        _nextPathAt = Time.timeAsDouble + _repathInterval;

        // 지정 대상만 추적하며 주변의 다른 대상은 제외
        if (!TryMoveToTarget(_target))
            ClearCommand();
    }

    void TryAttackOnce(CombatTarget target)
    {
        _movement.Stop();

        if (!_combat.TryAttack(target))
            return;

        _command = CommandKind.StationaryAttack;
        _destination = default;
        _target = target;
        _nextPathAt = 0d;
    }

    CombatTarget FindNearestTarget()
    {
        Collider[] hits = Physics.OverlapSphere(_combat.Owner.Position, _combat.AttackRange, _targetMask, QueryTriggerInteraction.Collide);

        CombatTarget nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        foreach (Collider hit in hits)
        {
            CombatTarget candidate = hit.GetComponentInParent<CombatTarget>();

            if (candidate == null || _combat.CheckBasicAttackTarget(candidate) != AttackTargetState.Valid)
            {
                continue;
            }

            float distanceSquared = (candidate.Position - _combat.Owner.Position).sqrMagnitude;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    bool TryMoveTo(Vector3 position)
    {
        if (!CanAcceptCommands || !_combat.CanRequestMovement)
            return false;

        if (!_movement.TryPrepareMove(position))
            return false;

        return ResumePreparedMove();
    }

    bool TryMoveToTarget(CombatTarget target)
    {
        if (!CanAcceptCommands || !_combat.CanRequestMovement)
            return false;

        if (!_movement.TryPrepareTargetMove(target.Position))
            return false;

        return ResumePreparedMove();
    }

    bool ResumePreparedMove()
    {
        // 유효한 경로 확보 이후 기존 기본 공격 취소
        _combat.CancelBasicAttack();

        if (!CanAcceptCommands || !_combat.CanRequestMovement)
        {
            ClearCommand();
            return false;
        }

        float moveSpeed = _combat.Owner.Status.MoveSpeed;
            

        if (!_movement.ResumeMove(moveSpeed))
        {
            ClearCommand();
            return false;
        }

        return true;
    }

    bool TryStartMoveCommand(Vector3 position, CommandKind commad)
    {
        if (!TryMoveTo(position))
            return false;

        _command = commad;
        _destination = position;
        _target = null;
        _nextPathAt = 0d;

        return true;
    }

    void ResetCommand()
    {
        _command = CommandKind.None;
        _destination = default;
        _target = null;
        _nextPathAt = 0d;
    }

    void ClearCommand()
    {
        ResetCommand();
        _movement.Stop();
    }

    /// <summary>
    /// 조작이 중단된 상태에서 생성한 외형의 애니메이션 연결
    /// </summary>
    /// <param name="animation">같은 플레이어 계층에 생성한 외형 애니메이션</param>
    public void BindAnimation(PlayerAnimation animation)
    {
        if (isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: PlayerController 비활성화 후 애니메이션 연결 필요");

        ValidateCoreReferences();
        ValidateAnimationReference(animation);

        if (_animation == animation)
            return;

        if (_animation != null)
            _animation.ClearAction();

        // Animator 준비는 외형 활성화 이후 별도 수행
        _animation = animation;
    }

    /// <summary>
    /// 조작이 중단된 상태에서 외형 파괴 전 애니메이션 연결 해제
    /// </summary>
    public void UnbindAnimation()
    {
        if (isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: PlayerController 비활성화 후 애니메이션 연결 해제 필요");

        if (_animation != null)
            _animation.ClearAction();

        _animation = null;
    }

    /// <summary>
    /// 이동 요청 검사와 공격 취소·이동 시작 조정
    /// </summary>
    /// <param name="position">이동할 월드 좌표</param>
    /// <returns>이동 시작 성공 여부</returns>
    public bool RequestMove(Vector3 position)
    {
        if (!CanAcceptCommands || !_combat.CanRequestMovement)
            return false;

        return TryStartMoveCommand(position, CommandKind.Move);
    }

    /// <summary>
    /// 플레이어와 대상의 현재 상호작용 가능 여부 확인
    /// </summary>
    /// <param name="target">확인할 상호작용 대상</param>
    /// <returns>현재 상호작용 가능 여부</returns>
    public bool CanInteract(IInteractable target)
    {
        if (!CanAcceptCommands || !(target is Component component) || component == null)
            return false;

        return target.CanInteract(_combat.Skills);
    }

    /// <summary>
    /// 대상의 조건 재확인과 상호작용 실행 요청
    /// </summary>
    /// <param name="target">상호작용을 요청할 대상</param>
    /// <returns>상호작용 성공 여부</returns>
    public bool RequestInteract(IInteractable target)
    {
        if (!CanInteract(target))
            return false;

        return target.TryInteract(_combat.Skills);
    }

    /// <summary>
    /// 지정 대상 공격 또는 빈 땅 공격 이동 명령 요청
    /// </summary>
    /// <param name="position">빈 땅 공격 이동의 목적지</param>
    /// <param name="clickedTarget">고정 공격 대상, 빈 땅 클릭이면 null</param>
    /// <returns>명령 접수 여부</returns>
    public bool RequestAttack(Vector3 position, CombatTarget clickedTarget)
    {
        if (!CanAcceptCommands || _combat.IsCasting || !_combat.CanRequestMovement)
            return false;

        if (clickedTarget == null)
            return TryStartMoveCommand(position, CommandKind.AttackMove);

        if (!clickedTarget.CanReceiveFrom(_combat.Owner))
            return false;

        if (_command == CommandKind.AttackTarget && _target == clickedTarget)
            return true;

        AttackTargetState state = _combat.CheckBasicAttackTarget(clickedTarget);

        if (state == AttackTargetState.Invalid)
            return false;

        CombatActionInfo currentAction = _combat.CurrentAction;

        // 같은 유효 대상에 대한 진행 중 공격의 불필요한 취소 방지
        if (state == AttackTargetState.Valid && currentAction.Kind == CombatActionKind.BasicAttack && currentAction.Target == clickedTarget)
            return true;

        if(state == AttackTargetState.Valid)
        {
            _combat.CancelBasicAttack();
            _movement.Stop();
            _nextPathAt = 0d;
        }
        else
        {
            if (!TryMoveToTarget(clickedTarget))
                return false;

            _nextPathAt = Time.timeAsDouble + _repathInterval;
        }

        _command = CommandKind.AttackTarget;
        _destination = default;
        _target = clickedTarget;

        return true;
    }

    /// <summary>
    /// 입력 방향에 따른 고정 이동 스킬 요청
    /// </summary>
    /// <param name="direction">입력 순간의 월드 방향</param>
    /// <returns>시전 요청 수락 여부</returns>
    public bool RequestMovementSkill(Vector3 direction)
    {
        if (!CanAcceptCommands)
            return false;

        return _combat.TryUseMovementSkill(direction);
    }

    /// <summary>
    /// 입력 순간의 방향·위치와 현재 공격 대상을 포함한 스킬 요청
    /// </summary>
    /// <param name="slot">사용할 스킬 슬롯 번호</param>
    /// <param name="direction">입력에서 계산한 월드 방향</param>
    /// <param name="worldPosition">입력 당시의 지정 위치</param>
    /// <param name="hasWorldPosition">지정 좌표 보유 여부</param>
    /// <returns>스킬 사용 요청 수락 여부</returns>
    public bool RequestUseSkill(int slot, Vector3 direction, Vector3 worldPosition, bool hasWorldPosition)
    {
        if (!CanAcceptCommands)
            return false;

        CombatActionInfo currentAction = _combat.CurrentAction;

        CombatTarget preferredTarget = currentAction.Kind == CombatActionKind.BasicAttack ? currentAction.Target : _target;

        SkillAimRequest request = new SkillAimRequest(direction, preferredTarget, worldPosition, hasWorldPosition);

        return _combat.TryUseSkill(slot, request);
    }
}