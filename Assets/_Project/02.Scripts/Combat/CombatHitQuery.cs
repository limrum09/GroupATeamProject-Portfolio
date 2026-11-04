using System;
using System.Collections.Generic;
using UnityEngine;

public class CombatHitQuery
{
    static Vector3 GetCenter(Vector3 origin, Quaternion rotation, Vector3 effectOffset, CombatHitbox hitbox)
    {
        return origin + rotation * (effectOffset + hitbox.Offset);
    }

    /// <summary>범위 안의 유효한 피해 대상 조회</summary>
    /// <param name="source">공격자</param>
    /// <param name="origin">범위 기준 위치</param>
    /// <param name="rotation">범위 기준 회전</param>
    /// <param name="effectOffset">공통 위치 보정</param>
    /// <param name="hitbox">범위 설정</param>
    /// <param name="targetMask">피격 Collider 레이어</param>
    /// <param name="blockingMask">차폐 레이어</param>
    /// <returns>중복을 제거한 피해 대상 배열</returns>
    public static CombatTarget[] FindTargets(CombatSource source, Vector3 origin, Quaternion rotation, Vector3 effectOffset, CombatHitbox hitbox, LayerMask targetMask, LayerMask blockingMask)
    {
        Vector3 center = GetCenter(origin, rotation, effectOffset, hitbox);

        Physics.SyncTransforms();

        Collider[] overlaps;

        switch (hitbox.HitShape)
        {
            case CombatHitShape.Box:
                overlaps = Physics.OverlapBox(center, hitbox.BoxSize * 0.5f, rotation, targetMask, QueryTriggerInteraction.Collide);
                break;
            case CombatHitShape.Sphere:
                overlaps = Physics.OverlapSphere(center, hitbox.Radius, targetMask, QueryTriggerInteraction.Collide);
                break;
            default:
                throw new InvalidOperationException("지원하지 않는 Hit Box 형태입니다.");
        }

        HashSet<CombatTarget> visited = new HashSet<CombatTarget>();
        List<CombatTarget> targets = new List<CombatTarget>();

        foreach(Collider overlap in overlaps)
        {
            CombatTarget target = overlap.GetComponentInParent<CombatTarget>();

            if (target == null || !target.CanReceiveFrom(source) || !visited.Add(target))
                continue;

            if (Physics.Linecast(origin, target.AimPosition, blockingMask, QueryTriggerInteraction.Ignore))
                continue;

            targets.Add(target);
        }

        return targets.ToArray();
    }

    /// <summary>벽 차단 없이 우선 대상 또는 가장 가까운 유효한 적 조회</summary>
    /// <param name="source">스킬 사용자</param>
    /// <param name="preferredTarget">우선 선택할 공격 대상, 없으면 null</param>
    /// <param name="range">대상 탐색 거리</param>
    /// <param name="targetMask">검색할 피격 Collider 레이어</param>
    /// <returns>선택한 적, 유효한 대상이 없으면 null</returns>
    public static CombatTarget FindPreferredOrNearestTarget(CombatTarget source, CombatTarget preferredTarget, float range, LayerMask targetMask)
    {
        Physics.SyncTransforms();

        Collider[] overlaps = Physics.OverlapSphere(source.Position, range, targetMask, QueryTriggerInteraction.Collide);

        HashSet<CombatTarget> visited = new HashSet<CombatTarget>();

        CombatTarget nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        foreach (Collider overlap in overlaps)
        {
            CombatTarget candidate = overlap.GetComponentInParent<CombatTarget>();

            if (candidate == null || !visited.Add(candidate))
                continue;

            // 스킬 대상 선택에서는 벽을 제외하고 생존·진영·거리 검사
            if (candidate.CheckHitFrom(source, range, 0) != AttackTargetState.Valid)
                continue;

            if (candidate == preferredTarget)
                return candidate;

            float distanceSquared = (candidate.Position - source.Position).sqrMagnitude;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    public static void DrawPreview(Vector3 origin, Quaternion rotation, Vector3 effectOffset, CombatHitbox hitbox)
    {
        Vector3 center = GetCenter(origin, rotation, effectOffset, hitbox);

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.color = Color.red;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

        if (hitbox.HitShape == CombatHitShape.Box)
            Gizmos.DrawWireCube(Vector3.zero, hitbox.BoxSize);
        else if (hitbox.HitShape == CombatHitShape.Sphere)
            Gizmos.DrawWireSphere(Vector3.zero, hitbox.Radius);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
