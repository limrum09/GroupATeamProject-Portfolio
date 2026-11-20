using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 첫 탄환 발사 이후 남은 연사 회차의 일정과 종료 관리
/// </summary>
[DisallowMultipleComponent]
public class ProjectileBurstRuntime : MonoBehaviour
{
    SkillExecutionContext _execution;
    CombatTarget _caster;
    Action _launch;
    int _remainingShots;
    float _interval;
    double _nextShotAt;

    void Update()
    {
        if (!_execution.CanContinue || _caster == null || !_caster.IsAlive || _caster.Source.Id != _execution.Source.Id)
        {
            Destroy(gameObject);
            return;
        }

        double now = Time.timeAsDouble;
        if (Time.timeScale <= 0f || now < _nextShotAt)
            return;

        // 프레임 지연 시에도 남은 탄환을 한 프레임에 몰아서 발사하지 않음
        _nextShotAt = now + _interval;
        _remainingShots--;
        if (_remainingShots == 0)
            Destroy(gameObject);

        _launch();
    }

    /// <summary>
    /// 발동한 씬에 남은 연사 단계 등록
    /// </summary>
    /// <param name="execution">첫 탄환과 공유할 발동 정보</param>
    /// <param name="caster">연사를 시작한 활성 시전자</param>
    /// <param name="remainingShots">첫 탄환을 제외한 발사 수</param>
    /// <param name="interval">발사 사이의 게임 시간 간격</param>
    /// <param name="launch">공통 탄환 발사 경로</param>
    internal static void Begin(SkillExecutionContext execution, CombatTarget caster, int remainingShots, float interval, Action launch)
    {
        GameObject burst = new GameObject(nameof(ProjectileBurstRuntime));
        if (burst.scene != execution.SourceScene)
            SceneManager.MoveGameObjectToScene(burst, execution.SourceScene);

        ProjectileBurstRuntime runtime = burst.AddComponent<ProjectileBurstRuntime>();
        runtime._execution = execution;
        runtime._caster = caster;
        runtime._remainingShots = remainingShots;
        runtime._interval = interval;
        runtime._nextShotAt = Time.timeAsDouble + interval;
        runtime._launch = launch;
    }
}
