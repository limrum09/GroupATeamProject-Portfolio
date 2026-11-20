using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 생성 TerrainData 크기와 해상도
/// </summary>
public readonly struct TerrainMapDimensions
{
    public TerrainMapDimensions(Vector3 size, int heightmapResolution, int alphamapResolution, int baseMapResolution)
    {
        Size = size;
        HeightmapResolution = heightmapResolution;
        AlphamapResolution = alphamapResolution;
        BaseMapResolution = baseMapResolution;
    }

    public Vector3 Size { get; }
    public int HeightmapResolution { get; }
    public int AlphamapResolution { get; }
    public int BaseMapResolution { get; }
}

/// <summary>
/// 배치 격자 기반 TerrainData 높이·나무 생성과 대상 Terrain 연결
/// </summary>
public static class TerrainMapGenerator
{
    /// <summary>
    /// 생성 TerrainData 저장 폴더
    /// </summary>
    public const string OutputFolder = "Assets/_Project/07.Terrain/MadeMap";

    const string UndoName = "레퍼런스 기반 맵 생성";
    const int BumpOctaves = 3;

    static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    static float SampleBump(Vector2 position, Vector2 offset, float wavelength)
    {
        // 크기가 다른 노이즈 여러 겹 합산 요철
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f / wavelength;
        for (int octave = 0; octave < BumpOctaves; octave++)
        {
            value += (Mathf.PerlinNoise(position.x * frequency + offset.x, position.y * frequency + offset.y) - 0.5f) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return value;
    }

    static void NormalizeBumps(float[] noise, List<int> cells, float targetDeviation)
    {
        // 지정 요철 편차에 맞춘 평균 0 정규화
        if (cells.Count == 0) return;

        float mean = 0f;
        foreach (int cell in cells)
        {
            mean += noise[cell];
        }
        mean /= cells.Count;

        float variance = 0f;
        foreach (int cell in cells)
        {
            variance += (noise[cell] - mean) * (noise[cell] - mean);
        }

        float deviation = Mathf.Sqrt(variance / cells.Count);
        float scale = deviation > 0f ? targetDeviation / deviation : 0f;
        foreach (int cell in cells)
        {
            noise[cell] = (noise[cell] - mean) * scale;
        }
    }

    static float[,] BuildHeights(TerrainMapLayout layout, TerrainMapSettings settings, float playerHeightLocal,
        float sizeY, float[] distance, System.Random random, out int clampedCount)
    {
        int width = layout.Width;
        int height = layout.Height;
        float[] noise = new float[width * height];
        List<int> highCells = new List<int>();
        List<int> lowCells = new List<int>();
        Vector2 offset = new Vector2(Range(random, 0f, 1000f), Range(random, 0f, 1000f));
        float wavelength = Mathf.Max(settings.RoomDiameter * 0.5f, layout.CellSize.x * 4f);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (layout.IsWalkable(x, z)) continue;
                int index = z * width + x;
                noise[index] = SampleBump(new Vector2(x * layout.CellSize.x, z * layout.CellSize.y), offset, wavelength);
                if (layout.IsLow(x, z)) lowCells.Add(index);
                else highCells.Add(index);
            }
        }

        NormalizeBumps(noise, highCells, settings.HighBumpDeviation);
        NormalizeBumps(noise, lowCells, settings.LowBumpDeviation);

        // 요철이 이동 높이로 판정되지 않도록 띄우는 최소 높이 차
        float gap = settings.HeightTolerance * 2f;
        float fadeCells = Mathf.Max(1f, settings.PathWidth * 0.5f / layout.CellSize.x);
        float[,] heights = new float[height, width];
        clampedCount = 0;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                float local;
                if (layout.IsWalkable(x, z))
                {
                    local = playerHeightLocal;
                }
                else
                {
                    // 절벽 윗선 유지를 위한 이동 영역 근처 요철 감소
                    float bump = noise[index] * Mathf.Clamp01(distance[index] / fadeCells);
                    local = layout.IsLow(x, z)
                        ? Mathf.Min(playerHeightLocal + settings.LowOffset + bump, playerHeightLocal - gap)
                        : Mathf.Max(playerHeightLocal + settings.HighOffset + bump, playerHeightLocal + gap);
                }

