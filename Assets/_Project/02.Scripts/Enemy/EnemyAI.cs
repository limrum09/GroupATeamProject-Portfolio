using System;
using UnityEngine;

/// <summary>적 상태 전환과 대상 및 전투 이벤트 연결</summary>
public class EnemyAI : MonoBehaviour
{
    [Header("----- 컴포넌트 -----")]
    [SerializeField] CombatController _combatCtr;	// 공격 요청과 공용 전투 상태 조회
    [SerializeField] EnemyMovement _enemyMovement;	// NavMesh 이동 실행
    [SerializeField] EnemyAnimation _enemyAnimation;	// 피격·사망 애니메이션 요청과 종료 알림
    [SerializeField] EnemyAttack _enemyAttack;	// 공격 선택과 요청 및 공격 모션 연결

    [Header("----- 배회 -----")]
    [SerializeField, Min(0f)] float _roamEnterCycle;	// 대기에서 배회로 전환할 시간
    [SerializeField, Min(0f)] float _roamExitCycle;	// 배회에서 대기로 복귀할 시간
    [SerializeField, Min(0f)] float _roamRadius;	// 배회 진입 위치 기준 이동 반경
    [SerializeField, Min(1)] int _minRoamMoveCount = 1;	// 배회 진입 시 최소 이동 횟수
    [SerializeField, Min(1)] int _maxRoamMoveCount = 3;	// 배회 진입 시 최대 이동 횟수
    [SerializeField, Min(1)] int _roamDestinationAttempts = 8;	// 한 번의 목적지 탐색에서 검사할 후보 수
    [SerializeField, Min(0.01f)] float _roamRetryInterval = 0.5f;	// 이동 실패 후 재시도 간격

    [Header("----- 추적 -----")]
    [SerializeField, Min(0f)] float _trackDetectRange;	// 추적 상태 진입 거리
    [SerializeField, Min(0f)] float _trackDisableDetectRange;	// 추적 상태 해제 거리
    [SerializeField, Min(0.01f)] float _trackRepathInterval = 0.2f;	// 추적 목적지 갱신 간격

    [Header("----- 공격 -----")]
    [SerializeField, Min(0f)] float _attackDetectRange;	// 공격 상태 진입 거리
    [SerializeField, Min(0f)] float _attackDisableDetectRange;	// 공격 상태 해제 거리

    [Header("----- 대치 -----")]
    [SerializeField, Min(0f)] float _confrontationMoveDelay = 1f;
    [SerializeField, Range(1f, 90f)] float _confrontationStepAngle = 30f;
    [SerializeField, Min(0.01f)] float _confrontationRepathInterval = 0.5f;

    IEnemyState[] _states;	// EnemyState 순서로 보관하는 재사용 상태 인스턴스
    IEnemyState _currentState;	// 현재 프레임을 처리할 상태
    Transform _target;	// 감지할 플레이어 참조
    CombatTarget _attackTarget;	// 이번 소환의 공격 대상
    CombatTarget _owner;	// 이 AI를 소유한 적의 전투 대상
    Vector3 _spawnPosition;	// 이번 소환의 최초 배치 위치
    bool _returnToSpawnRequired;	// 추적 종료 후 소환 위치 복귀 필요 여부
    bool _initialized;	// 이번 소환의 대상과 상태 초기화 여부
    bool _subscribed;	// 전투·애니메이션 이벤트 구독 여부

    public event Action OnDeathAnimationEnded;
    public EnemyState CurrentState { get; private set; }
    public bool IsInitialized => _initialized;
    public float TrackDetectRange => _trackDetectRange;
    public float TrackDisableDetectRange => _trackDisableDetectRange;
    public float AttackDetectRange => _attackDetectRange;
    public float AttackDisableDetectRange => _attackDisableDetectRange;

