using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 적 공격 모션의 지정 구간에 맞춘 파티클 재생
/// </summary>
public class EnemyAttackVfxPresenter : MonoBehaviour
{
    const float SwingTrailLifetime = 0.4f;
    static readonly int SkillAttackState = Animator.StringToHash("Base Layer.Attack.SkillAttack");

    [Serializable]
    struct VfxPose
    {
        public Vector3 Offset;
        public Vector3 Rotation;
    }

    [Serializable]
    class AttackVfx
    {
        public SkillDefinition Skill;
        public ParticleSystem Prefab;
        public ParticleSystem TrailPrefab;
        [Range(0f, 1f)] public float Start;
        [Range(0f, 1f)] public float Contact = 0.5f;
        [Range(0f, 1f)] public float End = 1f;
        [Min(0.01f)] public float Scale = 1f;
        public VfxPose StartPose;
        public VfxPose ContactPose;
        public VfxPose EndPose;
    }

    [Serializable]
    class ChargeVfx
    {
        public ChargeAttackSkillDefinition Skill;
        public ParticleSystem Prefab;
        public VfxPose Pose;
    }

    [Header("----- 공격 모션 이펙트 -----")]
    [SerializeField] AttackVfx[] _attacks = Array.Empty<AttackVfx>();
    [Header("----- 돌진 이펙트 -----")]
    [SerializeField] ChargeVfx[] _charges = Array.Empty<ChargeVfx>();

    readonly List<AnimatorClipInfo> _currentClips = new List<AnimatorClipInfo>(1);
    CombatController _combat;
    EnemyAttack _enemyAttack;
    Animator _animator;
    AttackVfx _activeAttack;
    ParticleSystem _activeEffect;
    ParticleSystem _activeTrail;
    long _actionId;
    ChargeVfx _chargeSetup;
    ParticleSystem _chargeEffect;
    long _chargeActionId;

    void OnEnable()
    {
        Subscribe();
    }

