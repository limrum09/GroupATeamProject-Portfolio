using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 이동 높이 기준 칸 지역
/// </summary>
public enum TerrainMapRegion
{
    Walkable,
    High,
    Low
}

/// <summary>
/// 같은 종류 배치물의 퍼짐 형태
/// </summary>
public enum TerrainMapPropPattern
{
    Unknown,        // 표본 부족
    Clustered,      // 군락형
    Random,         // 무작위
    Even            // 고르게
}

/// <summary>
/// 레퍼런스 Terrain 하나의 지역·배치물 인식 결과 미리보기 자료
/// </summary>
public class TerrainMapPropReferenceView
{
    readonly TerrainMapRegion[] _regions;
    readonly bool[] _inside;
    readonly bool[] _edgeCliffs;
    readonly float[] _edgeDistances;
    readonly List<Vector2> _positions = new List<Vector2>();                // 격자 기준 0~1 위치
    readonly List<TerrainMapPropStats> _owners = new List<TerrainMapPropStats>();

    internal TerrainMapPropReferenceView(string name, int width, int height, TerrainMapRegion[] regions, bool[] inside,
        bool[] edgeCliffs, float[] edgeDistances, float walkableRatio, float boundaryLength, float cliffWidth)
    {
        Name = name;
        Width = width;
        Height = height;
        _regions = regions;
        _inside = inside;
        _edgeCliffs = edgeCliffs;
        _edgeDistances = edgeDistances;
        WalkableRatio = walkableRatio;
        BoundaryLength = boundaryLength;
        CliffWidth = cliffWidth;
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public float WalkableRatio { get; }

    /// <summary>
    /// 평지와 평지 밖이 맞닿은 경계 길이 (m)
    /// </summary>
    public float BoundaryLength { get; }

    /// <summary>
    /// 평지에 이어진 절벽 넓이 ÷ 경계 길이로 구한 평균 절벽 가로 폭 (m)
    /// </summary>
    public float CliffWidth { get; }

    public int InstanceCount => _positions.Count;

    /// <summary>
    /// 지정 칸 지역
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>지역</returns>
    public TerrainMapRegion GetRegion(int x, int z) => _regions[z * Width + x];

    /// <summary>
    /// 지정 칸 Terrain 범위 포함 여부
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>포함 여부</returns>
    public bool IsInside(int x, int z) => _inside[z * Width + x];

    /// <summary>
    /// 지정 칸이 평지에 이어진 절벽인지 여부
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>경계 절벽 여부</returns>
    public bool IsCliff(int x, int z) => _edgeCliffs[z * Width + x];

    /// <summary>
    /// 지정 칸 경계까지 거리, 평지는 안쪽 깊이, 평지 밖은 절벽 꼭대기까지 거리 (m)
    /// </summary>
    /// <param name="x">X 방향 칸 번호</param>
    /// <param name="z">Z 방향 칸 번호</param>
    /// <returns>0 이상 거리</returns>
    public float GetEdgeDistance(int x, int z) => _edgeDistances[z * Width + x];

    /// <summary>
    /// 배치물 격자 기준 0~1 위치
    /// </summary>
    /// <param name="index">배치물 번호</param>
    /// <returns>X, Z 비율 위치</returns>
    public Vector2 GetPosition(int index) => _positions[index];

    /// <summary>
    /// 배치물이 속한 종류 측정 결과
    /// </summary>
    /// <param name="index">배치물 번호</param>
    /// <returns>종류 측정 결과</returns>
    public TerrainMapPropStats GetOwner(int index) => _owners[index];

    internal void AddInstance(TerrainMapPropStats owner, Vector2 position)
    {
        _owners.Add(owner);
        _positions.Add(position);
    }
}

/// <summary>
/// 레퍼런스 배치물 두 종류 사이 관계 측정 결과 (기준 종류 → 상대 종류)
/// </summary>
public class TerrainMapPropRelation
{
    const float MaxAttraction = 5f;     // 무작위 기대 비율이 0일 때 끌림 상한

    readonly List<float> _nearest = new List<float>();
    int _samples;
    int _companions;

    internal TerrainMapPropRelation(TerrainMapPropStats other)
    {
        Other = other;
    }

