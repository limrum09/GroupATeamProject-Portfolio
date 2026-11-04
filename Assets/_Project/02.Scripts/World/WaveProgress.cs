using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 전투 구역의 시간 간격 소환과 웨이브별 생존 적 추적
/// </summary>
public class WaveProgress : MonoBehaviour
{
    [Header("----- 소환 연결 -----")]
    [SerializeField] CombatAreaProgress _combatAreaProgress;
    [Header("----- 웨이브 간격 -----")]
    [SerializeField, Min(0f)] float _nextWaveThreshold = 3f;

    readonly Dictionary<Enemy, int> _alive = new Dictionary<Enemy, int>();
    MapBindings _map;
    WaveSpawnData[] _waves;
    EnemySpawnManager _manager;
    int _areaIndex;
    int _nextWave;
    int _requestingIndex = -1;
    float _elapsed;
    bool _subscribed;

    public event Action OnAreaCleared;
    public event Action OnSpawnFailed;
    public bool IsRunning { get; private set; }
    public bool HasSpawnFailure { get; private set; }
    public CombatAreaProgress CombatArea => _combatAreaProgress;

    void Update()
    {
        if (!IsRunning || HasSpawnFailure || _requestingIndex >= 0 || _nextWave >= _waves.Length)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _nextWaveThreshold)
            RequestNextWave();
    }

    void OnDisable()
    {
        Stop();
    }

    void HandleSpawned(WaveSpawnData wave, IReadOnlyList<Enemy> enemies)
    {
        if (_requestingIndex < 0 || !ReferenceEquals(wave, _waves[_requestingIndex]))
            return;

        foreach (Enemy enemy in enemies)
            _alive.Add(enemy, _requestingIndex);
    }

    void HandleDied(Enemy enemy)
    {
        if (_alive.Remove(enemy))
            TryComplete();
    }

    void TryComplete()
    {
        // 소환 콜백 도중 구역 완료에 따른 재진입 방지
        if (!IsRunning || HasSpawnFailure || _requestingIndex >= 0 ||
            _nextWave != _waves.Length || _alive.Count != 0)
            return;

        Stop();
        OnAreaCleared?.Invoke();
    }

    void RequestNextWave()
    {
        bool failed = false;
        _requestingIndex = _nextWave;
        try
        {
            if (!_combatAreaProgress.TrySpawnWave(_map, _areaIndex, _nextWave))
            {
                HasSpawnFailure = true;
                failed = true;
            }
            else
            {
                _nextWave++;
                _elapsed = 0f;
            }
        }
        catch
        {
            // 하위 경로의 실패 회수와 함께 이번 요청의 추적 기록 제거
            List<Enemy> failedEnemies = new List<Enemy>();
            foreach (KeyValuePair<Enemy, int> entry in _alive)
            {
                if (entry.Value == _requestingIndex)
                    failedEnemies.Add(entry.Key);
            }
            foreach (Enemy enemy in failedEnemies)
                _alive.Remove(enemy);
            HasSpawnFailure = true;
            failed = true;
            throw;
        }
        finally
        {
            _requestingIndex = -1;
            // 실패 구독자의 재시도 전에 현재 소환 요청 상태 정리
            if (failed)
                OnSpawnFailed?.Invoke();
        }

        if (!failed)
            TryComplete();
    }

    /// <summary>
    /// 구역 소환 결과 구독 후 첫 웨이브 요청
    /// </summary>
    /// <param name="map">현재 맵</param>
    /// <param name="areaIndex">진입한 전투 구역 순번</param>
    public void BeginArea(MapBindings map, int areaIndex)
    {
        if (IsRunning)
            throw new InvalidOperationException("이전 구역 완료 또는 중단 필요");
        if (_combatAreaProgress == null || _combatAreaProgress.SpawnManager == null ||
            map == null || !isActiveAndEnabled || float.IsNaN(_nextWaveThreshold) ||
            float.IsInfinity(_nextWaveThreshold) || _nextWaveThreshold < 0f)
            throw new InvalidOperationException("활성 진행 컴포넌트와 유효한 소환 설정 필요");

        _waves = map.GetCombatArea(areaIndex).SpawnData.WaveSpawnDatas;
        if (_waves == null || _waves.Length == 0)
            throw new InvalidOperationException("전투 구역의 웨이브 설정 필요");
        _map = map;
        _areaIndex = areaIndex;
        _nextWave = 0;
        _elapsed = 0f;
        HasSpawnFailure = false;
        _alive.Clear();
        _manager = _combatAreaProgress.SpawnManager;
        _manager.OnWaveSpawned += HandleSpawned;
        _manager.OnEnemyDied += HandleDied;
        _subscribed = true;
        IsRunning = true;
        RequestNextWave();
    }

    /// <summary>
    /// 소환 실패 이후 동일 웨이브의 명시적 재요청
    /// </summary>
    /// <returns>재요청 성공 여부</returns>
    public bool RetryFailedWave()
    {
        if (!IsRunning || !HasSpawnFailure || _requestingIndex >= 0)
            return false;

        HasSpawnFailure = false;
        RequestNextWave();
        return !HasSpawnFailure;
    }

    /// <summary>
    /// 처치나 완료를 발행하지 않는 구역 추적 중단
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        if (_subscribed && _manager != null)
        {
            _manager.OnWaveSpawned -= HandleSpawned;
            _manager.OnEnemyDied -= HandleDied;
        }
        _subscribed = false;
        _manager = null;
        _map = null;
        _waves = null;
        _alive.Clear();
        HasSpawnFailure = false;
        _requestingIndex = -1;
    }
}
