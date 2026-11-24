using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 확인된 적 처치의 체력 포션 확률 드롭과 노드 전환 시 잔여 포션 정리
/// </summary>
public class EnemyHealthPotionDropReceiver : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] EnemySpawnManager _enemySpawnManager;
    [SerializeField] SessionProgressManager _world;

    [Header("----- 드롭 설정 -----")]
    [SerializeField] HealthPotionDefinition _definition;
    [SerializeField] HealthPotionPickup _pickupPrefab;

    readonly List<HealthPotionPickup> _drops = new List<HealthPotionPickup>();

    CombatTarget _owner;
    bool _subscribed;

    public bool IsBound => _owner != null;
    public bool IsListening => _subscribed;

    void Awake()
    {
        ValidateReferences();
    }

    void OnEnable()
    {
        if (_owner != null)
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

    void ValidateReferences()
    {
        if (_enemySpawnManager == null || _world == null || _definition == null || _pickupPrefab == null)
            throw new MissingReferenceException($"{name}: EnemySpawnManager·SessionProgressManager·포션 정의·포션 프리팹 연결 필요");
    }

    void Subscribe()
    {
        if (_subscribed)
            return;

        ValidateReferences();

        _enemySpawnManager.OnEnemyDied += HandleEnemyDied;
        _world.OnNodeEntered += HandleNodeEntered;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        // 이미 시작된 이벤트 호출에서도 추가 드롭을 차단하도록 선반영
        _subscribed = false;

        if (_enemySpawnManager != null)
            _enemySpawnManager.OnEnemyDied -= HandleEnemyDied;

        if (_world != null)
            _world.OnNodeEntered -= HandleNodeEntered;
    }

    void ClearDrops()
    {
        foreach (HealthPotionPickup drop in _drops)
        {
            // 획득으로 이미 제거된 포션 제외
            if (drop != null)
                Destroy(drop.gameObject);
        }

        _drops.Clear();
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (!_subscribed || !_owner.IsAlive)
            return;

        float dropChance = _definition.DropChance;

        if (dropChance <= 0f || UnityEngine.Random.value > dropChance)
            return;

        HealthPotionPickup drop = Instantiate(_pickupPrefab, enemy.transform.position, Quaternion.identity, transform);

        drop.Init(_definition, _owner);
        _drops.Add(drop);
    }

    void HandleNodeEntered(EnemyWorldType worldType, int nodeId)
    {
        // 이전 노드 좌표에 남은 포션 제거
        ClearDrops();
    }

    /// <summary>
    /// 포션을 획득할 플레이어 전투 대상 연결
    /// </summary>
    /// <param name="owner">이번 런의 플레이어 전투 대상</param>
    public void Bind(CombatTarget owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        ValidateReferences();

        if (_owner != null && _owner != owner)
            throw new InvalidOperationException($"{name}: 기존 연결 해제 후 다른 플레이어 연결 필요");

        _owner = owner;

        if (isActiveAndEnabled)
            Subscribe();
    }

    /// <summary>
    /// 드롭 수신 중단과 잔여 포션 제거 및 플레이어 연결 해제
    /// </summary>
    public void UnBind()
    {
        Unsubscribe();
        ClearDrops();
        _owner = null;
    }
}
