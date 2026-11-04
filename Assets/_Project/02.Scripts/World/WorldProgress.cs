using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 월드의 위치와 노드별 방문 및 클리어 기록
/// </summary>
public class WorldProgress : MonoBehaviour
{
    readonly HashSet<int> _visited = new HashSet<int>();
    readonly HashSet<int> _cleared = new HashSet<int>();
    WorldGraphManager _graph;

    public int CurrentNodeId { get; private set; } = -1;
    public bool HasCurrentNode => CurrentNodeId >= 0;

    /// <summary>
    /// 새 그래프의 진행 기록 초기화
    /// </summary>
    /// <param name="graph">생성을 완료한 그래프</param>
    public void Initialize(WorldGraphManager graph)
    {
        if (graph == null || !graph.IsGenerated)
            throw new InvalidOperationException("생성이 완료된 그래프 필요");

        Clear();
        _graph = graph;
    }

    /// <summary>
    /// 방문 기록과 현재 위치의 연결에 따른 노드 확인 여부
    /// </summary>
    /// <param name="nodeId">조회할 노드 ID</param>
    /// <returns>현재 정보가 확인된 노드 여부</returns>
    public bool IsNodeKnown(int nodeId)
    {
        if (_graph == null)
            throw new InvalidOperationException("월드 진행 초기화 필요");

        _graph.GetNode(nodeId);
        if (_visited.Contains(nodeId))
            return true;

        if (!HasCurrentNode)
            return nodeId == _graph.StartNodeId;

        return nodeId == CurrentNodeId || _graph.AreConnected(CurrentNodeId, nodeId);
    }

    /// <summary>
    /// 첫 시작점 또는 현재 위치에 직접 연결된 노드 확인
    /// </summary>
    /// <param name="nodeId">이동할 노드 ID</param>
    /// <returns>위치 관계에 따른 이동 가능 여부</returns>
    public bool CanEnterNode(int nodeId)
    {
        return _graph != null && (!HasCurrentNode
            ? nodeId == _graph.StartNodeId : _graph.AreConnected(CurrentNodeId, nodeId));
    }

    /// <summary>
    /// 외부 플레이어 배치 성공 이후 위치와 방문 기록 확정
    /// </summary>
    /// <param name="nodeId">배치를 완료한 노드 ID</param>
    public void CommitNodeEntry(int nodeId)
    {
        if (!CanEnterNode(nodeId))
            throw new InvalidOperationException("진입 가능한 노드 배치 완료 필요");

        CurrentNodeId = nodeId;
        _visited.Add(nodeId);
    }

    /// <summary>
    /// 현재 노드의 확정된 완료 기록
    /// </summary>
    /// <param name="nodeId">완료한 노드 ID</param>
    /// <returns>첫 완료 기록 여부</returns>
    public bool MarkNodeCleared(int nodeId)
    {
        if (!HasCurrentNode || nodeId != CurrentNodeId)
            throw new InvalidOperationException("현재 노드의 완료 결과 필요");

        return _cleared.Add(nodeId);
    }

    /// <summary>
    /// 방문 여부 조회
    /// </summary>
    /// <param name="nodeId">조회할 노드 ID</param>
    /// <returns>배치를 완료한 방문 여부</returns>
    public bool IsVisited(int nodeId)
    {
        return _visited.Contains(nodeId);
    }

    /// <summary>
    /// 클리어 여부 조회
    /// </summary>
    /// <param name="nodeId">조회할 노드 ID</param>
    /// <returns>클리어 기록 여부</returns>
    public bool IsCleared(int nodeId)
    {
        return _cleared.Contains(nodeId);
    }

    /// <summary>
    /// 월드 진행 참조와 기록 해제
    /// </summary>
    public void Clear()
    {
        CurrentNodeId = -1;
        _visited.Clear();
        _cleared.Clear();
        _graph = null;
    }
}
