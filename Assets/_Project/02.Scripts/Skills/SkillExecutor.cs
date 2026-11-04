using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>승인된 스킬의 효과 실행</summary>
internal static class SkillExecutor
{
    internal static void ValidateDefinition(SkillDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        SkillAimResolver.ValidateDefinition(definition);

        if (definition is AreaDamageSkillDefinition area)
        {
            if (area.AreaEffect == null)
                throw new MissingReferenceException($"{area.name}: Area Effect 연결 필요");

            area.AreaEffect.Validate();
            ValidateAreaTracking(area);

            return;
        }

        if (definition is TargetDamageSkillDefinition target)
        {
            // 적중 범위 효과는 명시적인 선택 항목
            if (target.OnHitAreaEffect != null)
                target.OnHitAreaEffect.Validate();

            if (target.UseProjectile)
                ValidateProjectileDefinition(target);

            return;
        }

        throw new InvalidOperationException($"{definition.name}: 지원하지 않는 스킬 정의");
    }

    static void ValidateAreaDefinition(AreaDamageSkillDefinition definition)
    {
        if (definition.AreaEffect == null)
            throw new MissingReferenceException($"{definition.name}: Area Effect 연결 필요");

        definition.AreaEffect.Validate();

        if (definition.PlacementMode != AreaPlacementMode.CasterPosition && definition.PlacementMode != AreaPlacementMode.RequestedWorldPosition)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 배치 방식");

        if (definition.DeliveryMode != AreaDeliveryMode.Direct && definition.DeliveryMode != AreaDeliveryMode.Projectile)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 전달 방식");

        if (!IsFinite(definition.EffectOffset))
            throw new InvalidOperationException($"{definition.name}: 유한한 Effect Offset 필요");

        if (definition.PlacementMode == AreaPlacementMode.RequestedWorldPosition)
        {
            if (definition.TargetMode != SkillTargetMode.None || definition.AimMode != SkillAimMode.RequestedDirection)
                throw new InvalidOperationException($"{definition.name}: 위치 지정형은 " + "Target Mode None과 Aim Mode Requested Direction 필요");

            float rangeSquared = definition.MaxPlacementDistance * definition.MaxPlacementDistance;

            if (!IsPositiveFinite(definition.MaxPlacementDistance) || !IsPositiveFinite(rangeSquared))
                throw new InvalidOperationException($"{definition.name}: 유한한 양수 배치 사거리 필요");

            if (definition.GroundMask.value == 0)
                throw new InvalidOperationException($"{definition.name}: Ground Mask 설정 필요");

            float probeDistance = definition.GroundProbeHeight + definition.GroundProbeDepth;

            if (!IsPositiveFinite(definition.GroundProbeHeight) || !IsPositiveFinite(definition.GroundProbeDepth) || !IsPositiveFinite(probeDistance))
                throw new InvalidOperationException($"{definition.name}: 유한한 양수 지면 검사 거리 필요");
        }

        if (definition.DeliveryMode == AreaDeliveryMode.Projectile)
        {
            if (definition.PlacementMode != AreaPlacementMode.RequestedWorldPosition)
                throw new InvalidOperationException($"{definition.name}: 도착형 탄환은 지정 위치 배치 필요");

            ValidateProjectileMotion(definition.ProjectilePrefab, definition.ProjectileSpeed, definition.ProjectileLifetime, definition.ProjectileSpawnOffset, definition.name);
        }
    }

    static void ValidateAreaTracking(AreaDamageSkillDefinition definition)
    {
        if (definition.TrackingMode == AreaTrackingMode.Fixed)
            return;

        if (definition.TrackingMode != AreaTrackingMode.FollowCaster)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 위치 추적 방식");

        if (definition.PlacementMode != AreaPlacementMode.CasterPosition || definition.DeliveryMode != AreaDeliveryMode.Direct)
            throw new InvalidOperationException($"{definition.name}: FollowCaster는 CasterPosition / Direct 설정 필요");

        if (definition.AreaEffect == null || definition.AreaEffect.DamageMode != AreaEffectDamageMode.Repeat)
            throw new InvalidOperationException($"{definition.name}: FollowCaster는 Repeat 방식의 Area Effect 연결 필요");
    }

