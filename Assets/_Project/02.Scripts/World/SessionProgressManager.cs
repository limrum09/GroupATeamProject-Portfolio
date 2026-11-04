using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 월드 준비와 외부 배치 요청 및 노드 전환 순서 조율
/// </summary>
public class SessionProgressManager : MonoBehaviour
{
    [Header("----- 월드 연결 -----")]
    [SerializeField] WorldGraphManager _graph;
    [SerializeField] NodeMapAllocator _mapAllocator;
    [FormerlySerializedAs("_stageProgressManager")]
    [SerializeField] WorldProgress _worldProgress;
    [SerializeField] NodeProgress _nodeProgressManager;
    [SerializeField] EnemySpawnManager _enemySpawnManager;

    [Header("----- 월드 진행 -----")]
    [SerializeField] EnemyWorldType _initialWorldType = EnemyWorldType.World1;

    Node _pendingNode;
    readonly HashSet<EnemyWorldType> _clearedWorlds = new HashSet<EnemyWorldType>();
    bool _runClearRequested;
    long _placementSequence;
    long _pendingPlacementId;
    bool _prepared;
    bool _faulted;
    bool _placementFailed;
    bool _releasingWorld;
    bool _changingWorld;
    EnemyWorldType _currentWorldType;
    int _runSeed;

    /// <summary>
    /// 외부 이동 담당자에게 요청 ID와 목적 Pose 전달
    /// </summary>
    public event Action<long, Pose> OnPlacementRequested;
    public event Action OnWorldPrepared;
    public event Action<EnemyWorldType, int> OnNodeEntered;
    public event Action OnWorldReleased;
    public event Action OnWorldCompleted;
    public event Action OnRunClearRequested;
    public event Action OnStateChanged;
    public event Action OnProgressFailed;

    public WorldGraphManager Graph => _graph;
    public WorldProgress Progress => _worldProgress;
    public EnemyWorldType CurrentWorldType => _currentWorldType;
    public int ClearedWorldCount => _clearedWorlds.Count;
    public bool IsRunClearReady => ClearedWorldCount == (int)EnemyWorldType.Count;
    public bool IsTransitioning => _pendingNode != null;
    public bool IsReady => _prepared && !_faulted && _worldProgress.HasCurrentNode;
    public bool HasFailure => _faulted || _placementFailed || (_nodeProgressManager.Waves != null &&
        _nodeProgressManager.Waves.HasSpawnFailure);

    void OnDisable()
    {
        ReleaseWorld();
    }

    void ValidateReferences()
    {
        if (_graph == null || _mapAllocator == null || _worldProgress == null ||
            _nodeProgressManager == null || _enemySpawnManager == null ||
            _nodeProgressManager.Waves == null || _nodeProgressManager.Waves.CombatArea == null)
            throw new MissingReferenceException($"{name}: WorldGraph와 Progress 및 소환 구성 연결 필요");

        if (_nodeProgressManager.Waves.CombatArea.SpawnManager != _enemySpawnManager)
            throw new InvalidOperationException($"{name}: 동일 EnemySpawnManager 연결 필요");
    }

    void HandleNodeCleared()
    {
        int nodeId = _worldProgress.CurrentNodeId;
        bool firstClear = _worldProgress.MarkNodeCleared(nodeId);

        if (firstClear)
        {
            Node node = _graph.GetNode(nodeId);

            QuestManager.Instance.ReportNodeCleared(node.Type, node.Rank);

            if(nodeId == _graph.BossNodeId)
            {
                _clearedWorlds.Add(_currentWorldType);
                OnWorldCompleted?.Invoke();
            }
        }

        OnStateChanged?.Invoke();
    }

    void HandleProgressFailed()
    {
        OnProgressFailed?.Invoke();
        OnStateChanged?.Invoke();
    }

    void RestorePreviousNode()
    {
        if (_worldProgress.HasCurrentNode)
        {
            Node previous = _graph.GetNode(_worldProgress.CurrentNodeId);
            _mapAllocator.Activate(previous);
            _nodeProgressManager.BeginNode(previous, _worldProgress.IsCleared(previous.Id));
        }
        else
        {
            _mapAllocator.DeactivateMaps();
        }
    }

