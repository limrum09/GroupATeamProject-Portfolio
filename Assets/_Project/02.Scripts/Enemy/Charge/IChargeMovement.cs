using UnityEngine;

/// <summary>
/// 공용 돌진 실행에서 사용하는 이동 계약
/// </summary>
internal interface IChargeMovement
{
    CombatController Combat { get; }
    bool IsActiveAndEnabled { get; }

    /// <summary>
    /// 돌진 시작 가능 여부 확인
    /// </summary>
    /// <param name="direction">정규화한 수평 방향</param>
    /// <param name="distance">최대 이동 거리</param>
    /// <param name="duration">최대 거리 이동 시간</param>
    /// <returns>돌진 시작 가능 여부</returns>
    bool CanBegin(Vector3 direction, float distance, float duration);

    /// <summary>
    /// 이동 조건 재검사 후 돌진 시작
    /// </summary>
    /// <param name="direction">확정된 수평 방향</param>
    /// <param name="distance">최대 이동 거리</param>
    /// <param name="duration">최대 거리 이동 시간</param>
    /// <returns>돌진 시작 성공 여부</returns>
    bool TryBegin(Vector3 direction, float distance, float duration);

    /// <summary>
    /// NavMesh 경계를 확인하며 돌진 이동
    /// </summary>
    /// <param name="deltaTime">이번 이동에 사용할 게임 시간</param>
    /// <returns>이동 진행 또는 종료 결과</returns>
    SkillMovementStepResult Tick(float deltaTime);

    /// <summary>
    /// 이동과 경로 정리
    /// </summary>
    void Stop();
}
