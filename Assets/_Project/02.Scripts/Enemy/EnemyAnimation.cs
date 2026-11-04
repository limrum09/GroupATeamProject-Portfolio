using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>모델 Animator의 재생 요청과 완료 알림</summary>
public class EnemyAnimation : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Base Layer.Locomotion");
    static readonly int Idle1Hash = Animator.StringToHash("Base Layer.Idle1");
    static readonly int Idle2Hash = Animator.StringToHash("Base Layer.Idle2");

    static readonly int SkillAttackHash = Animator.StringToHash("Base Layer.Attack.SkillAttack");
    static readonly int ChargeAttackHash = Animator.StringToHash("Base Layer.Attack.ChargeAttack");
    const string AttackClipName = "BaseClip_Attack0";
    const string ChargeClipName = "BaseClip_ChargeAttack";

    RuntimeAnimatorController _originalController;
    AnimatorOverrideController _skillController;
    List<KeyValuePair<AnimationClip, AnimationClip>> _skillOverrides;
    int _attackClipIndex;
    int _chargeClipIndex;
    long _attackActionId;
    int _playingAttackStateHash;
    bool _attackEntered;
    bool _chargePlayback;

    Animator _animator;
    EnemyAnimationStateNotifier[] _notifiers;
    int _version;
    EnemyAnimationSignal _waitingSignal;
    bool _waitingForCompletion;
    bool _waitingForIdle;
    int _idleStateHash;
    float _idleEndTime;

    public event Action OnEmergenceAnimationEnded;
    public event Action OnHitAnimationEnded;
    public event Action OnDeathAnimationEnded;

    public bool IsAttackPlaying => _attackActionId != 0;
    public bool HasEmergence { get; private set; }
    public bool IsIdleCycleCompleted { get; private set; }
    public int PlaybackVersion => _version;

    void OnDisable()
    {
        InvalidateRequests();
    }

    void LateUpdate()
    {
        if (!IsAttackPlaying || _animator == null || !_animator.isActiveAndEnabled)
            return;

        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        bool transitioning = _animator.IsInTransition(0);
        if (state.fullPathHash == _playingAttackStateHash)
        {
            _attackEntered = true;

            // 돌진은 클립 반복 횟수와 무관하게 실행 종료까지 유지
            if (_chargePlayback)
                return;

            if (state.normalizedTime < 1f || transitioning)
                return;

            _animator.Play(LocomotionHash, 0, 0f);
        }
        else if (!_attackEntered || transitioning)
        {
            return;
        }

        ClearAttackTracking();
    }

    void ClearAttackTracking()
    {
        _attackActionId = 0;
        _playingAttackStateHash = 0;
        _attackEntered = false;
        _chargePlayback = false;
    }

    /// <summary>비활성 소환 객체의 모델 Animator 참조 연결</summary>
    /// <param name="animator">Content에 명시된 모델 Animator</param>
    /// <param name="hasEmergence">등장 모션 사용 여부</param>
    public void Bind(Animator animator, bool hasEmergence)
    {
        Unbind();
        if (animator == null || animator.runtimeAnimatorController == null)
            throw new MissingReferenceException($"{name}: 모델 Animator와 Controller가 필요합니다.");

        _animator = animator;
        _originalController = animator.runtimeAnimatorController;
        HasEmergence = hasEmergence;
    }

    void ValidateAndBindNotifiers()
    {
        ValidateParameter("Attack", AnimatorControllerParameterType.Trigger);
        ValidateParameter("IdleVariant", AnimatorControllerParameterType.Int);
        ValidateParameter("MoveSpeed", AnimatorControllerParameterType.Float);
        ValidateParameter("HoldIdleCycle", AnimatorControllerParameterType.Bool);
        ValidateParameter("Emergence", AnimatorControllerParameterType.Trigger);
        ValidateParameter("Hit", AnimatorControllerParameterType.Trigger);
        ValidateParameter("Die", AnimatorControllerParameterType.Trigger);

        if (!_animator.HasState(0, SkillAttackHash) || !_animator.HasState(0, ChargeAttackHash))
            throw new InvalidOperationException($"{name}: SkillAttack과 ChargeAttack 상태 연결 필요");

        EnemyAnimationStateNotifier[] notifiers =
            _animator.GetBehaviours<EnemyAnimationStateNotifier>();

        foreach (EnemyAnimationSignal signal in Enum.GetValues(typeof(EnemyAnimationSignal)))
        {
            bool found = false;
            foreach (EnemyAnimationStateNotifier notifier in notifiers)
            {
                if (notifier.Signal == signal)
                {
                    found = true;
                    break;
                }
            }
            if (found == false)
                throw new InvalidOperationException($"{name}: {signal} 상태 알림 설정이 필요합니다.");
        }

        // 모든 필수 설정의 검증 완료 이후 수신자 연결
        _notifiers = notifiers;
        foreach (EnemyAnimationStateNotifier notifier in _notifiers)
            notifier.Bind(this);
    }

    /// <summary>활성화된 소환의 Animator 검증과 재생 상태 초기화</summary>
    public void ResetPlaybackForSpawn()
    {
        if (_animator == null)
            throw new MissingReferenceException($"{name}: 모델 Animator 연결 필요");

        if (_animator.isActiveAndEnabled == false)
        {
            throw new InvalidOperationException(
                $"{name}: 모델 Animator 활성화 이후 재생 초기화 필요");
        }

        InvalidateRequests();
        _animator.Rebind();
        ValidateAndBindNotifiers();
        _animator.SetFloat("MoveSpeed", 0f);
        _animator.SetInteger("IdleVariant", 0);
        _animator.SetBool("HoldIdleCycle", false);
        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                _animator.ResetTrigger(parameter.nameHash);
        }
        _animator.Play(LocomotionHash, 0, 0f);
        _animator.Update(0f);
    }

    /// <summary>반환된 Content의 알림과 Animator 참조 해제</summary>
    public void Unbind()
    {
        InvalidateRequests();
        if (_notifiers != null)
        {
            foreach (EnemyAnimationStateNotifier notifier in _notifiers)
                notifier.Bind(null);
        }
        _notifiers = null;

        if (_skillController != null)
        {
            if (_animator != null)
                _animator.runtimeAnimatorController = _originalController;

            Destroy(_skillController);
        }

        _skillController = null;
        _skillOverrides = null;
        _originalController = null;
        _animator = null;
        HasEmergence = false;
    }

    /// <summary>
    /// 모델별 기존 클립을 보존한 소환 전용 스킬 Override 준비
    /// </summary>
    public void PrepareSkillPlayback()
    {
        if (_animator == null || _originalController == null)
            throw new MissingReferenceException($"{name}: 스킬 모션의 모델 Animator 연결 필요");

        if (_skillController != null)
            return;

        RuntimeAnimatorController controller = _originalController;
        List<AnimatorOverrideController> layers = new List<AnimatorOverrideController>();
        while (controller is AnimatorOverrideController overrides)
        {
            layers.Add(overrides);
            controller = overrides.runtimeAnimatorController;
        }

        if (controller == null)
            throw new MissingReferenceException($"{name}: 원본 AnimatorController 연결 필요");

        AnimatorOverrideController runtime = new AnimatorOverrideController(controller);
        runtime.name = $"{name}_SkillOverride";
        runtime.hideFlags = HideFlags.DontSave;
        List<KeyValuePair<AnimationClip, AnimationClip>> clips =
            new List<KeyValuePair<AnimationClip, AnimationClip>>(runtime.overridesCount);
        runtime.GetOverrides(clips);

        // 원본부터 바깥 Override 순서로 모델별 모션 보존
        for (int layer = layers.Count - 1; layer >= 0; layer--)
        {
            List<KeyValuePair<AnimationClip, AnimationClip>> layerOverrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>();
            layers[layer].GetOverrides(layerOverrides);
            foreach (KeyValuePair<AnimationClip, AnimationClip> pair in layerOverrides)
            {
                if (pair.Value == null)
                    continue;

                for (int i = 0; i < clips.Count; i++)
                {
                    if (clips[i].Key == pair.Key)
                        clips[i] = pair;
                }
            }
        }

        int attackIndex = -1;
        int chargeIndex = -1;
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i].Key.name == AttackClipName)
            {
                attackIndex = i;
            }
            else if (clips[i].Key.name == ChargeClipName)
                chargeIndex = i;
        }

        if (attackIndex < 0 || chargeIndex < 0)
        {
            Destroy(runtime);
            throw new InvalidOperationException($"{name}: {AttackClipName}, {ChargeClipName} 교체 기준 클립 필요");
        }

        runtime.ApplyOverrides(clips);
        _skillController = runtime;
        _skillOverrides = clips;
        _attackClipIndex = attackIndex;
        _chargeClipIndex = chargeIndex;
        _animator.runtimeAnimatorController = runtime;
    }

    /// <summary>
    /// 승인된 스킬 행동의 모션 재생
    /// </summary>
    /// <param name="actionId">승인된 행동 식별 번호</param>
    /// <param name="clip">재생할 스킬 모션</param>
    /// <param name="isCharge">돌진 실행 종료까지 모션 유지 여부</param>
    public void PlaySkill(long actionId, AnimationClip clip, bool isCharge)
    {
        if (_animator == null || !_animator.isActiveAndEnabled ||
            _skillController == null || _skillOverrides == null || clip == null)
        {
            throw new MissingReferenceException($"{name}: 활성 Animator, 스킬 Override, MotionClip 연결 필요");
        }

        // Combat 양수 ID와 돌진 음수 ID 모두 허용
        if (actionId == 0)
            throw new ArgumentOutOfRangeException(nameof(actionId), "승인된 행동 식별 번호 필요");

        int stateHash = isCharge ? ChargeAttackHash : SkillAttackHash;
        int clipIndex = isCharge ? _chargeClipIndex : _attackClipIndex;

        if (!_animator.HasState(0, stateHash))
            throw new InvalidOperationException($"{name}: 스킬 재생 State 연결 필요");

        CancelIdleCycleCompletion();
        AnimationClip original = _skillOverrides[clipIndex].Key;
        _skillOverrides[clipIndex] =
            new KeyValuePair<AnimationClip, AnimationClip>(original, clip);
        _skillController.ApplyOverrides(_skillOverrides);

        _attackActionId = actionId;
        _playingAttackStateHash = stateHash;
        _attackEntered = false;
        _chargePlayback = isCharge;
        _animator.ResetTrigger("Attack");
        _animator.Play(stateHash, 0, 0f);

        // 짧은 클립이 다음 관찰 전에 끝나도 진입 사실을 보존
        _animator.Update(0f);
        if (_attackActionId == actionId)
            _attackEntered = _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHash;
    }

    /// <summary>등장 모션 시작</summary>
    public void SetAnimEmergence()
    {
        BeginCompletion(EnemyAnimationSignal.Emergence);
        _animator.SetTrigger("Emergence");
    }

    /// <summary>중단된 등장 요청 무효화</summary>
    public void CancelEmergence()
    {
        CancelCompletion(EnemyAnimationSignal.Emergence);
        _animator.ResetTrigger("Emergence");
    }

    /// <summary>
    /// 일치하는 공격 모션 취소와 재생 추적 해제
    /// </summary>
    /// <param name="actionId">취소할 행동 번호, 0이면 현재 공격 전체 정리</param>
    public void CancelAnimAttack(long actionId = 0)
    {
        if (actionId != 0 && _attackActionId != actionId)
            return;

        ClearAttackTracking();

        // 비활성 초기화와 Unbind 이후에는 재생 요청 없이 논리 상태만 정리
        if (_animator == null || !_animator.isActiveAndEnabled)
            return;

        _animator.ResetTrigger("Attack");

        // Hit·Die 등 다른 상태로의 전환과 재생 보존
        if (_animator.IsInTransition(0))
            return;

        int stateHash = _animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        if (stateHash == SkillAttackHash || stateHash == ChargeAttackHash)
            _animator.Play(LocomotionHash, 0, 0f);
    }

    /// <summary>정규화한 실제 이동 속도 전달</summary>
    /// <param name="speed">실제 수평 이동 속도</param>
    /// <param name="referenceSpeed">최대 이동 모션의 기준 속도</param>
    public void SetAnimMove(float speed, float referenceSpeed)
    {
        if (referenceSpeed <= 0f || float.IsNaN(referenceSpeed) || float.IsInfinity(referenceSpeed))
            throw new InvalidOperationException($"{name}: 유효한 이동 기준 속도가 필요합니다.");
        _animator.SetFloat("MoveSpeed", Mathf.Clamp01(speed / referenceSpeed));
    }

    /// <summary>현재 대기 동작 또는 반복의 종료 예약</summary>
    public void RequestIdleCycleCompletion()
    {
        _waitingForIdle = true;
        IsIdleCycleCompleted = false;
        _idleStateHash = 0;
        _animator.SetBool("HoldIdleCycle", true);
        _animator.SetInteger("IdleVariant", 0);
        AnimatorStateInfo state = _animator.IsInTransition(0)
            ? _animator.GetNextAnimatorStateInfo(0)
            : _animator.GetCurrentAnimatorStateInfo(0);
        CaptureIdleEnd(state);
    }

    /// <summary>행동 전환에 따른 대기 완료 예약 해제</summary>
    public void CancelIdleCycleCompletion()
    {
        _waitingForIdle = false;
        IsIdleCycleCompleted = false;
        _idleStateHash = 0;
        _animator.SetBool("HoldIdleCycle", false);
        _animator.SetInteger("IdleVariant", 0);
    }

    /// <summary>피격 모션 시작과 기존 완료 요청 취소</summary>
    public void SetAnimHit()
    {
        BeginCompletion(EnemyAnimationSignal.Hit);
        _animator.ResetTrigger("Emergence");
        _animator.ResetTrigger("Attack");
        _animator.SetTrigger("Hit");
    }

    /// <summary>피격 완료 대기 취소</summary>
    public void CancelHit()
    {
        CancelCompletion(EnemyAnimationSignal.Hit);
        _animator.ResetTrigger("Hit");
    }

    /// <summary>사망 모션 시작과 이전 재생 요청 취소</summary>
    public void SetAnimDie()
    {
        BeginCompletion(EnemyAnimationSignal.Die);
        _animator.ResetTrigger("Emergence");
        _animator.ResetTrigger("Attack");
        _animator.ResetTrigger("Hit");
        _animator.SetTrigger("Die");
    }

    /// <summary>사망 완료 대기 취소</summary>
    public void CancelDeath()
    {
        CancelCompletion(EnemyAnimationSignal.Die);
        _animator.ResetTrigger("Die");
    }

    /// <summary>바인딩된 Animator의 상태 진행 수신</summary>
    /// <param name="animator">알림을 보낸 모델 Animator</param>
    /// <param name="signal">상태 알림 종류</param>
    /// <param name="state">현재 재생 상태</param>
    /// <param name="version">상태 진입 시점의 재생 세대</param>
    public void ObserveState(Animator animator, EnemyAnimationSignal signal,
        AnimatorStateInfo state, int version)
    {
        if (animator != _animator || version != _version || isActiveAndEnabled == false)
            return;

        if (signal == EnemyAnimationSignal.Idle)
        {
            if (_waitingForIdle == false) return;
            if (_idleStateHash == 0)
                CaptureIdleEnd(state);
            if (_idleStateHash == state.fullPathHash && state.normalizedTime >= _idleEndTime)
            {
                _waitingForIdle = false;
                IsIdleCycleCompleted = true;
            }
            return;
        }

        if (_waitingForCompletion == false || _waitingSignal != signal || state.normalizedTime < 1f)
            return;

        _waitingForCompletion = false;
        switch (signal)
        {
            case EnemyAnimationSignal.Emergence:
                OnEmergenceAnimationEnded?.Invoke();
                break;
            case EnemyAnimationSignal.Hit:
                OnHitAnimationEnded?.Invoke();
                break;
            case EnemyAnimationSignal.Die:
                OnDeathAnimationEnded?.Invoke();
                break;
        }
    }

    /// <summary>추가 대기 State 진입 후 선택 파라미터 소비</summary>
    /// <param name="animator">알림을 보낸 모델 Animator</param>
    /// <param name="state">진입한 상태</param>
    public void ConsumeIdleVariant(Animator animator, AnimatorStateInfo state)
    {
        if (animator == _animator && (state.fullPathHash == Idle1Hash || state.fullPathHash == Idle2Hash))
            _animator.SetInteger("IdleVariant", 0);
    }

    void CaptureIdleEnd(AnimatorStateInfo state)
    {
        if (state.fullPathHash != LocomotionHash && state.fullPathHash != Idle1Hash && state.fullPathHash != Idle2Hash)
            return;
        _idleStateHash = state.fullPathHash;
        _idleEndTime = state.loop ? Mathf.Floor(state.normalizedTime) + 1f : 1f;
    }

    void BeginCompletion(EnemyAnimationSignal signal)
    {
        InvalidateRequests();
        _animator.SetBool("HoldIdleCycle", false);
        _waitingSignal = signal;
        _waitingForCompletion = true;
    }

    void CancelCompletion(EnemyAnimationSignal signal)
    {
        if (_waitingForCompletion && _waitingSignal == signal)
        {
            _waitingForCompletion = false;
            _version++;
        }
    }

    void InvalidateRequests()
    {
        ClearAttackTracking();
        _version++;
        _waitingForCompletion = false;
        _waitingForIdle = false;
        IsIdleCycleCompleted = false;
        _idleStateHash = 0;
    }

    void ValidateParameter(string parameterName, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter[] parameters = _animator.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == parameterName && parameter.type == type)
                return;
        }
        throw new InvalidOperationException(
            $"{name}: Animator {type} '{parameterName}' 설정이 필요합니다. " +
            $"Animator={_animator.name}, " +
            $"Controller={_animator.runtimeAnimatorController.name}, " +
            $"Initialized={_animator.isInitialized}, " +
            $"ParameterCount={parameters.Length}");
    }
}
