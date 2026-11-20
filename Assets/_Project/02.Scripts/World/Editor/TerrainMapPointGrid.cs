using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 평면 위치 목록의 격자 기반 근접 검색
/// </summary>
public class TerrainMapPointGrid
{
    readonly Dictionary<Vector2Int, List<Vector2>> _cells = new Dictionary<Vector2Int, List<Vector2>>();
    readonly float _cellSize;

    public TerrainMapPointGrid(float cellSize)
    {
        _cellSize = Mathf.Max(0.01f, cellSize);
    }

    Vector2Int GetKey(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / _cellSize), Mathf.FloorToInt(position.y / _cellSize));
    }

    /// <summary>
    /// 위치 추가
    /// </summary>
    /// <param name="position">XZ 위치</param>
    public void Add(Vector2 position)
    {
        Vector2Int key = GetKey(position);
        if (!_cells.TryGetValue(key, out List<Vector2> cell))
        {
            cell = new List<Vector2>();
            _cells[key] = cell;
        }
        cell.Add(position);
    }

    /// <summary>
    /// 반경 안 위치 존재 여부
    /// </summary>
    /// <param name="position">기준 XZ 위치</param>
    /// <param name="radius">검사 반경, 0 이하면 검사 없음</param>
    /// <returns>반경 안 위치 존재 여부</returns>
    public bool AnyWithin(Vector2 position, float radius)
    {
        if (radius <= 0f) return false;

        Vector2Int center = GetKey(position);
        int reach = Mathf.CeilToInt(radius / _cellSize);
        float radiusSquared = radius * radius;
        for (int offsetZ = -reach; offsetZ <= reach; offsetZ++)
        {
            for (int offsetX = -reach; offsetX <= reach; offsetX++)
            {
                if (!_cells.TryGetValue(center + new Vector2Int(offsetX, offsetZ), out List<Vector2> cell)) continue;
                foreach (Vector2 other in cell)
                {
                    if ((other - position).sqrMagnitude < radiusSquared) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 같은 위치를 제외한 가장 가까운 위치까지 거리
    /// </summary>
    /// <param name="position">기준 XZ 위치</param>
    /// <param name="maxDistance">최대 검색 거리</param>
    /// <returns>거리, 없으면 float.MaxValue</returns>
    public float FindNearest(Vector2 position, float maxDistance)
    {
        Vector2Int center = GetKey(position);
        int maxRing = Mathf.CeilToInt(maxDistance / _cellSize);
        float best = float.MaxValue;
        for (int ring = 0; ring <= maxRing; ring++)
        {
            // 이번 고리의 점은 최소 (ring - 1)칸 떨어져 있어 더 가까운 점이 있으면 종료
            if (best <= (ring - 1) * _cellSize) break;

            for (int offsetZ = -ring; offsetZ <= ring; offsetZ++)
            {
                for (int offsetX = -ring; offsetX <= ring; offsetX++)
                {
                    if (Mathf.Abs(offsetX) != ring && Mathf.Abs(offsetZ) != ring) continue;
                    if (!_cells.TryGetValue(center + new Vector2Int(offsetX, offsetZ), out List<Vector2> cell)) continue;
                    foreach (Vector2 other in cell)
                    {
                        float distance = Vector2.Distance(other, position);
                        if (distance > 0f && distance < best) best = distance;
                    }
                }
            }
        }

        return best <= maxDistance ? best : float.MaxValue;
    }
}