    public TerrainMapPropStats Other { get; }

    /// <summary>
    /// 기준 종류에서 가장 가까운 상대 종류까지 거리 하위 5%, 생성 시 두 종류 최소 간격
    /// </summary>
    public float MinSpacing { get; private set; }

    public float NearestMedian { get; private set; }

    /// <summary>
    /// 기준 종류 중 CompanionRadius 안에 상대 종류가 있는 비율
    /// </summary>
    public float CompanionRatio { get; private set; }

    /// <summary>
    /// 무작위로 섞였을 때 대비 동반 비율, 1보다 크면 끌림, 작으면 피함
    /// </summary>
    public float Attraction { get; private set; }

    /// <summary>
    /// PairSearchRadius 안에서 상대 종류를 한 번이라도 찾았는지 여부
    /// </summary>
    public bool HasNeighbors => _nearest.Count > 0;

    internal void AddSample(float nearest)
    {
        _samples++;
        if (nearest == float.MaxValue) return;
        _nearest.Add(nearest);
        if (nearest <= TerrainMapPropProfile.CompanionRadius) _companions++;
    }

    internal void Finish(float expectedRatio)
    {
        MinSpacing = TerrainMapPropProfile.Percentile(_nearest, 0.05f);
        NearestMedian = TerrainMapPropProfile.Percentile(_nearest, 0.5f);
        CompanionRatio = _samples > 0 ? (float)_companions / _samples : 0f;
        if (expectedRatio > 0f) Attraction = Mathf.Min(MaxAttraction, CompanionRatio / expectedRatio);
        else Attraction = CompanionRatio > 0f ? MaxAttraction : 1f;
    }
}

/// <summary>
/// 레퍼런스 배치물 종류 하나의 평지 경계 거리별 분포 측정 결과
/// </summary>
public class TerrainMapPropStats
{
    const int MinPatternSamples = 5;
    const float ClusteredIndex = 0.7f;      // 무작위 배치 대비 이웃 거리 비율이 이보다 작으면 군락형
    const float EvenIndex = 1.3f;           // 이보다 크면 고르게 퍼짐
    const float LimitPercentile = 0.97f;    // 심는 한계로 볼 개체 누적 비율

    readonly List<Vector2> _scales = new List<Vector2>();
    readonly List<float> _edgeDistances = new List<float>();
    readonly List<float> _nearestDistances = new List<float>();
    readonly int[] _regionCounts = new int[TerrainMapPropProfile.RegionCount];
    readonly int[] _slotCounts = new int[TerrainMapPropProfile.SlotCount];
    readonly float[] _densities = new float[TerrainMapPropProfile.SlotCount];
    readonly Dictionary<TerrainMapPropStats, TerrainMapPropRelation> _relations =
        new Dictionary<TerrainMapPropStats, TerrainMapPropRelation>();
    readonly List<TerrainMapPropRelation> _relationList = new List<TerrainMapPropRelation>();
    float[] _areas;
    int _nearCount;
    int _coveredCells;

    internal TerrainMapPropStats(TreePrototype prototype)
    {
        Prototype = prototype;
    }

    public TreePrototype Prototype { get; }
    public GameObject Prefab => Prototype.prefab;
    public int Count { get; private set; }
    public IReadOnlyList<Vector2> Scales => _scales;
    public float MinScale { get; private set; }
    public float MaxScale { get; private set; }

    /// <summary>
    /// 레퍼런스 높이 배율 중간값
    /// </summary>
    public float MedianScale { get; private set; }

    public float MinWidthScale { get; private set; }
    public float MaxWidthScale { get; private set; }

    /// <summary>
    /// 프리팹 메시 크기 (배율 1 기준), HasPrefabSize가 false면 0
    /// </summary>
    public Vector3 PrefabSize { get; private set; }

    public bool HasPrefabSize { get; private set; }

    /// <summary>
    /// 평지 경계까지 거리 하위 5%, 평지 안쪽은 음수
    /// </summary>
    public float EdgeDistanceLow { get; private set; }

    public float EdgeDistanceMedian { get; private set; }
    public float EdgeDistanceHigh { get; private set; }

