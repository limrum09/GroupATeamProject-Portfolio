using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Terrain 지점별 이동 가능 영역 분류
/// </summary>
public enum TerrainWalkableCategory
{
    Outside,    // 대상 Terrain 범위 밖
    Walkable,   // 플레이어 이동 높이의 평지
    Cliff,      // 높이 차 옆면
    High,       // 이동 높이보다 높은 면
    Low         // 이동 높이보다 낮은 면
}

/// <summary>
/// 이동 가능 영역 판정 기준
/// </summary>
public readonly struct TerrainWalkableSettings
{
    /// <summary>
    /// 판정 기준 생성
    /// </summary>
    /// <param name="playerHeight">플레이어 이동 평지의 월드 높이</param>
    /// <param name="heightTolerance">이동 평지로 인정할 높이 허용치</param>
    /// <param name="cliffAngle">절벽으로 판정할 최소 경사 각도</param>
    public TerrainWalkableSettings(float playerHeight, float heightTolerance, float cliffAngle)
    {
        PlayerHeight = playerHeight;
        HeightTolerance = heightTolerance;
        CliffAngle = cliffAngle;
    }

    public float PlayerHeight { get; }
    public float HeightTolerance { get; }
    public float CliffAngle { get; }
}

/// <summary>
/// 월드 격자 지점별 이동 가능 영역 분류 결과와 격자 연산
/// </summary>
public class TerrainWalkableMask
{
    public static readonly int CategoryCount = Enum.GetValues(typeof(TerrainWalkableCategory)).Length;

    const float BoundsEpsilon = 0.01f;

    readonly TerrainWalkableCategory[] _cells;

