using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적의 공격 선택 결과
/// </summary>
public enum EnemyAttackDecision
{
    Ready,
    Busy,
    Wait,
    Approach,
    NoTarget
}

/// <summary>적의 스킬 선택과 공격 요청 및 승인된 모션 연결</summary>
public class EnemyAttack : MonoBehaviour
{
    [Header("----- 컴포넌트 -----")]
    [SerializeField] CombatController _combat;	// 공격 승인과 실행 담당
    [SerializeField] EnemyAnimation _animation;	// 승인된 공격의 모션 요청

    SkillInstance[] _skills = Array.Empty<SkillInstance>();
    long _actionId;
    readonly EnemyAttackSequenceExecution _sequence = new EnemyAttackSequenceExecution();
    readonly Dictionary<SkillDefinition, SkillInstance[]> _sequenceSkills =
        new Dictionary<SkillDefinition, SkillInstance[]>();

    ChargeAttackExecution _charge;
    EnemyMovement _chargeMovement;
    CombatEffect _effectPlayer;

    public CombatController Combat => _combat;
    public bool HasSkills => _skills.Length > 0;

    public bool IsAttackInProgress =>
        _sequence.IsRunning ||
        _combat.IsBusy ||
        (_charge != null && _charge.IsRunning) ||
        _animation.IsAttackPlaying;

    void OnEnable()
    {
        _combat.OnActionStarted += HandleActionStarted;
        _combat.OnActionEnded += HandleActionEnded;
    }

    void OnDisable()
    {
        StopAttack();

        _combat.OnActionStarted -= HandleActionStarted;
        _combat.OnActionEnded -= HandleActionEnded;
    }

    void BindCharge(
        EnemyMovement movement,
        Collider contactCollider,
        CombatEffect effectPlayer)
    {
        if (movement == null || contactCollider == null || effectPlayer == null)
        {
            throw new MissingReferenceException(
                $"{name}: 돌진 이동·Content Collider·이펙트 재생기 연결 필요");
        }

        if (effectPlayer.gameObject.scene != _combat.Owner.gameObject.scene ||
            effectPlayer.transform.IsChildOf(_combat.Owner.ActorTransform))
        {
            throw new InvalidOperationException(
                $"{name}: 같은 씬의 캐릭터 외부 이펙트 재생기 연결 필요");
        }

        _chargeMovement = movement;
        _effectPlayer = effectPlayer;
        _charge = new ChargeAttackExecution(_combat, movement, contactCollider);

        _charge.OnStarted += HandleActionStarted;
        _charge.OnEnded += HandleChargeEnded;
        _charge.OnExecutionCreated += HandleChargeExecutionCreated;

        movement.BindChargeExecution(_charge);
    }

    void HandleChargeEnded(long actionId, CombatActionResult result)
    {
        if (_actionId != actionId)
            return;

        _actionId = 0;

        // 접촉 정지 시 남은 돌진 모션도 종료
        _animation.CancelAnimAttack(actionId);
    }

    void HandleChargeExecutionCreated(SkillExecutionContext execution)
    {
        if (_actionId != execution.Action.Id)
            return;

        // 준비 종료와 이동 시작 성공 이후 돌진 모션 재생
        _animation.PlaySkill(execution.Action.Id, execution.Action.Skill.MotionClip, true);

        CombatEffect player = _effectPlayer;
        SkillDefinition definition = execution.Action.Skill;

        if (definition.UseEffectPrefab != null)
        {
            execution.OnExecuted += (action, position) =>
            {
                if (player != null)
                {
                    player.PlayAction(
                        definition.UseEffectPrefab, action, position,
                        definition.UseEffectRotation);
                }
            };
        }

        if (definition.HitEffectPrefab != null)
        {
            execution.OnImpact += impact =>
            {
                if (player != null)
                {
                    player.Play(
                        definition.HitEffectPrefab, impact.Position,
                        impact.Rotation, definition.HitEffectRotation);
                }
            };
        }
    }

    void ReleaseCharge()
    {
        if (_charge == null)
            return;

        _charge.Cancel();

        _charge.OnStarted -= HandleActionStarted;
        _charge.OnEnded -= HandleChargeEnded;
        _charge.OnExecutionCreated -= HandleChargeExecutionCreated;

        _chargeMovement.BindChargeExecution(null);
        _charge = null;
        _chargeMovement = null;
        _effectPlayer = null;
    }