    /// <summary>
    /// 평지 밖 개체 97%가 들어오는 경계 바깥 거리, 이보다 먼 곳은 심지 않음 (m)
    /// </summary>
    public float OutsideLimit { get; private set; }

    /// <summary>
    /// 평지 위 개체 97%가 들어오는 경계 안쪽 깊이, 평지 위 개체가 없으면 0 (m)
    /// </summary>
    public float InsideLimit { get; private set; }

    /// <summary>
    /// 평지 경계 NearDistance 이내 비율
    /// </summary>
    public float NearRatio { get; private set; }

    /// <summary>
    /// 평지 경계 1m당 개수 (참고 수치)
    /// </summary>
    public float PerBoundaryMeter { get; private set; }

    /// <summary>
    /// 평지 경계 칸 중 NearDistance 안에 이 종류가 있는 비율
    /// </summary>
    public float Coverage { get; private set; }

    /// <summary>
    /// 같은 종류 가장 가까운 이웃 거리 중간값
    /// </summary>
    public float NearestMedian { get; private set; }

    /// <summary>
    /// 같은 종류 이웃 거리 하위 5%, 생성 시 최소 간격
    /// </summary>
    public float MinSpacing { get; private set; }

    /// <summary>
    /// 무작위 배치 대비 평균 이웃 거리 비율
    /// </summary>
    public float ClusterIndex { get; private set; }

    public TerrainMapPropPattern Pattern { get; private set; }

    /// <summary>
    /// 지정 지역에 놓인 비율
    /// </summary>
    /// <param name="region">지역</param>
    /// <returns>0~1 비율</returns>
    public float GetRegionRatio(TerrainMapRegion region)
    {
        return Count > 0 ? (float)_regionCounts[(int)region] / Count : 0f;
    }

    /// <summary>
    /// 지역·평지 경계 거리대 칸의 1㎡당 개수, 심는 한계 밖은 0
    /// </summary>
    /// <param name="slot">TerrainMapPropProfile.GetSlot 기준 칸</param>
    /// <returns>1㎡당 개수</returns>
    public float GetDensity(int slot) => _densities[slot];

    /// <summary>
    /// 평지 밖(높은 곳·낮은 곳) 지정 거리대 구간의 1㎡당 개수
    /// </summary>
    /// <param name="fromBin">시작 거리대 번호 (포함)</param>
    /// <param name="toBin">끝 거리대 번호 (제외)</param>
    /// <returns>1㎡당 개수, 넓이가 없으면 0</returns>
    public float GetOutsideDensity(int fromBin, int toBin)
    {
        if (_areas == null) return 0f;

        int count = 0;
        float area = 0f;
        for (int bin = Mathf.Max(0, fromBin); bin < Mathf.Min(toBin, TerrainMapPropProfile.BinCount); bin++)
        {
            foreach (TerrainMapRegion region in new[] { TerrainMapRegion.High, TerrainMapRegion.Low })
            {
                int slot = TerrainMapPropProfile.GetSlot(region, bin);
                count += _slotCounts[slot];
                area += _areas[slot];
            }
        }
        return area > 0f ? count / area : 0f;
    }

    /// <summary>
    /// 다른 종류와의 관계, 동반 비율 높은 순
    /// </summary>
    public IReadOnlyList<TerrainMapPropRelation> Relations => _relationList;

    /// <summary>
    /// 지정 종류와의 관계
    /// </summary>
    /// <param name="other">상대 종류</param>
    /// <returns>관계, 같은 Terrain에 함께 있던 적이 없으면 null</returns>
    public TerrainMapPropRelation GetRelation(TerrainMapPropStats other)
    {
        return _relations.TryGetValue(other, out TerrainMapPropRelation relation) ? relation : null;
    }

    internal TerrainMapPropRelation GetOrAddRelation(TerrainMapPropStats other)
    {
        if (_relations.TryGetValue(other, out TerrainMapPropRelation relation)) return relation;
        relation = new TerrainMapPropRelation(other);
        _relations[other] = relation;
        _relationList.Add(relation);
        return relation;
    }

