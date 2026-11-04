using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 범위 효과 시작과 공통 범위 피해 실행
/// </summary>
internal static class AreaEffectExecutor
{
    /// <summary>
    /// 첫 범위 타격과 반복 실행 준비 이후 범위 수명 통지
    /// </summary>
    /// <param name="execution">발동 당시 공격 실행 정보</param>
    /// <param name="definition">범위 효과 설정</param>
    /// <param name="origin">발동 기준 위치</param>
    /// <param name="rotation">고정할 기준 회전</param>
    /// <param name="power">방어력과 치명타 적용 전 타격당 위력</param>
    /// <param name="initialTargets">즉시 타격용 검색 결과, 없으면 새로 검색</param>
    /// <param name="initialExcludedTarget">즉시 타격에서 제외할 직접 피격 대상</param>
    /// <param name="followCaster">추적 대상, 고정형은 null</param>
    internal static void Execute(SkillExecutionContext execution, AreaEffectDefinition definition, Vector3 origin, Quaternion rotation, float power, CombatTarget[] initialTargets = null, CombatTarget initialExcludedTarget = null, CombatTarget followCaster = null)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (!execution.CanContinue)
            return;

        definition.Validate();

        // 파괴된 Unity 객체도 추적형으로 구분하여 종료 검사
        bool followsCaster = !object.ReferenceEquals(followCaster, null);

        if (followsCaster && !AreaEffectRuntime.IsFollowCasterValid(execution, followCaster))
            return;

        double startedAt = Time.timeAsDouble;
        int initialPulseCount = 0;

        if (definition.HasImmediatePulse)
        {
            if (initialTargets != null)
            {
                ApplyPulse(execution, initialTargets, power, rotation, initialExcludedTarget);
            }
            else
            {
                ExecutePulse(execution, origin, rotation, Vector3.zero, definition.Hitbox, definition.TargetMask, definition.BlockingMask, power, initialExcludedTarget);
            }

            // 적이 없어도 범위 검사 한 번을 타격 횟수로 계산
            initialPulseCount = 1;
        }

        if (!execution.CanContinue)
            return;

        // 첫 타격의 콜백 중 사라진 시전자에 대한 반복 실행 방지
        if (followsCaster && !AreaEffectRuntime.IsFollowCasterValid(execution, followCaster))
            return;

        bool hasRuntime =definition.DamageMode == AreaEffectDamageMode.Repeat && (definition.EndMode != AreaEffectEndMode.PulseCount || initialPulseCount < definition.PulseCount);

        Transform followAnchor = null;

        if (hasRuntime)
        {
            GameObject runtimeObject = new GameObject($"AreaEffect_{definition.name}");

            runtimeObject.transform.SetPositionAndRotation(origin, rotation);

            if (runtimeObject.scene != execution.SourceScene)
                SceneManager.MoveGameObjectToScene(runtimeObject, execution.SourceScene);

            AreaEffectRuntime runtime = runtimeObject.AddComponent<AreaEffectRuntime>();

            runtime.Initialize(execution, definition, origin, rotation, power, startedAt, initialPulseCount, followCaster);

            if (followsCaster)
                followAnchor = runtimeObject.transform;
        }

        try
        {
            execution.PublishAreaStarted(origin, rotation, hasRuntime, followAnchor);
        }
        finally
        {
            if (!hasRuntime)
                execution.PublishAreaEnded();
        }
    }

    /// <summary>현재 범위의 대상을 새로 검색하여 한 회차 피해 적용</summary>
    /// <param name="execution">발동 단위 실행 정보</param>
    /// <param name="origin">범위 기준 위치</param>
    /// <param name="rotation">범위와 적중 연출의 기준 회전</param>
    /// <param name="effectOffset">기준 위치 보정</param>
    /// <param name="hitbox">타격 범위 설정</param>
    /// <param name="targetMask">피격 대상 레이어</param>
    /// <param name="blockingMask">차단 검사 레이어</param>
    /// <param name="power">방어력과 치명타 적용 전 피해</param>
    /// <param name="excludedTarget">이번 회차에서 제외할 대상, 없으면 null</param>
    internal static void ExecutePulse(SkillExecutionContext execution, Vector3 origin, Quaternion rotation, Vector3 effectOffset, CombatHitbox hitbox, LayerMask targetMask, LayerMask blockingMask, float power, CombatTarget excludedTarget = null)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        if (hitbox == null)
            throw new ArgumentNullException(nameof(hitbox));

        if (!execution.CanContinue)
            return;

        CombatTarget[] targets = CombatHitQuery.FindTargets(execution.Source, origin, rotation, effectOffset, hitbox, targetMask, blockingMask);

        ApplyPulse(execution, targets, power, rotation, excludedTarget);
    }

    /// <summary>미리 검색한 중복 없는 대상 목록에 한 회차 피해 적용</summary>
    /// <param name="execution">발동 단위 실행 정보</param>
    /// <param name="targets">FindTargets에서 얻은 중복 제거 대상 배열</param>
    /// <param name="power">방어력과 치명타 적용 전 피해</param>
    /// <param name="rotation">적중 연출 기준 회전</param>
    /// <param name="excludedTarget">이번 회차에서 제외할 대상, 없으면 null</param>
    internal static void ApplyPulse(SkillExecutionContext execution, CombatTarget[] targets, float power, Quaternion rotation, CombatTarget excludedTarget = null)
    {
        if (execution == null)
            throw new ArgumentNullException(nameof(execution));

        if (targets == null)
            throw new ArgumentNullException(nameof(targets));

        foreach (CombatTarget target in targets)
        {
            if (!execution.CanContinue)
                return;

            if (target == null || target == excludedTarget)
                continue;

            SkillDamageResolver.TryApply(execution, target, power, target.AimPosition, rotation, false);
        }
    }
}