    /// <summary>
    /// 보스 노드 완료와 생존 플레이어의 포탈 사용 조건 확인
    /// </summary>
    /// <returns>완료한 보스 포탈 사용 가능 여부</returns>
    bool CanUseCompletedBossPortal()
    {
        if (!IsReady || IsTransitioning || _changingWorld || _releasingWorld || HasFailure || _runClearRequested)
            return false;

        CombatTarget player = _enemySpawnManager.Spawner.PlayerTarget;
        int currentNodeId = _worldProgress.CurrentNodeId;
        return currentNodeId == _graph.BossNodeId && _worldProgress.IsCleared(currentNodeId) &&
            !_nodeProgressManager.IsBusy && _enemySpawnManager.EnemyCount == 0 &&
            player != null && player.IsAlive;
    }

    public CombatAreaProgress CombatArea => _nodeProgressManager.Waves.CombatArea;

    void ValidateRunDifficultyCoverage()
    {
        if ((int)_initialWorldType < 0 || _initialWorldType >= EnemyWorldType.Count)
            throw new InvalidOperationException("유효한 초기 월드 설정 필요");

        HashSet<string> encounterIds = new HashSet<string>(StringComparer.Ordinal);

        for (int index = (int)_initialWorldType; index < (int)EnemyWorldType.Count; index++)
        {
            EnemyWorldType worldType = (EnemyWorldType)index;
            foreach (MapBindings map in _mapAllocator.GetValidatedWorldCandidates(worldType))
            {
                foreach (CombatAreaSpawnData area in map.GetCombatAreaSpawnDatas())
                {
                    string encounterId = area.EncounterId;
                    if (string.IsNullOrWhiteSpace(encounterId) || !encounterIds.Add(encounterId))
                        throw new InvalidOperationException($"{map.name}: 유효하고 고유한 EncounterId 필요 ({encounterId})");

                    WaveSpawnData[] waves = area.WaveSpawnDatas;
                    if (waves == null || waves.Length == 0)
                        throw new InvalidOperationException($"{encounterId}: 웨이브 설정 필요");

                    for (int waveIndex = 0; waveIndex < waves.Length; waveIndex++)
                    {
                        WaveSpawnPlan plan = CombatArea.CreateSpawnPlan(encounterId, waveIndex, waves[waveIndex]);
                        _enemySpawnManager.Spawner.ValidateSpawnCandidates(worldType, plan.Counts);
                    }
                }
            }
        }
    }

    void PrepareWorldCore(EnemyWorldType worldType)
    {
        ValidateReferences();
        if (!isActiveAndEnabled || _prepared || _graph.IsGenerated || _releasingWorld)
            throw new InvalidOperationException("활성 상태에서 이전 월드 해제 후 준비 필요");
        if (OnPlacementRequested == null || OnPlacementRequested.GetInvocationList().Length != 1)
            throw new InvalidOperationException("배치 결과를 응답할 외부 이동 담당자 한 개 연결 필요");

        CombatTarget player = _enemySpawnManager.Spawner.PlayerTarget;
        if (player == null || !player.IsAlive || _enemySpawnManager.EnemyCount != 0)
            throw new InvalidOperationException("기존 런 담당자의 생존 타겟 연결과 적 정리 완료 필요");

        try
        {
            _graph.GenerateWorld(worldType, _runSeed);
            _mapAllocator.AllocateMaps(_graph.Nodes, worldType);
            _worldProgress.Initialize(_graph);
            _currentWorldType = worldType;
            _nodeProgressManager.OnNodeCleared += HandleNodeCleared;
            _nodeProgressManager.OnProgressFailed += HandleProgressFailed;
            _prepared = true;
            _faulted = false;
            TryEnterNode(_graph.StartNodeId);
        }
        catch
        {
            ReleaseWorld();
            throw;
        }
    }

