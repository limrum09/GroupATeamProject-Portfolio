using System;
using UnityEngine;

/// <summary>
/// 현재 노드의 구역 진입과 전체 구역 완료 판정
/// </summary>
public class NodeProgress : MonoBehaviour
{
    [Header("----- 구역 진행 -----")]
    [SerializeField] WaveProgress _waveProgress;

    MapBindings _map;
    bool[] _cleared;
    int _activeArea = -1;
    bool _subscribed;
    bool _hasFailure;
    int _failureNotificationVersion;

    public event Action OnNodeCleared;
    public event Action OnProgressFailed;
    public bool IsNodeCleared { get; private set; }
    public bool IsBusy => _activeArea >= 0 || _hasFailure;
    public WaveProgress Waves => _waveProgress;

    void Update()
    {
        if (_map == null || IsNodeCleared || IsBusy)
            return;

        CombatTarget player = _waveProgress.CombatArea.SpawnManager.Spawner.PlayerTarget;
        if (player == null || !player.IsAlive)
            return;

        for (int i = 0; i < _cleared.Length; i++)
        {
            if (_cleared[i])
                continue;

            SphereCollider entry = _map.CombatAreas[i].EntryArea;
            Vector3 local = entry.transform.InverseTransformPoint(player.Position) - entry.center;
            if (local.sqrMagnitude > entry.radius * entry.radius)
                continue;

            _activeArea = i;
            int failureVersion = _failureNotificationVersion;
            try
            {
                _waveProgress.BeginArea(_map, i);
            }
            catch
            {
                // 하위 소환 실패로 이미 통지했다면 동일 예외의 중복 통지 생략
                if (_failureNotificationVersion == failureVersion)
                {
                    _hasFailure = true;
                    HandleSpawnFailed();
                }
                throw;
            }
            break;
        }
    }

    void OnDisable()
    {
        Stop();
    }

    void HandleAreaCleared()
    {
        _cleared[_activeArea] = true;
        _activeArea = -1;
        _hasFailure = false;
        foreach (bool cleared in _cleared)
        {
            if (!cleared)
                return;
        }

        IsNodeCleared = true;
        OnNodeCleared?.Invoke();
    }

    void HandleSpawnFailed()
    {
        // Stop과 재시도로 상태가 바뀌어도 현재 호출의 통지 이력 유지
        unchecked
        {
            _failureNotificationVersion++;
        }
        OnProgressFailed?.Invoke();
    }

    /// <summary>
    /// 클리어 기록을 보존한 노드 진입과 구역 설정 검증
    /// </summary>
    /// <param name="node">진입을 확정한 노드</param>
    /// <param name="alreadyCleared">월드에 기록된 클리어 여부</param>
    public void BeginNode(Node node, bool alreadyCleared)
    {
        Stop();
        if (_waveProgress == null || _waveProgress.CombatArea == null ||
            node == null || node.Map == null)
            throw new MissingReferenceException("노드 맵과 웨이브 진행 연결 필요");

        _map = node.Map;
        IsNodeCleared = alreadyCleared;
        if (alreadyCleared)
            return;

        if (node.Type == MapBindings.MapType.Start || node.Type == MapBindings.MapType.Shop)
        {
            IsNodeCleared = true;
            OnNodeCleared?.Invoke();
            return;
        }

        if (_map.CombatAreas == null || _map.CombatAreas.Count == 0)
            throw new InvalidOperationException($"{_map.name}: 전투 구역 연결 필요");
        _cleared = new bool[_map.CombatAreas.Count];
        for (int i = 0; i < _cleared.Length; i++)
        {
            MapBindings.CombatAreaBinding area = _map.GetCombatArea(i);
            if (area.EntryArea == null || area.EntryArea.enabled || area.EntryArea.radius <= 0f ||
                float.IsNaN(area.EntryArea.radius) || float.IsInfinity(area.EntryArea.radius) ||
                !area.EntryArea.transform.IsChildOf(area.Root))
                throw new InvalidOperationException($"{_map.name}: 구역 하위의 비활성 진입 SphereCollider 필요");
        }

        _waveProgress.OnAreaCleared += HandleAreaCleared;
        _waveProgress.OnSpawnFailed += HandleSpawnFailed;
        _subscribed = true;
    }

    /// <summary>
    /// 월드 상태를 변경하지 않는 현재 노드 진행 중단
    /// </summary>
    public void Stop()
    {
        if (_subscribed && _waveProgress != null)
        {
            _waveProgress.OnAreaCleared -= HandleAreaCleared;
            _waveProgress.OnSpawnFailed -= HandleSpawnFailed;
        }
        _subscribed = false;
        if (_waveProgress != null)
            _waveProgress.Stop();
        _map = null;
        _cleared = null;
        _activeArea = -1;
        _hasFailure = false;
        IsNodeCleared = false;
    }
}
