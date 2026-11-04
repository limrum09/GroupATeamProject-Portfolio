using System;
using UnityEngine;

/// <summary>
/// 확인된 적 처치의 경험치 보상 전달
/// </summary>
public sealed class EnemyExpRewardReceiver : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] EnemySpawnManager _enemySpawnManager;

    RunLevelProgression _progression;
    bool _subscribed;

    public bool IsBound => _progression != null;
    public bool IsListening => _subscribed;
    void Awake()
    {
        ValidateSource();
    }

    void OnEnable()
    {
        if(_progression != null)
            Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Subscribe()
    {
        if (_subscribed)
            return;

        ValidateSource();

        if (_progression == null)
            throw new InvalidOperationException($"{name}: 이벤트 구독 전 RunLevelProgression 연결 필요");

        _enemySpawnManager.OnEnemyDied += HandleEnemyDied;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if(!_subscribed) 
            return;

        if (_enemySpawnManager != null)
            _enemySpawnManager.OnEnemyDied -= HandleEnemyDied;

        _subscribed = false;
    }

    void ValidateSource()
    {
        if (_enemySpawnManager == null)
            throw new MissingReferenceException($"{name}: EnemySpawnManager 연결 필요");
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (!_subscribed)
            return;

        RunLevelProgression progression = _progression;

        if(progression == null)
            throw new InvalidOperationException($"{name}: 구독 중인 경험치 수신기의 런 연결 필요");

        if (!progression.IsActive)
            return;

        EnemyDifficultyDefinition definition = enemy.DifficultyDefinition;

        if (definition == null)
            throw new MissingReferenceException("처치된 적의 난이도 설정 필요");

        float experience = definition.Exp;

        progression.TryAddExperience(experience);
    }

    /// <summary>
    /// 경험치를 지급할 런 성장 객체 연결
    /// </summary>
    /// <param name="progression">이번 런의 성장 객체</param>
    public void Bind(RunLevelProgression progression)
    {
        ValidateSource();

        if(_progression != null && _progression != progression && _progression.IsActive)
            throw new InvalidOperationException($"{name}: 기존 활성 런 종료 후 다른 런 연결 필요");

        Unsubscribe();
        _progression = progression;

        if (isActiveAndEnabled)
            Subscribe();
    }

    public void UnBind()
    {
        Unsubscribe();
        _progression = null;
    }
}