    /// <summary>
    /// 새 런의 시드 생성과 초기 월드 준비
    /// </summary>
    public void PrepareWorld()
    {
        if (_changingWorld)
            throw new InvalidOperationException("다음 월드 전환 완료 후 준비 필요");
        if (_prepared || _graph.IsGenerated || _releasingWorld)
            throw new InvalidOperationException("이전 월드 해제 후 새 런 준비 필요");
        ValidateReferences();
        ValidateRunDifficultyCoverage();
        _runSeed = Guid.NewGuid().GetHashCode();
        _clearedWorlds.Clear();
        _runClearRequested = false;
        PrepareWorldCore(_initialWorldType);
    }

    /// <summary>
    /// 완료한 보스 노드에서 다음 월드 진입 가능 여부 확인
    /// </summary>
    /// <returns>다음 월드 요청 가능 여부</returns>
    public bool CanEnterNextWorld()
    {
        return CanUseCompletedBossPortal() && !IsRunClearReady &&
            (int)_currentWorldType + 1 < (int)EnemyWorldType.Count;
    }

    /// <summary>
    /// 전체 월드 완료 후 보스 포탈의 런 종료 요청 가능 여부 확인
    /// </summary>
    /// <returns>클리어 종료 요청 가능 여부</returns>
    public bool CanRequestRunClear()
    {
        return CanUseCompletedBossPortal() && IsRunClearReady;
    }