    internal float GetRawDensity(int slot)
    {
        // 심는 한계로 자르기 전 1㎡당 개수
        if (_areas == null || _areas[slot] <= 0f) return 0f;
        return _slotCounts[slot] / _areas[slot];
    }

    internal void FinishRelations()
    {
        foreach (TerrainMapPropRelation relation in _relationList)
        {
            // 이 종류가 놓인 위치들에서 상대 종류 평균 밀도로 무작위 섞임 동반 기대 비율 계산
            float density = 0f;
            for (int slot = 0; slot < _slotCounts.Length; slot++)
            {
                density += _slotCounts[slot] * relation.Other.GetRawDensity(slot);
            }
            density = Count > 0 ? density / Count : 0f;
            float radius = TerrainMapPropProfile.CompanionRadius;
            relation.Finish(1f - Mathf.Exp(-density * Mathf.PI * radius * radius));
        }
        _relationList.Sort((a, b) => b.CompanionRatio.CompareTo(a.CompanionRatio));
    }

    internal void AddInstance(Vector2 scale, TerrainMapRegion region, float edgeDistance)
    {
        int bin = TerrainMapPropProfile.GetBin(edgeDistance);
        _scales.Add(scale);
        _regionCounts[(int)region]++;
        _slotCounts[TerrainMapPropProfile.GetSlot(region, bin)]++;
        _edgeDistances.Add(region == TerrainMapRegion.Walkable ? -edgeDistance : edgeDistance);
        if (bin < TerrainMapPropProfile.NearBins) _nearCount++;
        Count++;
    }

    internal void AddNearest(float distance)
    {
        _nearestDistances.Add(distance);
    }

    internal void AddCoveredCell()
    {
        _coveredCells++;
    }

    internal void Finish(float[] areas, float boundaryLength, int boundaryCells)
    {
        _areas = areas;
        PerBoundaryMeter = boundaryLength > 0f ? Count / boundaryLength : 0f;
        Coverage = boundaryCells > 0 ? (float)_coveredCells / boundaryCells : 0f;
        NearRatio = Count > 0 ? (float)_nearCount / Count : 0f;

        EdgeDistanceLow = TerrainMapPropProfile.Percentile(_edgeDistances, 0.05f);
        EdgeDistanceMedian = TerrainMapPropProfile.Percentile(_edgeDistances, 0.5f);
        EdgeDistanceHigh = TerrainMapPropProfile.Percentile(_edgeDistances, 0.95f);

        // 개체 97%가 들어오는 거리를 심는 한계로 사용해 멀리 떨어진 몇 개에 범위가 끌려가지 않게 함
        List<float> outside = new List<float>();
        List<float> inside = new List<float>();
        foreach (float distance in _edgeDistances)
        {
            if (distance < 0f) inside.Add(-distance);
            else outside.Add(distance);
        }
        OutsideLimit = TerrainMapPropProfile.Percentile(outside, LimitPercentile);
        InsideLimit = TerrainMapPropProfile.Percentile(inside, LimitPercentile);

        float occupiedArea = 0f;
        for (int slot = 0; slot < _slotCounts.Length; slot++)
        {
            if (_slotCounts[slot] == 0 || areas[slot] <= 0f) continue;
            occupiedArea += areas[slot];

            // 거리대 시작이 심는 한계보다 먼 칸은 밀도 0
            bool walkable = slot / TerrainMapPropProfile.BinCount == (int)TerrainMapRegion.Walkable;
            float limit = walkable ? InsideLimit : OutsideLimit;
            float binStart = slot % TerrainMapPropProfile.BinCount * TerrainMapPropProfile.BinSize;
            _densities[slot] = binStart <= limit ? _slotCounts[slot] / areas[slot] : 0f;
        }

        NearestMedian = TerrainMapPropProfile.Percentile(_nearestDistances, 0.5f);
        MinSpacing = TerrainMapPropProfile.Percentile(_nearestDistances, 0.05f);

        MinScale = float.MaxValue;
        MaxScale = 0f;
        MinWidthScale = float.MaxValue;
        MaxWidthScale = 0f;
        List<float> heights = new List<float>(_scales.Count);
        foreach (Vector2 scale in _scales)
        {
            MinScale = Mathf.Min(MinScale, scale.y);
            MaxScale = Mathf.Max(MaxScale, scale.y);
            MinWidthScale = Mathf.Min(MinWidthScale, scale.x);
            MaxWidthScale = Mathf.Max(MaxWidthScale, scale.x);
            heights.Add(scale.y);
        }
        MedianScale = TerrainMapPropProfile.Percentile(heights, 0.5f);

        // 배율을 실제 높이(m)로 보여 주기 위한 프리팹 메시 크기
        HasPrefabSize = TerrainMapPropClassifier.TryGetSize(Prefab, out Vector3 prefabSize);
        PrefabSize = HasPrefabSize ? prefabSize : Vector3.zero;

        float density = occupiedArea > 0f ? Count / occupiedArea : 0f;
        if (_nearestDistances.Count < MinPatternSamples || density <= 0f)
        {
            Pattern = TerrainMapPropPattern.Unknown;
            return;
        }

        // 같은 밀도의 무작위 배치 평균 이웃 거리(0.5/√밀도)와 비교한 퍼짐 형태
        float mean = 0f;
        foreach (float distance in _nearestDistances)
        {
            mean += distance;
        }
        mean /= _nearestDistances.Count;
        ClusterIndex = mean / (0.5f / Mathf.Sqrt(density));
        Pattern = ClusterIndex < ClusteredIndex ? TerrainMapPropPattern.Clustered
            : ClusterIndex > EvenIndex ? TerrainMapPropPattern.Even
            : TerrainMapPropPattern.Random;
    }
}

/// <summary>
/// 레퍼런스 Terrain 배치물 종류별 평지 경계 거리 기준 분포 통합 측정
/// </summary>
public class TerrainMapPropProfile
{
    public const int RegionCount = 3;
    public const int BinCount = 30;             // 평지 경계 거리대 수
    public const float BinSize = 1f;            // 평지 경계 거리대 폭 (m)
    public const int SlotCount = RegionCount * BinCount;
    public const int NearBins = 6;              // 경계 근처로 보는 거리대 수
    public const float NearDistance = BinSize * NearBins;
    public const float CompanionRadius = 3f;    // 함께 있다고 보는 두 종류 거리 (m)
    const float PairSearchRadius = 20f;         // 다른 종류 최대 검색 반경 (m)
    const float NearestSearchRadius = 50f;      // 이웃 거리 최대 검색 반경 (m)
    const float NeighborGridSize = 2f;

