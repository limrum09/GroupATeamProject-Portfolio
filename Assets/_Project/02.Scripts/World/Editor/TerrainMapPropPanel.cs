using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 맵·레퍼런스 미리보기 공통 색
/// </summary>
public static class TerrainMapPreviewPalette
{
    public static readonly Color Walkable = new Color(0.45f, 0.8f, 0.35f);
    public static readonly Color High = new Color(0.65f, 0.45f, 0.3f);
    public static readonly Color Low = new Color(0.3f, 0.5f, 0.85f);
    public static readonly Color Outside = Color.black;
    public static readonly Color Cliff = new Color(0.35f, 0.25f, 0.18f);
    public static readonly Color Tree = new Color(0.1f, 0.35f, 0.12f);
    public static readonly Color Rock = new Color(0.6f, 0.6f, 0.6f);
    public static readonly Color Grass = new Color(0.95f, 0.9f, 0.35f);

    /// <summary>
    /// 지역 표시 색
    /// </summary>
    /// <param name="region">지역</param>
    /// <returns>표시 색</returns>
    public static Color GetRegionColor(TerrainMapRegion region)
    {
        switch (region)
        {
            case TerrainMapRegion.Walkable: return Walkable;
            case TerrainMapRegion.Low: return Low;
            default: return High;
        }
    }

    /// <summary>
    /// 배치물 구분 표시 색
    /// </summary>
    /// <param name="category">배치물 구분</param>
    /// <returns>표시 색</returns>
    public static Color GetPropColor(TerrainMapPropCategory category)
    {
        switch (category)
        {
            case TerrainMapPropCategory.Rock: return Rock;
            case TerrainMapPropCategory.Grass: return Grass;
            default: return Tree;
        }
    }
}

/// <summary>
/// 레퍼런스 배치물 인식 결과와 종류별 심기 선택 편집 패널
/// </summary>
public class TerrainMapPropPanel
{
    const float ThumbnailSize = 48f;
    const float ViewMaxWidth = 260f;
    const int DotRadius = 1;                // 미리보기 배치물 점 반지름 (픽셀)
    const float BandTint = 0.3f;            // 심는 범위 띠 밝기
    const int CurveStepBins = 2;            // 밀도 곡선 한 구간 거리대 수
    const int MaxCurveSteps = 8;
    const int MaxRelations = 3;             // 표시할 함께 있는 종류 수

    static readonly TerrainMapPropCategory[] Categories =
    {
        TerrainMapPropCategory.Tree, TerrainMapPropCategory.Rock, TerrainMapPropCategory.Grass
    };

    readonly bool[] _foldouts = { true, true, true };
    readonly List<Texture2D> _viewTextures = new List<Texture2D>();
    TerrainMapPropProfile _viewProfile;
    bool _viewsDirty;
    bool _viewsFoldout = true;
    Vector2 _scroll;

    static string GetCategoryName(TerrainMapPropCategory category)
    {
        switch (category)
        {
            case TerrainMapPropCategory.Rock: return "돌";
            case TerrainMapPropCategory.Grass: return "풀";
            default: return "나무";
        }
    }

    static string GetPatternName(TerrainMapPropPattern pattern)
    {
        switch (pattern)
        {
            case TerrainMapPropPattern.Clustered: return "군락형";
            case TerrainMapPropPattern.Random: return "무작위";
            case TerrainMapPropPattern.Even: return "고르게";
            default: return "표본 부족";
        }
    }

    static string FormatRegions(System.Func<TerrainMapRegion, float> ratio)
    {
        // 0%인 지역은 생략
        List<string> parts = new List<string>();
        if (ratio(TerrainMapRegion.Walkable) > 0f) parts.Add($"평지 {ratio(TerrainMapRegion.Walkable):P0}");
        if (ratio(TerrainMapRegion.High) > 0f) parts.Add($"높은 곳 {ratio(TerrainMapRegion.High):P0}");
        if (ratio(TerrainMapRegion.Low) > 0f) parts.Add($"낮은 곳 {ratio(TerrainMapRegion.Low):P0}");
        return parts.Count > 0 ? string.Join(" / ", parts) : "-";
    }

