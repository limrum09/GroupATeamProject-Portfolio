using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어·적 공용 전투 이펙트 재생과 종료 정리</summary>
[DisallowMultipleComponent]
public sealed class CombatEffectPresenter : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] CombatController _combat;

    [Header("----- 스킬 없는 기본 공격의 선택 이펙트 -----")]
    [SerializeField] ParticleSystem _basicUseEffectPrefab;
    [SerializeField] ParticleSystem _basicHitEffectPrefab;

    CombatEffect _effectPlayer;
    CombatCameraShake _cameraShake;
    CombatTarget _damageTarget;
    bool _subscribed;
    public bool IsListening => _subscribed;

    void Awake()
    {
        ValidateEffectPlayer(_effectPlayer);
    }

    void OnEnable()
    {
        try
        {
            ValidateEffectPlayer(_effectPlayer);

            if (_subscribed)
                return;

            // 일부 구독 이후 실패하더라도 해제할 수 있도록 선기록
            _subscribed = true;

            _combat.OnExecutionCreated += HandleExecutionCreated;
            _combat.OnMovementCompleted += HandleMovementCompleted;
            if (_cameraShake != null)
            {
                _effectPlayer.OnAttackEffectPlayed += HandleAttackEffectPlayed;
                _damageTarget = _combat.Owner;
                _damageTarget.OnDamaged += HandleDamaged;
            }
        }
        catch
        {
            enabled = false;
            throw;
        }
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _subscribed = false;
        if (_effectPlayer != null)
            _effectPlayer.OnAttackEffectPlayed -= HandleAttackEffectPlayed;
        if (_damageTarget != null)
            _damageTarget.OnDamaged -= HandleDamaged;
        _damageTarget = null;

        if (_combat != null)
        {
            _combat.OnExecutionCreated -= HandleExecutionCreated;
            _combat.OnMovementCompleted -= HandleMovementCompleted;
        }
    }

    void ValidateEffectPlayer(CombatEffect player)
    {
        if (_combat == null)
            throw new MissingReferenceException($"{name}: 캐릭터 연결");

        if (player == null)
            throw new MissingReferenceException($"{name}: 활성화 전에 BindEffectPlayer 호출 필요");

        Transform actorTransform = _combat.Owner.ActorTransform;

        if (player.gameObject.scene != _combat.Owner.gameObject.scene)
            throw new System.InvalidOperationException($"{name}: 시전자와 같은 씬의 CombatEffect 연결 필요");
    }

    void HandleExecutionCreated(SkillExecutionContext execution)
    {
        if (!_subscribed || !isActiveAndEnabled)
            return;

        // 콜백에서 이 Presenter를 참조하지 않도록 필요한 값만 확보
        CombatEffect player = _effectPlayer;
        SkillDefinition definition = execution.Action.Skill;
        CombatSource attackSource = definition == null ||
            definition is TargetDamageSkillDefinition || definition is AreaDamageSkillDefinition || definition is DashStrikeSkillDefinition
                ? execution.Source
                : default;

        ParticleSystem hitPrefab = definition != null ? definition.HitEffectPrefab : _basicHitEffectPrefab;
        Vector3 useRotationOffset = definition != null ? definition.UseEffectRotation : Vector3.zero;
        Vector3 hitRotationOffset = definition != null ? definition.HitEffectRotation : Vector3.zero;
        Vector3 hitPositionOffset = definition != null ? definition.HitEffectOffset : Vector3.zero;
        bool playAreaHit = execution.Source.Team == CombatTeam.Enemy
            && definition is AreaDamageSkillDefinition;
        // 옵션을 켠 대상 스킬은 적중 범위 피해 대상에도 Hit Effect 재생
        bool playSplashHit = definition is TargetDamageSkillDefinition targeted && targeted.PlayHitEffectOnAreaTargets;


        if (definition is AreaDamageSkillDefinition area)
        {
            ParticleSystem areaPrefab = area.UseEffectPrefab;
            Vector3 areaOffset = area.EffectOffset;

            if (areaPrefab != null)
            {
                execution.OnAreaStarted += (position, rotation, hasRuntime, followAnchor) =>
                {
                    if (player == null)
                        return;

                    player.PlayArea(execution, areaPrefab, position, rotation, areaOffset, useRotationOffset, hasRuntime, followAnchor);
                };
            }

            ParticleSystem launchPrefab = area.ProjectileLaunchEffectPrefab;

            if (area.DeliveryMode == AreaDeliveryMode.Projectile && launchPrefab != null)
            {
                execution.OnExecuted += (action, position) =>
                {
                    if (player != null)
                        player.PlayAction(launchPrefab, action, position, Vector3.zero, attackSource);
                };
            }

            if (!playAreaHit)
                return;
        }
        else
        {
            ParticleSystem usePrefab = definition != null ? definition.UseEffectPrefab : _basicUseEffectPrefab;

            if (usePrefab != null)
            {
                execution.OnExecuted += (action, position) =>
                {
                    if (player != null)
                        player.PlayAction(usePrefab, action, position, useRotationOffset, attackSource);
                };
            }

            // 돌진 공격의 도착 범위 시작 시점 연출
            if (definition is DashStrikeSkillDefinition dashStrike && dashStrike.ArrivalEffectPrefab != null)
            {
                ParticleSystem arrivalPrefab = dashStrike.ArrivalEffectPrefab;

                execution.OnAreaStarted += (position, rotation, hasRuntime, followAnchor) =>
                {
                    if (player == null)
                        return;

                    player.PlayArea(execution, arrivalPrefab, position, rotation, Vector3.zero, Vector3.zero, hasRuntime, followAnchor);
                };
            }
        }

        if (hitPrefab != null)
        {
            execution.OnImpact += impact =>
            {
                if (player == null || (!playAreaHit && !playSplashHit && !impact.IsPrimaryTarget))
                    return;

                // 회전 보정 전의 공격 방향 기준으로 위치 보정
                player.Play(hitPrefab, impact.Position + impact.Rotation * hitPositionOffset, impact.Rotation, hitRotationOffset, attackSource);
            };
        }
    }

    void HandleMovementCompleted(CombatActionInfo action, Vector3 position)
    {
        if (!_subscribed || !isActiveAndEnabled)
            return;

        // 돌진 공격의 도착 연출은 범위 시작 시점에 별도 재생
        if (!(action.Skill is MovementSkillDefinition definition) || definition.ArrivalEffectPrefab == null)
            return;

        Quaternion rotation = Quaternion.LookRotation(action.Direction, Vector3.up);

        _effectPlayer.Play(definition.ArrivalEffectPrefab, position, rotation, Vector3.zero);
    }

    void HandleAttackEffectPlayed(CombatSource source)
    {
        if (_subscribed && _cameraShake != null && source.Id == _combat.Owner.Source.Id)
            _cameraShake.Play();
    }

    void HandleDamaged(DamageRequest request, float appliedDamage)
    {
        if (_subscribed && appliedDamage > 0f && _cameraShake != null)
            _cameraShake.PlayHit();
    }

    /// <summary>
    /// 비활성 캐릭터 구성 단계에서 씬 재생기 연결
    /// </summary>
    /// <param name="player">같은 씬의 이펙트 재생기</param>
    /// <param name="cameraShake">플레이어의 선택적 카메라 흔들림 연출</param>
    public void BindEffectPlayer(CombatEffect player, CombatCameraShake cameraShake = null)
    {
        if (gameObject.activeInHierarchy)
            throw new System.InvalidOperationException($"{name}: 비활성 캐릭터 구성 단계에서 재생기 연결 필요");

        // 검증 성공 이후 기존 연결 교체
        ValidateEffectPlayer(player);
        _effectPlayer = player;
        _cameraShake = cameraShake;
    }

    /// <summary>
    /// 비활성 Presenter의 구독과 주입받은 재생기 참조 해제
    /// </summary>
    public void UnbindEffectPlayer()
    {
        if (isActiveAndEnabled)
            throw new System.InvalidOperationException($"{name}: Presenter 비활성화 후 재생기 연결 해제 필요");

        Unsubscribe();
        _effectPlayer = null;
        _cameraShake = null;
    }
}