    public float RoamEnterCycle => _roamEnterCycle;
    public float RoamExitCycle => _roamExitCycle;
    public Vector3 Position => _owner.Position;
    public float RoamRadius => _roamRadius;
    public int MinRoamMoveCount => _minRoamMoveCount;
    public int MaxRoamMoveCount => _maxRoamMoveCount;
    public int RoamDestinationAttempts => _roamDestinationAttempts;
    public float RoamRetryInterval => _roamRetryInterval;
    public float TrackRepathInterval => _trackRepathInterval;
    public float MovementSpeed => _enemyMovement.MoveSpeed;
    public float ConfrontationMoveDelay => _confrontationMoveDelay;
    public float ConfrontationStepAngle => _confrontationStepAngle;
    public float ConfrontationRepathInterval => _confrontationRepathInterval;
    public bool IsMoving => _enemyMovement.IsMoving;
    public bool HasArrived => _enemyMovement.HasArrived;
    public bool ReturnToSpawnRequired => _returnToSpawnRequired;
    public bool IsIdleCycleCompleted => _enemyAnimation.IsIdleCycleCompleted;

    bool HasLivingTarget =>
        _attackTarget != null &&
        _attackTarget.IsAlive &&
        _target != null &&
        _target.gameObject.activeInHierarchy;

    void OnEnable()
    {
        if (_initialized)
            SubscribeEvents();
    }

    void Update()
    {
        // 이번 소환의 초기화 완료 여부 확인
        if (_initialized == false) return;

        // 최초 활성화 시 모든 컴포넌트의 Awake 이후 체력 판정
        if (_currentState == null)
        {
            _enemyAnimation.ResetPlaybackForSpawn();
            EnemyState initialState = _enemyAnimation.HasEmergence ? EnemyState.Emergence : EnemyState.Idle;
            ChangeState(_owner.Status.IsAlive ? initialState : EnemyState.Dead);
        }

        // 돌진 완료·접촉 결과를 반영한 뒤 일반 이동과 상태 판단 갱신
        _enemyAttack.TickExecution(Time.deltaTime);
        _enemyMovement.Tick();
        _currentState.Update();
        _enemyAnimation.SetAnimMove(_enemyMovement.CurrentSpeed, _enemyMovement.TrackingSpeed);
    }

    void OnDisable()
    {
        // 이벤트 구독과 현재 상태 정리
        UnsubscribeEvents();
        _currentState?.Exit();
        _currentState = null;

        // 비활성화 이후 이동이 이어지지 않도록 경로 제거
        if (_initialized)
            _enemyMovement.Stop();
    }

    /// <summary>소환 대상 연결과 상태 초기화</summary>
    /// <param name="playerTarget">감지하고 공격할 플레이어</param>
    /// <param name="model">이번 소환의 명시적인 모델 설정</param>
    /// <param name="effectPlayer">같은 씬의 전투 이펙트 재생기</param>
    public void Init(
        CombatTarget playerTarget,
        EnemyModel model,
        CombatEffect effectPlayer)
    {
        if (model == null)
            throw new MissingReferenceException($"{name}: 이번 소환의 EnemyModel이 필요합니다.");

        // 필수 참조와 행동 설정 검사
        ValidateReferences(playerTarget);
        ValidateSettings();

        // 이전 소환의 이벤트와 상태 정리
        UnsubscribeEvents();
        _currentState?.Exit();
        _currentState = null;

        _enemyAnimation.Bind(model.Animator, model.HasEmergence);

        // 비활성 상태에서 이동 기능 초기화
        _enemyMovement.Init();
        _enemyAttack.Init(
            model.Definition, _enemyMovement, model.ContactCollider, effectPlayer);

        // 이번 소환의 감지 대상과 전투 소유자 연결
        _target = playerTarget.ActorTransform;
        _attackTarget = playerTarget;
        _owner = _combatCtr.Owner;

        // 이번 소환 위치 저장과 이전 복귀 요청 제거
        _spawnPosition = _owner.Position;
        _returnToSpawnRequired = false;

        // 반복 소환에서 재사용할 상태 인스턴스 최초 생성
        if (_states == null)
        {
            _states = new IEnemyState[]
            {
                new IdleState(this),
                new RoamingState(this),
                new TrackingState(this),
                new AttackState(this),
                new HitState(this),
                new DeadState(this),
                new EmergenceState(this),
                new ConfrontationState(this)
            };
        }

        // 초기 상태 표시와 소환 초기화 완료
        CurrentState = EnemyState.Idle;
        _initialized = true;

        // 활성 상태에서는 즉시 이벤트 구독
        if (isActiveAndEnabled)
            SubscribeEvents();
    }

