using System;
using UnityEngine;

/// <summary>
/// 런 난이도 데이터 준비와 적용 대상 연결 수명 관리
/// </summary>
public class RunDifficultyInitializer : MonoBehaviour
{
    [Header("----- 난이도 데이터 -----")]
    [SerializeField] TextAsset _enemyJson;
    [SerializeField] TextAsset _waveJson;

    EnemySpawner _enemySpawner;
    CombatAreaProgress _combatArea;

    public bool HasBindings => _enemySpawner != null || _combatArea != null;

    /// <summary>
    /// 선택 난이도의 적과 웨이브 설정 연결
    /// </summary>
    /// <param name="difficulty">이번 런의 난이도</param>
    /// <param name="spawner">현재 세션의 적 소환기</param>
    /// <param name="combatArea">현재 월드의 웨이브 준비 담당자</param>
    public void Prepare(Difficulty difficulty, EnemySpawner spawner, CombatAreaProgress combatArea)
    {
        if (_enemyJson == null || _waveJson == null || spawner == null || combatArea == null)
            throw new MissingReferenceException($"{name}: 난이도 JSON과 적용 대상 연결 필요");

        if (HasBindings || spawner.gameObject.scene != gameObject.scene ||
            combatArea.gameObject.scene != gameObject.scene ||
            combatArea.SpawnManager == null || combatArea.SpawnManager.Spawner != spawner)
        {
            throw new InvalidOperationException($"{name}: 미연결 상태와 같은 씬의 소환 경로 필요");
        }

        var enemies = DifficultyJsonLoader.LoadEnemies(_enemyJson.text, _enemyJson.name, difficulty);
        var waves = DifficultyJsonLoader.LoadWaves(_waveJson.text, _waveJson.name, difficulty);

        // 세션 준비 실패 시 이미 연결한 대상만 해제
        spawner.BindDifficulty(enemies);
        _enemySpawner = spawner;
        combatArea.BindDifficulty(waves);
        _combatArea = combatArea;
    }

    /// <summary>
    /// 월드 중단과 적 반환 이후 소유한 난이도 연결 해제
    /// </summary>
    public void Release()
    {
        if (_combatArea != null)
        {
            _combatArea.UnbindDifficulty();
            _combatArea = null;
        }

        if (_enemySpawner != null)
        {
            _enemySpawner.UnbindDifficulty();
            _enemySpawner = null;
        }
    }
}
