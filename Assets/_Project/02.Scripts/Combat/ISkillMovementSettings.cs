using UnityEngine;

/// <summary>
/// 스킬 이동 실행에 필요한 설정 계약
/// </summary>
public interface ISkillMovementSettings
{
    MovementSkillMode Mode { get; }
    // 대쉬 속도 계산 기준 최대 거리
    float Distance { get; }
    float DashDuration { get; }
    LayerMask BlockingMask { get; }
}