    /// <summary>풀 반환 시 상태와 대상 및 이벤트 연결 해제</summary>
    public void Clear()
    {
        // 이벤트 구독과 현재 상태 정리
        UnsubscribeEvents();
        _currentState?.Exit();
        _currentState = null;

        // 풀 반환 이후 이동이 이어지지 않도록 경로 제거
        if (_initialized)
            _enemyMovement.Stop();

        _enemyAttack.Clear();
        _enemyAnimation.Unbind();

        // 이번 소환의 참조와 초기화 상태 제거
        _target = null;
        _attackTarget = null;
        _owner = null;
        _spawnPosition = Vector3.zero;
        _returnToSpawnRequired = false;
        _initialized = false;
        CurrentState = EnemyState.Idle;
    }

    /// <summary>소환 위치 복귀를 요청하며 대기 상태 진입</summary>
    public void ReturnToSpawn()
    {
        _returnToSpawnRequired = true;
        ChangeState(EnemyState.Idle);
    }

    /// <summary>소환 위치로 이동 요청</summary>
    /// <returns>이동 요청 수락 여부</returns>
    public bool TryReturnMove()
    {
        return _enemyMovement.TryMoveTo(_spawnPosition);
    }

    /// <summary>현재 위치의 소환 위치 도착 여부 확인</summary>
    /// <returns>소환 위치의 도착 허용 거리 이내이면 true</returns>
    public bool IsAtSpawnPosition()
    {
        return _enemyMovement.IsWithinArrivalDistance(_spawnPosition);
    }

    /// <summary>소환 위치 복귀 완료 처리</summary>
    public void CompleteReturn()
    {
        _enemyMovement.Stop();
        _returnToSpawnRequired = false;
    }

    /// <summary>배회 범위 안의 목적지로 이동 요청</summary>
    /// <param name="destination">목적지 후보</param>
    /// <param name="origin">이번 배회의 기준 위치</param>
    /// <returns>이동 요청 수락 여부</returns>
    public bool TryRoamMove(Vector3 destination, Vector3 origin)
    {
        return _enemyMovement.TryMoveWithin(destination, origin, _roamRadius);
    }

    /// <summary>생존 대상의 현재 위치로 추적 이동 요청</summary>
    /// <returns>이동 요청 수락 여부</returns>
    public bool TryTrackMove()
    {
        // 추적 대상의 생존과 활성 여부 확인
        if (HasLivingTarget == false)
        {
            _enemyMovement.Stop();
            return false;
        }

        // 대상의 현재 위치로 이동 요청
        return _enemyMovement.TryMoveTo(_target.position);
    }

    /// <summary>공격 상태에서 현재 대상에게 선택한 공격 요청</summary>
    public void TickAttack()
    {
        if (_initialized == false || isActiveAndEnabled == false) return;
        if (_owner.IsAlive == false || CurrentState != EnemyState.Attack) return;

        _enemyAttack.Tick(_attackTarget);
    }

    /// <summary>공격 상태 이탈에 따른 현재 공격과 모션 취소</summary>
    public void EndAttack()
    {
        _enemyAttack.StopAttack();
    }

    /// <summary>공격과 사망 상태를 제외한 피격 전환</summary>
    public void TakeHit()
    {
        if (_initialized == false || isActiveAndEnabled == false || _owner.IsAlive == false) return;
        if (CurrentState == EnemyState.Attack ||
            CurrentState == EnemyState.Hit || CurrentState == EnemyState.Dead)
        {
            return;
        }

        ChangeState(EnemyState.Hit);
    }

