using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 분류별 칠할 Layer와 경계 처리 옵션
/// </summary>
public readonly struct TerrainWalkablePaintOptions
{
    readonly TerrainLayer[] _layers;    // Walkable, Cliff, High, Low 순서

    /// <summary>
    /// 칠하기 옵션 생성, 비워둔 분류는 기존 칠하기 유지
    /// </summary>
    /// <param name="walkableLayer">이동 평지 Layer</param>
    /// <param name="cliffLayer">절벽 Layer</param>
    /// <param name="highLayer">높은 곳 Layer</param>
    /// <param name="lowLayer">낮은 곳 Layer</param>
    /// <param name="cliffExpand">절벽 확장 칸 수</param>
    /// <param name="blendRadius">경계 블렌딩 칸 수</param>
    public TerrainWalkablePaintOptions(TerrainLayer walkableLayer, TerrainLayer cliffLayer, TerrainLayer highLayer,
        TerrainLayer lowLayer, int cliffExpand, int blendRadius)
    {
        _layers = new TerrainLayer[] { walkableLayer, cliffLayer, highLayer, lowLayer };
        CliffExpand = cliffExpand;
        BlendRadius = blendRadius;
    }

    public int CliffExpand { get; }
    public int BlendRadius { get; }

    /// <summary>
    /// 하나 이상의 분류에 Layer 지정 여부
    /// </summary>
    public bool HasAnyLayer => Array.Exists(_layers, layer => layer != null);

    /// <summary>
    /// 모든 분류에 Layer 지정 여부
    /// </summary>
    public bool HasAllLayers => Array.TrueForAll(_layers, layer => layer != null);

    /// <summary>
    /// 분류별 칠할 Layer 조회
    /// </summary>
    /// <param name="category">조회할 분류</param>
    /// <returns>지정 Layer, 미지정 분류와 Outside는 null</returns>
    public TerrainLayer GetLayer(TerrainWalkableCategory category)
    {
        if (category == TerrainWalkableCategory.Outside) return null;
        return _layers[(int)category - 1];
    }
}

/// <summary>
/// 이동 가능 영역 분류 기반 Terrain 알파맵 칠하기와 되돌리기 처리
/// </summary>
public static class TerrainWalkablePainter
{
    const string UndoName = "Terrain 이동 영역 칠하기";

    class PaintPlan
    {
        public Terrain Terrain;
        public TerrainLayer[] Layers;
        public float[,,] Alphamaps;
    }