    static void ValidateProjectileMotion(TargetProjectile prefab, float speed, float lifetime, Vector3 spawnOffset, string skillName)
    {
        if (prefab == null)
            throw new MissingReferenceException($"{skillName}: Projectile Prefab 연결 필요");

        if (!prefab.enabled || !prefab.gameObject.activeSelf || prefab.transform.parent != null)
            throw new InvalidOperationException($"{skillName}: 활성화된 프리팹 루트의 TargetProjectile 필요");

        if (!IsPositiveFinite(speed) || !IsPositiveFinite(lifetime))
            throw new InvalidOperationException($"{skillName}: 유한한 양수 탄환 속도·수명 필요");

        float offsetSquared = spawnOffset.sqrMagnitude;

        if (float.IsNaN(offsetSquared) || float.IsInfinity(offsetSquared))
            throw new InvalidOperationException($"{skillName}: 유한한 탄환 발사 오프셋 필요");
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }


    static void ValidateProjectileDefinition(TargetDamageSkillDefinition definition)
    {
        TargetProjectile prefab = definition.ProjectilePrefab;

        if (definition.FlightMode != ProjectileFlightMode.Straight && definition.FlightMode != ProjectileFlightMode.Homing)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 탄환 비행 방식");

        if (prefab == null)
            throw new MissingReferenceException($"{definition.name}: Projectile Prefab 연결 필요");

        if (!prefab.enabled || !prefab.gameObject.activeSelf || prefab.transform.parent != null)
            throw new InvalidOperationException($"{definition.name}: 활성화된 프리팹 루트의 TargetProjectile 필요");

        if (!IsPositiveFinite(definition.ProjectileSpeed) || !IsPositiveFinite(definition.ProjectileRadius) || !IsPositiveFinite(definition.ProjectileLifetime))
            throw new InvalidOperationException($"{definition.name}: 탄환 속도, 반경, 수명은 유한한 양수 필요");

        float offsetSquared = definition.ProjectileSpawnOffset.sqrMagnitude;

        if (float.IsNaN(offsetSquared) || float.IsInfinity(offsetSquared))
            throw new InvalidOperationException($"{definition.name}: 유한한 탄환 발사 위치 필요");

        if (definition.TargetMask.value == 0)
            throw new InvalidOperationException($"{definition.name}: 탄환 피격 대상의 Target Mask 설정 필요");
    }