    static string FormatCurve(TerrainMapPropStats stats)
    {
        // 경계 바깥 심는 한계까지 2m 구간별 100㎡당 개수
        StringBuilder text = new StringBuilder("거리별 100㎡당");
        int bin = 0;
        for (int step = 0; step < MaxCurveSteps && bin * TerrainMapPropProfile.BinSize <= stats.OutsideLimit; step++)
        {
            float from = bin * TerrainMapPropProfile.BinSize;
            float to = (bin + CurveStepBins) * TerrainMapPropProfile.BinSize;
            text.Append($" · {from:0}~{to:0}m {stats.GetOutsideDensity(bin, bin + CurveStepBins) * 100f:0.#}");
            bin += CurveStepBins;
        }
        return text.ToString();
    }

    static string FormatRelations(TerrainMapPropStats stats)
    {
        // 동반 비율 높은 상대 종류 순, 끌림 ×1보다 크면 붙어 있고 작으면 피함
        StringBuilder text = new StringBuilder($"함께 있는 종류 ({TerrainMapPropProfile.CompanionRadius:0}m 안)");
        int shown = 0;
        foreach (TerrainMapPropRelation relation in stats.Relations)
        {
            if (!relation.HasNeighbors) continue;
            text.Append($" · {relation.Other.Prefab.name} {relation.CompanionRatio:P0} 끌림 ×{relation.Attraction:0.0} 최소 {relation.MinSpacing:0.#}m");
            if (++shown >= MaxRelations) break;
        }
        if (shown == 0) text.Append(" · 없음");
        return text.ToString();
    }

