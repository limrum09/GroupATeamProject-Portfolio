using UnityEngine;

/// <summary>
/// 그래프 생성이 확정된 노드의 불변 데이터
/// </summary>
public class WorldGraphNodeData
{
    public Vector2Int GridPosition { get; }
    public Vector2 LayoutPosition { get; }
    public int Rank { get; }
    public MapBindings.MapType Type { get; }

    /// <summary>
    /// 생성 완료된 노드 정보 구성
    /// </summary>
    /// <param name="gridPosition">논리 격자 좌표</param>
    /// <param name="layoutPosition">보정이 완료된 단위 표시 좌표</param>
    /// <param name="rank">시작점에서의 최단 이동 횟수</param>
    /// <param name="type">실제 맵 할당 용도</param>
    public WorldGraphNodeData(Vector2Int gridPosition, Vector2 layoutPosition,
        int rank, MapBindings.MapType type)
    {
        GridPosition = gridPosition;
        LayoutPosition = layoutPosition;
        Rank = rank;
        Type = type;
    }
}