    /// <summary>현재 상태 종료와 다음 상태 진입, 사망 이후 전환과 동일 상태 재진입 제외</summary>
    /// <param name="nextState">전환할 상태</param>
    public void ChangeState(EnemyState nextState)
    {
        // 사망 이후 전환과 동일 상태 재진입 차단
        if (_currentState != null &&
            (CurrentState == EnemyState.Dead || CurrentState == nextState))
        {
            return;
        }

        // 이전 상태 종료와 진행 중인 이동 취소
        _currentState?.Exit();
        _enemyMovement.Stop();

        // 전투 재개 또는 사망 시 기존 복귀 요청 취소
        if (nextState == EnemyState.Tracking ||
            nextState == EnemyState.Attack ||
            nextState == EnemyState.Dead)
        {
            _returnToSpawnRequired = false;
        }

        // 다음 상태 연결과 진입 처리
        CurrentState = nextState;
        _enemyMovement.SetSpeedForState(nextState);
        _currentState = _states[(int)nextState];
        _currentState.Enter();
    }

    /// <summary>플레이어의 추적 진입 거리 충족 여부 확인</summary>
    /// <returns>생존·활성 플레이어가 추적 진입 거리 이내이면 true</returns>
    public bool IsWithinTrackRange()
    {
        return IsWithinRange(_trackDetectRange);
    }

    /// <summary>AttackState 진입 거리 충족 여부 확인</summary>
    /// <returns>생존 대상이 진입 거리 안이면 true</returns>
    public bool IsWithinAttackRange()
    {
        return IsWithinRange(_attackDetectRange);
    }

    /// <summary>
    /// 공격 요청 전 접근 이동 정지
    /// </summary>
    public void StopAttackApproach()
    {
        _enemyMovement.Stop();
    }

    /// <summary>
    /// 진행 중인 공격과 대상 및 스킬 조건에 따른 행동 판단
    /// </summary>
    /// <returns>공격 또는 대치와 추적 및 복귀 판단</returns>
    public EnemyAttackDecision GetAttackDecision()
    {
        // 대상 이탈 시에도 이미 승인된 공격과 모션 완료까지 유지
        if (_enemyAttack.IsAttackInProgress)
            return EnemyAttackDecision.Busy;

        if (!HasLivingTarget || IsOutsideTrackRange())
            return EnemyAttackDecision.NoTarget;

        return _enemyAttack.GetDecision(_attackTarget);
    }

    /// <summary>
    /// 대치 이동 기준이 될 생존 대상 위치 조회
    /// </summary>
    /// <param name="position">현재 대상의 월드 위치</param>
    /// <returns>생존 대상 위치 제공 여부</returns>
    public bool TryGetTargetPosition(out Vector3 position)
    {
        position = HasLivingTarget ? _target.position : default;
        return HasLivingTarget;
    }

    /// <summary>
    /// 대치 목적지로 도달 가능한 이동 요청
    /// </summary>
    /// <param name="destination">대치 목적지의 월드 위치</param>
    /// <returns>이동 요청 수락 여부</returns>
    public bool TryConfrontationMove(Vector3 destination)
    {
        return _enemyMovement.TryMoveTo(destination);
    }

    /// <summary>추적 해제 거리 초과 또는 대상 부재 확인</summary>
    /// <returns>추적 해제 거리 밖이거나 생존·활성 대상이 없으면 true</returns>
    public bool IsOutsideTrackRange()
    {
        return IsWithinRange(_trackDisableDetectRange) == false;
    }

    /// <summary>공격 해제 거리 초과 또는 대상 부재 확인</summary>
    /// <returns>공격 해제 거리 밖이거나 생존·활성 대상이 없으면 true</returns>
    public bool IsOutsideAttackRange()
    {
        return IsWithinRange(_attackDisableDetectRange) == false;
    }

