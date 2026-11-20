using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerController : MonoBehaviour
{
    enum CommandKind
    {
        None,
        Move,
        // 사거리 밖 대상을 추적해 1회 공격
        AttackTarget,
        // 추적 중 시작한 공격 이후 전진 거리까지 이동만 남은 상태
        Advance
    }

    [Header("----- 연결 -----")]
    [SerializeField] CombatController _combat;
    [SerializeField] PlayerMovement _movement;
    

    [Header("----- 대상 탐색 -----")]
    [SerializeField] LayerMask _targetMask;
    [SerializeField, Min(0.05f)] float _repathInterval = 0.2f;
    [SerializeField, Min(0.1f)] float _clickTargetRadius = 15f;

    [Header("----- 입력 보관 -----")]
    [Tooltip("공격 직후 이동 제한 중 누른 클릭을 보관하는 시간(초)")]
    [SerializeField, Min(0f)] float _clickBufferDuration = 0.3f;

    [Header("----- 이동 공격 -----")]
    [Tooltip("추적 중 사거리 진입 후 공격하며 더 다가가는 거리")]
    [SerializeField, Min(0f)] float _chaseAdvanceDistance = 1f;

    [Header("----- 공격 방향 -----")]
    [Tooltip("기본 공격 종료 후 대상을 계속 바라보는 시간(초)")]
    [SerializeField, Min(0f)] float _attackFacingHold = 0.25f;

    CommandKind _command;
    CombatTarget _target;
    PlayerAnimation _animation;
    double _nextPathAt;
    bool _subscribed;
    // 클릭으로 요청한 1회 공격의 미실행 여부, 이동 명령과 함께 유지 가능
    bool _attackPending;

    // 기본 공격 중과 종료 직후 바라볼 대상
    CombatTarget _facingTarget;
    long _facingActionId;
    double _faceUntil;

    bool _hasBufferedClick;
    bool _bufferedIsAttack;
    Vector3 _bufferedPosition;
    CombatTarget _bufferedTarget;
    double _bufferedUntil;

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
        ClearBufferedClick();
        _facingTarget = null;

        if (wasSubscribed)
        {
            if (_movement != null)
            {
                _movement.Stop();
                _movement.ClearFacingPoint();
            }

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
        ReplayBufferedClick();
        UpdateCommand();

        CharacterStatus status = _combat.Owner.Status;

        _movement.Tick(status.MoveSpeed, _combat.CanMove);

        UpdateAttackFacing();
        _movement.TickFacing(Time.deltaTime);

        _animation.Present(status.IsAlive, _movement.LocomotionSpeed, _movement.LocomotionDirection, status.MoveSpeed);
    }

    // 기본 공격 중과 종료 직후 유지 시간 동안 대상 방향, 그 밖에는 이동 방향
    void UpdateAttackFacing()
    {
        bool attacking = _combat.Action == CombatActionKind.BasicAttack;

        if (_facingTarget != null && _facingTarget.IsAlive && (attacking || Time.timeAsDouble < _faceUntil))
        {
            _movement.SetFacingPoint(_facingTarget.Position);
            return;
        }

        _facingTarget = null;
        _movement.ClearFacingPoint();
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
                ClearBufferedClick();

                // 스킬 방향 전환이 기본 공격 바라보기보다 우선
                _facingTarget = null;
                _movement.ClearFacingPoint();

                if (info.Skill.RotationMode == SkillRotationMode.FaceDirection)
                {
                    _movement.Face(_combat.Owner.Position + info.Direction);
                }
            }

            // 이동 유지 시 기존 경로와 이동 방향을 그대로 사용
        }
        else if (info.Target != null)
        {
            // 이동 여부와 관계없이 공격하는 동안 대상 쪽으로 빠르게 회전
            _facingTarget = info.Target;
            _facingActionId = info.Id;
            _faceUntil = double.PositiveInfinity;
        }

        _animation.BeginAction(info);
    }

    void HandleActionEnded(long actionId, CombatActionResult result)
    {
        // 공격 종료 후 유지 시간 동안 대상 방향 유지
        if (actionId == _facingActionId)
            _faceUntil = Time.timeAsDouble + _attackFacingHold;

        _animation.EndAction(actionId, result);
    }

    void HandleDeath()
    {
        ClearCommand();
        ClearBufferedClick();
        _facingTarget = null;
        _movement.ClearFacingPoint();
        _animation.Present(false, 0f, Vector3.zero, 0f);
    }

    void HandleArrived()
    {
        if (_command != CommandKind.Move)
            return;

        // 도착 후에도 예약된 1회 공격은 유지
        if (_attackPending)
        {
            _command = CommandKind.None;
            return;
        }

        ClearCommand();
    }

    void UpdateCommand()
    {
        if (!_combat.isActiveAndEnabled || !_combat.Owner.IsAlive)
        {
            ClearCommand();
            return;
        }

        if (Time.timeScale <= 0f)
            return;

        if (_command == CommandKind.Advance)
            UpdateAdvance();

        if (_command == CommandKind.AttackTarget)
        {
            if (!_combat.IsBusy)
                UpdateTargetAttack();

            return;
        }

        if (_attackPending)
            UpdatePendingAttack();
    }

    // 추적 중 시작한 공격은 사거리 안쪽 전진 거리까지 다가가거나 이동이 끝나면 정지
    void UpdateAdvance()
    {
        if (_movement.HasMoveRequest && !IsWithinAdvanceStop(_target))
            return;

        ClearCommand();
    }

    // 이동 중이거나 정지한 상태에서 클릭으로 예약한 사거리 안 대상의 1회 공격
    void UpdatePendingAttack()
    {
        AttackTargetState state = _combat.CheckBasicAttackTarget(_target);

        // 죽었거나 사거리를 벗어난 대상은 예약 취소, 이동 중에는 추적하지 않음
        if (state == AttackTargetState.Invalid || state == AttackTargetState.OutOfRange)
        {
            _attackPending = false;
            _target = null;
            return;
        }

        // 시야가 가려졌거나 이전 공격 진행 중이면 다음 프레임에 재시도
        if (state != AttackTargetState.Valid || _combat.IsBusy)
            return;

        TryAttackOnce(_target, false);
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
            TryAttackOnce(_target, true);
            return;
        }

        if (Time.timeAsDouble < _nextPathAt)
            return;

        _nextPathAt = Time.timeAsDouble + _repathInterval;

        // 지정 대상만 추적하며 주변의 다른 대상은 제외
        if (!TryMoveToTarget(_target))
            ClearCommand();
    }

    void TryAttackOnce(CombatTarget target, bool advance)
    {
        // 이동 없는 공격과 전진 거리에 이미 도달한 추적은 정지 후 공격
        if (!CanAttackWhileMoving() || (advance && IsWithinAdvanceStop(target)))
            _movement.Stop();

        // 공격 대기 시간 중이면 예약 유지 후 다음 프레임에 재시도
        if (!_combat.TryAttack(target))
            return;

        _attackPending = false;

        // 이동 명령은 목적지까지 유지, 추적 공격은 전진 거리만 남기고 종료
        if (_command == CommandKind.AttackTarget)
        {
            if (advance && _movement.HasMoveRequest)
            {
                _command = CommandKind.Advance;
                return;
            }

            _command = CommandKind.None;
        }

        _target = null;
        _nextPathAt = 0d;
    }

    // 이동 중이면 대상 방향과 관계없이 이동을 유지한 채 공격
    bool CanAttackWhileMoving()
    {
        return _combat.MovesDuringBasicAttack && _movement.HasMoveRequest;
    }

    // 추적 이동 공격의 정지 거리 도달 여부, 사거리에서 전진 거리를 뺀 값
    bool IsWithinAdvanceStop(CombatTarget target)
    {
        if (target == null)
            return true;

        float stopDistance = Mathf.Max(0f, _combat.AttackRange - _chaseAdvanceDistance);

        return (target.Position - _combat.Owner.Position).sqrMagnitude <= stopDistance * stopDistance;
    }

    // 클릭 지점 주변에서 추적 가능한 가장 가까운 대상
    CombatTarget FindNearestTarget(Vector3 center, float radius)
    {
        Collider[] hits = Physics.OverlapSphere(center, radius, _targetMask, QueryTriggerInteraction.Collide);

        CombatTarget nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        foreach (Collider hit in hits)
        {
            CombatTarget candidate = hit.GetComponentInParent<CombatTarget>();

            if (candidate == null)
                continue;

            if (_combat.CheckBasicAttackTarget(candidate) == AttackTargetState.Invalid)
                continue;

            float distanceSquared = (candidate.Position - center).sqrMagnitude;

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
        // 유효한 경로 확보 이후 기존 기본 공격 취소, 이동 공격 허용 시 공격 유지
        if (!_combat.MovesDuringBasicAttack)
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

        // 예약된 1회 공격은 이동 중에도 유지, 사거리 밖이면 다음 갱신에서 취소
        _command = commad;
        _nextPathAt = 0d;

        return true;
    }

    void ResetCommand()
    {
        _command = CommandKind.None;
        _target = null;
        _nextPathAt = 0d;
        _attackPending = false;
    }

    void ClearCommand()
    {
        ResetCommand();
        _movement.Stop();
    }

    // 공격 직후 이동 제한 중의 클릭만 보관, 스킬 시전과 조작 불가 상태는 제외
    bool TryBufferClick(bool isAttack, Vector3 position, CombatTarget target)
    {
        if (!CanAcceptCommands || _combat.IsCasting || _clickBufferDuration <= 0f)
            return false;

        // 마지막 클릭으로 덮어쓰기
        _hasBufferedClick = true;
        _bufferedIsAttack = isAttack;
        _bufferedPosition = position;
        _bufferedTarget = target;
        _bufferedUntil = Time.timeAsDouble + _clickBufferDuration;

        return true;
    }

    void ReplayBufferedClick()
    {
        if (!_hasBufferedClick)
            return;

        if (Time.timeAsDouble > _bufferedUntil || !CanAcceptCommands)
        {
            ClearBufferedClick();
            return;
        }

        if (!_combat.CanRequestMovement)
            return;

        bool isAttack = _bufferedIsAttack;
        Vector3 position = _bufferedPosition;
        CombatTarget target = _bufferedTarget;

        ClearBufferedClick();

        // 사라진 대상은 빈 땅 클릭으로 처리되어 주변 대상 재탐색
        if (isAttack)
            RequestAttack(position, target);
        else
            RequestMove(position);
    }

    void ClearBufferedClick()
    {
        _hasBufferedClick = false;
        _bufferedTarget = null;
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
            return TryBufferClick(false, position, null);

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
    /// 지정 대상 공격 또는 빈 땅 클릭 지점 주변 대상 공격, 대상이 없으면 이동 명령 요청
    /// </summary>
    /// <param name="position">클릭 지점, 주변 대상이 없을 때의 이동 목적지</param>
    /// <param name="clickedTarget">직접 클릭한 대상, 빈 땅 클릭이면 null</param>
    /// <returns>명령 접수 여부</returns>
    public bool RequestAttack(Vector3 position, CombatTarget clickedTarget)
    {
        if (!CanAcceptCommands || _combat.IsCasting || !_combat.CanRequestMovement)
            return TryBufferClick(true, position, clickedTarget);

        if (clickedTarget == null)
        {
            // 빈 땅 클릭은 클릭 지점에서 가장 가까운 대상 지정, 없으면 일반 이동
            clickedTarget = FindNearestTarget(position, _clickTargetRadius);

            if (clickedTarget == null)
                return TryStartMoveCommand(position, CommandKind.Move);
        }

        if (!clickedTarget.CanReceiveFrom(_combat.Owner))
            return false;

        if (_command == CommandKind.AttackTarget && _target == clickedTarget)
            return true;

        AttackTargetState state = _combat.CheckBasicAttackTarget(clickedTarget);

        if (state == AttackTargetState.Invalid)
            return false;

        if (state == AttackTargetState.Valid)
        {
            CombatActionInfo currentAction = _combat.CurrentAction;

            // 다른 대상 공격은 취소, 같은 대상 공격 중이면 끝난 뒤 다음 1회로 예약
            if (!(currentAction.Kind == CombatActionKind.BasicAttack && currentAction.Target == clickedTarget))
                _combat.CancelBasicAttack();

            // 이동 명령은 유지, 추적·전진 중이거나 정지 상태면 그 자리에서 공격
            if (_command != CommandKind.Move)
            {
                _command = CommandKind.None;
                _movement.Stop();
            }

            _target = clickedTarget;
            _attackPending = true;
            _nextPathAt = 0d;

            return true;
        }

        if (!TryMoveToTarget(clickedTarget))
            return false;

        _command = CommandKind.AttackTarget;
        _target = clickedTarget;
        _attackPending = true;
        _nextPathAt = Time.timeAsDouble + _repathInterval;

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