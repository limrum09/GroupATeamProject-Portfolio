using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성 배치물 종류 하나의 배치 결과 요약
/// </summary>
public class TerrainMapPropPlanEntry
{
    readonly int[] _regionCounts = new int[TerrainMapPropProfile.RegionCount];
    int _nearCount;

    internal TerrainMapPropPlanEntry(GameObject prefab, TerrainMapPropCategory category, int targetCount)
    {
        Prefab = prefab;
        Category = category;
        TargetCount = targetCount;
    }

    public GameObject Prefab { get; }
    public TerrainMapPropCategory Category { get; }

    /// <summary>
    /// 심는 범위 안 거리대 밀도 × 생성 맵 거리대 넓이 × 밀도 배율 목표 개수
    /// </summary>
    public int TargetCount { get; }

    public int PlacedCount { get; private set; }

    /// <summary>
    /// 평지 경계 NearDistance 이내 배치 비율
    /// </summary>
    public float NearRatio => PlacedCount > 0 ? (float)_nearCount / PlacedCount : 0f;

    /// <summary>
    /// 평지 밖 배치 중 경계에서 가장 먼 거리대 끝 (m)
    /// </summary>
    public float FarthestOutside { get; private set; }

    /// <summary>
    /// 레퍼런스에서 가장 자주 함께 있던 종류 중 생성에도 심은 종류, 없으면 null
    /// </summary>
    public GameObject CompanionPrefab { get; private set; }

    /// <summary>
    /// 생성 배치 중 CompanionRadius 안에 CompanionPrefab이 있는 비율
    /// </summary>
    public float CompanionRatio { get; private set; }

    /// <summary>
    /// 지정 지역에 배치된 비율
    /// </summary>
    /// <param name="region">지역</param>
    /// <returns>0~1 비율</returns>
    public float GetRegionRatio(TerrainMapRegion region)
    {
        return PlacedCount > 0 ? (float)_regionCounts[(int)region] / PlacedCount : 0f;
    }

    internal void AddPlaced(TerrainMapRegion region, int bin)
    {
        _regionCounts[(int)region]++;
        if (bin < TerrainMapPropProfile.NearBins) _nearCount++;
        if (region != TerrainMapRegion.Walkable)
            FarthestOutside = Mathf.Max(FarthestOutside, (bin + 1) * TerrainMapPropProfile.BinSize);
        PlacedCount++;
    }

    internal void SetCompanion(GameObject prefab, float ratio)
    {
        CompanionPrefab = prefab;
        CompanionRatio = ratio;
    }
}

/// <summary>
/// 생성 맵 배치물 배치 결과
/// </summary>
public class TerrainMapPropPlan
{
    readonly List<TerrainMapPropPlanEntry> _entries;

    internal TerrainMapPropPlan(TreePrototype[] prototypes, TreeInstance[] instances, List<TerrainMapPropPlanEntry> entries)
    {
        Prototypes = prototypes;
        Instances = instances;
        _entries = entries;
    }

    /// <summary>
    /// 심기로 선택한 종류만 담은 TreePrototype 목록
    /// </summary>
    public TreePrototype[] Prototypes { get; }

    public TreeInstance[] Instances { get; }

    /// <summary>
    /// Prototypes와 같은 순서의 종류별 요약
    /// </summary>
    public IReadOnlyList<TerrainMapPropPlanEntry> Entries => _entries;

    /// <summary>
    /// 프리팹에 해당하는 종류별 요약 검색
    /// </summary>
    /// <param name="prefab">찾을 프리팹</param>
    /// <returns>요약, 심지 않은 종류면 null</returns>
    public TerrainMapPropPlanEntry Find(GameObject prefab)
    {
        foreach (TerrainMapPropPlanEntry entry in _entries)
        {
            if (entry.Prefab == prefab) return entry;
        }
        return null;
    }

    /// <summary>
    /// 지정 구분의 배치 개수 합계
    /// </summary>
    /// <param name="category">배치물 구분</param>
    /// <returns>배치 개수</returns>
    public int CountPlaced(TerrainMapPropCategory category)
    {
        int count = 0;
        foreach (TerrainMapPropPlanEntry entry in _entries)
        {
            if (entry.Category == category) count += entry.PlacedCount;
        }
        return count;
    }
}

