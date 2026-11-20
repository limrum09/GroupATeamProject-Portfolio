using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이동 영역을 둘러싼 지형 모양
/// </summary>
public enum TerrainMapShape
{
    [InspectorName("감싼 형태")] Enclosed,      // 평지 외 전부 높은 곳
    [InspectorName("한쪽 절벽")] CliffSide,     // 한쪽 가장자리만 낮은 곳
    [InspectorName("섬")] Island                // 평지 외 전부 낮은 곳
}

/// <summary>
/// 한쪽 절벽 모양의 낮은 곳 방향
/// </summary>
public enum TerrainMapCliffSide
{
    [InspectorName("랜덤 (Seed)")] Random,
    [InspectorName("북 (+Z)")] North,
    [InspectorName("동 (+X)")] East,
    [InspectorName("남 (-Z)")] South,
    [InspectorName("서 (-X)")] West
}

/// <summary>
/// 생성 TerrainData 크기·해상도 기준
/// </summary>
public enum TerrainMapSizeSource
{
    [InspectorName("대상 Terrain 크기 유지")] Target,
    [InspectorName("레퍼런스 크기")] Reference
}

/// <summary>
/// 맵 생성 입력값
/// </summary>
[Serializable]
public class TerrainMapSettings
{
    public float PlayerHeight;                  // 플레이어 이동 평지의 월드 높이
    public float HeightTolerance = 0.5f;        // 이동 평지 판정 높이 허용치
    public TerrainMapSizeSource SizeSource = TerrainMapSizeSource.Target;
    public TerrainMapShape Shape = TerrainMapShape.Enclosed;
    public TerrainMapCliffSide CliffSide = TerrainMapCliffSide.Random;
    public int RoomCount = 2;                   // 넓은 공간 개수
    [Range(0f, 1f)] public float Irregularity = 0.6f;   // 공간·샛길 모양 불규칙도
    public float RoomDiameter = 20f;            // 넓은 공간 지름
    public float PathWidth = 6f;                // 샛길 폭
    public float EdgeMargin = 4f;               // 맵 가장자리와 평지 사이 최소 여백
    public float HighOffset = 4f;               // 이동 높이 기준 높은 곳 높이
    public float LowOffset = -4f;               // 이동 높이 기준 낮은 곳 높이
    public float HighBumpDeviation = 0.5f;      // 높은 곳 요철 편차
    public float LowBumpDeviation = 0.3f;       // 낮은 곳 요철 편차
    public List<TerrainMapPropSetting> Props = new List<TerrainMapPropSetting>();   // 배치물 종류별 심기 선택값
    public int Seed = 1;
}

/// <summary>
/// 넓은 공간을 경로 하나로 잇는 이동 영역과 지형 모양별 낮은 곳 배치 격자
/// </summary>
public class TerrainMapLayout
{
    const int MaxPlacementTries = 60;       // 공간 크기 단계별 배치 재시도 횟수
    const int MaxRoomAttempts = 400;        // 배치 1회당 공간 위치 시도 횟수
    const int MaxUntangleIterations = 50;
    const int SmoothIterations = 2;
    const int CorridorSegments = 12;        // 샛길 곡선 분할 수
    const float Clearance = 2f;             // 경로상 이웃이 아닌 공간·샛길 간 최소 간격 (샛길 폭 배율)
    const float ShrinkStep = 0.9f;          // 공간이 다 안 들어갈 때 공간 반지름 축소 비율
    const float MinRoomScale = 1.5f;        // 축소 하한 공간 지름 (샛길 폭 배율)
    const float MaxBend = 0.3f;             // 불규칙도 1일 때 샛길 휨 (샛길 길이 배율)
    const float WidthVariation = 0.3f;      // 불규칙도 1일 때 샛길 폭 변화 비율
    const float WidthWaves = 3f;            // 샛길 하나당 폭 변화 주기 수
    const float WarpStrength = 0.25f;       // 불규칙도 1일 때 전체 휨 세기 (공간 반지름 배율)
    const float WarpWavelength = 5f;        // 전체 휨 파장 (공간 반지름 배율)

