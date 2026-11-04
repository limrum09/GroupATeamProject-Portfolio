using UnityEngine;

/// <summary>확인된 적 처치의 세션 골드 지급 연결</summary>
public sealed class EnemyGoldRewardReceiver : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] EnemySpawnManager _enemySpawnManager;
    [SerializeField] SessionEconomyManager _economy;

    bool _subscribed;

    public bool IsListening => _subscribed;

    void Awake()
    {
        ValidateReferences();
    }

    void OnEnable()
    {
        try
        {
            ValidateReferences();

            if (_subscribed)
                return;

            _enemySpawnManager.OnEnemyDied += HandleEnemyDied;
            _subscribed = true;
        }
        catch
        {
            enabled = false;
            throw;
        }
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void ValidateReferences()
    {
        if (_enemySpawnManager == null || _economy == null)
            throw new MissingReferenceException($"{name}: EnemySpawnManager와 SessionEconomyManager 연결 필요");
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        // 이미 시작된 이벤트 호출에서도 추가 지급을 차단하도록 선반영
        _subscribed = false;

        if (_enemySpawnManager != null)
            _enemySpawnManager.OnEnemyDied -= HandleEnemyDied;
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (!_subscribed || !isActiveAndEnabled)
            return;

        EnemyDifficultyDefinition definition = enemy.DifficultyDefinition;

        if (definition == null)
            throw new MissingReferenceException("처치된 적의 난이도 설정 필요");

        _economy.AddGold(definition.SessionGold, GoldGainSource.EnemyReward);
    }
}