/// <summary>
/// 레퍼런스 종류별 분포·종류 쌍 관계와 선택값 기반 배치물 배치 계산
/// </summary>
public static class TerrainMapPropPlanter
{
    const int AttemptsPerProp = 30;         // 목표 1개당 위치 시도 횟수
    const float GridSize = 2f;              // 간격 검사 격자 크기 (m)
    const float GapWavelength = 12f;        // 경계를 따라 숲이 끊기는 구간 크기 (m)
    const float GapFloor = 0.05f;           // 빈틈 구간 최소 배치 가중치
    const float ClusterThreshold = 0.45f;   // 군락형 노이즈에서 군락으로 볼 최소 값
    const float ClusterFloor = 0.05f;       // 군락 밖 최소 배치 가중치
    const float ClusterWavelength = 6f;     // 군락 크기 (이웃 간격 배율)
    const float AttractMax = 5f;            // 상대 근처 가중치 상한 (끌림)
    const float FollowAttraction = 1.5f;    // 이 이상 끌리고
    const float FollowCompanion = 0.3f;     // 이 이상 함께 있던 상대는 먼저 심음

    static readonly TerrainMapPropCategory[] PlantOrder =
    {
        TerrainMapPropCategory.Tree, TerrainMapPropCategory.Rock, TerrainMapPropCategory.Grass
    };

    sealed class MapCells
    {
        public TerrainMapRegion[] Regions;
        public int[] Slots;
        public bool[] Steep;        // 높이 단계가 다른 칸과 맞닿은 절벽면 칸
    }

    /// <summary>
    /// 미리보기와 생성이 같은 배치를 얻는 배치물 전용 seed
    /// </summary>
    /// <param name="seed">맵 생성 seed</param>
    /// <returns>배치물 seed</returns>
    public static int GetPropSeed(int seed) => unchecked(seed * 31 + 13);

    static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    static bool IsFollow(TerrainMapPropRelation relation)
    {
        return relation.HasNeighbors && relation.Attraction >= FollowAttraction &&
               relation.CompanionRatio >= FollowCompanion;
    }

    static float GetSpacing(TerrainMapPropStats stats, TerrainMapPropStats other,
        Dictionary<TerrainMapPropStats, TerrainMapPropCategory> categories)
    {
        if (stats == other) return stats.MinSpacing;

        // 풀은 나무·돌 밑에도 자라는 바닥 덮개라 다른 종류와 간격 제한 없음
        if (categories[stats] == TerrainMapPropCategory.Grass || categories[other] == TerrainMapPropCategory.Grass)
            return 0f;

        // 레퍼런스에서 가까이 있던 적이 없는 쌍은 잴 근거가 없어 간격 제한 없음
        TerrainMapPropRelation relation = stats.GetRelation(other);
        if (relation == null || !relation.HasNeighbors) return 0f;
        return relation.MinSpacing;
    }

