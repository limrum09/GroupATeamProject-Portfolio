using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레퍼런스 Terrain 지형·배치물 특징 분석 결과
/// </summary>
public class TerrainMapReference
{
    const float RoomPeakRatio = 1.5f;     // 공간 중심으로 인정할 최소 폭 (길 폭 중간값 배율)

    readonly List<float> _highOffsets = new List<float>();
    readonly List<float> _lowOffsets = new List<float>();
    readonly List<float> _ridgeWidths = new List<float>();
    readonly List<float> _roomWidths = new List<float>();
    readonly List<TerrainLayer> _layers = new List<TerrainLayer>();
    readonly TerrainMapPropProfile _props = new TerrainMapPropProfile();
    int _totalCells;
    int _walkableCells;
    float _edgeMargin = float.MaxValue;

    TerrainMapReference()
    {
    }

    public Vector3 Size { get; private set; }
    public int HeightmapResolution { get; private set; }
    public int AlphamapResolution { get; private set; }
    public int BaseMapResolution { get; private set; }
    public float WalkableRatio { get; private set; }
    public bool HasHighArea => _highOffsets.Count > 0;
    public bool HasLowArea => _lowOffsets.Count > 0;
    public float HighOffset { get; private set; }
    public float LowOffset { get; private set; }
    public float HighBumpDeviation { get; private set; }
    public float LowBumpDeviation { get; private set; }
    public float PathWidth { get; private set; }
    public float RoomDiameter { get; private set; }
    public float EdgeMargin { get; private set; }
    public IReadOnlyList<TerrainLayer> Layers => _layers;

    /// <summary>
    /// 레퍼런스 배치물 종류별 분포 측정 결과
    /// </summary>
    public TerrainMapPropProfile Props => _props;

    static float Percentile(List<float> values, float percentile)
    {
        if (values.Count == 0) return 0f;
        List<float> sorted = new List<float>(values);
        sorted.Sort();
        return sorted[Mathf.Clamp(Mathf.RoundToInt(percentile * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    static float Deviation(List<float> values)
    {
        if (values.Count == 0) return 0f;

        float mean = 0f;
        foreach (float value in values)
        {
            mean += value;
        }
        mean /= values.Count;

        float variance = 0f;
        foreach (float value in values)
        {
            variance += (value - mean) * (value - mean);
        }
        return Mathf.Sqrt(variance / values.Count);
    }

    void AddRidgeWidths(bool[] walkable, float[] distance, int width, int height, float cellSize)
    {
        // 주변보다 경계에서 먼 평지 중심선 칸의 거리로 길 폭 측정
        List<int> ridges = new List<int>();
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                if (!walkable[index]) continue;

                float value = distance[index];
                bool ridge = true;
                for (int offsetZ = -1; offsetZ <= 1 && ridge; offsetZ++)
                {
                    int sampleZ = z + offsetZ;
                    if (sampleZ < 0 || sampleZ >= height) continue;
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int sampleX = x + offsetX;
                        if (sampleX < 0 || sampleX >= width) continue;
                        if (distance[sampleZ * width + sampleX] <= value) continue;
                        ridge = false;
                        break;
                    }
                }

                if (!ridge) continue;
                ridges.Add(index);
                _ridgeWidths.Add(value * 2f * cellSize);
            }
        }

        AddRoomWidths(ridges, distance, width, cellSize);
    }

    void AddRoomWidths(List<int> ridges, float[] distance, int width, float cellSize)
    {
        if (ridges.Count == 0) return;

        // 같은 공간 안 여러 중심선 칸 중 가장 넓은 칸 하나만 공간 크기로 집계
        ridges.Sort((a, b) => distance[b].CompareTo(distance[a]));
        float threshold = distance[ridges[ridges.Count / 2]] * RoomPeakRatio;
        List<int> peaks = new List<int>();
        foreach (int candidate in ridges)
        {
            if (peaks.Count > 0 && distance[candidate] < threshold) break;

            bool covered = false;
            foreach (int peak in peaks)
            {
                int offsetX = candidate % width - peak % width;
                int offsetZ = candidate / width - peak / width;
                float reach = distance[peak] + distance[candidate];
                if (offsetX * offsetX + offsetZ * offsetZ >= reach * reach) continue;
                covered = true;
                break;
            }
            if (covered) continue;

            peaks.Add(candidate);
            _roomWidths.Add(distance[candidate] * 2f * cellSize);
        }
    }

    static bool[] MarkEdgeCliffs(bool[] walkable, bool[] cliffs, int width, int height)
    {
        // 평지에서 절벽 칸만 따라 이어지는 절벽 표시, 높은 곳·낮은 곳 사이에 따로 있는 절벽 제외
        bool[] edge = new bool[walkable.Length];
        Queue<int> queue = new Queue<int>();
        for (int i = 0; i < walkable.Length; i++)
        {
            if (walkable[i]) queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % width;
            int z = cell / width;
            VisitCliff(x - 1, z, width, height, cliffs, edge, queue);
            VisitCliff(x + 1, z, width, height, cliffs, edge, queue);
            VisitCliff(x, z - 1, width, height, cliffs, edge, queue);
            VisitCliff(x, z + 1, width, height, cliffs, edge, queue);
        }
        return edge;
    }

    static void VisitCliff(int x, int z, int width, int height, bool[] cliffs, bool[] edge, Queue<int> queue)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return;
        int index = z * width + x;
        if (!cliffs[index] || edge[index]) return;
        edge[index] = true;
        queue.Enqueue(index);
    }