                float normalized = local / sizeY;
                if (normalized < 0f || normalized > 1f) clampedCount++;
                heights[z, x] = Mathf.Clamp01(normalized);
            }
        }

        return heights;
    }

    static bool TryValidate(Terrain target, IReadOnlyList<Terrain> references, TerrainMapReference profile,
        TerrainMapSettings settings, out TerrainData existing, out TerrainCollider terrainCollider, out string error)
    {
        existing = null;
        terrainCollider = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            error = "플레이 모드에서는 맵을 생성할 수 없습니다.";
            return false;
        }

        if (profile == null)
        {
            error = "레퍼런스 분석을 먼저 실행하세요.";
            return false;
        }

        if (settings.RoomCount < 1 || settings.PathWidth <= 0f || settings.RoomDiameter < settings.PathWidth ||
            settings.HeightTolerance <= 0f)
        {
            error = "공간 개수는 1 이상, 샛길 폭은 0보다 크게, 공간 지름은 샛길 폭 이상, 높이 허용치는 0보다 크게 지정하세요.";
            return false;
        }

        if (settings.Irregularity < 0f || settings.Irregularity > 1f)
        {
            error = "모양 불규칙도는 0~1 사이로 지정하세요.";
            return false;
        }

        foreach (TerrainMapPropStats stats in profile.Props.Stats)
        {
            if (TerrainMapPropSetting.Find(settings.Props, stats.Prefab) != null) continue;
            error = "배치물 설정이 레퍼런스 분석 결과와 맞지 않습니다. 레퍼런스 분석을 다시 실행하세요.";
            return false;
        }

        if (target == null)
        {
            error = "생성 대상 Terrain을 지정하세요.";
            return false;
        }

        if (!target.TryGetComponent(out terrainCollider))
        {
            error = $"{target.name}: TerrainCollider가 필요합니다.";
            return false;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            error = $"저장 폴더가 없습니다: {OutputFolder}";
            return false;
        }

        string assetPath = GetOutputPath(target);
        existing = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
        if (existing == null && AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
        {
            error = $"{assetPath}: TerrainData가 아닌 에셋이 이미 있습니다.";
            return false;
        }

        foreach (Terrain terrain in references)
        {
            if (terrain == target)
            {
                error = "생성 대상은 레퍼런스와 다른 Terrain이어야 합니다.";
                return false;
            }

            if (existing != null && terrain != null && terrain.terrainData == existing)
            {
                error = $"{assetPath}: 레퍼런스가 사용하는 TerrainData는 덮어쓸 수 없습니다.";
                return false;
            }
        }

        if (existing != null && !AssetDatabase.IsOpenForEdit(existing))
        {
            error = $"{assetPath}: 편집할 수 없습니다. 파일 잠금 또는 버전 관리 상태를 확인하세요.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    static void Apply(Terrain target, TerrainCollider terrainCollider, TerrainData existing, string assetPath,
        TerrainMapReference profile, TerrainMapDimensions dimensions, float[,] heights, TerrainMapPropPlan plan)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);

        try
        {
            TerrainData data = existing;
            if (data == null) data = new TerrainData();
            else Undo.RegisterCompleteObjectUndo(data, UndoName);

            // 해상도 변경 시 크기가 초기화되므로 해상도 먼저 적용
            data.heightmapResolution = dimensions.HeightmapResolution;
            data.size = dimensions.Size;
            data.alphamapResolution = dimensions.AlphamapResolution;
            data.baseMapResolution = dimensions.BaseMapResolution;
            data.terrainLayers = new List<TerrainLayer>(profile.Layers).ToArray();
            data.SetHeights(0, 0, heights);
            data.treePrototypes = plan.Prototypes;
            data.RefreshPrototypes();
            data.SetTreeInstances(plan.Instances, true);

            if (existing == null) AssetDatabase.CreateAsset(data, assetPath);
            else EditorUtility.SetDirty(data);

            Undo.RecordObject(target, UndoName);
            target.terrainData = data;
            Undo.RecordObject(terrainCollider, UndoName);
            terrainCollider.terrainData = data;
            target.Flush();

            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            AssetDatabase.SaveAssetIfDirty(data);
            Undo.CollapseUndoOperations(undoGroup);
        }
        catch
        {
            // 씬 연결과 기존 에셋 변경 복원
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    /// <summary>
    /// 대상 Terrain 이름 기반 생성 TerrainData 경로
    /// </summary>
    /// <param name="target">생성 대상 Terrain</param>
    /// <returns>저장 경로</returns>
    public static string GetOutputPath(Terrain target)
    {
        return $"{OutputFolder}/{target.name}.asset";
    }

    /// <summary>
    /// 크기 기준 설정에 따른 생성 크기와 해상도 결정
    /// </summary>
    /// <param name="target">생성 대상 Terrain</param>
    /// <param name="profile">레퍼런스 분석 결과</param>
    /// <param name="source">크기 기준</param>
    /// <param name="dimensions">생성 크기와 해상도</param>
    /// <param name="error">결정 불가 사유</param>
    /// <returns>결정 성공 여부</returns>
    public static bool TryGetDimensions(Terrain target, TerrainMapReference profile, TerrainMapSizeSource source,
        out TerrainMapDimensions dimensions, out string error)
    {
        dimensions = default;
        if (source == TerrainMapSizeSource.Reference)
        {
            if (profile == null)
            {
                error = "레퍼런스 분석을 먼저 실행하세요.";
                return false;
            }

            dimensions = new TerrainMapDimensions(profile.Size, profile.HeightmapResolution,
                profile.AlphamapResolution, profile.BaseMapResolution);
            error = string.Empty;
            return true;
        }

        TerrainData data = target != null ? target.terrainData : null;
        if (data == null)
        {
            error = "대상 Terrain에 TerrainData가 없습니다. 크기 기준을 레퍼런스 크기로 바꾸세요.";
            return false;
        }

        dimensions = new TerrainMapDimensions(data.size, data.heightmapResolution, data.alphamapResolution,
            data.baseMapResolution);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 입력값과 레퍼런스 분석 기반 TerrainData 생성 후 대상 Terrain 연결
    /// </summary>
    /// <param name="target">생성 대상 Terrain</param>
    /// <param name="references">분석에 사용한 레퍼런스 Terrain 목록</param>
    /// <param name="profile">레퍼런스 분석 결과, 나무·Layer 기준</param>
    /// <param name="settings">맵 생성 입력값</param>
    /// <param name="message">생성 결과 또는 실행 불가 사유</param>
    /// <returns>검증과 생성 처리 성공 여부</returns>
    public static bool TryGenerate(Terrain target, IReadOnlyList<Terrain> references, TerrainMapReference profile,
        TerrainMapSettings settings, out string message)
    {
        if (!TryValidate(target, references, profile, settings, out TerrainData existing,
                out TerrainCollider terrainCollider, out message)) return false;
        if (!TryGetDimensions(target, profile, settings.SizeSource, out TerrainMapDimensions dimensions,
                out message)) return false;

        // 배치 미리보기와 같은 결과를 위해 배치와 요철·나무 난수 분리
        TerrainMapLayout layout = TerrainMapLayout.Generate(settings, dimensions.Size, dimensions.HeightmapResolution);
        if (layout.PlacedRoomCount == 0)
        {
            message = "공간을 하나도 배치하지 못했습니다. 샛길 폭이나 가장자리 여백을 줄여 보세요.";
            return false;
        }

        System.Random random = new System.Random(unchecked(settings.Seed * 31 + 7));
        Vector3 origin = target.GetPosition();
        float playerHeightLocal = settings.PlayerHeight - origin.y;
        float[] distance = layout.GetWalkableDistance();
        float[,] heights = BuildHeights(layout, settings, playerHeightLocal, dimensions.Size.y, distance, random,
            out int clampedCount);
        TerrainMapPropPlan plan = TerrainMapPropPlanter.Plan(layout, dimensions.Size, profile.Props, settings.Props,
            TerrainMapPropPlanter.GetPropSeed(settings.Seed));

        string assetPath = GetOutputPath(target);
        Apply(target, terrainCollider, existing, assetPath, profile, dimensions, heights, plan);

        message = $"{assetPath} 생성 · seed {settings.Seed}\n" +
                  $"크기 {dimensions.Size.x:0.#} x {dimensions.Size.z:0.#} · 높이 {dimensions.Size.y:0.#}\n" +
                  $"넓은 공간 {layout.PlacedRoomCount}/{settings.RoomCount}개 · 공간 지름 {layout.UsedRoomDiameter:0.#} · 평지 비율 {layout.WalkableRatio:P0} · 배치물 {plan.Instances.Length}개 (나무 {plan.CountPlaced(TerrainMapPropCategory.Tree)} · 돌 {plan.CountPlaced(TerrainMapPropCategory.Rock)} · 풀 {plan.CountPlaced(TerrainMapPropCategory.Grass)})\n" +
                  $"시작 공간 중심 ({origin.x + layout.StartCenter.x:0.#}, {settings.PlayerHeight:0.##}, {origin.z + layout.StartCenter.y:0.#})\n" +
                  $"끝 공간 중심 ({origin.x + layout.EndCenter.x:0.#}, {settings.PlayerHeight:0.##}, {origin.z + layout.EndCenter.y:0.#})";
        if (settings.Shape == TerrainMapShape.CliffSide)
            message += $"\n절벽 방향 {TerrainMapLayout.GetSideName(layout.ResolvedCliffSide)}";
        if (layout.PlacedRoomCount < settings.RoomCount)
            message += "\n공간 지름을 최소까지 줄여도 공간 개수를 다 채우지 못했습니다. 공간 개수, 샛길 폭, 가장자리 여백을 줄여 보세요.";
        if (layout.ConflictCount > 0)
            message += $"\n샛길이 다른 공간·샛길에 붙은 곳 {layout.ConflictCount}곳: 갈림길이 생길 수 있으니 seed를 바꿔 보세요.";
        if (clampedCount > 0)
            message += $"\nTerrain 높이 범위를 벗어나 잘린 지점 {clampedCount}개";
        message += "\n이동 영역 도구로 칠하기를 실행한 뒤 씬을 저장하세요.";
        return true;
    }
}