    void LateUpdate()
    {
        AttackVfx attack = _activeAttack;
        if (attack == null)
            return;

        if (!TryGetClipTime(attack.Skill.MotionClip, out float clipTime) || clipTime >= attack.End)
        {
            ClearAction();
            return;
        }

        if (clipTime < attack.Start)
            return;

        if (_activeEffect == null)
        {
            _activeEffect = Instantiate(attack.Prefab, transform, false);
            _activeEffect.transform.localScale *= attack.Scale;
        }

        UpdatePose(_activeEffect.transform, attack, clipTime);
        UpdateTrail(attack);

        // 파티클 타임라인에 대응하는 휘두르기 구간 진행도
        float progress = Mathf.InverseLerp(attack.Start, attack.End, clipTime);
        ParticleSystem.MainModule main = _activeEffect.main;
        float sampleTime = progress * main.duration / main.simulationSpeed;
        _activeEffect.Simulate(
            Mathf.Max(sampleTime, 0.0001f), withChildren: false, restart: true, fixedTimeStep: false);
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Subscribe()
    {
        if (_combat != null && _attacks.Length > 0)
        {
            _combat.OnActionStarted += HandleActionStarted;
            _combat.OnActionEnded += HandleActionEnded;
            HandleActionStarted(_combat.CurrentAction);
        }

        if (_enemyAttack != null && _charges.Length > 0)
        {
            _enemyAttack.OnChargeMovementStarted += HandleChargeStarted;
            _enemyAttack.OnChargeMovementEnded += HandleChargeEnded;
        }
    }

    void Unsubscribe()
    {
        if (_combat != null)
        {
            _combat.OnActionStarted -= HandleActionStarted;
            _combat.OnActionEnded -= HandleActionEnded;
        }

        if (_enemyAttack != null)
        {
            _enemyAttack.OnChargeMovementStarted -= HandleChargeStarted;
            _enemyAttack.OnChargeMovementEnded -= HandleChargeEnded;
        }

        ClearAction();
        ClearCharge();
    }

    static void UpdatePose(Transform effectTransform, AttackVfx attack, float clipTime)
    {
        bool beforeContact = clipTime < attack.Contact;
        VfxPose from = beforeContact ? attack.StartPose : attack.ContactPose;
        VfxPose to = beforeContact ? attack.ContactPose : attack.EndPose;
        float progress = beforeContact
            ? Mathf.InverseLerp(attack.Start, attack.Contact, clipTime)
            : Mathf.InverseLerp(attack.Contact, attack.End, clipTime);

        effectTransform.SetLocalPositionAndRotation(
            Vector3.Lerp(from.Offset, to.Offset, progress),
            Quaternion.Slerp(Quaternion.Euler(from.Rotation), Quaternion.Euler(to.Rotation), progress));
    }

    void UpdateTrail(AttackVfx attack)
    {
        if (attack.TrailPrefab == null)
            return;

        Transform swing = _activeEffect.transform;
        Quaternion rotation = swing.rotation * attack.TrailPrefab.transform.localRotation;

        if (_activeTrail == null)
        {
            _activeTrail = Instantiate(attack.TrailPrefab, swing.position, rotation);
            if (_activeTrail.gameObject.scene != gameObject.scene)
                SceneManager.MoveGameObjectToScene(_activeTrail.gameObject, gameObject.scene);

            _activeTrail.transform.localScale = Vector3.Scale(_activeTrail.transform.localScale, swing.lossyScale);
            _activeTrail.Play(true);
            return;
        }

        _activeTrail.transform.SetPositionAndRotation(swing.position, rotation);
    }

    void HandleActionStarted(CombatActionInfo action)
    {
        ClearAction();
        if (action.Kind != CombatActionKind.Skill || action.Skill == null)
            return;

        foreach (AttackVfx attack in _attacks)
        {
            if (attack.Skill != action.Skill)
                continue;

            _activeAttack = attack;
            _actionId = action.Id;
            return;
        }
    }

    void HandleActionEnded(long actionId, CombatActionResult result)
    {
        // 피해 실행 완료 이후 이어지는 공격 모션 보존
        if (_actionId == actionId && result != CombatActionResult.Completed)
            ClearAction();
    }

    void HandleChargeStarted(CombatActionInfo action)
    {
        foreach (ChargeVfx setup in _charges)
        {
            if (setup.Skill != action.Skill)
                continue;

            if (_chargeSetup != setup || _chargeEffect == null)
            {
                ClearCharge();
                _chargeSetup = setup;
                _chargeEffect = Instantiate(setup.Prefab, transform, false);
                _chargeEffect.transform.SetLocalPositionAndRotation(
                    setup.Pose.Offset, Quaternion.Euler(setup.Pose.Rotation));
            }

            _chargeActionId = action.Id;
            _chargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _chargeEffect.Play(true);
            return;
        }
    }

    void HandleChargeEnded(long actionId)
    {
        if (_chargeActionId != actionId)
            return;

        _chargeActionId = 0;
        _chargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    bool TryGetClipTime(AnimationClip clip, out float clipTime)
    {
        clipTime = 0f;
        if (!_animator.isActiveAndEnabled || _animator.IsInTransition(0))
            return false;

        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        if (state.fullPathHash != SkillAttackState)
            return false;

        _animator.GetCurrentAnimatorClipInfo(0, _currentClips);
        if (_currentClips.Count != 1 || _currentClips[0].clip != clip)
            return false;

        clipTime = state.normalizedTime;
        return true;
    }

    void ClearAction()
    {
        if (_activeEffect != null)
        {
            _activeEffect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Destroy(_activeEffect.gameObject);
        }

        if (_activeTrail != null)
        {
            // 다음 타의 시작과 별개로 방출된 월드 공간 잔상 유지
            _activeTrail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_activeTrail.gameObject, SwingTrailLifetime);
        }

        _activeEffect = null;
        _activeTrail = null;
        _activeAttack = null;
        _actionId = 0;
    }

    void ClearCharge()
    {
        if (_chargeEffect != null)
        {
            _chargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Destroy(_chargeEffect.gameObject);
        }

        _chargeEffect = null;
        _chargeSetup = null;
        _chargeActionId = 0;
    }

    void ValidateCharges()
    {
        HashSet<ChargeAttackSkillDefinition> skills = new HashSet<ChargeAttackSkillDefinition>();
        foreach (ChargeVfx charge in _charges)
        {
            if (charge == null || charge.Skill == null || charge.Prefab == null)
                throw new MissingReferenceException($"{name}: 돌진 스킬과 이펙트 프리팹 연결 필요");

            if (!skills.Add(charge.Skill))
                throw new InvalidOperationException($"{name}: 돌진 이펙트의 스킬 중복");
        }
    }

    /// <summary>
    /// 이번 소환의 전투 행동과 공격 모션 재생 모델 연결
    /// </summary>
    /// <param name="combat">승인된 공격 행동을 알리는 전투 구성 요소</param>
    /// <param name="animator">이번 소환의 모델 Animator</param>
    /// <param name="enemyAttack">실제 돌진 구간을 알리는 적 공격 구성 요소</param>
    public void Bind(CombatController combat, Animator animator, EnemyAttack enemyAttack)
    {
        Unsubscribe();
        _combat = null;
        _enemyAttack = null;
        _animator = null;
        if (_attacks.Length == 0 && _charges.Length == 0)
            return;

        if (combat == null || animator == null)
            throw new MissingReferenceException($"{name}: 전투 구성 요소와 모델 Animator 연결 필요");

        if (_charges.Length > 0 && enemyAttack == null)
            throw new MissingReferenceException($"{name}: 돌진 이펙트의 EnemyAttack 연결 필요");

        HashSet<SkillDefinition> skills = new HashSet<SkillDefinition>();
        foreach (AttackVfx attack in _attacks)
        {
            if (attack == null || attack.Skill == null ||
                attack.Skill.MotionClip == null || attack.Prefab == null)
            {
                throw new MissingReferenceException($"{name}: 공격 스킬, 모션과 이펙트 프리팹 연결 필요");
            }

            if (!skills.Add(attack.Skill))
                throw new InvalidOperationException($"{name}: 공격 모션 이펙트의 스킬 중복");

            if (!(0f <= attack.Start && attack.Start < attack.Contact &&
                attack.Contact < attack.End && attack.End <= 1f))
            {
                throw new InvalidOperationException($"{name}: 이펙트 구간은 0 ≤ 시작 < 접촉 < 종료 ≤ 1 필요");
            }

            if (!(attack.Scale > 0f) || float.IsInfinity(attack.Scale))
                throw new InvalidOperationException($"{name}: 이펙트 크기 배율은 유한한 양수 필요");
        }

        ValidateCharges();
        _combat = combat;
        _animator = animator;
        _enemyAttack = enemyAttack;
        if (isActiveAndEnabled)
            Subscribe();
    }
}