    /// <summary>같은 적의 전투와 애니메이션 연결 검사</summary>
    public void ValidateReferences()
    {
        if (_combat == null || _animation == null ||
            _combat.Owner == null || _combat.Owner.ActorTransform == null)
        {
            throw new MissingReferenceException(
                $"{name}: CombatController, EnemyAnimation과 전투 소유자 연결 필요");
        }

        Transform actorTransform = _combat.Owner.ActorTransform;

        if (transform.IsChildOf(actorTransform) == false ||
            _animation.transform.IsChildOf(actorTransform) == false)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 적의 전투와 애니메이션 연결 필요");
        }
    }

    SkillAimRequest CreateRequest(CombatTarget target)
    {
        return new SkillAimRequest(
            target.Position - _combat.Owner.Position, target, target.Position, true);
    }

    bool IsWithinSkillRange(SkillDefinition definition, CombatTarget target)
    {
        // 지정 위치형은 Combat과 같은 배치 가능 거리·지면 검사 사용
        if (definition is AreaDamageSkillDefinition area &&
            area.PlacementMode == AreaPlacementMode.RequestedWorldPosition)
        {
            return SkillExecutor.TryResolveRequestedAreaPosition(
                area, _combat.Owner.Position, CreateRequest(target), out _);
        }

        // 직접 대상 공격만 기존 차폐 조건 확인, 투사체 적중은 실행 경로에서 판정
        LayerMask blockingMask = 0;
        if (definition is TargetDamageSkillDefinition targeted && !targeted.UseProjectile)
            blockingMask = targeted.BlockingMask;

        return target.CheckHitFrom(_combat.Owner, definition.Range, blockingMask)
            == AttackTargetState.Valid;
    }

    void HandleActionStarted(CombatActionInfo info)
    {
        if (info.Kind != CombatActionKind.Skill)
            return;

        if (info.Skill == null || info.Skill.MotionClip == null)
        {
            throw new MissingReferenceException($"{name}: 승인된 스킬과 MotionClip 연결 필요");
        }

        _sequence.NotifyStarted(info);
        _actionId = info.Id;

        if (info.Skill.RotationMode == SkillRotationMode.FaceDirection)
        {
            _combat.Owner.ActorTransform.rotation =
                Quaternion.LookRotation(info.Direction, Vector3.up);
        }

        // 돌진 준비 중에는 대기, 실제 이동 시작 알림에서 Run 재생
        if (info.Skill is ChargeAttackSkillDefinition)
            return;

        _animation.PlaySkill(info.Id, info.Skill.MotionClip, false);
    }

    void HandleActionEnded(long actionId, CombatActionResult result)
    {
        _sequence.NotifyEnded(actionId, result);
        if (_actionId != actionId)
            return;

        _actionId = 0;

        // 효과 실행 완료 후에도 남은 모션 재생 유지
        if (result == CombatActionResult.Completed)
            return;

        _animation.CancelAnimAttack(actionId);
    }

    void ValidateSkill(SkillDefinition skill)
    {
        if (skill is ChargeAttackSkillDefinition charge)
        {
            charge.Validate();
        }
        else
        {
            SkillExecutor.ValidateDefinition(skill);
        }

        if (skill.MotionClip == null)
            throw new MissingReferenceException($"{skill.name}: MotionClip 연결 필요");

        bool isCharge = skill is ChargeAttackSkillDefinition;
        if (skill.MotionClip.legacy || skill.MotionClip.length <= 0f ||
            (!isCharge && skill.MotionClip.isLooping))
        {
            throw new InvalidOperationException($"{skill.name}: 유효한 Animator 모션 필요, 돌진 외 반복 모션 불가");
        }

        if (float.IsNaN(skill.CastTime) || float.IsInfinity(skill.CastTime) || skill.CastTime < 0f ||
            float.IsNaN(skill.Cooldown) || float.IsInfinity(skill.Cooldown) || skill.Cooldown < 0f ||
            float.IsNaN(skill.Range) || float.IsInfinity(skill.Range) || skill.Range <= 0f)
        {
            throw new InvalidOperationException($"{skill.name}: 시전 시간·쿨타임·사거리 설정 확인 필요");
        }

        if (skill is TargetDamageSkillDefinition targeted &&
            (!targeted.UseProjectile || targeted.FlightMode == ProjectileFlightMode.Homing) &&
            skill.TargetMode == SkillTargetMode.None)
        {
            throw new InvalidOperationException($"{skill.name}: 대상 피해 또는 유도 탄환의 대상 선택 설정 필요");
        }
    }

    bool TryRequestSkill(SkillInstance skill, CombatTarget target)
    {
        if (!isActiveAndEnabled || !_combat.Owner.IsAlive ||
            target == null || !target.CanReceiveFrom(_combat.Owner) ||
            !skill.IsReady || !IsWithinSkillRange(skill.Definition, target))
        {
            return false;
        }

        SkillAimRequest request = CreateRequest(target);
        if (skill.Definition is ChargeAttackSkillDefinition)
            return _charge.TryStart(skill, request);

        return _combat.TryEnemyAttack(skill, request);
    }

    Dictionary<SkillDefinition, SkillInstance[]> CreateSequences(
        EnemyDefinition definition, SkillInstance[] skills,
        HashSet<SkillDefinition> registered)
    {
        Dictionary<SkillDefinition, SkillInstance[]> sequences =
            new Dictionary<SkillDefinition, SkillInstance[]>();
        HashSet<SkillDefinition> children = new HashSet<SkillDefinition>();

        if (definition.AttackSequences == null)
            throw new MissingReferenceException($"{definition.name}: 연속 공격 배열 필요");

        foreach (EnemyAttackSequence sequence in definition.AttackSequences)
        {
            if (sequence == null || sequence.Steps == null || sequence.Steps.Count < 2)
                throw new InvalidOperationException($"{definition.name}: 연속 공격에 두 단계 이상 연결 필요");

            SkillDefinition first = sequence.Steps[0];
            if (first == null || !registered.Contains(first) || sequences.ContainsKey(first))
                throw new InvalidOperationException($"{sequence.name}: 중복되지 않는 등록 스킬을 첫 단계로 지정 필요");

            SkillInstance[] steps = new SkillInstance[sequence.Steps.Count];
            // 대표 스킬의 기존 인스턴스 공유로 연속 공격 쿨타임 유지
            foreach (SkillInstance skill in skills)
            {
                if (skill.Definition == first)
                {
                    steps[0] = skill;
                    break;
                }
            }

            for (int i = 0; i < steps.Length; i++)
            {
                SkillDefinition skill = sequence.Steps[i];
                if (skill == null)
                    throw new MissingReferenceException($"{sequence.name}: {i}번 스킬 연결 필요");

                if (skill is ChargeAttackSkillDefinition || skill is MovementSkillDefinition)
                    throw new InvalidOperationException($"{sequence.name}: 연속 공격에 이동 스킬 등록 불가");

                if (i == 0)
                    continue;

                if (registered.Contains(skill) || !children.Add(skill) ||
                    skill == definition.BasicAttackSkill || skill.Cooldown != 0f)
                {
                    throw new InvalidOperationException(
                        $"{sequence.name}: 후속 단계는 중복되지 않는 전용 스킬과 쿨타임 0 설정 필요");
                }

                ValidateSkill(skill);
                steps[i] = new SkillInstance(skill);
            }

            sequences.Add(first, steps);
        }

        return sequences;
    }

    /// <summary>
    /// 이번 소환의 스킬 설정 검증과 개체별 인스턴스 초기화
    /// </summary>
    /// <param name="definition">이번 소환의 적 정의</param>
    /// <param name="movement">같은 적의 이동 기능</param>
    /// <param name="contactCollider">이번 Content의 접촉 Collider</param>
    /// <param name="effectPlayer">같은 씬의 이펙트 재생기</param>
    public void Init(
        EnemyDefinition definition,
        EnemyMovement movement,
        Collider contactCollider,
        CombatEffect effectPlayer)
    {
        if (definition == null || definition.Skills == null)
            throw new MissingReferenceException($"{name}: 적 정의와 스킬 배열 필요");

        SkillInstance[] skills = new SkillInstance[definition.Skills.Count];
        HashSet<SkillDefinition> registered = new HashSet<SkillDefinition>();
        bool hasChargeSkill = false;

        for (int i = 0; i < skills.Length; i++)
        {
            SkillDefinition skill = definition.Skills[i];
            if (skill == null)
                throw new MissingReferenceException($"{definition.name}: {i}번 스킬 연결 필요");

            if (!registered.Add(skill) || skill == definition.BasicAttackSkill)
                throw new InvalidOperationException($"{definition.name}: 중복 스킬 또는 고정 기본 공격 등록 불가");

            ValidateSkill(skill);
            if (skill is ChargeAttackSkillDefinition)
                hasChargeSkill = true;

            skills[i] = new SkillInstance(skill);
        }

        Dictionary<SkillDefinition, SkillInstance[]> sequences =
            CreateSequences(definition, skills, registered);

        StopAttack();
        ReleaseCharge();

        if (skills.Length > 0)
            _animation.PrepareSkillPlayback();

        if (hasChargeSkill)
            BindCharge(movement, contactCollider, effectPlayer);

        _skills = skills;
        _sequenceSkills.Clear();
        foreach (KeyValuePair<SkillDefinition, SkillInstance[]> sequence in sequences)
            _sequenceSkills.Add(sequence.Key, sequence.Value);
    }

    /// <summary>
    /// 이번 소환의 공격과 스킬 인스턴스 해제
    /// </summary>
    public void Clear()
    {
        StopAttack();
        ReleaseCharge();
        _skills = Array.Empty<SkillInstance>();
        _sequenceSkills.Clear();
    }

    /// <summary>
    /// 돌진 실행과 연속 공격의 프레임 갱신
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    public void TickExecution(float deltaTime)
    {
        if (_charge != null)
            _charge.Tick(deltaTime);

        if (!_combat.Owner.IsAlive)
        {
            _sequence.Cancel();
            return;
        }

        bool actionOrMotionPlaying = _combat.IsBusy ||
            (_charge != null && _charge.IsRunning) || _animation.IsAttackPlaying;
        _sequence.Tick(actionOrMotionPlaying, TryRequestSkill);
    }

    /// <summary>
    /// 실행 상태와 준비된 스킬의 사거리에 따른 공격 판단
    /// </summary>
    /// <param name="target">공격할 대상</param>
    /// <returns>진행 유지·공격·접근·대기 또는 대상 부재</returns>
    public EnemyAttackDecision GetDecision(CombatTarget target)
    {
        if (IsAttackInProgress)
            return EnemyAttackDecision.Busy;

        if (target == null || !target.CanReceiveFrom(_combat.Owner))
            return EnemyAttackDecision.NoTarget;

        bool hasReadySkill = false;
        foreach (SkillInstance skill in _skills)
        {
            if (!skill.IsReady)
                continue;

            hasReadySkill = true;
            if (IsWithinSkillRange(skill.Definition, target))
                return EnemyAttackDecision.Ready;
        }

        return hasReadySkill ? EnemyAttackDecision.Approach : EnemyAttackDecision.Wait;
    }

    /// <summary>
    /// 현재 대상에게 등록 순서로 준비된 공격 요청
    /// </summary>
    /// <param name="target">공격할 대상</param>
    public void Tick(CombatTarget target)
    {
        if (!isActiveAndEnabled || IsAttackInProgress || !HasSkills ||
            target == null || !target.CanReceiveFrom(_combat.Owner))
            return;

        foreach (SkillInstance skill in _skills)
        {
            if (!skill.IsReady || !IsWithinSkillRange(skill.Definition, target))
                continue;

            if (_sequenceSkills.TryGetValue(skill.Definition, out SkillInstance[] steps))
            {
                _sequence.Start(steps, target, TryRequestSkill);
                return;
            }

            if (TryRequestSkill(skill, target))
                return;
        }
    }

    /// <summary>
    /// 연속 공격 예약과 진행 중인 돌진 및 남은 공격 모션 정리
    /// </summary>
    public void StopAttack()
    {
        _sequence.Cancel();

        if (_charge != null)
            _charge.Cancel();

        _actionId = 0;
        _animation.CancelAnimAttack();
    }
}