    /// <summary>
    /// 전체 월드 완료를 재검사한 뒤 최초 클리어 종료 요청 전달
    /// </summary>
    /// <returns>최초 요청 접수 여부</returns>
    public bool TryRequestRunClear()
    {
        if (!CanRequestRunClear()) return false;
        if (OnRunClearRequested == null)
            throw new InvalidOperationException("런 클리어 요청 담당자 연결 필요");

        _runClearRequested = true;
        OnRunClearRequested.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 세션 상태를 유지한 다음 월드 재구성과 시작 노드 배치 요청
    /// </summary>
    /// <returns>다음 월드 준비와 시작 노드 배치 완료 여부</returns>
    public bool TryEnterNextWorld()
    {
        if (!CanEnterNextWorld())
            return false;

        // 필수 후보가 없으면 기존 월드와 플레이어 위치를 유지
        EnemyWorldType nextWorld = (EnemyWorldType)((int)_currentWorldType + 1);
        _mapAllocator.ValidateWorldCandidates(nextWorld);
        _changingWorld = true;
        try
        {
            _enemySpawnManager.ClearSessionEnemies();
            ReleaseWorld();
            PrepareWorldCore(nextWorld);
            if (!IsReady || IsTransitioning || HasFailure)
                throw new InvalidOperationException($"{nextWorld} 시작 노드 배치 완료 필요");
            return true;
        }
        catch
        {
            // 이미 해제된 이전 월드의 노드로 복구하거나 완료로 취급하지 않음
            _faulted = true;
            _nodeProgressManager.Stop();
            HandleProgressFailed();
            throw;
        }
        finally
        {
            _changingWorld = false;
        }
    }

    /// <summary>
    /// 실제 연결과 현재 노드 완료 상태에 따른 이동 요청 가능 여부
    /// </summary>
    /// <param name="nodeId">목적 노드 ID</param>
    /// <returns>현재 이동 요청 가능 여부</returns>
    public bool CanEnterNode(int nodeId)
    {
        return _prepared && !_faulted && !_runClearRequested && !IsTransitioning &&
            _worldProgress.CanEnterNode(nodeId) &&
            (!_worldProgress.HasCurrentNode ||
             _worldProgress.IsCleared(_worldProgress.CurrentNodeId)) &&
            !_nodeProgressManager.IsBusy && _enemySpawnManager.EnemyCount == 0;
    }

    /// <summary>
    /// 맵 준비 후 외부 이동 담당자에게 배치 요청
    /// </summary>
    /// <param name="nodeId">목적 노드 ID</param>
    /// <returns>이동 요청 접수 여부</returns>
    public bool TryEnterNode(int nodeId)
    {
        if (!CanEnterNode(nodeId))
            return false;
        if (OnPlacementRequested == null || OnPlacementRequested.GetInvocationList().Length != 1)
            throw new InvalidOperationException("외부 이동 담당자 한 개 연결 필요");

        Node destination = _graph.GetNode(nodeId);
        if (destination.Map == null || destination.Map.PlayerEntry == null)
            throw new MissingReferenceException("목적지 맵과 PlayerEntry 연결 필요");

        _pendingNode = destination;
        _placementFailed = false;
        _pendingPlacementId = checked(++_placementSequence);
        long requestId = _pendingPlacementId;
        try
        {
            _nodeProgressManager.Stop();
            // 완료된 노드의 사망 연출 대기 적만 정리, 타겟 연결은 유지
            _enemySpawnManager.ClearSessionEnemies();
            _mapAllocator.Activate(destination);
            Transform entry = destination.Map.PlayerEntry;
            OnStateChanged?.Invoke();
            if (_pendingNode != null && _pendingPlacementId == requestId)
                OnPlacementRequested.Invoke(requestId, new Pose(entry.position, entry.rotation));
        }
        catch
        {
            if (_pendingNode != null && _pendingPlacementId == requestId)
            {
                _pendingNode = null;
                _faulted = true;
                _nodeProgressManager.Stop();
                HandleProgressFailed();
            }
            throw;
        }
        return true;
    }

    /// <summary>
    /// 외부 이동 담당자의 성공 결과에 따른 위치 확정
    /// </summary>
    /// <param name="requestId">배치 요청에서 전달한 ID</param>
    /// <param name="succeeded">플레이어 배치 성공 여부</param>
    /// <returns>현재 요청의 응답 접수 여부</returns>
    public bool CompletePlacement(long requestId, bool succeeded)
    {
        // 해제 이전 요청이나 이전 이동의 늦은 응답 거절
        if (!_prepared || _pendingNode == null || requestId != _pendingPlacementId)
            return false;

        Node destination = _pendingNode;
        _pendingNode = null;
        if (!succeeded)
        {
            _placementFailed = true;
            RestorePreviousNode();
            HandleProgressFailed();
            return true;
        }

        bool firstEntry = !_worldProgress.HasCurrentNode;
        try
        {
            _worldProgress.CommitNodeEntry(destination.Id);
            _nodeProgressManager.BeginNode(destination, _worldProgress.IsCleared(destination.Id));
        }
        catch
        {
            _faulted = true;
            _nodeProgressManager.Stop();
            HandleProgressFailed();
            throw;
        }

        if (!_prepared || _worldProgress.CurrentNodeId != destination.Id)
            return true;

        OnNodeEntered?.Invoke(_currentWorldType, destination.Id);

        if (firstEntry)
            OnWorldPrepared?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 배치 담당자와 타겟 및 런 상태를 소유하지 않는 World 전용 해제
    /// </summary>
    public void ReleaseWorld()
    {
        if (_releasingWorld) return;
        bool hadWorld = _prepared || (_graph != null && _graph.IsGenerated);
        _releasingWorld = true;
        try
        {
            _prepared = false;
            _pendingNode = null;
            _faulted = false;
            _placementFailed = false;
            if (_nodeProgressManager != null)
            {
                _nodeProgressManager.OnNodeCleared -= HandleNodeCleared;
                _nodeProgressManager.OnProgressFailed -= HandleProgressFailed;
                _nodeProgressManager.Stop();
            }
            if (_mapAllocator != null)
                _mapAllocator.ReleaseMaps();
            if (_worldProgress != null)
                _worldProgress.Clear();
            if (_graph != null)
                _graph.Clear();

            // 맵 복원과 진행 해제 이후 동일 월드의 종료를 한 번만 통지
            if (hadWorld)
                OnWorldReleased?.Invoke();
            OnStateChanged?.Invoke();
        }
        finally
        {
            _releasingWorld = false;
        }
    }
}
