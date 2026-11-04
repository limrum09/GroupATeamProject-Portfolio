using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성된 월드 그래프의 런타임 노드 생명주기와 연결 조회
/// </summary>
public class WorldGraphManager : MonoBehaviour
{
    [Header("----- 생성 설정 -----")]
    [SerializeField] WorldGraphGenerationSettings _generationSettings = new WorldGraphGenerationSettings();

    readonly List<Node> _nodes = new List<Node>();
    readonly List<List<int>> _connections = new List<List<int>>();
    IReadOnlyList<Node> _readOnlyNodes;

    public IReadOnlyList<Node> Nodes => _readOnlyNodes ?? (_readOnlyNodes = _nodes.AsReadOnly());
    public int StartNodeId { get; private set; } = -1;
    public int BossNodeId { get; private set; } = -1;
    public bool IsGenerated => _nodes.Count > 0;

    void OnValidate()
    {
        if (_generationSettings == null)
            throw new MissingReferenceException($"{name}: 그래프 생성 설정 필요");

        _generationSettings.Validate();
    }

    void CreateNode(WorldGraphNodeData data)
    {
        GameObject instance = new GameObject($"Node_{_nodes.Count}");
        instance.transform.SetParent(transform, false);
        Node node = instance.AddComponent<Node>();
        node.Initialize(_nodes.Count, data.Rank, data.Type, data.GridPosition, data.LayoutPosition);
        _nodes.Add(node);
        _connections.Add(new List<int>());
    }

    /// <summary>
    /// 런 시드에서 파생한 월드 그래프의 런타임 구성
    /// </summary>
    /// <param name="worldType">생성할 월드 구분</param>
    /// <param name="runSeed">현재 런의 기준 시드</param>
    public void GenerateWorld(EnemyWorldType worldType, int runSeed)
    {
        if (IsGenerated)
            throw new InvalidOperationException("기존 그래프 해제 후 생성 필요");
        if ((int)worldType < 0 || worldType >= EnemyWorldType.Count)
            throw new ArgumentOutOfRangeException(nameof(worldType));

        if (_generationSettings == null)
            throw new MissingReferenceException($"{name}: 그래프 생성 설정 필요");

        int worldSeed = unchecked(runSeed + (int)worldType * 104729);
        WorldGraphGenerator generator = new WorldGraphGenerator();
        WorldGraphGenerationResult result = generator.Generate(_generationSettings, worldSeed);
        try
        {
            StartNodeId = result.StartNodeId;
            BossNodeId = result.BossNodeId;
            foreach (WorldGraphNodeData data in result.Nodes)
                CreateNode(data);

            for (int id = 0; id < result.Nodes.Count; id++)
            {
                foreach (int next in result.GetConnections(id))
                    _connections[id].Add(next);
            }
        }
        catch
        {
            Clear();
            throw;
        }
    }

    /// <summary>
    /// 유효 ID의 노드 조회
    /// </summary>
    /// <param name="id">조회할 노드 ID</param>
    /// <returns>생성된 노드</returns>
    public Node GetNode(int id)
    {
        if (id < 0 || id >= _nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(id));

        return _nodes[id];
    }

    /// <summary>
    /// 직접 연결된 노드 ID 조회
    /// </summary>
    /// <param name="id">기준 노드 ID</param>
    /// <returns>수정 불가능한 연결 목록</returns>
    public IReadOnlyList<int> GetConnections(int id)
    {
        GetNode(id);
        return _connections[id].AsReadOnly();
    }

    /// <summary>
    /// 두 노드의 실제 연결 여부 조회
    /// </summary>
    /// <param name="first">출발 노드 ID</param>
    /// <param name="second">목적 노드 ID</param>
    /// <returns>직접 연결 여부</returns>
    public bool AreConnected(int first, int second)
    {
        return first >= 0 && first < _nodes.Count && _connections[first].Contains(second);
    }

    /// <summary>
    /// 생성한 노드와 연결 해제
    /// </summary>
    public void Clear()
    {
        foreach (Node node in _nodes)
        {
            if (node != null)
            {
                node.gameObject.SetActive(false);
                Destroy(node.gameObject);
            }
        }

        _nodes.Clear();
        _connections.Clear();
        StartNodeId = -1;
        BossNodeId = -1;
    }
}
