using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 레퍼런스 Terrain 분석과 대상 Terrain 맵 생성 편집 창
/// </summary>
public class TerrainMapGeneratorWindow : EditorWindow
{
    const string PrefsKey = "GroupATeamProject.TerrainMapGeneratorWindow";
    const float LeftWidth = 440f;

    [SerializeField] List<Terrain> _references = new List<Terrain>();
    [SerializeField] Terrain _target;
    [SerializeField] float _cliffAngle = 45f;
    [SerializeField] TerrainMapSettings _settings = new TerrainMapSettings();

    readonly TerrainMapPropPanel _propPanel = new TerrainMapPropPanel();
    SerializedObject _windowState;
    SerializedProperty _referencesProperty;
    SerializedProperty _targetProperty;
    SerializedProperty _settingsProperty;
    TerrainMapReference _profile;
    TerrainMapPropPlan _plan;
    Texture2D _preview;
    Vector2 _previewStart;      // 미리보기 기준 0~1 시작 위치
    Vector2 _previewEnd;        // 미리보기 기준 0~1 끝 위치
    string _previewInfo;
    int _previewConflicts;
    Vector2 _scroll;
    string _message;
    MessageType _messageType = MessageType.Info;

    [MenuItem("Tools/Terrain/레퍼런스 기반 맵 생성")]
    static void OpenWindow()
    {
        TerrainMapGeneratorWindow window = GetWindow<TerrainMapGeneratorWindow>("레퍼런스 기반 맵 생성");
        window.minSize = new Vector2(880f, 640f);
        window.Show();
    }

