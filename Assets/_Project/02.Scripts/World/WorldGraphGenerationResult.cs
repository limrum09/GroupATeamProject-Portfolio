using System;
using System.Collections.Generic;

/// <summary>
/// 런타임 노드 구성에 전달할 읽기 전용 그래프 생성 결과
/// </summary>
public class WorldGraphGenerationResult
{
    readonly IReadOnlyList<int>[] _connections;

    public IReadOnlyList<WorldGraphNodeData> Nodes { get; }
    public IReadOnlyList<int> MainRouteNodeIds { get; }
    public int StartNodeId { get; }
    public int BossNodeId { get; }

    /// <summary>
    /// 생성 결과의 노드와 연결 및 기본 경로 복사
    /// </summary>
    /// <param name="nodes">ID 순서의 노드 데이터</param>
    /// <param name="connections">노드별 양방향 연결</param>
    /// <param name="mainRouteNodeIds">시작부터 보스까지의 기본 경로</param>
    /// <param name="startNodeId">시작 노드 ID</param>
    /// <param name="bossNodeId">보스 노드 ID</param>
    public WorldGraphGenerationResult(List<WorldGraphNodeData> nodes,
        List<List<int>> connections, List<int> mainRouteNodeIds, int startNodeId, int bossNodeId)
    {
        Nodes = new List<WorldGraphNodeData>(nodes).AsReadOnly();
        MainRouteNodeIds = new List<int>(mainRouteNodeIds).AsReadOnly();
        StartNodeId = startNodeId;
        BossNodeId = bossNodeId;
        _connections = new IReadOnlyList<int>[connections.Count];
        for (int id = 0; id < connections.Count; id++)
            _connections[id] = new List<int>(connections[id]).AsReadOnly();
    }

    /// <summary>
    /// 생성된 노드의 직접 연결 조회
    /// </summary>
    /// <param name="nodeId">조회할 노드 ID</param>
    /// <returns>수정 불가능한 연결 목록</returns>
    public IReadOnlyList<int> GetConnections(int nodeId)
    {
        if (nodeId < 0 || nodeId >= _connections.Length)
            throw new ArgumentOutOfRangeException(nameof(nodeId));

        return _connections[nodeId];
    }
}