    static PaintPlan BuildPlan(Terrain terrain, IReadOnlyList<Terrain> targets, TerrainWalkableSettings settings,
        TerrainWalkablePaintOptions options)
    {
        TerrainData data = terrain.terrainData;
        int categoryCount = TerrainWalkableMask.CategoryCount;

        // 분류별 대상 Layer 번호, -1은 기존 칠하기 유지
        List<TerrainLayer> layers = new List<TerrainLayer>(data.terrainLayers);
        int[] layerIndices = new int[categoryCount];
        for (int category = 0; category < categoryCount; category++)
        {
            TerrainLayer layer = options.GetLayer((TerrainWalkableCategory)category);
            if (layer == null)
            {
                layerIndices[category] = -1;
                continue;
            }

            int index = layers.IndexOf(layer);
            if (index < 0)
            {
                // 기존 알파맵 채널 순서 보존을 위한 끝 추가
                layers.Add(layer);
                index = layers.Count - 1;
            }
            layerIndices[category] = index;
        }

        TerrainWalkableMask mask = TerrainWalkableMask.BuildForAlphamap(terrain, targets, settings);
        mask.ExpandCategory(TerrainWalkableCategory.Cliff, options.CliffExpand);

        float[][] weights = new float[categoryCount][];
        for (int category = 0; category < categoryCount; category++)
        {
            weights[category] = mask.GetWeights((TerrainWalkableCategory)category, options.BlendRadius);
        }

        int resolution = data.alphamapResolution;
        int oldLayerCount = data.alphamapLayers;
        float[,,] oldMaps = data.GetAlphamaps(0, 0, resolution, resolution);
        float[,,] newMaps = new float[resolution, resolution, layers.Count];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int cell = z * resolution + x;
                float keep = 0f;
                for (int category = 0; category < categoryCount; category++)
                {
                    float weight = weights[category][cell];
                    if (weight <= 0f) continue;

                    if (layerIndices[category] < 0) keep += weight;
                    else newMaps[z, x, layerIndices[category]] += weight;
                }

                // 미지정 분류 비중만큼 기존 칠하기 유지
                if (keep <= 0f) continue;
                for (int layer = 0; layer < oldLayerCount; layer++)
                {
                    newMaps[z, x, layer] += oldMaps[z, x, layer] * keep;
                }
            }
        }

        return new PaintPlan { Terrain = terrain, Layers = layers.ToArray(), Alphamaps = newMaps };
    }

    static void ApplyPlans(List<PaintPlan> plans)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);

        try
        {
            foreach (PaintPlan plan in plans)
            {
                TerrainData data = plan.Terrain.terrainData;
                List<UnityEngine.Object> undoTargets = new List<UnityEngine.Object> { data };
                undoTargets.AddRange(data.alphamapTextures);
                Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), UndoName);

                // Layer 추가 시 알파맵 채널 수 갱신 후 적용
                if (plan.Layers.Length != data.terrainLayers.Length) data.terrainLayers = plan.Layers;
                data.SetAlphamaps(0, 0, plan.Alphamaps);
                EditorUtility.SetDirty(data);
                plan.Terrain.Flush();
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }
        catch
        {
            // 일부 대상만 변경된 상태 방지를 위한 전체 실행 그룹 복원
            Undo.RevertAllDownToGroup(undoGroup);
            SceneView.RepaintAll();
            throw;
        }
    }

    /// <summary>
    /// 모든 대상 사전 검증 후 이동 가능 영역 분류에 따른 알파맵 일괄 칠하기
    /// </summary>
    /// <param name="targets">칠할 Terrain 목록</param>
    /// <param name="settings">판정 기준</param>
    /// <param name="options">분류별 Layer와 경계 처리 옵션</param>
    /// <param name="message">실행 결과 또는 실행 불가 사유</param>
    /// <returns>검증과 칠하기 처리 성공 여부</returns>
    public static bool TryPaint(IReadOnlyList<Terrain> targets, TerrainWalkableSettings settings,
        TerrainWalkablePaintOptions options, out string message)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            message = "플레이 모드에서는 칠할 수 없습니다.";
            return false;
        }

        if (targets == null || targets.Count == 0)
        {
            message = "칠할 Terrain을 하나 이상 선택하세요.";
            return false;
        }

        if (!options.HasAnyLayer)
        {
            message = "칠할 Layer를 하나 이상 지정하세요.";
            return false;
        }

        List<PaintPlan> plans = new List<PaintPlan>();
        HashSet<TerrainData> visited = new HashSet<TerrainData>();
        foreach (Terrain target in targets)
        {
            if (!TerrainDataEditGuard.TryValidate(target, out message)) return false;

            // 같은 TerrainData 공유 시 첫 Terrain 위치 기준 한 번만 처리
            if (!visited.Add(target.terrainData)) continue;

            if (target.terrainData.alphamapLayers == 0 && !options.HasAllLayers)
            {
                message = $"{target.name}: 기존 Layer가 없는 Terrain은 네 분류의 Layer를 모두 지정하세요.";
                return false;
            }

            plans.Add(BuildPlan(target, targets, settings, options));
        }

        ApplyPlans(plans);
        message = $"Terrain {plans.Count}개를 칠했습니다. Ctrl+Z로 되돌리거나 Unity에서 변경 내용을 저장하세요.";
        return true;
    }
}