    static void DrawMarker(Color[] pixels, TerrainMapLayout layout, Vector2 center, Color color)
    {
        int centerX = Mathf.RoundToInt(center.x / layout.CellSize.x);
        int centerZ = Mathf.RoundToInt(center.y / layout.CellSize.y);
        int radius = Mathf.Max(2, layout.Width / 100);
        for (int z = centerZ - radius; z <= centerZ + radius; z++)
        {
            if (z < 0 || z >= layout.Height) continue;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= layout.Width) continue;
                pixels[z * layout.Width + x] = color;
            }
        }
    }

    void OnEnable()
    {
        // 창을 닫았다 열어도 입력값과 배치물 선택값 유지
        string json = EditorPrefs.GetString(PrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(json)) EditorJsonUtility.FromJsonOverwrite(json, _settings);

        // 이전 저장값에 없던 크기 배율은 기본값 1로 복원
        foreach (TerrainMapPropSetting prop in _settings.Props)
        {
            if (prop.HeightMultiplier <= 0f) prop.HeightMultiplier = 1f;
            if (prop.WidthMultiplier <= 0f) prop.WidthMultiplier = 1f;
        }

        _windowState = new SerializedObject(this);
        _referencesProperty = _windowState.FindProperty("_references");
        _targetProperty = _windowState.FindProperty("_target");
        _settingsProperty = _windowState.FindProperty("_settings");
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PrefsKey, EditorJsonUtility.ToJson(_settings));
        _windowState.Dispose();
        ClearPreview();
        _propPanel.ReleaseTextures();
    }

    void ClearPreview()
    {
        if (_preview != null) DestroyImmediate(_preview);
        _preview = null;
        _plan = null;
    }

    void ClampSettings()
    {
        _settings.HeightTolerance = Mathf.Max(0.01f, _settings.HeightTolerance);
        _settings.RoomCount = Mathf.Max(1, _settings.RoomCount);
        _settings.Irregularity = Mathf.Clamp01(_settings.Irregularity);
        _settings.PathWidth = Mathf.Max(0.5f, _settings.PathWidth);
        _settings.RoomDiameter = Mathf.Max(_settings.PathWidth, _settings.RoomDiameter);
        _settings.EdgeMargin = Mathf.Max(0f, _settings.EdgeMargin);
        _settings.HighBumpDeviation = Mathf.Max(0f, _settings.HighBumpDeviation);
        _settings.LowBumpDeviation = Mathf.Max(0f, _settings.LowBumpDeviation);
        foreach (TerrainMapPropSetting prop in _settings.Props)
        {
            prop.DensityMultiplier = Mathf.Max(0f, prop.DensityMultiplier);
            prop.HeightMultiplier = Mathf.Max(0.01f, prop.HeightMultiplier);
            prop.WidthMultiplier = Mathf.Max(0.01f, prop.WidthMultiplier);
        }
    }

    void SetTargetFromSelection()
    {
        GameObject selected = Selection.activeGameObject;
        Terrain terrain = selected != null ? selected.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            _message = "Hierarchy에서 생성 대상 Terrain을 선택하세요.";
            _messageType = MessageType.Warning;
            return;
        }

        _target = terrain;
        _windowState.Update();
        _message = string.Empty;
    }

    void AddSelectionToReferences()
    {
        int added = 0;
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (!selected.TryGetComponent(out Terrain terrain)) continue;
            if (terrain == _target || _references.Contains(terrain)) continue;
            _references.Add(terrain);
            added++;
        }

        if (added == 0)
        {
            _message = "Hierarchy에서 대상과 다른 레퍼런스 Terrain을 선택하세요.";
            _messageType = MessageType.Warning;
            return;
        }

        _windowState.Update();
        _profile = null;
        ClearPreview();
        _message = string.Empty;
    }

    void Analyze()
    {
        ClearPreview();
        if (!TerrainMapReference.TryAnalyze(_references, _settings.PlayerHeight, _settings.HeightTolerance,
                _cliffAngle, out TerrainMapReference profile, out string error))
        {
            _profile = null;
            _message = error;
            _messageType = MessageType.Warning;
            return;
        }

        _profile = profile;
        SyncPropSettings();
        _message = "레퍼런스 분석을 마쳤습니다. 세부 수치 옆 참고 값을 보고 직접 입력하거나 레퍼런스 값 채우기를 실행하세요.";
        _messageType = MessageType.Info;
    }

    void SyncPropSettings()
    {
        // 새로 인식한 종류만 자동 분류로 추가, 기존 선택값 유지
        Undo.RecordObject(this, "배치물 목록 갱신");
        foreach (TerrainMapPropStats stats in _profile.Props.Stats)
        {
            if (TerrainMapPropSetting.Find(_settings.Props, stats.Prefab) != null) continue;
            _settings.Props.Add(new TerrainMapPropSetting
            {
                Prefab = stats.Prefab,
                Category = TerrainMapPropClassifier.Classify(stats.Prefab)
            });
        }
        _windowState.Update();
    }

    void FillFromReference()
    {
        // 직접 입력한 세부 수치 덮어쓰기 되돌리기 지원
        Undo.RecordObject(this, "레퍼런스 수치 채우기");
        _settings.RoomDiameter = _profile.RoomDiameter;
        _settings.PathWidth = _profile.PathWidth;
        _settings.EdgeMargin = _profile.EdgeMargin;
        if (_profile.HasHighArea)
        {
            _settings.HighOffset = _profile.HighOffset;
            _settings.HighBumpDeviation = _profile.HighBumpDeviation;
        }
        if (_profile.HasLowArea)
        {
            _settings.LowOffset = _profile.LowOffset;
            _settings.LowBumpDeviation = _profile.LowBumpDeviation;
        }
        ClampSettings();
        _windowState.Update();
        ClearPreview();

        _message = "세부 수치를 레퍼런스 값으로 채웠습니다.";
        if (!_profile.HasHighArea) _message += "\n레퍼런스에 높은 곳이 없어 높은 곳 수치는 유지했습니다.";
        if (!_profile.HasLowArea) _message += "\n레퍼런스에 낮은 곳이 없어 낮은 곳 수치는 유지했습니다.";
        _messageType = MessageType.Info;
    }

    void RefreshPreview()
    {
        ClearPreview();
        if (!TerrainMapGenerator.TryGetDimensions(_target, _profile, _settings.SizeSource,
                out TerrainMapDimensions dimensions, out string error))
        {
            _message = error;
            _messageType = MessageType.Warning;
            return;
        }

        Vector3 size = dimensions.Size;
        TerrainMapLayout layout = TerrainMapLayout.Generate(_settings, size, dimensions.HeightmapResolution);
        Color[] pixels = new Color[layout.Width * layout.Height];
        for (int z = 0; z < layout.Height; z++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                Color color = layout.IsWalkable(x, z) ? TerrainMapPreviewPalette.Walkable
                    : layout.IsLow(x, z) ? TerrainMapPreviewPalette.Low : TerrainMapPreviewPalette.High;
                pixels[z * layout.Width + x] = color;
            }
        }

        _plan = TerrainMapPropPlanter.Plan(layout, size, _profile.Props, _settings.Props,
            TerrainMapPropPlanter.GetPropSeed(_settings.Seed));
        foreach (TreeInstance instance in _plan.Instances)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(instance.position.x * (layout.Width - 1)), 0, layout.Width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(instance.position.z * (layout.Height - 1)), 0, layout.Height - 1);
            pixels[z * layout.Width + x] = TerrainMapPreviewPalette.GetPropColor(_plan.Entries[instance.prototypeIndex].Category);
        }

        DrawMarker(pixels, layout, layout.StartCenter, Color.white);
        DrawMarker(pixels, layout, layout.EndCenter, Color.red);
        _preview = new Texture2D(layout.Width, layout.Height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _preview.SetPixels(pixels);
        _preview.Apply();

        _previewStart = new Vector2(layout.StartCenter.x / size.x, layout.StartCenter.y / size.z);
        _previewEnd = new Vector2(layout.EndCenter.x / size.x, layout.EndCenter.y / size.z);
        _previewConflicts = layout.ConflictCount;
        bool shrunk = layout.UsedRoomDiameter > 0f && layout.UsedRoomDiameter < _settings.RoomDiameter - 0.01f;
        _previewInfo = $"넓은 공간 {layout.PlacedRoomCount}/{_settings.RoomCount}개 · 공간 지름 {layout.UsedRoomDiameter:0.#}" +
                       (shrunk ? " (자동 축소)" : string.Empty) + $" · 평지 비율 {layout.WalkableRatio:P0}";
        if (_settings.Shape == TerrainMapShape.CliffSide)
            _previewInfo += $" · 절벽 방향 {TerrainMapLayout.GetSideName(layout.ResolvedCliffSide)}";
    }

    void Generate()
    {
        try
        {
            bool succeeded = TerrainMapGenerator.TryGenerate(_target, _references, _profile, _settings, out _message);
            _messageType = succeeded ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            _message = "맵 생성에 실패했습니다. 씬 연결 변경은 되돌렸습니다.\n" + exception.Message;
            _messageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    void DrawSetting(string fieldName, string label)
    {
        EditorGUILayout.PropertyField(_settingsProperty.FindPropertyRelative(fieldName), new GUIContent(label));
    }

    void DrawDetail(string fieldName, string label, bool available, float referenceValue)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSetting(fieldName, label);
            // 레퍼런스에 해당 지형이 없거나 분석 전이면 참고 값 없음
            string hint = _profile != null && available ? $"참고 {referenceValue:0.##}" : "참고 -";
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel, GUILayout.Width(70f));
        }
    }

    void DrawTargets()
    {
        EditorGUILayout.LabelField("대상과 레퍼런스", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_referencesProperty, new GUIContent("레퍼런스 Terrain"), true);
        bool referencesChanged = EditorGUI.EndChangeCheck();
        EditorGUILayout.PropertyField(_targetProperty, new GUIContent("생성 대상 Terrain"));
        _windowState.ApplyModifiedProperties();
        if (referencesChanged)
        {
            _profile = null;
            ClearPreview();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("선택 Terrain을 대상으로")) SetTargetFromSelection();
            if (GUILayout.Button("선택 Terrain을 레퍼런스에 추가")) AddSelectionToReferences();
        }
    }

    void DrawSettings()
    {
        EditorGUILayout.LabelField("배치 설정", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        DrawSetting(nameof(TerrainMapSettings.PlayerHeight), "플레이어 높이 (월드 Y)");
        DrawSetting(nameof(TerrainMapSettings.SizeSource), "크기 기준");
        DrawSetting(nameof(TerrainMapSettings.Shape), "지형 모양");
        if (_settings.Shape == TerrainMapShape.CliffSide)
            DrawSetting(nameof(TerrainMapSettings.CliffSide), "절벽 방향");
        DrawSetting(nameof(TerrainMapSettings.RoomCount), "넓은 공간 개수");
        DrawSetting(nameof(TerrainMapSettings.Irregularity), "모양 불규칙도");
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSetting(nameof(TerrainMapSettings.Seed), "Seed");
            if (GUILayout.Button("랜덤", GUILayout.Width(60f)))
            {
                _settingsProperty.FindPropertyRelative(nameof(TerrainMapSettings.Seed)).intValue =
                    UnityEngine.Random.Range(1, int.MaxValue);
                GUI.changed = true;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("세부 수치 (직접 입력, 오른쪽은 레퍼런스 참고 값)", EditorStyles.boldLabel);
        DrawSetting(nameof(TerrainMapSettings.HeightTolerance), "높이 허용치");
        bool hasProfile = _profile != null;
        bool hasHigh = hasProfile && _profile.HasHighArea;
        bool hasLow = hasProfile && _profile.HasLowArea;
        DrawDetail(nameof(TerrainMapSettings.RoomDiameter), "넓은 공간 지름", hasProfile, hasProfile ? _profile.RoomDiameter : 0f);
        DrawDetail(nameof(TerrainMapSettings.PathWidth), "샛길 폭", hasProfile, hasProfile ? _profile.PathWidth : 0f);
        DrawDetail(nameof(TerrainMapSettings.EdgeMargin), "가장자리 여백", hasProfile, hasProfile ? _profile.EdgeMargin : 0f);
        DrawDetail(nameof(TerrainMapSettings.HighOffset), "높은 곳 높이 (+)", hasHigh, hasHigh ? _profile.HighOffset : 0f);
        DrawDetail(nameof(TerrainMapSettings.LowOffset), "낮은 곳 높이 (-)", hasLow, hasLow ? _profile.LowOffset : 0f);
        DrawDetail(nameof(TerrainMapSettings.HighBumpDeviation), "높은 곳 요철", hasHigh, hasHigh ? _profile.HighBumpDeviation : 0f);
        DrawDetail(nameof(TerrainMapSettings.LowBumpDeviation), "낮은 곳 요철", hasLow, hasLow ? _profile.LowBumpDeviation : 0f);
        bool changed = EditorGUI.EndChangeCheck();
        _windowState.ApplyModifiedProperties();
        if (!changed) return;

        ClampSettings();
        _windowState.Update();
        ClearPreview();
    }

    void DrawProfile()
    {
        EditorGUILayout.LabelField("레퍼런스 분석", EditorStyles.boldLabel);
        _cliffAngle = EditorGUILayout.Slider("절벽 경사 각도", _cliffAngle, 1f, 89f);
        using (new EditorGUI.DisabledScope(_references.Count == 0))
        {
            if (GUILayout.Button("레퍼런스 분석")) Analyze();
        }
        using (new EditorGUI.DisabledScope(_profile == null))
        {
            if (GUILayout.Button("레퍼런스 값으로 세부 수치 채우기")) FillFromReference();
        }

        if (_profile == null)
        {
            EditorGUILayout.HelpBox("플레이어 높이를 입력한 뒤 레퍼런스 분석을 실행하세요. 나무와 Layer는 레퍼런스에서 가져오고, 크기는 크기 기준을 따르며, 세부 수치는 직접 입력하거나 레퍼런스 값으로 채울 수 있습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("레퍼런스 크기",
            $"{_profile.Size.x:0.#} x {_profile.Size.z:0.#} · 높이 {_profile.Size.y:0.#} · 높이맵 {_profile.HeightmapResolution}");
        EditorGUILayout.LabelField("레퍼런스 평지 비율", $"{_profile.WalkableRatio:P0}");
        EditorGUILayout.LabelField("배치물",
            $"{_profile.Props.Stats.Count}종 {_profile.Props.TotalCount}개 · 자세한 내용은 오른쪽 패널");
        EditorGUILayout.LabelField("Terrain Layer", $"{_profile.Layers.Count}개");
    }

    void DrawPreview()
    {
        using (new EditorGUI.DisabledScope(_profile == null))
        {
            if (GUILayout.Button("배치 미리보기")) RefreshPreview();
        }

        if (_preview == null) return;

        Rect area = GUILayoutUtility.GetAspectRect((float)_preview.width / _preview.height, GUILayout.MaxWidth(320f));
        GUI.DrawTexture(area, _preview, ScaleMode.ScaleToFit);
        // 텍스처 첫 행이 월드 -Z라 화면 Y는 뒤집어 표시
        GUI.Label(new Rect(area.x + _previewStart.x * area.width + 6f, area.y + (1f - _previewStart.y) * area.height - 8f, 20f, 16f), "S", EditorStyles.whiteBoldLabel);
        GUI.Label(new Rect(area.x + _previewEnd.x * area.width + 6f, area.y + (1f - _previewEnd.y) * area.height - 8f, 20f, 16f), "E", EditorStyles.whiteBoldLabel);
        EditorGUILayout.LabelField("초록 평지 · 갈색 높은 곳 · 파랑 낮은 곳 · 진초록 나무 · 회색 돌 · 노랑 풀 · 흰 점 시작(S) · 빨간 점 끝(E)", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(_previewInfo, EditorStyles.miniLabel);
        if (_previewConflicts > 0)
            EditorGUILayout.HelpBox($"샛길이 다른 공간·샛길에 붙은 곳 {_previewConflicts}곳: 갈림길이 생길 수 있으니 seed를 바꿔 보세요.", MessageType.Warning);
    }

    void DrawGeneration(bool playing)
    {
        EditorGUILayout.LabelField("생성", EditorStyles.boldLabel);
        DrawPreview();

        if (TerrainMapGenerator.TryGetDimensions(_target, _profile, _settings.SizeSource,
                out TerrainMapDimensions dimensions, out string sizeError))
        {
            EditorGUILayout.LabelField("생성 크기",
                $"{dimensions.Size.x:0.#} x {dimensions.Size.z:0.#} · 높이 {dimensions.Size.y:0.#} · 높이맵 {dimensions.HeightmapResolution}");
        }
        else if (_profile != null)
        {
            EditorGUILayout.HelpBox(sizeError, MessageType.Warning);
        }

        if (_target != null)
        {
            string assetPath = TerrainMapGenerator.GetOutputPath(_target);
            bool exists = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath) != null;
            EditorGUILayout.LabelField(exists ? $"기존 에셋 덮어쓰기: {assetPath}" : $"새 에셋 생성: {assetPath}",
                EditorStyles.wordWrappedMiniLabel);
        }

        if (playing) EditorGUILayout.HelpBox("플레이 모드를 종료한 뒤 생성하세요.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(_profile == null || _target == null || playing))
        {
            if (GUILayout.Button("맵 생성", GUILayout.Height(30f))) Generate();
        }
    }

    void OnGUI()
    {
        _windowState.Update();
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWidth)))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.HelpBox("넓은 공간을 시작부터 끝까지 경로 하나로 잇고, 지형 모양에 맞춰 높은 곳과 낮은 곳을 만듭니다. 나무와 Layer는 레퍼런스를, 크기는 크기 기준을 따릅니다.", MessageType.Info);

                DrawTargets();
                EditorGUILayout.Space();
                DrawSettings();
                EditorGUILayout.Space();
                DrawProfile();
                EditorGUILayout.Space();
                DrawGeneration(playing);

                if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
                EditorGUILayout.EndScrollView();
            }

            using (new EditorGUILayout.VerticalScope())
            {
                if (_propPanel.Draw(_profile?.Props, _settings.Props, _plan, this))
                {
                    // 선택값이 바뀌면 열려 있던 미리보기를 새 값으로 다시 계산
                    bool hadPreview = _preview != null;
                    _windowState.Update();
                    ClearPreview();
                    if (hadPreview) RefreshPreview();
                }
            }
        }

        // 프리팹 썸네일이 비동기로 만들어지는 동안 다시 그리기
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }
}
