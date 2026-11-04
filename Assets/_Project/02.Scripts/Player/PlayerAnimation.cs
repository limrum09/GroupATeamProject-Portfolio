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

    static readonly int ActionRateHash = Animator.StringToHash("ActionRate");
    static readonly int MoveRateHash = Animator.StringToHash("MoveRate");

    int _idleHash;
    int _moveHash;
    int _attackHash;
    int _castHash;
    int _deathHash;
    int _currentStateHash;

    RuntimeAnimatorController _originalController;
    AnimatorOverrideController _overrideController;

    AnimationClip _appliedAttackClip;
    AnimationClip _appliedSkillClip;
    AnimationClip _actionClip;

    CombatActionInfo _action;
    bool _completed;
    bool _startPending;
    bool _prepared;
    bool _preparing;

    public Animator PrefabAnimator => _animator;
    // 이 외형이 소유한 런타임 Animator Controller의 준비 완료 여부
    public bool IsPrepared => _prepared && _animator != null && _overrideController != null && _animator.runtimeAnimatorController == _overrideController;

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
        _prepared = false;

        if (ownedController == null)
            return;

        // 이 인스턴스가 연결한 Controller만 원래 상태로 복원
        if (_animator != null && _animator.runtimeAnimatorController == ownedController)
            _animator.runtimeAnimatorController = originalController;

        Destroy(ownedController);
    }

    void RequirePrepared()
    {
        if (!IsPrepared)
            throw new InvalidOperationException($"{name}: PlayerAnimation.Prepare 완료 후 사용 필요");
    }

    void RequirePlaceholder(AnimationClip placeholder)
    {
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(_overrideController.overridesCount);

        _overrideController.GetOverrides(overrides);

        foreach (KeyValuePair<AnimationClip, AnimationClip> entry in overrides)
        {
            if (entry.Key == placeholder)
                return;
        }

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

    void PlayAction()
    {
        int stateHash;
        float playbackDuration;

        if(_action.Kind == CombatActionKind.BasicAttack)
        {
            stateHash = _attackHash;
            playbackDuration = _action.Skill != null ? _actionClip.length : _action.Duration;

            if (_appliedAttackClip != _actionClip)
            {
                _overrideController[_attackClip] = _actionClip;
                _appliedAttackClip = _actionClip;
            }

            _animator.SetFloat(ActionRateHash, _actionClip.length / playbackDuration);
        }
        else
        {
            stateHash = _castHash;
            playbackDuration = _actionClip.length;

            if(_appliedSkillClip != _actionClip)
            {
                _overrideController[_skillPlaceholderClip] = _actionClip;
                _appliedSkillClip = _actionClip;
            }
        }

        // 재활성화 또는 전달 지연 시 현재 행동 시각으로 복구
        float normalizedTime = Mathf.Clamp01((float)((Time.timeAsDouble - _action.StartedAt) / playbackDuration));

        _animator.Play(stateHash, 0, normalizedTime);
        _currentStateHash = stateHash;
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

        AnimationClip clip;

        if (action.Kind == CombatActionKind.BasicAttack)
        {
            if (action.Skill == null && action.Duration <= 0f)
                throw new InvalidOperationException($"{name}: 스킬 미지정 기본 공격의 양수 시간 설정 필요");

            // 스킬 자체가 없을 때만 기존 기본 공격 클립 사용
            clip = action.Skill != null ? action.Skill.MotionClip : _attackClip;
        }
        else if (action.Kind == CombatActionKind.Skill)
        {
            if (action.Skill == null)
                throw new InvalidOperationException($"{name}: 스킬 행동에는 SkillDefinition이 필요합니다.");

            clip = action.Skill.MotionClip;
        }
        else
        {
            throw new InvalidOperationException($"{name}: 지원하지 않는 행동 종류입니다.");
        }

        // 연결된 스킬의 MotionClip 미지정은 모션 없는 발동
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

        if (_attackClip.isLooping || _skillPlaceholderClip.isLooping)
            throw new InvalidOperationException($"{name}: 공격과 시전 원본 클립의 Loop Time 해제 필요");

        if (_moveReferenceSpeed <= 0f || _moveThreshold < 0f || _blendTime < 0f)
            throw new InvalidOperationException($"{name}: 보행 속도와 전환 설정 확인 필요");

        _preparing = true;

        try
        {
            _idleHash = Animator.StringToHash(_idleState);
            _moveHash = Animator.StringToHash(_moveState);
            _attackHash = Animator.StringToHash(_attackState);
            _castHash = Animator.StringToHash(_castState);
            _deathHash = Animator.StringToHash(_deathState);

            _originalController = _animator.runtimeAnimatorController;
            _overrideController = new AnimatorOverrideController(_originalController);

            RequirePlaceholder(_attackClip);
            RequirePlaceholder(_skillPlaceholderClip);

            _animator.runtimeAnimatorController = _overrideController;

            // 새 외형의 최초 준비에서만 Animator 바인딩 확정
            _animator.Rebind();

            if (!isActiveAndEnabled || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController != _overrideController)
                throw new OperationCanceledException($"{name}: 애니메이션 준비 중 외형 또는 Controller 변경");

            RequireState(_idleHash, _idleState);
            RequireState(_moveHash, _moveState);
            RequireState(_attackHash, _attackState);
            RequireState(_castHash, _castState);
            RequireState(_deathHash, _deathState);

            RequireFloat(ActionRateHash, "ActionRate");
            RequireFloat(MoveRateHash, "MoveRate");

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
    /// <param name="moveSpeed">캐릭터의 이동 속도 스탯</param>
    public void Present(bool alive, float locomotionSpeed, float moveSpeed)
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
            SelectState(_deathHash, true);
            return;
        }

        bool moving = locomotionSpeed > _moveThreshold;

        if (_action.Id != 0)
        {
            if (_completed && moving)
            {
                // 전투 행동 완료 이후 이동에 의한 후속 모션 중단
                ClearAction();
            }
            else if (_startPending)
            {
                PlayAction();
                return;
            }
            else if (_completed)
            {
                AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);

                bool animationFinished = !_animator.IsInTransition(0) && state.fullPathHash == _currentStateHash && state.normalizedTime >= 1f;

                if (!animationFinished)
                    return;

                ClearAction();
            }
            else
            {
                // 시전 중 내부 모션 전환은 Animator에서 처리
                return;
            }
        }

        // 짧은 가감속과 분리된 이동속도 스탯 기준 재생 배율
        _animator.SetFloat(MoveRateHash, moveSpeed / _moveReferenceSpeed);

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