    bool TryAddTerrain(Terrain terrain, TerrainWalkableSettings settings, out string error)
    {
        TerrainData data = terrain.terrainData;
        float originY = terrain.GetPosition().y;
        TerrainWalkableMask mask = TerrainWalkableMask.BuildForArea(new[] { terrain }, data.heightmapResolution, settings);
        int width = mask.Width;
        int height = mask.Height;
        Vector2 step = new Vector2(mask.WorldRect.width / width, mask.WorldRect.height / height);
        float cellSize = (step.x + step.y) * 0.5f;
        bool[] walkable = new bool[width * height];
        bool[] blocked = new bool[width * height];
        bool[] inside = new bool[width * height];
        TerrainMapRegion[] regions = new TerrainMapRegion[width * height];
        bool[] cliffs = new bool[width * height];
        int localWalkable = 0;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                TerrainWalkableCategory category = mask.Get(x, z);
                if (category == TerrainWalkableCategory.Outside)
                {
                    blocked[index] = true;
                    continue;
                }

                inside[index] = true;
                _totalCells++;
                if (category == TerrainWalkableCategory.Walkable)
                {
                    walkable[index] = true;
                    regions[index] = TerrainMapRegion.Walkable;
                    localWalkable++;
                    float edgeX = Mathf.Min(x + 0.5f, width - x - 0.5f) * step.x;
                    float edgeZ = Mathf.Min(z + 0.5f, height - z - 0.5f) * step.y;
                    _edgeMargin = Mathf.Min(_edgeMargin, Mathf.Min(edgeX, edgeZ));
                    continue;
                }

                blocked[index] = true;
                Vector3 position = new Vector3(mask.WorldRect.x + (x + 0.5f) * step.x, 0f,
                    mask.WorldRect.y + (z + 0.5f) * step.y);
                float offset = terrain.SampleHeight(position) + originY - settings.PlayerHeight;
                // 절벽은 생성 맵에 따로 없어 높이 기준으로 높은 곳·낮은 곳에 포함
                regions[index] = offset >= 0f ? TerrainMapRegion.High : TerrainMapRegion.Low;
                if (category == TerrainWalkableCategory.Cliff)
                {
                    cliffs[index] = true;
                    continue;
                }

                if (category == TerrainWalkableCategory.High) _highOffsets.Add(offset);
                else _lowOffsets.Add(offset);
            }
        }

        if (localWalkable == 0)
        {
            error = $"{terrain.name}: 플레이어 높이({settings.PlayerHeight:0.##})의 평지를 찾지 못했습니다.";
            return false;
        }

        _walkableCells += localWalkable;
        float[] depth = TerrainMapLayout.DistanceTo(blocked, width, height);
        AddRidgeWidths(walkable, depth, width, height, cellSize);

        // 평지에 이어진 절벽까지를 경계로 보고 절벽 꼭대기에서 거리 측정
        bool[] edgeCliffs = MarkEdgeCliffs(walkable, cliffs, width, height);
        bool[] edges = new bool[width * height];
        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = walkable[i] || edgeCliffs[i];
        }
        float[] outside = TerrainMapLayout.DistanceTo(edges, width, height);

        // 평지 칸은 경계까지 안쪽 깊이, 나머지는 절벽 꼭대기까지 거리, 경계 절벽 칸은 0 (m)
        float[] edgeDistances = new float[width * height];
        for (int i = 0; i < edgeDistances.Length; i++)
        {
            edgeDistances[i] = (walkable[i] ? depth[i] : outside[i]) * cellSize;
        }
        if (!_props.TryAddTerrain(terrain, regions, inside, edgeCliffs, edgeDistances, width, height,
                mask.WorldRect, out error)) return false;

        foreach (TerrainLayer layer in data.terrainLayers)
        {
            if (layer != null && !_layers.Contains(layer)) _layers.Add(layer);
        }

        error = string.Empty;
        return true;
    }

    void Finish()
    {
        WalkableRatio = (float)_walkableCells / _totalCells;
        HighOffset = Percentile(_highOffsets, 0.5f);
        LowOffset = Percentile(_lowOffsets, 0.5f);
        HighBumpDeviation = Deviation(_highOffsets);
        LowBumpDeviation = Deviation(_lowOffsets);
        PathWidth = Percentile(_ridgeWidths, 0.5f);
        RoomDiameter = Percentile(_roomWidths, 0.5f);
        EdgeMargin = _edgeMargin;
        _props.Finish();
    }

    /// <summary>
    /// 지정 플레이어 높이 기준 레퍼런스 Terrain 목록의 지형·나무 특징 통합 분석
    /// </summary>
    /// <param name="references">분석할 레퍼런스 Terrain 목록, 첫 항목이 크기 기준</param>
    /// <param name="playerHeight">레퍼런스 공통 플레이어 이동 월드 높이</param>
    /// <param name="heightTolerance">이동 평지로 인정할 높이 허용치</param>
    /// <param name="cliffAngle">절벽으로 판정할 최소 경사 각도</param>
    /// <param name="result">분석 결과</param>
    /// <param name="error">분석 불가 사유</param>
    /// <returns>분석 성공 여부</returns>
    public static bool TryAnalyze(IReadOnlyList<Terrain> references, float playerHeight, float heightTolerance,
        float cliffAngle, out TerrainMapReference result, out string error)
    {
        result = null;
        if (references == null || references.Count == 0)
        {
            error = "레퍼런스 Terrain을 하나 이상 지정하세요.";
            return false;
        }

        foreach (Terrain terrain in references)
        {
            if (terrain != null && terrain.terrainData != null) continue;
            error = "레퍼런스 Terrain 또는 TerrainData 연결이 없습니다.";
            return false;
        }

        TerrainData first = references[0].terrainData;
        TerrainMapReference reference = new TerrainMapReference
        {
            Size = first.size,
            HeightmapResolution = first.heightmapResolution,
            AlphamapResolution = first.alphamapResolution,
            BaseMapResolution = first.baseMapResolution
        };

        TerrainWalkableSettings settings = new TerrainWalkableSettings(playerHeight, heightTolerance, cliffAngle);
        foreach (Terrain terrain in references)
        {
            if (!reference.TryAddTerrain(terrain, settings, out error)) return false;
        }

        reference.Finish();
        result = reference;
        error = string.Empty;
        return true;
    }
}