    static MapCells BuildCells(TerrainMapLayout layout)
    {
        int width = layout.Width;
        int height = layout.Height;
        float cellSize = (layout.CellSize.x + layout.CellSize.y) * 0.5f;
        bool[] blocked = new bool[width * height];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                blocked[z * width + x] = !layout.IsWalkable(x, z);
            }
        }

        MapCells cells = new MapCells
        {
            Regions = new TerrainMapRegion[width * height],
            Slots = new int[width * height]
        };
        float[] outside = layout.GetWalkableDistance();
        float[] inside = TerrainMapLayout.DistanceTo(blocked, width, height);
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                bool walkable = !blocked[index];
                cells.Regions[index] = walkable ? TerrainMapRegion.Walkable
                    : layout.IsLow(x, z) ? TerrainMapRegion.Low : TerrainMapRegion.High;
                float distance = (walkable ? inside[index] : outside[index]) * cellSize;
                cells.Slots[index] = TerrainMapPropProfile.GetSlot(cells.Regions[index], TerrainMapPropProfile.GetBin(distance));
            }
        }

        // 평지·높은 곳·낮은 곳 중 다른 단계와 8방향으로 맞닿은 칸은 절벽 경사면
        cells.Steep = new bool[width * height];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                for (int offsetZ = -1; offsetZ <= 1 && !cells.Steep[index]; offsetZ++)
                {
                    int sampleZ = z + offsetZ;
                    if (sampleZ < 0 || sampleZ >= height) continue;
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int sampleX = x + offsetX;
                        if (sampleX < 0 || sampleX >= width) continue;
                        if (cells.Regions[sampleZ * width + sampleX] == cells.Regions[index]) continue;
                        cells.Steep[index] = true;
                        break;
                    }
                }
            }
        }
        return cells;
    }

    static List<int> OrderByFollow(List<TerrainMapPropStats> plantStats)
    {
        // 끌리는 상대가 먼저 심어지도록 따라가는 종류를 상대 뒤로 이동, 서로 따라가는 순환은 횟수 제한
        List<int> order = new List<int>();
        for (int i = 0; i < plantStats.Count; i++)
        {
            order.Add(i);
        }

        for (int pass = 0; pass < plantStats.Count; pass++)
        {
            bool moved = false;
            for (int i = 0; i < order.Count && !moved; i++)
            {
                foreach (TerrainMapPropRelation relation in plantStats[order[i]].Relations)
                {
                    if (!IsFollow(relation)) continue;
                    int leader = order.FindIndex(k => plantStats[k] == relation.Other);
                    if (leader <= i) continue;

                    int item = order[i];
                    order.RemoveAt(i);
                    order.Insert(leader, item);
                    moved = true;
                    break;
                }
            }
            if (!moved) break;
        }
        return order;
    }

    static float GetExpectedCount(TerrainMapPropStats stats, MapCells cells, float cellArea)
    {
        float expected = 0f;
        foreach (int slot in cells.Slots)
        {
            expected += stats.GetDensity(slot) * cellArea;
        }
        return expected;
    }

    static float[] BuildCloseness(TerrainMapLayout layout, List<Vector2> placed)
    {
        // 배치물 위치 1에서 CompanionRadius 거리 0까지 줄어드는 가까움 정도
        float[] closeness = new float[layout.Width * layout.Height];
        float radius = TerrainMapPropProfile.CompanionRadius;
        int reachX = Mathf.CeilToInt(radius / layout.CellSize.x);
        int reachZ = Mathf.CeilToInt(radius / layout.CellSize.y);
        foreach (Vector2 position in placed)
        {
            int centerX = Mathf.RoundToInt(position.x / layout.CellSize.x);
            int centerZ = Mathf.RoundToInt(position.y / layout.CellSize.y);
            for (int z = centerZ - reachZ; z <= centerZ + reachZ; z++)
            {
                if (z < 0 || z >= layout.Height) continue;
                for (int x = centerX - reachX; x <= centerX + reachX; x++)
                {
                    if (x < 0 || x >= layout.Width) continue;
                    float distance = Vector2.Distance(new Vector2(x * layout.CellSize.x, z * layout.CellSize.y), position);
                    if (distance >= radius) continue;
                    int index = z * layout.Width + x;
                    closeness[index] = Mathf.Max(closeness[index], 1f - distance / radius);
                }
            }
        }
        return closeness;
    }

    static float BuildWeights(TerrainMapPropStats stats, TerrainMapLayout layout, MapCells cells,
        Dictionary<TerrainMapPropStats, float[]> closenessMaps, System.Random random, float[] cumulative)
    {
        Vector2 gapOffset = new Vector2(Range(random, 0f, 1000f), Range(random, 0f, 1000f));
        Vector2 clusterOffset = new Vector2(Range(random, 0f, 1000f), Range(random, 0f, 1000f));
        bool useGaps = stats.Coverage > 0f && stats.Coverage < 1f;
        bool clustered = stats.Pattern == TerrainMapPropPattern.Clustered;
        float clusterWavelength = Mathf.Max(stats.NearestMedian * ClusterWavelength, layout.CellSize.x * 4f);

        // 먼저 심은 상대 종류 중 레퍼런스에서 끌렸던 종류 근처만 가중치 증가, 피함은 구멍을 만들어 적용 안 함
        List<KeyValuePair<float[], float>> influences = new List<KeyValuePair<float[], float>>();
        foreach (TerrainMapPropRelation relation in stats.Relations)
        {
            if (!relation.HasNeighbors || relation.Attraction <= 1f) continue;
            if (!closenessMaps.TryGetValue(relation.Other, out float[] map)) continue;
            influences.Add(new KeyValuePair<float[], float>(map, Mathf.Min(relation.Attraction, AttractMax) - 1f));
        }

        // 경계 덮음 비율만큼만 노이즈 낮은 구간에 몰아 경계를 따라 숲이 끊기는 빈틈 생성
        float[] gapNoise = new float[cells.Slots.Length];
        List<float> candidates = new List<float>();
        for (int z = 0; z < layout.Height; z++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                int index = z * layout.Width + x;
                gapNoise[index] = Mathf.PerlinNoise(x * layout.CellSize.x / GapWavelength + gapOffset.x,
                    z * layout.CellSize.y / GapWavelength + gapOffset.y);
                if (useGaps && !cells.Steep[index] && stats.GetDensity(cells.Slots[index]) > 0f) candidates.Add(gapNoise[index]);
            }
        }
        float threshold = useGaps ? TerrainMapPropProfile.Percentile(candidates, stats.Coverage) : float.MaxValue;

        // 심는 한계 밖 칸은 밀도 0이라 가중치 없음
        float total = 0f;
        for (int z = 0; z < layout.Height; z++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                int index = z * layout.Width + x;
                // 절벽 경사면에는 배치하지 않고 개수는 주변 평평한 칸으로 넘김
                float weight = cells.Steep[index] ? 0f : stats.GetDensity(cells.Slots[index]);
                if (weight > 0f)
                {
                    if (gapNoise[index] > threshold) weight *= GapFloor;
                    if (clustered)
                    {
                        // 노이즈 높은 곳에 몰아 심어 레퍼런스 군락 형태 재현
                        float noise = Mathf.PerlinNoise(x * layout.CellSize.x / clusterWavelength + clusterOffset.x,
                            z * layout.CellSize.y / clusterWavelength + clusterOffset.y);
                        weight *= Mathf.Max(ClusterFloor, Mathf.Clamp01((noise - ClusterThreshold) / (1f - ClusterThreshold)));
                    }
                    foreach (KeyValuePair<float[], float> influence in influences)
                    {
                        weight *= 1f + influence.Value * influence.Key[index];
                    }
                }
                total += weight;
                cumulative[index] = total;
            }
        }
        return total;
    }

    static int PickCell(float[] cumulative, float total, System.Random random)
    {
        // 누적 가중치에서 처음으로 뽑은 값보다 큰 칸
        float pick = (float)random.NextDouble() * total;
        int low = 0;
        int high = cumulative.Length - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (cumulative[middle] <= pick) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    static bool IsTooClose(TerrainMapPropStats stats, Vector2 position,
        Dictionary<TerrainMapPropStats, TerrainMapPointGrid> grids,
        Dictionary<TerrainMapPropStats, TerrainMapPropCategory> categories)
    {
        // 이미 심은 모든 종류와 레퍼런스 쌍별 최소 간격 비교
        foreach (KeyValuePair<TerrainMapPropStats, TerrainMapPointGrid> other in grids)
        {
            if (other.Value.AnyWithin(position, GetSpacing(stats, other.Key, categories))) return true;
        }
        return false;
    }

    static void MeasureCompanions(Dictionary<TerrainMapPropStats, TerrainMapPropPlanEntry> entries,
        Dictionary<TerrainMapPropStats, TerrainMapPointGrid> grids,
        Dictionary<TerrainMapPropStats, List<Vector2>> positions)
    {
        // 레퍼런스 동반 비율과 비교할 생성 결과 동반 비율
        foreach (KeyValuePair<TerrainMapPropStats, TerrainMapPropPlanEntry> pair in entries)
        {
            foreach (TerrainMapPropRelation relation in pair.Key.Relations)
            {
                if (!relation.HasNeighbors || !grids.TryGetValue(relation.Other, out TerrainMapPointGrid grid)) continue;

                List<Vector2> placed = positions[pair.Key];
                int near = 0;
                foreach (Vector2 position in placed)
                {
                    if (grid.AnyWithin(position, TerrainMapPropProfile.CompanionRadius)) near++;
                }
                pair.Value.SetCompanion(relation.Other.Prefab, placed.Count > 0 ? (float)near / placed.Count : 0f);
                break;
            }
        }
    }

    /// <summary>
    /// 배치 격자에 레퍼런스 종류별 경계 거리 밀도·심는 한계·덮음 비율·군락 형태와 종류 쌍별 간격·끌림을 따라 배치물 배치
    /// </summary>
    /// <param name="layout">생성 배치 격자</param>
    /// <param name="size">생성 Terrain 크기</param>
    /// <param name="profile">레퍼런스 배치물 측정 결과</param>
    /// <param name="settings">종류별 심기 선택값</param>
    /// <param name="seed">GetPropSeed 결과</param>
    /// <returns>배치 결과, 선택값이 없거나 꺼진 종류는 제외</returns>
    public static TerrainMapPropPlan Plan(TerrainMapLayout layout, Vector3 size, TerrainMapPropProfile profile,
        IReadOnlyList<TerrainMapPropSetting> settings, int seed)
    {
        System.Random random = new System.Random(seed);
        MapCells cells = BuildCells(layout);
        float cellArea = layout.CellSize.x * layout.CellSize.y;

        // 기본은 나무, 돌, 풀 순서
        List<TerrainMapPropStats> plantStats = new List<TerrainMapPropStats>();
        List<TerrainMapPropSetting> plantSettings = new List<TerrainMapPropSetting>();
        foreach (TerrainMapPropCategory category in PlantOrder)
        {
            foreach (TerrainMapPropStats stats in profile.Stats)
            {
                TerrainMapPropSetting setting = TerrainMapPropSetting.Find(settings, stats.Prefab);
                if (setting == null || !setting.Enabled || setting.DensityMultiplier <= 0f) continue;
                if (setting.Category != category) continue;
                plantStats.Add(stats);
                plantSettings.Add(setting);
            }
        }

        Dictionary<TerrainMapPropStats, TerrainMapPropCategory> categories = new Dictionary<TerrainMapPropStats, TerrainMapPropCategory>();
        for (int i = 0; i < plantStats.Count; i++)
        {
            categories[plantStats[i]] = plantSettings[i].Category;
        }

        Dictionary<TerrainMapPropStats, TerrainMapPointGrid> grids = new Dictionary<TerrainMapPropStats, TerrainMapPointGrid>();
        Dictionary<TerrainMapPropStats, List<Vector2>> positions = new Dictionary<TerrainMapPropStats, List<Vector2>>();
        Dictionary<TerrainMapPropStats, float[]> closenessMaps = new Dictionary<TerrainMapPropStats, float[]>();
        Dictionary<TerrainMapPropStats, TerrainMapPropPlanEntry> entryMap = new Dictionary<TerrainMapPropStats, TerrainMapPropPlanEntry>();
        List<TreePrototype> prototypes = new List<TreePrototype>();
        List<TreeInstance> instances = new List<TreeInstance>();
        List<TerrainMapPropPlanEntry> entries = new List<TerrainMapPropPlanEntry>();
        float[] cumulative = new float[cells.Slots.Length];

        foreach (int k in OrderByFollow(plantStats))
        {
            TerrainMapPropStats stats = plantStats[k];
            TerrainMapPropSetting setting = plantSettings[k];
            int target = Mathf.RoundToInt(GetExpectedCount(stats, cells, cellArea) * setting.DensityMultiplier);
            float total = BuildWeights(stats, layout, cells, closenessMaps, random, cumulative);
            int prototypeIndex = prototypes.Count;
            TerrainMapPropPlanEntry entry = new TerrainMapPropPlanEntry(stats.Prefab, setting.Category, target);
            prototypes.Add(new TreePrototype
            {
                prefab = stats.Prototype.prefab,
                bendFactor = stats.Prototype.bendFactor,
                navMeshLod = stats.Prototype.navMeshLod
            });
            entries.Add(entry);
            entryMap[stats] = entry;

            TerrainMapPointGrid grid = new TerrainMapPointGrid(GridSize);
            List<Vector2> placed = new List<Vector2>();
            grids[stats] = grid;
            positions[stats] = placed;

            for (int attempt = 0; total > 0f && attempt < target * AttemptsPerProp && entry.PlacedCount < target; attempt++)
            {
                int cell = PickCell(cumulative, total, random);
                int x = cell % layout.Width;
                int z = cell / layout.Width;
                Vector2 position = new Vector2(
                    Mathf.Clamp((x + Range(random, -0.5f, 0.5f)) * layout.CellSize.x, 0f, size.x),
                    Mathf.Clamp((z + Range(random, -0.5f, 0.5f)) * layout.CellSize.y, 0f, size.z));
                if (IsTooClose(stats, position, grids, categories)) continue;

                grid.Add(position);
                placed.Add(position);
                Vector2 scale = stats.Scales[random.Next(stats.Scales.Count)];
                instances.Add(new TreeInstance
                {
                    position = new Vector3(position.x / size.x, 0f, position.y / size.z),
                    prototypeIndex = prototypeIndex,
                    widthScale = scale.x * setting.WidthMultiplier,
                    heightScale = scale.y * setting.HeightMultiplier,
                    rotation = Range(random, 0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white
                });
                entry.AddPlaced(cells.Regions[cell], cells.Slots[cell] % TerrainMapPropProfile.BinCount);
            }

            // 뒤에 심을 종류가 끌림을 계산할 가까움 정도
            closenessMaps[stats] = BuildCloseness(layout, placed);
        }

        MeasureCompanions(entryMap, grids, positions);
        return new TerrainMapPropPlan(prototypes.ToArray(), instances.ToArray(), entries);
    }
}