    readonly List<TerrainMapPropStats> _stats = new List<TerrainMapPropStats>();
    readonly List<TerrainMapPropReferenceView> _views = new List<TerrainMapPropReferenceView>();
    readonly float[] _areas = new float[SlotCount];
    int _boundaryCells;
    float _cliffArea;

    /// <summary>
    /// 종류별 측정 결과, 개수 많은 순
    /// </summary>
    public IReadOnlyList<TerrainMapPropStats> Stats => _stats;

    /// <summary>
    /// 레퍼런스 Terrain별 인식 결과 미리보기 자료
    /// </summary>
    public IReadOnlyList<TerrainMapPropReferenceView> Views => _views;

    public int TotalCount { get; private set; }

    /// <summary>
    /// 레퍼런스 전체 평지 경계 길이 (m)
    /// </summary>
    public float BoundaryLength { get; private set; }

    /// <summary>
    /// 레퍼런스 전체 평균 절벽 가로 폭 (m)
    /// </summary>
    public float AverageCliffWidth => BoundaryLength > 0f ? _cliffArea / BoundaryLength : 0f;

    /// <summary>
    /// 종류별 경계 바깥 심는 한계 중 가장 먼 거리 (m)
    /// </summary>
    public float MaxOutsideLimit { get; private set; }

    /// <summary>
    /// 평지 경계 거리의 거리대 번호
    /// </summary>
    /// <param name="distance">평지 경계까지 거리 (m)</param>
    /// <returns>0 ~ BinCount-1</returns>
    public static int GetBin(float distance)
    {
        if (!(distance < BinSize * BinCount)) return BinCount - 1;
        return Mathf.Max(0, Mathf.FloorToInt(distance / BinSize));
    }