    static TargetProjectile ExecuteArea(AreaDamageSkillDefinition definition, Vector3 origin, SkillExecutionContext execution, CombatTarget caster)
    {
        ValidateAreaTracking(definition);

        AreaEffectDefinition effect = definition.AreaEffect;

        Vector3 areaOrigin = ResolveAreaOrigin(definition, origin, execution.Action);

        Quaternion areaRotation = Quaternion.LookRotation(execution.Action.Direction, Vector3.up);

        float power = SkillDamageResolver.CalculatePower(execution, effect.DamageMultiplier, effect.SpellPowerMultiplier);

        if (definition.DeliveryMode == AreaDeliveryMode.Direct)
        {
            CombatTarget followCaster = definition.TrackingMode == AreaTrackingMode.FollowCaster ? caster : null;

            if (definition.TrackingMode == AreaTrackingMode.FollowCaster && object.ReferenceEquals(followCaster, null))
                throw new MissingReferenceException($"{definition.name}: 추적할 시전자 전달 필요");

            ActivateArea(execution, effect, areaOrigin, areaRotation, power, followCaster);

            return null;
        }

        Vector3 spawnPosition = origin + areaRotation * definition.ProjectileSpawnOffset;

        Vector3 towardDestination = areaOrigin - spawnPosition;
        float lengthSquared = towardDestination.sqrMagnitude;

        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared))
            throw new InvalidOperationException($"{definition.name}: 유효한 탄환 발사 위치와 목적지 필요");

        // 같은 위치에서도 유효한 초기 회전 사용
        Vector3 launchDirection = lengthSquared > 0.0001f ? towardDestination.normalized : execution.Action.Direction;

        Quaternion launchRotation = Quaternion.LookRotation(launchDirection, Vector3.up);

        TargetProjectile projectile = UnityEngine.Object.Instantiate(definition.ProjectilePrefab, spawnPosition, launchRotation);

        if (projectile.gameObject.scene != execution.SourceScene)
            SceneManager.MoveGameObjectToScene(projectile.gameObject, execution.SourceScene);

        projectile.InitializeToPosition(areaOrigin, launchDirection, definition.ProjectileSpeed, definition.ProjectileLifetime, arrivedPosition =>
            {
                ActivateArea(execution, effect, arrivedPosition, areaRotation, power);
            });

        return projectile;
    }

    static void ActivateArea(SkillExecutionContext execution, AreaEffectDefinition effect, Vector3 origin, Quaternion rotation, float power, CombatTarget followCaster = null)
    {
        if (!execution.CanContinue)
            return;

        AreaEffectExecutor.Execute(execution, effect, origin, rotation, power, followCaster: followCaster);
    }

    static TargetProjectile ExecuteTarget(TargetDamageSkillDefinition definition, Vector3 origin, SkillExecutionContext execution)
    {
        CombatTarget target = execution.Action.Target;
        Vector3 direction = execution.Action.Direction;

        Quaternion castRotation = Quaternion.LookRotation(direction, Vector3.up);

        float power = SkillDamageResolver.CalculatePower(execution, definition.DamageMultiplier, definition.SpellPowerMultiplier);

        if (!definition.UseProjectile)
        {
            if (target == null)
                return null;

            Physics.SyncTransforms();

            if (target.CheckHitFrom(execution.Source, execution.SourcePosition, execution.SourceAimPosition, definition.Range, definition.BlockingMask) != AttackTargetState.Valid)
                return null;

            ResolveTargetHit(definition, target, power, target.AimPosition, target.Position, castRotation, execution);

            return null;
        }

        Vector3 spawnPosition = origin + castRotation * definition.ProjectileSpawnOffset;

        Vector3 launchDirection = direction;

        // 최초 조준과 비행 중 유도를 별도로 처리
        if (definition.AimMode == SkillAimMode.SelectedTarget && target != null && target.CanReceiveFrom(execution.Source))
        {
            Vector3 towardTarget = target.AimPosition - spawnPosition;
            float lengthSquared = towardTarget.sqrMagnitude;

            if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared))
                throw new InvalidOperationException($"{definition.name}: 유효한 대상 조준 위치 필요");

            if (lengthSquared > 0.0001f)
                launchDirection = towardTarget.normalized;
        }

        Quaternion launchRotation = Quaternion.LookRotation(launchDirection, Vector3.up);

        TargetProjectile projectile = UnityEngine.Object.Instantiate(definition.ProjectilePrefab, spawnPosition, launchRotation);

        if (projectile.gameObject.scene != execution.SourceScene)
            SceneManager.MoveGameObjectToScene(projectile.gameObject, execution.SourceScene);

        projectile.Initialize(definition, execution.Source, target, launchDirection, (hitTarget, hitPosition, hitDirection) =>
        {
            Quaternion hitRotation = Quaternion.LookRotation(hitDirection, Vector3.up);

            ResolveTargetHit(definition, hitTarget, power, hitPosition, hitPosition, hitRotation, execution);
        });
          
        return projectile;
    }

    static void ResolveTargetHit(TargetDamageSkillDefinition definition, CombatTarget target, float power, Vector3 impactPosition, Vector3 splashOrigin, Quaternion rotation, SkillExecutionContext execution)
    {
        if (!execution.CanContinue || target == null || !target.CanReceiveFrom(execution.Source))
            return;

        AreaEffectDefinition effect = definition.OnHitAreaEffect;

        CombatTarget[] initialTargets = null;
        float areaPower = 0f;

        if (effect != null)
        {
            areaPower = SkillDamageResolver.CalculatePower(execution, effect.DamageMultiplier, effect.SpellPowerMultiplier);

            // 즉시 범위 타격에만 직접 피해 이전 검색 결과 사용
            if (effect.HasImmediatePulse)
                initialTargets = CombatHitQuery.FindTargets(execution.Source, splashOrigin, rotation, Vector3.zero, effect.Hitbox, effect.TargetMask, effect.BlockingMask);
        }

        if (!SkillDamageResolver.TryApply(execution, target, power, impactPosition, rotation, true))
            return;

        if (effect == null)
            return;

        AreaEffectExecutor.Execute(execution, effect,splashOrigin, rotation, areaPower, initialTargets, initialExcludedTarget: effect.HasImmediatePulse ? target : null);
    }

    internal static bool TryResolveRequestedAreaPosition(AreaDamageSkillDefinition definition, Vector3 casterPosition, SkillAimRequest request, out Vector3 position)
    {
        position = default;

        if (!request.HasWorldPosition)
            return false;

        Vector3 displacement = request.WorldPosition - casterPosition;
        displacement.y = 0f;

        float distanceSquared = displacement.sqrMagnitude;
        float rangeSquared = definition.MaxPlacementDistance * definition.MaxPlacementDistance;

        if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared > rangeSquared)
            return false;

        Vector3 rayOrigin = request.WorldPosition + Vector3.up * definition.GroundProbeHeight;

        if (!IsFinite(rayOrigin))
            return false;

        float rayDistance = definition.GroundProbeHeight + definition.GroundProbeDepth;

        Physics.SyncTransforms();

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, definition.GroundMask, QueryTriggerInteraction.Ignore))
            return false;

        position = hit.point;
        return true;
    }

    internal static Vector3 ResolveAreaOrigin(AreaDamageSkillDefinition definition, Vector3 casterOrigin, CombatActionInfo action)
    {
        switch (definition.PlacementMode)
        {
            case AreaPlacementMode.CasterPosition:
                return casterOrigin;

            case AreaPlacementMode.RequestedWorldPosition:
                if (!action.HasWorldPosition)
                    throw new InvalidOperationException($"{definition.name}: 확정된 배치 좌표 필요");

                return action.WorldPosition;

            default:
                throw new InvalidOperationException($"{definition.name}: 지원하지 않는 배치 방식");
        }
    }

    static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    /// <summary>
    /// 확정된 행동 정보에 따른 스킬 실행과 탄환 반환
    /// </summary>
    /// <param name="origin">시전자 측 발동 기준 위치</param>
    /// <param name="execution">발동 당시 전투 정보</param>
    /// <param name="caster">추적형 범위에 전달할 시전자</param>
    /// <returns>생성한 탄환, 탄환이 없으면 null</returns>
    internal static TargetProjectile Execute(Vector3 origin, SkillExecutionContext execution, CombatTarget caster)
    {
        if (!execution.CanContinue)
            return null;

        SkillDefinition definition = execution.Action.Skill;

        if (definition is AreaDamageSkillDefinition area)
            return ExecuteArea(area, origin, execution, caster);

        if (definition is TargetDamageSkillDefinition targeted)
            return ExecuteTarget(targeted, origin, execution);

        throw new InvalidOperationException($"{definition.name}: 지원하지 않는 스킬 정의");
    }

    // 스킬 없는 기존 기본 공격, 피해 계산과 적중 처리는 스킬과 공유
    internal static void ExecuteBasicAttack(SkillExecutionContext execution, CombatTarget target, float range, LayerMask blockingMask)
    {
        if (!execution.CanContinue || target == null)
            return;

        Physics.SyncTransforms();

        if (target.CheckHitFrom(execution.Source, execution.SourcePosition, execution.SourceAimPosition, range, blockingMask) != AttackTargetState.Valid)
            return;

        float power = SkillDamageResolver.CalculatePower(execution, 1f, 0f);

        SkillDamageResolver.TryApply(execution, target, power, target.AimPosition, execution.SourceRotation, true);
    }
}