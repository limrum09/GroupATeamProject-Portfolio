using System;
using UnityEngine;

/// <summary>스킬 설정과 요청에 따른 조준 대상 및 방향 결정</summary>
internal static class SkillAimResolver
{
    /// <summary>조준 설정 값과 조합 검사</summary>
    /// <param name="definition">검사할 스킬 정의</param>
    internal static void ValidateDefinition(SkillDefinition definition)
    {
        if (definition.TargetMode != SkillTargetMode.None && definition.TargetMode != SkillTargetMode.PreferAttackTargetThenNearest)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 대상 선택 방식");

        if (definition.AimMode != SkillAimMode.RequestedDirection && definition.AimMode != SkillAimMode.CurrentForward && definition.AimMode != SkillAimMode.SelectedTarget)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 공격 방향 방식");

        if (definition.RotationMode != SkillRotationMode.FaceDirection && definition.RotationMode != SkillRotationMode.KeepFacing)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 캐릭터 회전 방식");

        if (definition.AimMode == SkillAimMode.SelectedTarget && definition.TargetMode == SkillTargetMode.None)
            throw new InvalidOperationException($"{definition.name}: SelectedTarget 사용 시 대상 선택 방식 설정 필요");

        if (definition.TargetMode != SkillTargetMode.None)
        {
            if (float.IsNaN(definition.Range) || float.IsInfinity(definition.Range) || definition.Range <= 0f)
                throw new InvalidOperationException($"{definition.name}: 유한한 양수 탐색 거리 필요");

            if (definition.TargetMask.value == 0)
                throw new InvalidOperationException($"{definition.name}: 대상 탐색용 Target Mask 설정 필요");
        }
    }

    /// <summary>검사된 설정으로 대상과 정규화 전 공격 방향 결정</summary>
    /// <param name="source">스킬 사용자</param>
    /// <param name="definition">검사된 스킬 정의</param>
    /// <param name="request">입력 방향과 우선 대상</param>
    /// <param name="target">선택한 대상, 없으면 null</param>
    /// <returns>정규화 전 월드 공격 방향</returns>
    internal static Vector3 Resolve(CombatTarget source, SkillDefinition definition, SkillAimRequest request, out CombatTarget target)
    {
        switch (definition.TargetMode)
        {
            case SkillTargetMode.None:
                target = null;
                break;
            case SkillTargetMode.PreferAttackTargetThenNearest:
                target = CombatHitQuery.FindPreferredOrNearestTarget(source, request.PreferredTarget, definition.Range, definition.TargetMask);
                break;
            default:
                throw new InvalidOperationException($"{definition.name}: 지원하지 않는 대상 선택 방식");
        }

        switch (definition.AimMode)
        {
            case SkillAimMode.RequestedDirection:
                return request.RequestedDirection;
            case SkillAimMode.CurrentForward:
                return source.ActorTransform.forward;
            case SkillAimMode.SelectedTarget:
                if (target != null)
                    return target.Position - source.Position;
                return request.RequestedDirection;
            default:
                throw new InvalidOperationException($"{definition.name}: 지원하지 않는 공격 방향 방식");
        }
    }
}