    static Texture2D BuildViewTexture(TerrainMapPropReferenceView view, List<TerrainMapPropSetting> settings,
        float bandLimit)
    {
        Color[] pixels = new Color[view.Width * view.Height];
        for (int z = 0; z < view.Height; z++)
        {
            for (int x = 0; x < view.Width; x++)
            {
                Color color = TerrainMapPreviewPalette.Outside;
                if (view.IsInside(x, z))
                {
                    TerrainMapRegion region = view.GetRegion(x, z);
                    color = TerrainMapPreviewPalette.GetRegionColor(region);
                    // 경계 절벽은 따로, 평지 밖 심는 범위 띠는 옅게 표시
                    if (view.IsCliff(x, z)) color = TerrainMapPreviewPalette.Cliff;
                    else if (region != TerrainMapRegion.Walkable && view.GetEdgeDistance(x, z) <= bandLimit)
                        color = Color.Lerp(color, Color.white, BandTint);
                }
                pixels[z * view.Width + x] = color;
            }
        }

        for (int i = 0; i < view.InstanceCount; i++)
        {
            TerrainMapPropSetting setting = TerrainMapPropSetting.Find(settings, view.GetOwner(i).Prefab);
            Color color = setting != null
                ? TerrainMapPreviewPalette.GetPropColor(setting.Category)
                : TerrainMapPreviewPalette.Tree;
            Vector2 position = view.GetPosition(i);
            int centerX = Mathf.Clamp((int)(position.x * view.Width), 0, view.Width - 1);
            int centerZ = Mathf.Clamp((int)(position.y * view.Height), 0, view.Height - 1);
            for (int z = centerZ - DotRadius; z <= centerZ + DotRadius; z++)
            {
                if (z < 0 || z >= view.Height) continue;
                for (int x = centerX - DotRadius; x <= centerX + DotRadius; x++)
                {
                    if (x < 0 || x >= view.Width) continue;
                    pixels[z * view.Width + x] = color;
                }
            }
        }

        Texture2D texture = new Texture2D(view.Width, view.Height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    static string FormatSize(TerrainMapPropStats stats, TerrainMapPropSetting setting)
    {
        // 레퍼런스 배율과 프리팹 메시 높이로 실제 높이 계산, 생성 높이는 높이 배율 적용
        string text = $"레퍼런스 높이 배율 {stats.MinScale:0.##}~{stats.MaxScale:0.##} (중간 {stats.MedianScale:0.##}) · 너비 배율 {stats.MinWidthScale:0.##}~{stats.MaxWidthScale:0.##}";
        if (!stats.HasPrefabSize)
            return text + $" → 생성 높이 배율 중간 {stats.MedianScale * setting.HeightMultiplier:0.##}";

        float height = stats.PrefabSize.y;
        return text + $"\n실제 높이 {height * stats.MinScale:0.#}~{height * stats.MaxScale:0.#}m (중간 {height * stats.MedianScale:0.#}m) → 생성 {height * stats.MedianScale * setting.HeightMultiplier:0.#}m";
    }

    static void DrawStats(TerrainMapPropStats stats, TerrainMapPropSetting setting)
    {
        EditorGUILayout.LabelField($"레퍼런스 {stats.Count}개 · {FormatRegions(stats.GetRegionRatio)}",
            EditorStyles.wordWrappedMiniLabel);
        string inside = stats.InsideLimit > 0f ? $" · 안쪽 {stats.InsideLimit:0.#}m" : string.Empty;
        EditorGUILayout.LabelField(
            $"심는 범위 경계 바깥 {stats.OutsideLimit:0.#}m{inside}까지 · 경계 {TerrainMapPropProfile.NearDistance:0}m 이내 {stats.NearRatio:P0} · 경계 덮음 {stats.Coverage:P0}",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(
            $"경계 거리 {stats.EdgeDistanceLow:0.#}~{stats.EdgeDistanceHigh:0.#}m (중간 {stats.EdgeDistanceMedian:0.#}m, 평지 밖은 절벽 꼭대기 기준, 음수는 평지 안쪽)",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(FormatCurve(stats), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(
            $"경계 100m당 {stats.PerBoundaryMeter * 100f:0.#}개 · 이웃 간격 {stats.NearestMedian:0.#}m (최소 {stats.MinSpacing:0.#}m) · {GetPatternName(stats.Pattern)}",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(FormatRelations(stats), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(FormatSize(stats, setting), EditorStyles.wordWrappedMiniLabel);
    }

    static void DrawPlan(TerrainMapPropStats stats, TerrainMapPropSetting setting, TerrainMapPropPlan plan)
    {
        string text;
        if (!setting.Enabled) text = "생성 안 함 (체크 해제)";
        else if (plan == null) text = "생성 예정: 배치 미리보기 필요";
        else
        {
            TerrainMapPropPlanEntry entry = plan.Find(stats.Prefab);
            if (entry == null) text = "생성 예정 0개";
            else
            {
                text = $"생성 예정 {entry.PlacedCount}/{entry.TargetCount}개 · 경계 {TerrainMapPropProfile.NearDistance:0}m 이내 {entry.NearRatio:P0} · 가장 먼 배치 경계 바깥 {entry.FarthestOutside:0}m · {FormatRegions(entry.GetRegionRatio)}";
                if (entry.CompanionPrefab != null)
                    text += $" · {entry.CompanionPrefab.name} {TerrainMapPropProfile.CompanionRadius:0}m 안 {entry.CompanionRatio:P0}";
                if (entry.PlacedCount < entry.TargetCount) text += " (간격 부족으로 일부 생략)";
            }
        }
        EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
    }

    static bool DrawEntry(TerrainMapPropStats stats, TerrainMapPropSetting setting, TerrainMapPropPlan plan,
        Object undoTarget)
    {
        bool changed = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Texture preview = AssetPreview.GetAssetPreview(stats.Prefab);
                if (preview == null) preview = AssetPreview.GetMiniThumbnail(stats.Prefab);
                GUILayout.Label(preview, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize));

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool enabled = EditorGUILayout.ToggleLeft(stats.Prefab.name, setting.Enabled, EditorStyles.boldLabel);
                    TerrainMapPropCategory category = (TerrainMapPropCategory)EditorGUILayout.EnumPopup("구분", setting.Category);
                    float multiplier = EditorGUILayout.FloatField("밀도 배율", setting.DensityMultiplier);
                    float heightMultiplier = EditorGUILayout.FloatField("높이 배율", setting.HeightMultiplier);
                    float widthMultiplier = EditorGUILayout.FloatField("너비 배율", setting.WidthMultiplier);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(undoTarget, "배치물 설정 변경");
                        setting.Enabled = enabled;
                        setting.Category = category;
                        setting.DensityMultiplier = Mathf.Max(0f, multiplier);
                        setting.HeightMultiplier = Mathf.Max(0.01f, heightMultiplier);
                        setting.WidthMultiplier = Mathf.Max(0.01f, widthMultiplier);
                        changed = true;
                    }
                }
            }

            DrawStats(stats, setting);
            DrawPlan(stats, setting, plan);
        }
        return changed;
    }

    void RefreshViews(TerrainMapPropProfile profile, List<TerrainMapPropSetting> settings)
    {
        if (profile == _viewProfile && !_viewsDirty) return;

        ReleaseTextures();
        foreach (TerrainMapPropReferenceView view in profile.Views)
        {
            _viewTextures.Add(BuildViewTexture(view, settings, profile.MaxOutsideLimit));
        }
        _viewProfile = profile;
        _viewsDirty = false;
    }

    void DrawViews(TerrainMapPropProfile profile)
    {
        _viewsFoldout = EditorGUILayout.Foldout(_viewsFoldout,
            $"레퍼런스 인식 미리보기 · 평지 경계 총 {profile.BoundaryLength:0}m · 평균 절벽 폭 {profile.AverageCliffWidth:0.#}m · 심는 범위 최대 {profile.MaxOutsideLimit:0.#}m", true);
        if (!_viewsFoldout) return;

        for (int i = 0; i < profile.Views.Count; i++)
        {
            TerrainMapPropReferenceView view = profile.Views[i];
            EditorGUILayout.LabelField(
                $"{view.Name} · 평지 {view.WalkableRatio:P0} · 경계 {view.BoundaryLength:0}m · 절벽 폭 {view.CliffWidth:0.#}m · 배치물 {view.InstanceCount}개",
                EditorStyles.miniLabel);
            Texture2D texture = _viewTextures[i];
            Rect area = GUILayoutUtility.GetAspectRect((float)texture.width / texture.height, GUILayout.MaxWidth(ViewMaxWidth));
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
        }
        EditorGUILayout.LabelField("초록 평지 · 진갈색 절벽 · 갈색 높은 곳 · 파랑 낮은 곳 · 옅은 띠 심는 범위 · 진초록 나무 · 회색 돌 · 노랑 풀",
            EditorStyles.wordWrappedMiniLabel);
    }

    /// <summary>
    /// 레퍼런스 인식 미리보기 텍스처 해제
    /// </summary>
    public void ReleaseTextures()
    {
        foreach (Texture2D texture in _viewTextures)
        {
            if (texture != null) Object.DestroyImmediate(texture);
        }
        _viewTextures.Clear();
        _viewProfile = null;
    }

    /// <summary>
    /// 레퍼런스 인식 미리보기와 나무·돌·풀 구분별 인식 결과·선택값 표시
    /// </summary>
    /// <param name="profile">레퍼런스 배치물 측정 결과, 분석 전이면 null</param>
    /// <param name="settings">종류별 심기 선택값</param>
    /// <param name="plan">미리보기 배치 결과, 없으면 null</param>
    /// <param name="undoTarget">선택값을 직렬화하는 실행 취소 대상</param>
    /// <returns>선택값 변경 여부</returns>
    public bool Draw(TerrainMapPropProfile profile, List<TerrainMapPropSetting> settings, TerrainMapPropPlan plan,
        Object undoTarget)
    {
        EditorGUILayout.LabelField("배치물 인식 결과", EditorStyles.boldLabel);
        if (profile == null)
        {
            ReleaseTextures();
            EditorGUILayout.HelpBox("레퍼런스 분석을 실행하면 나무·돌·풀 인식 결과가 표시됩니다.", MessageType.Info);
            return false;
        }
        if (plan == null)
            EditorGUILayout.HelpBox("배치 미리보기를 실행하면 생성 예정 수치가 표시됩니다.", MessageType.Info);

        RefreshViews(profile, settings);
        bool changed = false;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawViews(profile);
        EditorGUILayout.Space();

        for (int i = 0; i < Categories.Length; i++)
        {
            TerrainMapPropCategory category = Categories[i];
            int kinds = 0;
            int referenceCount = 0;
            foreach (TerrainMapPropStats stats in profile.Stats)
            {
                TerrainMapPropSetting setting = TerrainMapPropSetting.Find(settings, stats.Prefab);
                if (setting == null || setting.Category != category) continue;
                kinds++;
                referenceCount += stats.Count;
            }

            string planned = plan != null ? $" · 생성 예정 {plan.CountPlaced(category)}개" : string.Empty;
            _foldouts[i] = EditorGUILayout.Foldout(_foldouts[i],
                $"{GetCategoryName(category)}  {kinds}종 · 레퍼런스 {referenceCount}개{planned}", true);
            if (!_foldouts[i]) continue;

            foreach (TerrainMapPropStats stats in profile.Stats)
            {
                TerrainMapPropSetting setting = TerrainMapPropSetting.Find(settings, stats.Prefab);
                if (setting == null || setting.Category != category) continue;
                changed |= DrawEntry(stats, setting, plan, undoTarget);
            }
        }
        EditorGUILayout.EndScrollView();

        // 구분 변경 시 레퍼런스 미리보기 점 색 갱신
        if (changed) _viewsDirty = true;
        return changed;
    }
}
