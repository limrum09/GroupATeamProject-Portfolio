using System;
using UnityEngine;

/// <summary>
/// 확인된 적 처치의 점수를 연결된 런에 전달
/// </summary>
public class EnemyScoreRewardReceiver : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] EnemySpawnManager _enemySpawnManager;

    RunScoreProgression _progression;
    bool _subscribed;

    public EnemySpawnManager Source => _enemySpawnManager;
    public bool IsBound => _progression != null;
    public bool IsListening => _subscribed;

    void Awake()
    {
        ValidateSource();
    }

    void OnEnable()
    {
        if (_progression != null) Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void ValidateSource()
    {
        if (_enemySpawnManager == null)
            throw new MissingReferenceException($"{name}: EnemySpawnManager 연결 필요");
    }

    void Subscribe()
    {
        if (_subscribed) return;
        ValidateSource();
        _enemySpawnManager.OnEnemyDied += HandleEnemyDied;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;
        if (_enemySpawnManager != null)
            _enemySpawnManager.OnEnemyDied -= HandleEnemyDied;
        _subscribed = false;
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (!_subscribed || !isActiveAndEnabled || !_progression.IsActive) return;
        if (enemy == null || enemy.Definition == null || enemy.DifficultyDefinition == null)
            throw new MissingReferenceException("처치된 적의 난이도 설정 필요");

        _progression.TryAddKill(enemy.Definition.RankType, enemy.DifficultyDefinition.Score);
    }

    /// <summary>
    /// 처치 점수를 받을 런 연결
    /// </summary>
    /// <param name="progression">이번 런의 점수 객체</param>
    public void Bind(RunScoreProgression progression)
    {
        if (progression == null) throw new ArgumentNullException(nameof(progression));
        ValidateSource();
        if (_progression != null && _progression != progression && _progression.IsActive)
            throw new InvalidOperationException("기존 활성 런 종료 후 다른 런 연결 필요");

        Unsubscribe();
        _progression = progression;
        if (isActiveAndEnabled) Subscribe();
    }

    /// <summary>
    /// 처치 이벤트 구독과 런 연결 해제
    /// </summary>
    public void Unbind()
    {
        Unsubscribe();
        _progression = null;
    }
}
