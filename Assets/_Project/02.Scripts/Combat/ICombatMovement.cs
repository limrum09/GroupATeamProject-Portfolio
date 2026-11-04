using UnityEngine;

/// <summary>
/// 이동 스킬의 프레임 처리 결과
/// </summary>
public enum SkillMovementStepResult
{
    Running,
    Completed,
    Failed
}

/// <summary>
/// Combat이 승인한 이동의 검사와 실행 계약
/// </summary>
public interface ICombatMovement
{
    /// <summary>
    /// 이동을 적용할 캐릭터 기준 Transform
    /// </summary>
    Transform ActorTransform { get; }

    /// <summary>
    /// 위치 변경 없이 이동 시작 가능 여부 검사
    /// </summary>
    /// <param name="definition">이동 설정</param>
    /// <param name="direction">정규화된 수평 이동 방향</param>
    /// <returns>이동 가능한 구간 존재 여부</returns>
    bool CanStartSkillMovement(MovementSkillDefinition definition, Vector3 direction);

    /// <summary>
    /// 이동 경로 재검사 후 승인된 이동 시작
    /// </summary>
    /// <param name="definition">이동 설정</param>
    /// <param name="direction">정규화된 수평 이동 방향</param>
    /// <returns>이동 시작 성공 여부</returns>
    bool TryBeginSkillMovement(MovementSkillDefinition definition, Vector3 direction);

    /// <summary>
    /// 진행 중인 이동의 프레임 처리
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 게임 시간</param>
    /// <returns>진행 또는 종료 결과</returns>
    SkillMovementStepResult TickSkillMovement(float deltaTime);

    /// <summary>
    /// 진행 중인 스킬 이동과 이동 경로 정리
    /// </summary>
    void CancelSkillMovement();
}