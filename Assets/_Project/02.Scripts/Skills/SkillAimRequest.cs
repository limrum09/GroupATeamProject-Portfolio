using System;
using UnityEngine;

/// <summary>스킬 사용 순간의 방향·우선 대상·지정 위치</summary>
public readonly struct SkillAimRequest
{
    public Vector3 RequestedDirection { get; }
    public CombatTarget PreferredTarget { get; }
    /// <summary>입력 당시의 월드 위치, HasWorldPosition이 true일 때 사용</summary>
    public Vector3 WorldPosition { get; }
    /// <summary>지정 좌표 보유 여부, 거리·지면 유효성 판정과 별개</summary>
    public bool HasWorldPosition { get; }

    /// <summary>지정 위치가 없는 방향형 요청 구성</summary>
    /// <param name="requestedDirection">입력 또는 AI가 계산한 월드 방향</param>
    /// <param name="preferredTarget">우선 공격 대상, 없으면 null</param>
    public SkillAimRequest(Vector3 requestedDirection, CombatTarget preferredTarget) : this(requestedDirection, preferredTarget, default, false)
    {

    }

    /// <summary>지정 위치를 포함한 스킬 요청 구성</summary>
    /// <param name="requestedDirection">입력 또는 AI가 계산한 월드 방향</param>
    /// <param name="preferredTarget">우선 공격 대상, 없으면 null</param>
    /// <param name="worldPosition">요청 순간 확정한 월드 위치</param>
    /// <param name="hasWorldPosition">지정 좌표 보유 여부</param>
    public SkillAimRequest(Vector3 requestedDirection, CombatTarget preferredTarget, Vector3 worldPosition, bool hasWorldPosition)
    {
        if (hasWorldPosition && !IsFinite(worldPosition))
            throw new ArgumentException("유한한 월드 위치 필요", nameof(worldPosition));

        RequestedDirection = requestedDirection;
        PreferredTarget = preferredTarget;
        WorldPosition = hasWorldPosition ? worldPosition : default;
        HasWorldPosition = hasWorldPosition;
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}