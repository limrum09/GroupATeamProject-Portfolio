using System;
using UnityEngine;

/// <summary>
/// 현재 노드의 구역 진입과 전체 구역 완료 판정
/// </summary>
public class NodeProgress : MonoBehaviour
{
    const float ArrivalSpawnDelay = 2f;

    [Header("----- 구역 진행 -----")]
    [SerializeField] WaveProgress _waveProgress;

    MapBindings _map;
    bool[] _cleared;
    int _activeArea = -1;
    bool _subscribed;
    bool _hasFailure;
    int _failureNotificationVersion;
    bool _waitingForArrivalDissolve;
    double _spawnAllowedAt;

    public event Action OnNodeCleared;
    public event Action OnProgressFailed;
    public bool IsNodeCleared { get; private set; }
    public bool IsBusy => _activeArea >= 0 || _hasFailure;
    public WaveProgress Waves => _waveProgress;

    void Update()
    {
        if (_waitingForArrivalDissolve || _map == null || IsNodeCleared || IsBusy ||
            Time.timeAsDouble < _spawnAllowedAt)
            return;

        CombatTarget player = _waveProgress.CombatArea.SpawnManager.Spawner.PlayerTarget;
        if (player == null || !player.IsAlive)
            return;

        for (int i = 0; i < _cleared.Length; i++)
        {
            if (_cleared[i])
                continue;

            SphereCollider entry = _map.CombatAreas[i].EntryArea;
            if (!IsInsideEntry(entry, player.Position))
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

    static bool IsInsideEntry(SphereCollider entry, Vector3 position)
    {
        Vector3 offset = entry.transform.InverseTransformPoint(position) - entry.center;
        return offset.sqrMagnitude <= entry.radius * entry.radius;
    }

    void HandleAreaCleared()
    {
        _cleared[_activeArea] = true;
        SetProgressBlockerActive(_activeArea, false);
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

    void ValidateProgressBlockers()
    {
        for (int i = 0; i < _map.CombatAreas.Count; i++)
        {
            MapBindings.CombatAreaBinding area = _map.GetCombatArea(i);
            GameObject blocker = area.ProgressBlocker;

            // 차단판이 없는 구역 허용
            if (blocker == null) continue;

            Transform blockerRoot = blocker.transform;
            if (blockerRoot == area.Root ||
                !blockerRoot.IsChildOf(area.Root) ||
                blocker.scene != _map.gameObject.scene)
            {
                throw new InvalidOperationException(
                    $"{_map.name}: 해당 구역 하위의 차단판 연결 필요");
            }

            if (area.EntryArea != null &&
                area.EntryArea.transform.IsChildOf(blockerRoot))
            {
                throw new InvalidOperationException(
                    $"{_map.name}: 진입 영역과 차단판의 계층 분리 필요");
            }

            if (area.SpawnAreas == null)
                throw new InvalidOperationException($"{_map.name}: 소환 범위 목록 구성 필요");

            foreach (SphereCollider spawnArea in area.SpawnAreas)
            {
                if (spawnArea != null &&
                    spawnArea.transform.IsChildOf(blockerRoot))
                {
                    throw new InvalidOperationException(
                        $"{_map.name}: 소환 범위와 차단판의 계층 분리 필요");
                }
            }
        }
    }

    void SetProgressBlockerActive(int areaIndex, bool active)
    {
        GameObject blocker = _map.CombatAreas[areaIndex].ProgressBlocker;
        if (blocker == null) return;

        blocker.SetActive(active);
    }

    /// <summary>
    /// 클리어 기록을 보존한 노드 진입과 구역 설정 및 차단판 상태 적용
    /// </summary>
    /// <param name="node">진입을 확정한 노드</param>
    /// <param name="alreadyCleared">월드에 기록된 클리어 여부</param>
    /// <param name="arrivalPosition">워프 배치 확정 위치, 일반 진입이면 생략</param>
    public void BeginNode(Node node, bool alreadyCleared, Vector3? arrivalPosition = null)
    {
        Stop();
        if (_waveProgress == null || _waveProgress.CombatArea == null ||
            node == null || node.Map == null)
            throw new MissingReferenceException("노드 맵과 웨이브 진행 연결 필요");

        _map = node.Map;
        IsNodeCleared = alreadyCleared;
        if (node.Type == MapBindings.MapType.Combat ||
            node.Type == MapBindings.MapType.Boss)
        {
            if (_map.CombatAreas == null || _map.CombatAreas.Count == 0)
                throw new InvalidOperationException($"{_map.name}: 전투 구역 연결 필요");

            ValidateProgressBlockers();
            for (int i = 0; i < _map.CombatAreas.Count; i++)
                SetProgressBlockerActive(i, !alreadyCleared);
        }

        if (alreadyCleared)
            return;

        if (node.Type == MapBindings.MapType.Start || node.Type == MapBindings.MapType.Shop)
        {
            IsNodeCleared = true;
            OnNodeCleared?.Invoke();
            return;
        }

        _waitingForArrivalDissolve = arrivalPosition.HasValue;
        _cleared = new bool[_map.CombatAreas.Count];
        for (int i = 0; i < _cleared.Length; i++)
        {
            MapBindings.CombatAreaBinding area = _map.GetCombatArea(i);
            if (area.EntryArea == null || area.EntryArea.enabled || area.EntryArea.radius <= 0f ||
                float.IsNaN(area.EntryArea.radius) || float.IsInfinity(area.EntryArea.radius) ||
                !area.EntryArea.transform.IsChildOf(area.Root))
                throw new InvalidOperationException($"{_map.name}: 구역 하위의 비활성 진입 SphereCollider 필요");

            // 진입 영역 안에 배치된 경우 디졸브 완료 이후 추가 대기 예약
            if (arrivalPosition.HasValue && IsInsideEntry(area.EntryArea, arrivalPosition.Value))
                _spawnAllowedAt = double.PositiveInfinity;
        }

        _waveProgress.OnAreaCleared += HandleAreaCleared;
        _waveProgress.OnSpawnFailed += HandleSpawnFailed;
        _subscribed = true;
    }

    /// <summary>
    /// 디졸브 대기 해제 및 직접 진입한 구역의 추가 스폰 대기 시작
    /// </summary>
    public void CompleteArrivalDissolve()
    {
        if (!_waitingForArrivalDissolve) return;

        _waitingForArrivalDissolve = false;
        if (double.IsPositiveInfinity(_spawnAllowedAt))
            _spawnAllowedAt = Time.timeAsDouble + ArrivalSpawnDelay;
    }

    /// <summary>
    /// 디졸브 완료 이후 접수된 지면 클릭으로 추가 스폰 대기 해제
    /// </summary>
    public void AcceptArrivalGroundClick()
    {
        if (!_waitingForArrivalDissolve)
            _spawnAllowedAt = 0d;
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
        _waitingForArrivalDissolve = false;
        _spawnAllowedAt = 0d;
        IsNodeCleared = false;
    }
}