    /// <summary>
    /// 지역과 거리대 조합 칸 번호
    /// </summary>
    /// <param name="region">지역</param>
    /// <param name="bin">거리대 번호</param>
    /// <returns>0 ~ SlotCount-1</returns>
    public static int GetSlot(TerrainMapRegion region, int bin) => (int)region * BinCount + bin;

    /// <summary>
    /// 평지 칸과 맞닿은 평지 밖 칸 경계 길이 측정
    /// </summary>
    /// <param name="regions">칸별 지역</param>
    /// <param name="inside">칸별 범위 포함 여부, null이면 전부 포함</param>
    /// <param name="width">X 방향 칸 수</param>
    /// <param name="height">Z 방향 칸 수</param>
    /// <param name="step">칸 간격 (m)</param>
    /// <param name="boundaryCells">경계 평지 칸 번호를 담을 목록, 필요 없으면 null</param>
    /// <returns>경계 길이 (m)</returns>
    public static float MeasureBoundary(TerrainMapRegion[] regions, bool[] inside, int width, int height, Vector2 step,
        List<int> boundaryCells)
    {
        float length = 0f;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                if (inside != null && !inside[index]) continue;
                if (regions[index] != TerrainMapRegion.Walkable) continue;

                // 좌우 이웃은 세로 변, 앞뒤 이웃은 가로 변 길이만큼 경계
                float before = length;
                if (IsBlocked(regions, inside, width, height, x - 1, z)) length += step.y;
                if (IsBlocked(regions, inside, width, height, x + 1, z)) length += step.y;
                if (IsBlocked(regions, inside, width, height, x, z - 1)) length += step.x;
                if (IsBlocked(regions, inside, width, height, x, z + 1)) length += step.x;
                if (length > before) boundaryCells?.Add(index);
            }
        }
        return length;
    }

    static bool IsBlocked(TerrainMapRegion[] regions, bool[] inside, int width, int height, int x, int z)
    {
        // 맵 가장자리 바깥은 경계로 보지 않음
        if (x < 0 || x >= width || z < 0 || z >= height) return false;
        int index = z * width + x;
        if (inside != null && !inside[index]) return false;
        return regions[index] != TerrainMapRegion.Walkable;
    }

    internal static float Percentile(List<float> values, float percentile)
    {
        if (values.Count == 0) return 0f;
        List<float> sorted = new List<float>(values);
        sorted.Sort();
        return sorted[Mathf.Clamp(Mathf.RoundToInt(percentile * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    TerrainMapPropStats GetOrAddStats(TreePrototype prototype)
    {
        foreach (TerrainMapPropStats stats in _stats)
        {
            if (stats.Prefab == prototype.prefab) return stats;
        }

        TerrainMapPropStats added = new TerrainMapPropStats(new TreePrototype
        {
            prefab = prototype.prefab,
            bendFactor = prototype.bendFactor,
            navMeshLod = prototype.navMeshLod
        });
        _stats.Add(added);
        return added;
    }

    internal bool TryAddTerrain(Terrain terrain, TerrainMapRegion[] regions, bool[] inside, bool[] edgeCliffs,
        float[] edgeDistances, int width, int height, Rect worldRect, out string error)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.GetPosition();
        Vector3 size = data.size;
        Vector2 step = new Vector2(worldRect.width / width, worldRect.height / height);
        float cellArea = step.x * step.y;
        int insideCells = 0;
        int walkableCells = 0;
        float cliffArea = 0f;
        for (int i = 0; i < regions.Length; i++)
        {
            if (!inside[i]) continue;
            insideCells++;
            if (regions[i] == TerrainMapRegion.Walkable) walkableCells++;
            if (edgeCliffs[i]) cliffArea += cellArea;
            _areas[GetSlot(regions[i], GetBin(edgeDistances[i]))] += cellArea;
        }

        List<int> boundaryCells = new List<int>();
        float boundaryLength = MeasureBoundary(regions, inside, width, height, step, boundaryCells);
        BoundaryLength += boundaryLength;
        _boundaryCells += boundaryCells.Count;
        _cliffArea += cliffArea;
        TerrainMapPropReferenceView view = new TerrainMapPropReferenceView(terrain.name, width, height, regions, inside,
            edgeCliffs, edgeDistances, insideCells > 0 ? (float)walkableCells / insideCells : 0f, boundaryLength,
            boundaryLength > 0f ? cliffArea / boundaryLength : 0f);
        _views.Add(view);

        TreePrototype[] prototypes = data.treePrototypes;
        Dictionary<TerrainMapPropStats, TerrainMapPointGrid> grids = new Dictionary<TerrainMapPropStats, TerrainMapPointGrid>();
        List<KeyValuePair<TerrainMapPropStats, Vector2>> placed = new List<KeyValuePair<TerrainMapPropStats, Vector2>>();
        foreach (TreeInstance tree in data.treeInstances)
        {
            TreePrototype prototype = prototypes[tree.prototypeIndex];
            if (prototype.prefab == null)
            {
                error = $"{terrain.name}: {tree.prototypeIndex + 1}번째 나무의 프리팹 연결이 없습니다.";
                return false;
            }

            Vector2 position = new Vector2(origin.x + tree.position.x * size.x, origin.z + tree.position.z * size.z);
            int x = Mathf.FloorToInt((position.x - worldRect.x) / step.x);
            int z = Mathf.FloorToInt((position.y - worldRect.y) / step.y);
            if (x < 0 || x >= width || z < 0 || z >= height) continue;
            int cell = z * width + x;
            if (!inside[cell]) continue;

            TerrainMapPropStats stats = GetOrAddStats(prototype);
            stats.AddInstance(new Vector2(tree.widthScale, tree.heightScale), regions[cell], edgeDistances[cell]);
            view.AddInstance(stats, new Vector2((x + 0.5f) / width, (z + 0.5f) / height));
            if (!grids.TryGetValue(stats, out TerrainMapPointGrid grid))
            {
                grid = new TerrainMapPointGrid(NeighborGridSize);
                grids[stats] = grid;
            }
            grid.Add(position);
            placed.Add(new KeyValuePair<TerrainMapPropStats, Vector2>(stats, position));
            TotalCount++;
        }

        // 같은 Terrain 안 같은 종류끼리 가장 가까운 이웃 거리 측정
        foreach (KeyValuePair<TerrainMapPropStats, Vector2> pair in placed)
        {
            float nearest = grids[pair.Key].FindNearest(pair.Value, NearestSearchRadius);
            if (nearest < float.MaxValue) pair.Key.AddNearest(nearest);
        }

        // 서로 다른 종류 쌍마다 가장 가까운 상대까지 거리와 동반 여부 측정
        foreach (KeyValuePair<TerrainMapPropStats, Vector2> pair in placed)
        {
            foreach (KeyValuePair<TerrainMapPropStats, TerrainMapPointGrid> other in grids)
            {
                if (other.Key == pair.Key) continue;
                pair.Key.GetOrAddRelation(other.Key).AddSample(other.Value.FindNearest(pair.Value, PairSearchRadius));
            }
        }

        // 경계 평지 칸마다 근처에 있는 종류 집계로 경계를 얼마나 덮는지 측정
        foreach (KeyValuePair<TerrainMapPropStats, TerrainMapPointGrid> pair in grids)
        {
            foreach (int cell in boundaryCells)
            {
                Vector2 position = new Vector2(worldRect.x + (cell % width + 0.5f) * step.x,
                    worldRect.y + (cell / width + 0.5f) * step.y);
                if (pair.Value.AnyWithin(position, NearDistance)) pair.Key.AddCoveredCell();
            }
        }

        error = string.Empty;
        return true;
    }

    internal void Finish()
    {
        MaxOutsideLimit = 0f;
        foreach (TerrainMapPropStats stats in _stats)
        {
            stats.Finish(_areas, BoundaryLength, _boundaryCells);
            MaxOutsideLimit = Mathf.Max(MaxOutsideLimit, stats.OutsideLimit);
        }

        // 다른 종류 밀도가 모두 계산된 뒤 관계 기대 비율 계산
        foreach (TerrainMapPropStats stats in _stats)
        {
            stats.FinishRelations();
        }
        _stats.Sort((a, b) => b.Count.CompareTo(a.Count));
    }
}
