using System;
using UnityEngine;

/// <summary>
/// 삼각 격자의 이웃 관계와 단위 평면 좌표
/// </summary>
public static class TriangularGrid
{
    static readonly Vector2Int[] _directions =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 1),
        new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(1, -1)
    };

    public static int NeighborCount => _directions.Length;

    /// <summary>
    /// 지정 방향의 이웃 좌표 조회
    /// </summary>
    /// <param name="coordinate">기준 좌표</param>
    /// <param name="direction">0부터 5까지의 방향</param>
    /// <returns>해당 방향의 이웃 좌표</returns>
    public static Vector2Int GetNeighbor(Vector2Int coordinate, int direction)
    {
        if (direction < 0 || direction >= _directions.Length)
            throw new ArgumentOutOfRangeException(nameof(direction));

        return coordinate + _directions[direction];
    }

    /// <summary>
    /// 두 격자 좌표의 인접 여부 확인
    /// </summary>
    /// <param name="first">첫 번째 좌표</param>
    /// <param name="second">두 번째 좌표</param>
    /// <returns>한 변으로 연결 가능한 이웃 여부</returns>
    public static bool AreNeighbors(Vector2Int first, Vector2Int second)
    {
        Vector2Int delta = second - first;
        foreach (Vector2Int direction in _directions)
        {
            if (delta == direction)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 격자 좌표의 단위 평면 위치 변환
    /// </summary>
    /// <param name="coordinate">변환할 격자 좌표</param>
    /// <returns>단위 변 길이의 정삼각형 배열 위치</returns>
    public static Vector2 ToPosition(Vector2Int coordinate)
    {
        return new Vector2(coordinate.x + coordinate.y * 0.5f,
            coordinate.y * Mathf.Sqrt(3f) * 0.5f);
    }
}