    sealed class Placement
    {
        public readonly List<Vector2> Centers = new List<Vector2>();
        public readonly List<TerrainMapBlob> Rooms = new List<TerrainMapBlob>();
        public readonly List<List<Vector2>> Corridors = new List<List<Vector2>>();  // 경로 순서 i번째와 i+1번째 공간을 잇는 곡선
        public readonly List<float> CorridorPhases = new List<float>();            // 샛길별 폭 변화 노이즈 위치
        public List<int> Order = new List<int>();
        public float RoomRadius;
        public int Missing;
        public int Conflicts;
    }

    readonly bool[] _walkable;
    readonly bool[] _low;

    TerrainMapLayout(int width, int height, Vector2 cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        _walkable = new bool[width * height];
        _low = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 격자 한 칸의 월드 XZ 간격
    /// </summary>
    public Vector2 CellSize { get; }

    /// <summary>
    /// 시작 공간 중심 (Terrain 원점 기준 XZ)
    /// </summary>
    public Vector2 StartCenter { get; private set; }

    /// <summary>
    /// 끝 공간 중심 (Terrain 원점 기준 XZ)
    /// </summary>
    public Vector2 EndCenter { get; private set; }

    /// <summary>
    /// 자동 축소 후 실제 배치에 쓴 공간 지름, 배치 실패 시 0
    /// </summary>
    public float UsedRoomDiameter { get; private set; }

    /// <summary>
    /// 배치되어 시작과 끝 경로에 이어진 공간 수
    /// </summary>
    public int PlacedRoomCount { get; private set; }

    /// <summary>
    /// 샛길이 경로상 이웃이 아닌 공간·샛길에 붙은 곳의 수, 0이면 경로 하나 보장
    /// </summary>
    public int ConflictCount { get; private set; }

    /// <summary>
    /// 랜덤을 seed로 확정한 절벽 방향
    /// </summary>
    public TerrainMapCliffSide ResolvedCliffSide { get; private set; }

    /// <summary>
    /// 전체 칸 대비 이동 영역 비율
    /// </summary>
    public float WalkableRatio
    {
        get
        {
            int count = 0;
            foreach (bool walkable in _walkable)
            {
                if (walkable) count++;
            }
            return (float)count / _walkable.Length;
        }
    }

    static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared <= 0f) return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + ab * t);
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        // 각 선분의 양 끝점이 다른 선분의 반대편에 있는지로 교차 판정
        float d1 = Cross(b - a, c - a);
        float d2 = Cross(b - a, d - a);
        float d3 = Cross(d - c, a - c);
        float d4 = Cross(d - c, b - c);
        return d1 * d2 < 0f && d3 * d4 < 0f;
    }

    static float DistanceBetweenSegments(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (SegmentsIntersect(a, b, c, d)) return 0f;

        return Mathf.Min(
            Mathf.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
            Mathf.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
    }

    static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    static Vector2 ClampToRect(Vector2 point, Rect rect)
    {
        return new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
    }

    static float DistanceToPolyline(Vector2 point, List<Vector2> line)
    {
        float distance = float.MaxValue;
        for (int i = 0; i < line.Count - 1; i++)
        {
            distance = Mathf.Min(distance, DistanceToSegment(point, line[i], line[i + 1]));
        }
        return distance;
    }

    static float DistanceBetweenPolylines(List<Vector2> first, List<Vector2> second)
    {
        float distance = float.MaxValue;
        for (int i = 0; i < first.Count - 1; i++)
        {
            for (int j = 0; j < second.Count - 1; j++)
            {
                distance = Mathf.Min(distance, DistanceBetweenSegments(first[i], first[i + 1], second[j], second[j + 1]));
            }
        }
        return distance;
    }

    static void PlaceRooms(System.Random random, Placement placement, int count, Vector3 size, float margin,
        float irregularity, float gap)
    {
        for (int attempt = 0; attempt < MaxRoomAttempts && placement.Centers.Count < count; attempt++)
        {
            TerrainMapBlob room = TerrainMapBlob.Create(random, placement.RoomRadius * Range(random, 0.85f, 1.15f), irregularity);
            float extent = room.Extent;
            float minX = margin + extent;
            float maxX = size.x - margin - extent;
            float minZ = margin + extent;
            float maxZ = size.z - margin - extent;

            // 맵에 들어가지 않는 모양은 버리고 다시 생성
            if (minX > maxX || minZ > maxZ) continue;

            Vector2 center = new Vector2(Range(random, minX, maxX), Range(random, minZ, maxZ));

            // 공간끼리 맞닿아 경로 밖 지름길이 생기지 않도록 간격 유지
            bool tooClose = false;
            for (int i = 0; i < placement.Centers.Count && !tooClose; i++)
            {
                tooClose = Vector2.Distance(placement.Centers[i], center) < placement.Rooms[i].Extent + extent + gap;
            }
            if (tooClose) continue;

            placement.Centers.Add(center);
            placement.Rooms.Add(room);
        }
    }

    static void BuildCorridors(System.Random random, Placement placement, Rect bounds, float irregularity)
    {
        for (int i = 0; i < placement.Order.Count - 1; i++)
        {
            Vector2 from = placement.Centers[placement.Order[i]];
            Vector2 to = placement.Centers[placement.Order[i + 1]];
            Vector2 direction = to - from;
            Vector2 side = new Vector2(-direction.y, direction.x).normalized;
            float bend = MaxBend * irregularity * direction.magnitude;

            // 조절점을 안쪽 영역으로 제한하면 곡선 전체가 안쪽 영역 안에 머묾
            Vector2 controlA = ClampToRect(from + direction / 3f + side * Range(random, -bend, bend), bounds);
            Vector2 controlB = ClampToRect(from + direction * (2f / 3f) + side * Range(random, -bend, bend), bounds);
            List<Vector2> points = new List<Vector2>(CorridorSegments + 1);
            for (int step = 0; step <= CorridorSegments; step++)
            {
                points.Add(Bezier(from, controlA, controlB, to, (float)step / CorridorSegments));
            }

            placement.Corridors.Add(points);
            placement.CorridorPhases.Add(Range(random, 0f, 1000f));
        }
    }

    static void Untangle(List<int> order, List<Vector2> centers)
    {
        // 교차하는 두 샛길 사이 순서를 뒤집어 교차 해소, 시작과 끝은 고정
        for (int iteration = 0; iteration < MaxUntangleIterations; iteration++)
        {
            bool changed = false;
            for (int i = 0; i < order.Count - 1; i++)
            {
                for (int j = i + 2; j < order.Count - 1; j++)
                {
                    if (!SegmentsIntersect(centers[order[i]], centers[order[i + 1]],
                            centers[order[j]], centers[order[j + 1]])) continue;

                    order.Reverse(i + 1, j - i);
                    changed = true;
                }
            }

            if (!changed) return;
        }
    }

    static List<int> OrderPath(List<Vector2> centers)
    {
        List<int> order = new List<int>();
        if (centers.Count == 1)
        {
            order.Add(0);
            return order;
        }

        // 가장 먼 두 공간을 시작과 끝으로 지정
        int start = 0;
        int end = 1;
        float farthest = -1f;
        for (int i = 0; i < centers.Count; i++)
        {
            for (int j = i + 1; j < centers.Count; j++)
            {
                float distance = (centers[i] - centers[j]).sqrMagnitude;
                if (distance <= farthest) continue;
                farthest = distance;
                start = i;
                end = j;
            }
        }

        order.Add(start);
        order.Add(end);
        List<int> remaining = new List<int>();
        for (int i = 0; i < centers.Count; i++)
        {
            if (i != start && i != end) remaining.Add(i);
        }

        // 경로 길이 증가가 가장 적은 위치에 나머지 공간 삽입
        while (remaining.Count > 0)
        {
            float bestCost = float.MaxValue;
            int bestRoom = -1;
            int bestPosition = -1;
            foreach (int room in remaining)
            {
                for (int position = 1; position < order.Count; position++)
                {
                    Vector2 previous = centers[order[position - 1]];
                    Vector2 next = centers[order[position]];
                    float cost = Vector2.Distance(previous, centers[room]) + Vector2.Distance(centers[room], next)
                        - Vector2.Distance(previous, next);
                    if (cost >= bestCost) continue;
                    bestCost = cost;
                    bestRoom = room;
                    bestPosition = position;
                }
            }

            order.Insert(bestPosition, bestRoom);
            remaining.Remove(bestRoom);
        }

        Untangle(order, centers);
        return order;
    }

    static int CountConflicts(Placement placement, float corridorRadius, float gap)
    {
        // 샛길이 경로상 이웃이 아닌 공간이나 샛길에 붙으면 갈림길이 되므로 충돌로 집계
        int conflicts = 0;
        List<int> order = placement.Order;
        for (int segment = 0; segment < placement.Corridors.Count; segment++)
        {
            List<Vector2> corridor = placement.Corridors[segment];
            for (int k = 0; k < order.Count; k++)
            {
                if (k == segment || k == segment + 1) continue;
                int room = order[k];
                if (DistanceToPolyline(placement.Centers[room], corridor) <
                    placement.Rooms[room].Extent + corridorRadius + gap) conflicts++;
            }

            for (int other = segment + 2; other < placement.Corridors.Count; other++)
            {
                if (DistanceBetweenPolylines(corridor, placement.Corridors[other]) < corridorRadius * 2f + gap) conflicts++;
            }
        }

        return conflicts;
    }

    static void FillMissingDepths(float[] depths, int depthCount)
    {
        // 평지가 없는 줄은 양쪽 가까운 줄 깊이로 선형 보간
        int previous = -1;
        for (int line = 0; line <= depths.Length; line++)
        {
            if (line < depths.Length && depths[line] < 0f) continue;

            float left = previous >= 0 ? depths[previous] : -1f;
            float right = line < depths.Length ? depths[line] : -1f;
            for (int missing = previous + 1; missing < line; missing++)
            {
                if (left < 0f && right < 0f) depths[missing] = depthCount * 0.5f;
                else if (left < 0f) depths[missing] = right;
                else if (right < 0f) depths[missing] = left;
                else depths[missing] = Mathf.Lerp(left, right, (float)(missing - previous) / (line - previous));
            }

            previous = line;
        }
    }

    void StampDisc(bool[] grid, Vector2 center, float radius)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - radius) / CellSize.x));
        int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt((center.x + radius) / CellSize.x));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((center.y - radius) / CellSize.y));
        int maxZ = Mathf.Min(Height - 1, Mathf.CeilToInt((center.y + radius) / CellSize.y));
        float radiusSquared = radius * radius;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 position = new Vector2(x * CellSize.x, z * CellSize.y);
                if ((position - center).sqrMagnitude <= radiusSquared) grid[z * Width + x] = true;
            }
        }
    }

    void StampBlob(bool[] grid, Vector2 center, TerrainMapBlob blob)
    {
        float extent = blob.Extent;
        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - extent) / CellSize.x));
        int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt((center.x + extent) / CellSize.x));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((center.y - extent) / CellSize.y));
        int maxZ = Mathf.Min(Height - 1, Mathf.CeilToInt((center.y + extent) / CellSize.y));

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 position = new Vector2(x * CellSize.x, z * CellSize.y);
                if (blob.Contains(position - center)) grid[z * Width + x] = true;
            }
        }
    }

    void StampCorridor(bool[] grid, List<Vector2> points, float phase, float pathRadius, float irregularity)
    {
        float stepLength = Mathf.Min(CellSize.x, CellSize.y) * 0.5f;
        int segments = points.Count - 1;
        for (int segment = 0; segment < segments; segment++)
        {
            Vector2 from = points[segment];
            Vector2 to = points[segment + 1];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / stepLength));
            for (int i = 0; i <= steps; i++)
            {
                // 샛길을 따라 폭이 좁아졌다 넓어지는 변화
                float t = (segment + (float)i / steps) / segments;
                float noise = Mathf.Clamp(Mathf.PerlinNoise(t * WidthWaves + phase, phase) * 2f - 1f, -1f, 1f);
                StampDisc(grid, Vector2.Lerp(from, to, (float)i / steps),
                    pathRadius * (1f + WidthVariation * irregularity * noise));
            }
        }
    }

    void StampPlacement(bool[] grid, Placement placement, float pathRadius, float irregularity)
    {
        for (int i = 0; i < placement.Centers.Count; i++)
        {
            StampBlob(grid, placement.Centers[i], placement.Rooms[i]);
        }
        for (int i = 0; i < placement.Corridors.Count; i++)
        {
            StampCorridor(grid, placement.Corridors[i], placement.CorridorPhases[i], pathRadius, irregularity);
        }
    }

    int CountConnectedRooms(List<Vector2> centers)
    {
        // 경계 변형과 가장자리 여백으로 끊겨 제거된 공간 제외
        int count = 0;
        foreach (Vector2 center in centers)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(center.x / CellSize.x), 0, Width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(center.y / CellSize.y), 0, Height - 1);
            if (_walkable[z * Width + x]) count++;
        }
        return count;
    }

    void Warp(bool[] source, System.Random random, float amplitude, float wavelength)
    {
        // 공간 크기보다 긴 파장 노이즈로 샘플 위치를 밀어 배치 전체를 자연스럽게 휨
        Vector2 offsetX = new Vector2(Range(random, 0f, 1000f), Range(random, 0f, 1000f));
        Vector2 offsetZ = new Vector2(Range(random, 0f, 1000f), Range(random, 0f, 1000f));

        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float worldX = x * CellSize.x;
                float worldZ = z * CellSize.y;
                float shiftX = (Mathf.PerlinNoise(worldX / wavelength + offsetX.x, worldZ / wavelength + offsetX.y) - 0.5f) * 2f * amplitude;
                float shiftZ = (Mathf.PerlinNoise(worldX / wavelength + offsetZ.x, worldZ / wavelength + offsetZ.y) - 0.5f) * 2f * amplitude;
                int sourceX = Mathf.Clamp(Mathf.RoundToInt((worldX + shiftX) / CellSize.x), 0, Width - 1);
                int sourceZ = Mathf.Clamp(Mathf.RoundToInt((worldZ + shiftZ) / CellSize.y), 0, Height - 1);
                _walkable[z * Width + x] = source[sourceZ * Width + sourceX];
            }
        }
    }

    void Smooth()
    {
        // 3x3 다수결로 한두 칸짜리 돌출과 구멍 제거
        for (int iteration = 0; iteration < SmoothIterations; iteration++)
        {
            bool[] source = (bool[])_walkable.Clone();
            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int count = 0;
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        int sampleZ = z + offsetZ;
                        if (sampleZ < 0 || sampleZ >= Height) continue;
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int sampleX = x + offsetX;
                            if (sampleX < 0 || sampleX >= Width) continue;
                            if (source[sampleZ * Width + sampleX]) count++;
                        }
                    }
                    _walkable[z * Width + x] = count >= 5;
                }
            }
        }
    }

    void ApplyMargin(float margin)
    {
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float edgeX = Mathf.Min(x, Width - 1 - x) * CellSize.x;
                float edgeZ = Mathf.Min(z, Height - 1 - z) * CellSize.y;
                if (edgeX < margin || edgeZ < margin) _walkable[z * Width + x] = false;
            }
        }
    }

    void KeepLargestRegion()
    {
        // 경계 변형으로 떨어져 나간 평지 조각 제거
        int[] labels = new int[_walkable.Length];
        Queue<int> queue = new Queue<int>();
        int label = 0;
        int bestLabel = 0;
        int bestSize = 0;

        for (int start = 0; start < _walkable.Length; start++)
        {
            if (!_walkable[start] || labels[start] != 0) continue;

            label++;
            int size = 0;
            labels[start] = label;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                size++;
                int x = cell % Width;
                int z = cell / Width;
                TryVisit(x - 1, z, label, labels, queue);
                TryVisit(x + 1, z, label, labels, queue);
                TryVisit(x, z - 1, label, labels, queue);
                TryVisit(x, z + 1, label, labels, queue);
            }

            if (size <= bestSize) continue;
            bestSize = size;
            bestLabel = label;
        }

        for (int i = 0; i < _walkable.Length; i++)
        {
            if (_walkable[i] && labels[i] != bestLabel) _walkable[i] = false;
        }
    }

    void TryVisit(int x, int z, int label, int[] labels, Queue<int> queue)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height) return;
        int index = z * Width + x;
        if (!_walkable[index] || labels[index] != 0) return;
        labels[index] = label;
        queue.Enqueue(index);
    }

    int GetSideIndex(TerrainMapCliffSide side, int line, int depth)
    {
        // 낮은 쪽 가장자리에서 안쪽으로 depth만큼 들어간 칸
        switch (side)
        {
            case TerrainMapCliffSide.North: return (Height - 1 - depth) * Width + line;
            case TerrainMapCliffSide.South: return depth * Width + line;
            case TerrainMapCliffSide.East: return line * Width + (Width - 1 - depth);
            default: return line * Width + depth;
        }
    }

    void PlaceCliffSideLow(TerrainMapCliffSide side)
    {
        // 낮은 쪽 가장자리에서 줄마다 첫 평지 직전까지 낮은 곳 지정
        bool alongX = side == TerrainMapCliffSide.North || side == TerrainMapCliffSide.South;
        int lineCount = alongX ? Width : Height;
        int depthCount = alongX ? Height : Width;
        float[] depths = new float[lineCount];

        for (int line = 0; line < lineCount; line++)
        {
            depths[line] = -1f;
            for (int depth = 0; depth < depthCount; depth++)
            {
                if (!_walkable[GetSideIndex(side, line, depth)]) continue;
                depths[line] = depth;
                break;
            }
        }

        FillMissingDepths(depths, depthCount);
        for (int line = 0; line < lineCount; line++)
        {
            int limit = Mathf.RoundToInt(depths[line]);
            for (int depth = 0; depth < limit; depth++)
            {
                _low[GetSideIndex(side, line, depth)] = true;
            }
        }
    }

    void PlaceLow(TerrainMapShape shape)
    {
        // 감싼 형태는 낮은 곳 없음
        switch (shape)
        {
            case TerrainMapShape.Island:
                for (int i = 0; i < _walkable.Length; i++)
                {
                    _low[i] = !_walkable[i];
                }
                break;
            case TerrainMapShape.CliffSide:
                PlaceCliffSideLow(ResolvedCliffSide);
                break;
        }
    }

    /// <summary>
    /// 지정 칸 이동 영역 여부
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>이동 영역 여부</returns>
    public bool IsWalkable(int x, int z) => _walkable[z * Width + x];

    /// <summary>
    /// 지정 칸 낮은 곳 여부
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>이동 불가 칸 중 낮은 곳 여부</returns>
    public bool IsLow(int x, int z) => _low[z * Width + x];

    /// <summary>
    /// 칸별 가장 가까운 이동 영역까지 칸 단위 거리 계산
    /// </summary>
    /// <returns>칸별 거리, 이동 영역은 0</returns>
    public float[] GetWalkableDistance()
    {
        return DistanceTo(_walkable, Width, Height);
    }

    /// <summary>
    /// 절벽 방향 표시 이름
    /// </summary>
    /// <param name="side">절벽 방향</param>
    /// <returns>표시 이름</returns>
    public static string GetSideName(TerrainMapCliffSide side)
    {
        switch (side)
        {
            case TerrainMapCliffSide.North: return "북 (+Z)";
            case TerrainMapCliffSide.East: return "동 (+X)";
            case TerrainMapCliffSide.South: return "남 (-Z)";
            case TerrainMapCliffSide.West: return "서 (-X)";
            default: return "랜덤";
        }
    }

    /// <summary>
    /// 칸별 가장 가까운 기준 칸까지 칸 단위 근사 거리 계산
    /// </summary>
    /// <param name="sources">기준 칸 여부</param>
    /// <param name="width">X 방향 칸 수</param>
    /// <param name="height">Z 방향 칸 수</param>
    /// <returns>칸별 거리, 기준 칸이 없으면 float.MaxValue</returns>
    public static float[] DistanceTo(bool[] sources, int width, int height)
    {
        // 가로·세로 1, 대각선 √2 가중치 두 방향 순회 근사 거리
        const float Diagonal = 1.41421356f;
        float[] distance = new float[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            distance[i] = sources[i] ? 0f : float.MaxValue;
        }

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                float value = distance[index];
                if (x > 0) value = Mathf.Min(value, distance[index - 1] + 1f);
                if (z > 0) value = Mathf.Min(value, distance[index - width] + 1f);
                if (x > 0 && z > 0) value = Mathf.Min(value, distance[index - width - 1] + Diagonal);
                if (x < width - 1 && z > 0) value = Mathf.Min(value, distance[index - width + 1] + Diagonal);
                distance[index] = value;
            }
        }

        for (int z = height - 1; z >= 0; z--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int index = z * width + x;
                float value = distance[index];
                if (x < width - 1) value = Mathf.Min(value, distance[index + 1] + 1f);
                if (z < height - 1) value = Mathf.Min(value, distance[index + width] + 1f);
                if (x < width - 1 && z < height - 1) value = Mathf.Min(value, distance[index + width + 1] + Diagonal);
                if (x > 0 && z < height - 1) value = Mathf.Min(value, distance[index + width - 1] + Diagonal);
                distance[index] = value;
            }
        }

        return distance;
    }

    /// <summary>
    /// 입력값과 seed 기반 배치 격자 생성
    /// </summary>
    /// <param name="settings">맵 생성 입력값</param>
    /// <param name="size">Terrain 크기</param>
    /// <param name="resolution">높이맵 해상도</param>
    /// <returns>높이맵 해상도 배치 격자</returns>
    public static TerrainMapLayout Generate(TerrainMapSettings settings, Vector3 size, int resolution)
    {
        TerrainMapLayout layout = new TerrainMapLayout(resolution, resolution,
            new Vector2(size.x / (resolution - 1), size.z / (resolution - 1)));
        System.Random random = new System.Random(settings.Seed);

        int roomCount = Mathf.Max(1, settings.RoomCount);
        float irregularity = Mathf.Clamp01(settings.Irregularity);
        float pathRadius = settings.PathWidth * 0.5f;
        float corridorRadius = pathRadius * (1f + WidthVariation * irregularity);
        float minRoomRadius = pathRadius * MinRoomScale;
        float roomRadius = Mathf.Max(settings.RoomDiameter * 0.5f, pathRadius);
        Placement best = null;

        // 공간 수를 모두 채우고 갈림길이 없는 배치를 찾을 때까지 공간 크기를 줄여 가며 재시도
        while (true)
        {
            float warpAmplitude = roomRadius * WarpStrength * irregularity;
            float margin = settings.EdgeMargin + warpAmplitude;
            // 전체 휨으로 경계가 밀려도 맞닿지 않도록 휨 폭만큼 간격 추가
            float gap = settings.PathWidth * Clearance + warpAmplitude * 2f;
            Rect bounds = Rect.MinMaxRect(margin + corridorRadius, margin + corridorRadius,
                size.x - margin - corridorRadius, size.z - margin - corridorRadius);

            for (int attempt = 0; attempt < MaxPlacementTries; attempt++)
            {
                Placement placement = new Placement { RoomRadius = roomRadius };
                PlaceRooms(random, placement, roomCount, size, margin, irregularity, gap);
                if (placement.Centers.Count == 0) continue;

                placement.Order = OrderPath(placement.Centers);
                BuildCorridors(random, placement, bounds, irregularity);
                placement.Missing = roomCount - placement.Centers.Count;
                placement.Conflicts = CountConflicts(placement, corridorRadius, gap);
                if (best != null && (placement.Missing > best.Missing ||
                                     (placement.Missing == best.Missing && placement.Conflicts >= best.Conflicts))) continue;

                best = placement;
                if (best.Missing == 0 && best.Conflicts == 0) break;
            }

            bool solved = best != null && best.Missing == 0 && best.Conflicts == 0;
            if (solved || roomRadius <= minRoomRadius) break;
            roomRadius = Mathf.Max(minRoomRadius, roomRadius * ShrinkStep);
        }

        if (best != null)
        {
            bool[] stamped = new bool[resolution * resolution];
            layout.StampPlacement(stamped, best, pathRadius, irregularity);
            layout.Warp(stamped, random, best.RoomRadius * WarpStrength * irregularity, best.RoomRadius * WarpWavelength);
            layout.Smooth();
            layout.ApplyMargin(settings.EdgeMargin);
            layout.KeepLargestRegion();

            layout.StartCenter = best.Centers[best.Order[0]];
            layout.EndCenter = best.Centers[best.Order[best.Order.Count - 1]];
            layout.UsedRoomDiameter = best.RoomRadius * 2f;
            layout.ConflictCount = best.Conflicts;
            layout.PlacedRoomCount = layout.CountConnectedRooms(best.Centers);
        }
        else
        {
            layout.StartCenter = new Vector2(size.x, size.z) * 0.5f;
            layout.EndCenter = layout.StartCenter;
        }

        layout.ResolvedCliffSide = settings.CliffSide == TerrainMapCliffSide.Random
            ? (TerrainMapCliffSide)(1 + random.Next(4))
            : settings.CliffSide;
        layout.PlaceLow(settings.Shape);
        return layout;
    }
}