    TerrainWalkableMask(int width, int height, Rect worldRect)
    {
        Width = width;
        Height = height;
        WorldRect = worldRect;
        _cells = new TerrainWalkableCategory[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 격자가 덮는 월드 범위 (x, y = 월드 X, Z)
    /// </summary>
    public Rect WorldRect { get; }

    static List<Terrain> CollectSources(IReadOnlyList<Terrain> targets)
    {
        // 대상 우선 조회, 이웃 타일은 경계 경사 계산용 읽기 전용
        List<Terrain> sources = new List<Terrain>();
        foreach (Terrain target in targets)
        {
            AddSource(sources, target);
        }

        foreach (Terrain target in targets)
        {
            AddSource(sources, target.leftNeighbor);
            AddSource(sources, target.rightNeighbor);
            AddSource(sources, target.topNeighbor);
            AddSource(sources, target.bottomNeighbor);
        }

        return sources;
    }

    static void AddSource(List<Terrain> sources, Terrain terrain)
    {
        // 연결된 이웃이 없는 방향은 null
        if (terrain == null || terrain.terrainData == null || sources.Contains(terrain)) return;
        sources.Add(terrain);
    }

    static Terrain FindTerrain(IReadOnlyList<Terrain> terrains, float x, float z)
    {
        foreach (Terrain terrain in terrains)
        {
            Vector3 origin = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            if (x < origin.x - BoundsEpsilon || x > origin.x + size.x + BoundsEpsilon) continue;
            if (z < origin.z - BoundsEpsilon || z > origin.z + size.z + BoundsEpsilon) continue;
            return terrain;
        }

        return null;
    }

    static float SampleHeight(Terrain terrain, float x, float z)
    {
        // SampleHeight 반환값은 Terrain 원점 기준 높이
        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y;
    }

    static float SampleNeighborHeight(IReadOnlyList<Terrain> sources, float x, float z, float centerHeight)
    {
        // 맵 바깥 가장자리는 중심 높이로 간주해 가장자리 절벽 오판정 방지
        Terrain terrain = FindTerrain(sources, x, z);
        return terrain != null ? SampleHeight(terrain, x, z) : centerHeight;
    }

    static float SampleSlope(IReadOnlyList<Terrain> sources, Terrain terrain, float x, float z, float centerHeight)
    {
        // 높이맵 한 칸 간격 중앙 차분
        Vector3 scale = terrain.terrainData.heightmapScale;
        float left = SampleNeighborHeight(sources, x - scale.x, z, centerHeight);
        float right = SampleNeighborHeight(sources, x + scale.x, z, centerHeight);
        float back = SampleNeighborHeight(sources, x, z - scale.z, centerHeight);
        float front = SampleNeighborHeight(sources, x, z + scale.z, centerHeight);

        float gradientX = (right - left) / (2f * scale.x);
        float gradientZ = (front - back) / (2f * scale.z);
        return Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
    }

    static TerrainWalkableCategory Classify(float height, float slope, TerrainWalkableSettings settings)
    {
        // 절벽 판정 우선, 이후 이동 높이 기준 상하 분류
        if (slope >= settings.CliffAngle) return TerrainWalkableCategory.Cliff;

        float offset = height - settings.PlayerHeight;
        if (Mathf.Abs(offset) <= settings.HeightTolerance) return TerrainWalkableCategory.Walkable;
        return offset > 0f ? TerrainWalkableCategory.High : TerrainWalkableCategory.Low;
    }

    static TerrainWalkableMask Build(IReadOnlyList<Terrain> targets, Rect worldRect, Vector2 firstSample,
        Vector2 step, int width, int height, TerrainWalkableSettings settings)
    {
        List<Terrain> sources = CollectSources(targets);
        TerrainWalkableMask mask = new TerrainWalkableMask(width, height, worldRect);

        for (int z = 0; z < height; z++)
        {
            float worldZ = firstSample.y + z * step.y;
            for (int x = 0; x < width; x++)
            {
                float worldX = firstSample.x + x * step.x;

                // 선택하지 않은 이웃 영역은 Outside 유지
                Terrain terrain = FindTerrain(targets, worldX, worldZ);
                if (terrain == null) continue;

                float worldHeight = SampleHeight(terrain, worldX, worldZ);
                float slope = SampleSlope(sources, terrain, worldX, worldZ, worldHeight);
                mask._cells[z * width + x] = Classify(worldHeight, slope, settings);
            }
        }

        return mask;
    }

    static void BoxBlur(float[] values, int width, int height, int radius)
    {
        // 가로·세로 분리 평균, 가장자리는 범위 안 칸만 평균해 분류별 비중 합 1 유지
        float[] buffer = new float[values.Length];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sampleX = x + offset;
                    if (sampleX < 0 || sampleX >= width) continue;
                    sum += values[z * width + sampleX];
                    count++;
                }
                buffer[z * width + x] = sum / count;
            }
        }

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sampleZ = z + offset;
                    if (sampleZ < 0 || sampleZ >= height) continue;
                    sum += buffer[sampleZ * width + x];
                    count++;
                }
                values[z * width + x] = sum / count;
            }
        }
    }

    /// <summary>
    /// 지정 지점 분류 조회
    /// </summary>
    /// <param name="x">X 방향 격자 번호</param>
    /// <param name="z">Z 방향 격자 번호</param>
    /// <returns>지점 분류</returns>
    public TerrainWalkableCategory Get(int x, int z) => _cells[z * Width + x];

    /// <summary>
    /// 지정 분류를 주변 칸까지 확장, Outside 칸은 유지
    /// </summary>
    /// <param name="category">확장할 분류</param>
    /// <param name="radius">확장 칸 수</param>
    public void ExpandCategory(TerrainWalkableCategory category, int radius)
    {
        if (radius <= 0) return;

        // 확장 결과가 다시 확장 기준이 되지 않도록 원본 복사 기준 처리
        TerrainWalkableCategory[] source = (TerrainWalkableCategory[])_cells.Clone();
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (source[z * Width + x] != category) continue;

                for (int offsetZ = -radius; offsetZ <= radius; offsetZ++)
                {
                    int targetZ = z + offsetZ;
                    if (targetZ < 0 || targetZ >= Height) continue;

                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        int targetX = x + offsetX;
                        if (targetX < 0 || targetX >= Width) continue;

                        int index = targetZ * Width + targetX;
                        if (_cells[index] != TerrainWalkableCategory.Outside) _cells[index] = category;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 지정 분류의 칸별 비중 계산
    /// </summary>
    /// <param name="category">비중을 계산할 분류</param>
    /// <param name="blurRadius">경계 블렌딩 칸 수</param>
    /// <returns>칸별 0~1 비중</returns>
    public float[] GetWeights(TerrainWalkableCategory category, int blurRadius)
    {
        float[] weights = new float[_cells.Length];
        for (int i = 0; i < _cells.Length; i++)
        {
            weights[i] = _cells[i] == category ? 1f : 0f;
        }

        if (blurRadius > 0) BoxBlur(weights, Width, Height, blurRadius);
        return weights;
    }

    /// <summary>
    /// Terrain 목록 전체를 덮는 월드 범위 계산
    /// </summary>
    /// <param name="terrains">범위 계산 대상 Terrain 목록</param>
    /// <returns>월드 범위 (x, y = 월드 X, Z)</returns>
    public static Rect GetWorldRect(IReadOnlyList<Terrain> terrains)
    {
        float minX = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxZ = float.MinValue;

        foreach (Terrain terrain in terrains)
        {
            Vector3 origin = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            minX = Mathf.Min(minX, origin.x);
            minZ = Mathf.Min(minZ, origin.z);
            maxX = Mathf.Max(maxX, origin.x + size.x);
            maxZ = Mathf.Max(maxZ, origin.z + size.z);
        }

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    /// <summary>
    /// Terrain 알파맵 텍셀 위치 기준 분류 계산
    /// </summary>
    /// <param name="terrain">알파맵 기준 Terrain</param>
    /// <param name="targets">분류 대상 Terrain 목록</param>
    /// <param name="settings">판정 기준</param>
    /// <returns>알파맵 해상도 분류 결과</returns>
    public static TerrainWalkableMask BuildForAlphamap(Terrain terrain, IReadOnlyList<Terrain> targets,
        TerrainWalkableSettings settings)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        Vector3 origin = terrain.GetPosition();
        Rect worldRect = new Rect(origin.x, origin.z, data.size.x, data.size.z);

        // TerrainLit 알파맵 샘플 위치와 같은 i / (resolution - 1) 지점
        Vector2 step = worldRect.size / (resolution - 1);
        return Build(targets, worldRect, worldRect.position, step, resolution, resolution, settings);
    }

    /// <summary>
    /// 대상 Terrain 전체 범위의 이미지 픽셀 기준 분류 계산
    /// </summary>
    /// <param name="targets">분류 대상 Terrain 목록</param>
    /// <param name="longSide">긴 변 픽셀 수</param>
    /// <param name="settings">판정 기준</param>
    /// <returns>범위 비율을 유지한 분류 결과</returns>
    public static TerrainWalkableMask BuildForArea(IReadOnlyList<Terrain> targets, int longSide,
        TerrainWalkableSettings settings)
    {
        Rect worldRect = GetWorldRect(targets);
        Vector2Int size = MinimapImageExporter.GetImageSize(worldRect, longSide);
        int width = size.x;
        int height = size.y;

        // 픽셀 중심 지점 판정
        Vector2 step = new Vector2(worldRect.width / width, worldRect.height / height);
        return Build(targets, worldRect, worldRect.position + step * 0.5f, step, width, height, settings);
    }
}
