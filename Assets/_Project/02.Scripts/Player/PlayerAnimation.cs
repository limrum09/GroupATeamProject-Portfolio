using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>PlayerController가 전달한 상태의 플레이어 애니메이션 재생</summary>
[DisallowMultipleComponent]
public sealed class PlayerAnimation : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] Animator _animator;

    [Header("----- 실제 재생 클립 -----")]
    [SerializeField] AnimationClip _attackClip;
    [FormerlySerializedAs("_castClip")]
    [SerializeField] AnimationClip _skillPlaceholderClip;

    [Header("----- 플레이어 Animator 상태 -----")]
    [SerializeField] string _idleState = "Base Layer.PlayerIdle";
    [SerializeField] string _moveState = "Base Layer.PlayerRunning";
    [SerializeField] string _attackState = "Base Layer.root__Punch";
    [SerializeField] string _castState = "Base Layer.Cast";
    [SerializeField] string _deathState = "Base Layer.root__Dying_1";

    [Header("----- 보행과 전환 -----")]
    [SerializeField, Min(0.01f)] float _moveReferenceSpeed = 5f;
    [SerializeField, Min(0f)] float _moveThreshold = 0.05f;
    [SerializeField, Min(0f)] float _blendTime = 0.08f;
    [Tooltip("이동 방향 값이 새 방향으로 따라가는 시간(초)")]
    [SerializeField, Min(0f)] float _directionDamp = 0.08f;

    [Header("----- 선택 공격 연결 상태 -----")]
    [Tooltip("비우면 공격 상태 하나에서 매번 처음부터 재생, 채우면 두 공격 상태를 번갈아 섞으며 연결")]
    [SerializeField] string _blendAttackState;
    [Tooltip("선택 공격 연결 상태의 원본 클립, 공격과 시전 원본 클립과 달라야 함")]
    [SerializeField] AnimationClip _blendAttackClip;
    [Tooltip("선택 공격 연결 상태가 있을 때 공격 사이를 섞는 시간(초)")]
    [SerializeField, Min(0f)] float _attackBlendTime = 0.1f;

    [Header("----- 선택 상체 레이어 -----")]
    [Tooltip("비우면 공격을 기본 레이어에서만 재생, 채우면 이동 중 공격의 상체를 이 레이어에서 재생")]
    [SerializeField] string _upperBodyLayer;
    [Tooltip("상체 레이어의 공격 상태 전체 경로, 공격 원본 클립과 같은 클립 사용")]
    [SerializeField] string _upperAttackState;

    static readonly int MoveRateHash = Animator.StringToHash("MoveRate");
    static readonly int MoveXHash = Animator.StringToHash("MoveX");
    static readonly int MoveZHash = Animator.StringToHash("MoveZ");

    int _idleHash;
    int _moveHash;
    int _castHash;
    int _deathHash;
    int _currentStateHash;
    // 기본 레이어에서 재생 중인 공격 상태
    int _baseAttackHash;
    // 준비된 상체 레이어 번호, 미사용이면 -1
    int _upperLayer = -1;
    int _upperAttackHash;
    float _upperWeight;

    RuntimeAnimatorController _originalController;
    AnimatorOverrideController _overrideController;
    AnimatorClipSlotRotation _attackRotation;

    AnimationClip _appliedSkillClip;
    AnimationClip _actionClip;

    CombatActionInfo _action;
    bool _completed;
    bool _startPending;
    bool _prepared;
    bool _preparing;

    // 현재 행동이 상체 레이어로 재생되는 기본 공격인지 여부
    bool PlaysUpperAction => _upperLayer >= 0 && _action.Id != 0 && _action.Kind == CombatActionKind.BasicAttack;

    public Animator PrefabAnimator => _animator;
    // 이 외형이 소유한 런타임 Animator Controller의 준비 완료 여부
    public bool IsPrepared => _prepared && _animator != null && _overrideController != null && _attackRotation != null && _animator.runtimeAnimatorController == _overrideController;

    void OnEnable()
    {
        _currentStateHash = 0;
        _startPending = _action.Id != 0;
    }

    void OnDestroy()
    {
        ReleaseRuntimeController();
    }

    void ReleaseRuntimeController()
    {
        AnimatorOverrideController ownedController = _overrideController;
        RuntimeAnimatorController originalController = _originalController;

        _overrideController = null;
        _originalController = null;
        _attackRotation = null;
        _appliedSkillClip = null;
        _prepared = false;
        _upperLayer = -1;

        if (ownedController == null)
            return;

        // 이 인스턴스가 연결한 Controller만 원래 상태로 복원
        if (_animator != null && _animator.runtimeAnimatorController == ownedController)
            _animator.runtimeAnimatorController = originalController;

        Destroy(ownedController);
    }

    // 외형별 Override Controller는 원본 Controller 기준 런타임 Override로 펼쳐 중첩 회피, 외형 클립 교체 목록 그대로 복사
    static AnimatorOverrideController CreateRuntimeOverride(RuntimeAnimatorController source)
    {
        if (source is not AnimatorOverrideController appearance)
            return new AnimatorOverrideController(source);

        if (appearance.runtimeAnimatorController == null)
            throw new MissingReferenceException($"{appearance.name}: 원본 Animator Controller 연결 필요");

        AnimatorOverrideController runtime = new AnimatorOverrideController(appearance.runtimeAnimatorController);
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(appearance.overridesCount);

        appearance.GetOverrides(overrides);
        runtime.ApplyOverrides(overrides);
        return runtime;
    }

    void RequirePrepared()
    {
        if (!IsPrepared)
            throw new InvalidOperationException($"{name}: PlayerAnimation.Prepare 완료 후 사용 필요");
    }

    void RequirePlaceholder(AnimationClip placeholder)
    {
        if (!AnimatorClipSlotRotation.HasOverride(_overrideController, placeholder))
            throw new InvalidOperationException($"{name}: Animator Controller에서 원본 클립 '{placeholder.name}'을 찾을 수 없습니다.");
    }

    void RequireState(int hash, string stateName)
    {
        if (!_animator.HasState(0, hash))
            throw new InvalidOperationException($"{name}: Animator 상태 '{stateName}'를 확인하세요.");
    }

    void RequireFloat(int hash, string parameterName)
    {
        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Float)
                return;
        }

        throw new InvalidOperationException($"{name}: Float 파라미터 '{parameterName}'가 필요합니다.");
    }

    void PlayAction(bool keepBaseLocomotion)
    {
        // 재활성화 또는 전달 지연 시 현재 행동 시각으로 복구
        float normalizedTime = Mathf.Clamp01((float)((Time.timeAsDouble - _action.StartedAt) / _actionClip.length));

        if (_action.Kind == CombatActionKind.BasicAttack)
        {
            // 이전 모션이 있을 때만 섞어서 연결, 재동기화는 즉시 재생
            // 선택 공격 연결 상태가 없으면 슬롯 교대 객체가 즉시 재생으로 처리
            float blendTime = _currentStateHash != 0 ? _attackBlendTime : 0f;
            AnimatorStateInfo baseState = _animator.GetCurrentAnimatorStateInfo(0);
            int locomotionHash = _currentStateHash;

            _baseAttackHash = _attackRotation.Play(_actionClip, normalizedTime, blendTime);
            _currentStateHash = _baseAttackHash;

            if (_upperLayer >= 0)
            {
                // 같은 원본 클립의 상체 상태라 Override 교체가 함께 적용
                _animator.Play(_upperAttackHash, _upperLayer, normalizedTime);
                SetUpperWeight(1f);

                // 달리는 중 시작한 공격은 같은 프레임의 마지막 재생 요청으로 하체 보행 유지
                if (keepBaseLocomotion && locomotionHash != 0)
                {
                    _animator.Play(baseState.fullPathHash, 0, baseState.normalizedTime);
                    _currentStateHash = locomotionHash;
                }
            }
        }
        else
        {
            if (_appliedSkillClip != _actionClip)
            {
                _overrideController[_skillPlaceholderClip] = _actionClip;
                _appliedSkillClip = _actionClip;
            }

            _animator.Play(_castHash, 0, normalizedTime);
            _currentStateHash = _castHash;
        }

        _startPending = false;
    }

    void SelectState(int stateHash, bool immediate)
    {
        if (_currentStateHash == stateHash)
            return;

        if (immediate || _currentStateHash == 0 || _blendTime <= 0f)
        {
            _animator.Play(stateHash, 0, 0f);
        }
        else
        {
            _animator.CrossFadeInFixedTime(stateHash, _blendTime, 0, 0f);
        }

        _currentStateHash = stateHash;
    }

    // 상체 레이어 공격 중 정지한 하체를 공격 자세의 현재 진행 시각으로 복귀
    void SelectBaseAttack()
    {
        if (_currentStateHash == _baseAttackHash)
            return;

        float elapsed = Mathf.Clamp((float)(Time.timeAsDouble - _action.StartedAt), 0f, _actionClip.length);

        if (_blendTime <= 0f)
            _animator.Play(_baseAttackHash, 0, elapsed / _actionClip.length);
        else
            _animator.CrossFadeInFixedTime(_baseAttackHash, _blendTime, 0, elapsed);

        _currentStateHash = _baseAttackHash;
    }

    // 짧은 가감속과 분리된 이동속도 스탯 기준 재생 배율, 이동 방향을 외형 기준 앞·옆 성분으로 바꿔 방향별 달리기 혼합
    void ApplyLocomotion(Vector3 moveDirection, float moveSpeed)
    {
        _animator.SetFloat(MoveRateHash, moveSpeed / _moveReferenceSpeed);

        if (moveDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 local = _animator.transform.InverseTransformDirection(moveDirection);

        _animator.SetFloat(MoveXHash, local.x, _directionDamp, Time.deltaTime);
        _animator.SetFloat(MoveZHash, local.z, _directionDamp, Time.deltaTime);
    }

    void SetUpperWeight(float weight)
    {
        _upperWeight = weight;
        _animator.SetLayerWeight(_upperLayer, weight);
    }

    // 상체 공격이 끝나면 보행 전환 시간 동안 상체 레이어를 서서히 해제
    void UpdateUpperWeight()
    {
        if (_upperLayer < 0)
            return;

        float target = PlaysUpperAction ? 1f : 0f;
        float step = _blendTime > 0f ? Time.deltaTime / _blendTime : 1f;

        SetUpperWeight(Mathf.MoveTowards(_upperWeight, target, step));
    }

    internal void Synchronize(CombatActionInfo action)
    {
        _currentStateHash = 0;
        BeginAction(action);
    }

    internal void BeginAction(CombatActionInfo action)
    {
        RequirePrepared();
        ClearAction();

        if (action.Id == 0)
            return;

        if (action.Kind != CombatActionKind.BasicAttack && action.Kind != CombatActionKind.Skill)
            throw new InvalidOperationException($"{name}: 지원하지 않는 행동 종류입니다.");

        // 플레이어 기본 공격은 항상 콤보 단계 스킬로 전달
        if (action.Skill == null)
            throw new InvalidOperationException($"{name}: 기본 공격 콤보 단계 또는 스킬 정의 필요");

        // 기본 공격은 콤보 단계 모션, 스킬은 스킬 MotionClip
        AnimationClip clip = action.MotionClip;

        // 모션 미지정은 모션 없는 발동
        if (clip == null)
            return;

        if (clip.length <= 0f || clip.isLooping)
            throw new InvalidOperationException($"{name}: 모션은 길이가 양수인 비반복 클립이어야 합니다.");

        _action = action;
        _actionClip = clip;
        _startPending = true;
    }

    internal void EndAction(long actionId, CombatActionResult result)
    {
        if (_action.Id != actionId)
            return;

        if (result == CombatActionResult.Completed)
        {
            // 정상 완료 이후 남은 공격 모션 유지
            _completed = true;
        }
        else
        {
            ClearAction();
        }
    }

    /// <summary>
    /// 활성 외형의 Animator 설정 검증과 런타임 Controller 준비
    /// </summary>
    public void Prepare()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 애니메이션 준비 필요");

        if (_animator == null)
            throw new MissingReferenceException($"{name}: Animator 연결 필요");

        if (!isActiveAndEnabled || !_animator.isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 외형과 Animator 활성화 이후 준비 필요");

        if (_preparing)
            throw new InvalidOperationException($"{name}: 애니메이션 준비 중 재진입 불가");

        // 같은 외형의 재활성화에서는 Controller와 재생 상태 유지
        if (_prepared)
        {
            RequirePrepared();
            return;
        }

        if (_animator.runtimeAnimatorController == null)
            throw new MissingReferenceException($"{name}: Animator Controller 연결 필요");

        if (_attackClip == null || _skillPlaceholderClip == null)
            throw new MissingReferenceException($"{name}: 공격과 시전 상태의 원본 클립 연결 필요");

        if (_attackClip == _skillPlaceholderClip)
            throw new InvalidOperationException($"{name}: 공격과 시전에 서로 다른 원본 클립 사용 필요");

        if (_skillPlaceholderClip.isLooping)
            throw new InvalidOperationException($"{name}: 시전 원본 클립의 Loop Time 해제 필요");

        bool hasBlendState = !string.IsNullOrWhiteSpace(_blendAttackState);

        // 선택 연결 상태는 상태 이름과 원본 클립을 함께 지정
        if (hasBlendState != (_blendAttackClip != null))
            throw new InvalidOperationException($"{name}: 선택 공격 연결 상태와 원본 클립은 함께 지정하거나 함께 비워야 함");

        if (_blendAttackClip != null && _blendAttackClip == _skillPlaceholderClip)
            throw new InvalidOperationException($"{name}: 선택 공격 연결 상태와 시전에 서로 다른 원본 클립 사용 필요");

        bool hasUpperLayer = !string.IsNullOrWhiteSpace(_upperBodyLayer);

        // 상체 레이어는 레이어와 공격 상태를 함께 지정
        if (hasUpperLayer != !string.IsNullOrWhiteSpace(_upperAttackState))
            throw new InvalidOperationException($"{name}: 상체 레이어와 상체 공격 상태는 함께 지정하거나 함께 비워야 함");

        // 상체 상태는 하나라 공격 상태 두 개를 번갈아 쓰는 연결 상태와 함께 사용 불가
        if (hasUpperLayer && hasBlendState)
            throw new InvalidOperationException($"{name}: 상체 레이어와 선택 공격 연결 상태는 함께 사용할 수 없음");

        if (_moveReferenceSpeed <= 0f || _moveThreshold < 0f || _blendTime < 0f || _attackBlendTime < 0f)
            throw new InvalidOperationException($"{name}: 보행 속도와 전환 설정 확인 필요");

        _preparing = true;

        try
        {
            _idleHash = Animator.StringToHash(_idleState);
            _moveHash = Animator.StringToHash(_moveState);
            _castHash = Animator.StringToHash(_castState);
            _deathHash = Animator.StringToHash(_deathState);

            _originalController = _animator.runtimeAnimatorController;
            _overrideController = CreateRuntimeOverride(_originalController);

            RequirePlaceholder(_skillPlaceholderClip);

            _animator.runtimeAnimatorController = _overrideController;

            // 새 외형의 최초 준비에서만 Animator 바인딩 확정
            _animator.Rebind();

            if (!isActiveAndEnabled || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController != _overrideController)
                throw new OperationCanceledException($"{name}: 애니메이션 준비 중 외형 또는 Controller 변경");

            RequireState(_idleHash, _idleState);
            RequireState(_moveHash, _moveState);
            RequireState(_castHash, _castState);
            RequireState(_deathHash, _deathState);

            RequireFloat(MoveRateHash, "MoveRate");
            RequireFloat(MoveXHash, "MoveX");
            RequireFloat(MoveZHash, "MoveZ");

            AnimatorClipSlot[] attackSlots = hasBlendState
                ? new[] { new AnimatorClipSlot(_attackState, _attackClip), new AnimatorClipSlot(_blendAttackState, _blendAttackClip) }
                : new[] { new AnimatorClipSlot(_attackState, _attackClip) };

            // 공격 상태와 원본 클립 검증은 슬롯 교대 객체가 담당
            _attackRotation = new AnimatorClipSlotRotation(_animator, _overrideController, attackSlots);

            _upperLayer = -1;

            if (hasUpperLayer)
            {
                int layer = _animator.GetLayerIndex(_upperBodyLayer);

                if (layer <= 0)
                    throw new InvalidOperationException($"{name}: 상체 레이어 '{_upperBodyLayer}'를 확인하세요.");

                _upperAttackHash = Animator.StringToHash(_upperAttackState);

                if (!_animator.HasState(layer, _upperAttackHash))
                    throw new InvalidOperationException($"{name}: 상체 레이어의 Animator 상태 '{_upperAttackState}'를 확인하세요.");

                _upperLayer = layer;
                SetUpperWeight(0f);
            }

            _currentStateHash = 0;
            _startPending = _action.Id != 0;

            // 모든 검사 성공 이후 준비 완료 공개
            _prepared = true;
        }
        catch
        {
            ReleaseRuntimeController();
            throw;
        }
        finally
        {
            _preparing = false;
        }
    }

    /// <summary>
    /// 준비된 외형에 생존 상태와 이동 및 전투 애니메이션 반영
    /// </summary>
    /// <param name="alive">캐릭터 생존 여부</param>
    /// <param name="locomotionSpeed">현재 실제 이동 속도</param>
    /// <param name="moveDirection">현재 실제 이동의 수평 월드 방향, 정지 중이면 zero</param>
    /// <param name="moveSpeed">캐릭터의 이동 속도 스탯</param>
    public void Present(bool alive, float locomotionSpeed, Vector3 moveDirection, float moveSpeed)
    {
        RequirePrepared();

        if (!isActiveAndEnabled || !_animator.isActiveAndEnabled)
        {
            _currentStateHash = 0;
            _startPending = _action.Id != 0;
            return;
        }

        if (!alive)
        {
            ClearAction();

            if (_upperLayer >= 0)
                SetUpperWeight(0f);

            SelectState(_deathHash, true);
            return;
        }

        UpdateUpperWeight();

        bool moving = locomotionSpeed > _moveThreshold;

        if (_action.Id != 0)
        {
            bool upper = PlaysUpperAction;

            if (_completed && moving && !upper)
            {
                // 전투 행동 완료 이후 이동에 의한 후속 모션 중단
                ClearAction();
            }
            else if (_startPending)
            {
                PlayAction(upper && moving);

                if (!upper)
                    return;
            }
            else if (_completed)
            {
                // 상체 레이어 공격은 이동 여부와 관계없이 상체 모션 종료까지 유지
                int layer = upper ? _upperLayer : 0;
                int stateHash = upper ? _upperAttackHash : _currentStateHash;
                AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(layer);

                bool animationFinished = !_animator.IsInTransition(layer) && state.fullPathHash == stateHash && state.normalizedTime >= 1f;

                if (animationFinished)
                    ClearAction();
                else if (!upper)
                    return;
            }
            else if (!upper)
            {
                // 시전 중 내부 모션 전환은 Animator에서 처리
                return;
            }

            // 상체 레이어 공격 중 하체는 이동이면 보행, 정지면 공격 자세
            if (_action.Id != 0)
            {
                if (moving)
                {
                    ApplyLocomotion(moveDirection, moveSpeed);
                    SelectState(_moveHash, false);
                }
                else
                {
                    SelectBaseAttack();
                }

                return;
            }
        }

        ApplyLocomotion(moveDirection, moveSpeed);

        SelectState(moving ? _moveHash : _idleHash, false);
    }

    public void ClearAction()
    {
        _action = default;
        _actionClip = null;
        _completed = false;
        _startPending = false;
    }
}