    /// <summary>현재 대기 동작 종료 예약</summary>
    public void RequestIdleCycleCompletion()
    {
        _enemyAnimation.RequestIdleCycleCompletion();
    }

    /// <summary>행동 전환 시 대기 완료 예약 취소</summary>
    public void CancelIdleCycleCompletion()
    {
        _enemyAnimation.CancelIdleCycleCompletion();
    }

    /// <summary>등장 모션 시작</summary>
    public void BeginEmergence()
    {
        _enemyAnimation.SetAnimEmergence();
    }

    /// <summary>등장 완료 대기 취소</summary>
    public void CancelEmergence()
    {
        _enemyAnimation.CancelEmergence();
    }

    /// <summary>피격 애니메이션 시작 요청</summary>
    public void BeginHit()
    {
        _enemyAnimation.SetAnimHit();
    }

    /// <summary>피격 애니메이션 종료 알림 대기 취소</summary>
    public void EndHit()
    {
        _enemyAnimation.CancelHit();
    }

    /// <summary>사망 애니메이션 시작 요청</summary>
    public void BeginDeath()
    {
        _enemyAnimation.SetAnimDie();
    }

    /// <summary>사망 애니메이션 종료 알림 대기 취소</summary>
    public void EndDeath()
    {
        _enemyAnimation.CancelDeath();
    }

    bool IsWithinRange(float range)
    {
        if (HasLivingTarget == false) return false;

        Vector3 offset = _target.position - _owner.Position;
        return offset.sqrMagnitude <= range * range;
    }

    void HandleDamaged(DamageRequest request, float appliedDamage)
    {
        // 피해량 0을 포함한 피격 알림 처리
        TakeHit();
    }

    void HandleDied()
    {
        if (_initialized == false || isActiveAndEnabled == false) return;

        ChangeState(EnemyState.Dead);
    }

    void HandleEmergenceAnimationEnded()
    {
        if (_initialized == false || isActiveAndEnabled == false || CurrentState != EnemyState.Emergence) return;

        ChangeState(_owner.Status.IsAlive ? EnemyState.Idle : EnemyState.Dead);
    }

    void HandleHitAnimationEnded()
    {
        if (_initialized == false || isActiveAndEnabled == false || CurrentState != EnemyState.Hit) return;

        if (_owner.Status.IsAlive == false)
            ChangeState(EnemyState.Dead);
        else if (IsWithinAttackRange())
            ChangeState(EnemyState.Attack);
        else if (IsWithinTrackRange())
            ChangeState(EnemyState.Tracking);
        else
            ChangeState(EnemyState.Idle);
    }

    void HandleDeathAnimationEnded()
    {
        if (_initialized == false || isActiveAndEnabled == false || CurrentState != EnemyState.Dead) return;

        OnDeathAnimationEnded?.Invoke();
    }

    void SubscribeEvents()
    {
        if (_subscribed) return;

        _owner.OnDamaged += HandleDamaged;
        _owner.Status.OnDied += HandleDied;
        _enemyAnimation.OnEmergenceAnimationEnded += HandleEmergenceAnimationEnded;
        _enemyAnimation.OnHitAnimationEnded += HandleHitAnimationEnded;
        _enemyAnimation.OnDeathAnimationEnded += HandleDeathAnimationEnded;
        _subscribed = true;
    }

    void UnsubscribeEvents()
    {
        if (_subscribed == false) return;

        _owner.OnDamaged -= HandleDamaged;
        _owner.Status.OnDied -= HandleDied;
        _enemyAnimation.OnEmergenceAnimationEnded -= HandleEmergenceAnimationEnded;
        _enemyAnimation.OnHitAnimationEnded -= HandleHitAnimationEnded;
        _enemyAnimation.OnDeathAnimationEnded -= HandleDeathAnimationEnded;
        _subscribed = false;
    }

