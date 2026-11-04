using System;
using UnityEngine;

/// <summary>
/// 생성된 노드의 식별 정보와 할당된 맵 참조
/// </summary>
public class Node : MonoBehaviour
{
    [SerializeField] MapBindings _mapBindings;

    public int Id { get; private set; }
    public int Rank { get; private set; }
    public Vector2Int GridPosition { get; private set; }
    public Vector2 LayoutPosition { get; private set; }
    public MapBindings.MapType Type { get; private set; }
    public MapBindings Map => _mapBindings;

    /// <summary>
    /// 그래프 생성 시 노드의 고정 정보 구성
    /// </summary>
    /// <param name="id">월드 내 고유 식별자</param>
    /// <param name="rank">시작점에서의 최소 이동 횟수</param>
    /// <param name="type">할당할 맵 용도</param>
    /// <param name="gridPosition">삼각 격자상의 논리 좌표</param>
    /// <param name="layoutPosition">위치 보정이 확정된 단위 표시 좌표</param>
    public void Initialize(int id, int rank, MapBindings.MapType type,
        Vector2Int gridPosition, Vector2 layoutPosition)
    {
        if (id < 0 || rank < 0)
            throw new ArgumentOutOfRangeException(nameof(id), "0 이상의 노드 ID와 Rank 필요");

        Id = id;
        Rank = rank;
        Type = type;
        GridPosition = gridPosition;
        LayoutPosition = layoutPosition;
    }

    /// <summary>
    /// 용도가 일치하는 실제 맵 연결
    /// </summary>
    /// <param name="map">해당 노드에 할당한 맵</param>
    public void AssignMap(MapBindings map)
    {
        if (map != null && map.Type != Type)
            throw new InvalidOperationException($"{name}: 노드와 맵의 용도 일치 필요");

        _mapBindings = map;
    }
}
