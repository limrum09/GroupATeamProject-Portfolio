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

        ParticleSystem hitPrefab = definition != null ? definition.HitEffectPrefab : _basicHitEffectPrefab;
        Vector3 useRotationOffset = definition != null ? definition.UseEffectRotation : Vector3.zero;
        Vector3 hitRotationOffset = definition != null ? definition.HitEffectRotation : Vector3.zero;


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
                        player.PlayAction(launchPrefab, action, position, Vector3.zero);
                };
            }

            // 장판의 각 대상과 반복 타격에 대한 Hit Effect 구독 제외
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
                        player.PlayAction(usePrefab, action, position, useRotationOffset);
                };
            }
        }

        if (hitPrefab != null)
        {
            execution.OnImpact += impact =>
            {
                if (player == null || !impact.IsPrimaryTarget)
                    return;

                player.Play(hitPrefab, impact.Position, impact.Rotation, hitRotationOffset);
            };
        }
    }

    void HandleMovementCompleted(CombatActionInfo action, Vector3 position)
    {
        if (!_subscribed || !isActiveAndEnabled)
            return;

        MovementSkillDefinition definition = (MovementSkillDefinition)action.Skill;

        if (definition.ArrivalEffectPrefab == null)
            return;

        Quaternion rotation = Quaternion.LookRotation(action.Direction, Vector3.up);

        _effectPlayer.Play(definition.ArrivalEffectPrefab, position, rotation, Vector3.zero);
    }

    /// <summary>
    /// 비활성 캐릭터 구성 단계에서 씬 재생기 연결
    /// </summary>
    /// <param name="player">같은 씬의 이펙트 재생기</param>
    public void BindEffectPlayer(CombatEffect player)
    {
        if (gameObject.activeInHierarchy)
            throw new System.InvalidOperationException($"{name}: 비활성 캐릭터 구성 단계에서 재생기 연결 필요");

        // 검증 성공 이후 기존 연결 교체
        ValidateEffectPlayer(player);
        _effectPlayer = player;
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
    }
}