    void ValidateReferences(CombatTarget playerTarget)
    {
        // 행동 실행에 필요한 대상과 컴포넌트 연결 검사
        if (playerTarget == null || playerTarget.ActorTransform == null ||
            _combatCtr == null || _enemyMovement == null ||
            _enemyAnimation == null || _enemyAttack == null)
        {
            throw new MissingReferenceException(
                $"{name}: 플레이어 CombatTarget과 ActorTransform, CombatController, EnemyMovement, EnemyAnimation, EnemyAttack 연결 필요");
        }

        _enemyAttack.ValidateReferences();

        // 전투 소유자의 필수 참조 검사
        if (_combatCtr.Owner == null || _combatCtr.Owner.Status == null ||
            _combatCtr.Owner.ActorTransform == null)
        {
            throw new MissingReferenceException($"{name}: 전투 대상과 CharacterStatus 및 캐릭터 Transform이 필요합니다.");
        }

        // 같은 적의 AI와 이동 및 전투와 애니메이션 연결 검사
        if (_combatCtr.Owner.Team != CombatTeam.Enemy ||
            transform.IsChildOf(_combatCtr.Owner.ActorTransform) == false ||
            _enemyMovement.transform.IsChildOf(_combatCtr.Owner.ActorTransform) == false ||
            _enemyAnimation.transform.IsChildOf(_combatCtr.Owner.ActorTransform) == false ||
            _enemyMovement.Combat != _combatCtr || _enemyAttack.Combat != _combatCtr)
        {
            throw new InvalidOperationException($"{name}: 같은 적의 AI, 이동, 전투, 애니메이션을 연결하세요.");
        }
    }

    void ValidateSettings()
    {
        // 주기와 반경 및 감지 거리의 유효 범위 검사
        if (IsNonNegativeFinite(_roamEnterCycle) == false || IsNonNegativeFinite(_roamExitCycle) == false ||
            IsNonNegativeFinite(_roamRadius) == false ||
            IsNonNegativeFinite(_trackDetectRange) == false || IsNonNegativeFinite(_trackDisableDetectRange) == false ||
            IsNonNegativeFinite(_attackDetectRange) == false || IsNonNegativeFinite(_attackDisableDetectRange) == false)
        {
            throw new InvalidOperationException($"{name}: 주기와 감지 거리는 유한한 0 이상의 값이어야 합니다.");
        }

        // 랜덤 이동 횟수와 탐색 횟수 및 갱신 간격 검사
        if (_minRoamMoveCount < 1 || _maxRoamMoveCount < _minRoamMoveCount ||
            _maxRoamMoveCount == int.MaxValue || _roamDestinationAttempts < 1 ||
            IsNonNegativeFinite(_roamRetryInterval) == false || _roamRetryInterval <= 0f ||
            IsNonNegativeFinite(_trackRepathInterval) == false || _trackRepathInterval <= 0f)
        {
            throw new InvalidOperationException($"{name}: 배회 이동 횟수와 탐색 횟수 및 갱신 간격을 확인하세요.");
        }

        if (!IsNonNegativeFinite(_confrontationMoveDelay) ||
            !IsNonNegativeFinite(_confrontationStepAngle) || _confrontationStepAngle < 1f ||
            _confrontationStepAngle > 90f ||
            !IsNonNegativeFinite(_confrontationRepathInterval) || _confrontationRepathInterval < 0.01f)
        {
            throw new InvalidOperationException($"{name}: 대치 이동 대기 시간·각도·갱신 간격 확인 필요");
        }

        // 공격과 추적의 진입 거리 및 해제 거리 순서 검사
        if (_attackDetectRange > _trackDetectRange ||
            _attackDetectRange > _attackDisableDetectRange ||
            _attackDisableDetectRange > _trackDisableDetectRange ||
            _trackDetectRange > _trackDisableDetectRange)
        {
            throw new InvalidOperationException($"{name}: 공격·추적 진입 거리와 해제 거리의 순서를 확인하세요.");
        }
    }

    static bool IsNonNegativeFinite(float value)
    {
        return float.IsNaN(value) == false && float.IsInfinity(value) == false && value >= 0f;
    }
}
