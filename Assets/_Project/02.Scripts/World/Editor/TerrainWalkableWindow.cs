using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 선택 Terrain의 이동 가능 영역 판정, 칠하기, 미니맵 PNG 내보내기 편집 창
/// </summary>
public class TerrainWalkableWindow : EditorWindow
{
    const string PrefsKey = "GroupATeamProject.TerrainWalkableWindow";
    const int PreviewLongSide = 160;

    // 분류 순서별 미리보기 색상
    static readonly Color[] PreviewColors =
    {
        Color.clear,
        new Color(0.45f, 0.8f, 0.35f),
        new Color(0.55f, 0.55f, 0.55f),
        new Color(0.65f, 0.45f, 0.3f),
        new Color(0.3f, 0.5f, 0.85f)
    };

    [Serializable]
    class SavedState
    {
        public float PlayerHeight;
        public float HeightTolerance = 0.5f;
        public float CliffAngle = 45f;
        public TerrainLayer WalkableLayer;
        public TerrainLayer CliffLayer;
        public TerrainLayer HighLayer;
        public TerrainLayer LowLayer;
        public int CliffExpand = 1;
        public int BlendRadius = 1;
        public int MinimapLongSide = 512;
        public Color FillColor = MinimapImageOptions.DefaultFillColor;
        public Color OutlineColor = MinimapImageOptions.DefaultOutlineColor;
        public int OutlineWidth = MinimapImageOptions.DefaultOutlineWidth;
        public int SmoothRadius = 1;
    }

    SavedState _state = new SavedState();
    readonly List<Terrain> _targets = new List<Terrain>();
    readonly List<string> _missingData = new List<string>();
    Texture2D _preview;
    Rect _previewWorldRect;
    bool _picking;
    Vector2 _scroll;
    string _message;
    MessageType _messageType = MessageType.Info;

    bool CanRun => _targets.Count > 0 && _missingData.Count == 0;

    [MenuItem("Tools/Terrain/이동 영역 칠하기·미니맵")]
    static void OpenWindow()
    {
        TerrainWalkableWindow window = GetWindow<TerrainWalkableWindow>("이동 영역 칠하기·미니맵");
        window.minSize = new Vector2(420f, 520f);
        window.Show();
    }

    static bool TryPickHeight(Ray ray, out float height)
    {
        // 가장 가까운 TerrainCollider 충돌 지점 높이
        height = 0f;
        float nearest = float.MaxValue;
        bool found = false;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (!terrain.TryGetComponent(out TerrainCollider terrainCollider)) continue;
            if (!terrainCollider.Raycast(ray, out RaycastHit hit, nearest)) continue;

            nearest = hit.distance;
            height = hit.point.y;
            found = true;
        }

        return found;
    }

    void OnEnable()
    {
        // 창 재생성과 Unity 재시작 후에도 마지막 설정 유지
        string json = EditorPrefs.GetString(PrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(json)) EditorJsonUtility.FromJsonOverwrite(json, _state);

        SceneView.duringSceneGui += OnSceneGUI;
        RefreshTargets();
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PrefsKey, EditorJsonUtility.ToJson(_state));
        SceneView.duringSceneGui -= OnSceneGUI;
        _picking = false;
        ClearPreview();
    }

    void OnSelectionChange()
    {
        RefreshTargets();
        ClearPreview();
        Repaint();
    }

    void RefreshTargets()
    {
        _targets.Clear();
        _missingData.Clear();
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (!selected.TryGetComponent(out Terrain terrain)) continue;

            if (terrain.terrainData == null) _missingData.Add(terrain.name);
            else _targets.Add(terrain);
        }
    }

    void ClearPreview()
    {
        if (_preview != null) DestroyImmediate(_preview);
        _preview = null;
    }

    TerrainWalkableSettings CreateSettings()
    {
        return new TerrainWalkableSettings(_state.PlayerHeight, _state.HeightTolerance, _state.CliffAngle);
    }

    TerrainWalkablePaintOptions CreatePaintOptions()
    {
        return new TerrainWalkablePaintOptions(_state.WalkableLayer, _state.CliffLayer, _state.HighLayer,
            _state.LowLayer, _state.CliffExpand, _state.BlendRadius);
    }

    MinimapImageOptions CreateMinimapOptions()
    {
        return new MinimapImageOptions(_state.MinimapLongSide, _state.FillColor, _state.OutlineColor,
            _state.OutlineWidth);
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!_picking) return;

        Event current = Event.current;
        // 스포이트 대기 중 Scene 뷰 클릭의 오브젝트 선택 변경 방지
        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            _picking = false;
            current.Use();
            Repaint();
            return;
        }

        if (current.type != EventType.MouseDown || current.button != 0) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        if (TryPickHeight(ray, out float height))
        {
            _state.PlayerHeight = height;
            _message = $"플레이어 높이를 {height:0.###}로 지정했습니다.";
            _messageType = MessageType.Info;
            ClearPreview();
        }
        else
        {
            _message = "클릭 위치에서 TerrainCollider가 있는 Terrain을 찾지 못했습니다.";
            _messageType = MessageType.Warning;
        }

        _picking = false;
        current.Use();
        Repaint();
    }

    void RefreshPreview()
    {
        ClearPreview();
        TerrainWalkableMask mask = TerrainWalkableMask.BuildForArea(_targets, PreviewLongSide, CreateSettings());

        Color[] pixels = new Color[mask.Width * mask.Height];
        for (int z = 0; z < mask.Height; z++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                pixels[z * mask.Width + x] = PreviewColors[(int)mask.Get(x, z)];
            }
        }

        _preview = new Texture2D(mask.Width, mask.Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        _preview.SetPixels(pixels);
        _preview.Apply();
        _previewWorldRect = mask.WorldRect;
    }

    void Paint()
    {
        try
        {
            bool succeeded = TerrainWalkablePainter.TryPaint(_targets, CreateSettings(), CreatePaintOptions(), out _message);
            _messageType = succeeded ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            _message = "칠하기에 실패했습니다. 실행 중 변경은 되돌렸습니다.\n" + exception.Message;
            _messageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    void Export()
    {
        string assetPath = MinimapImageExporter.AskSavePath($"{_targets[0].name}_Minimap");

        // 저장 대화상자 취소
        if (string.IsNullOrEmpty(assetPath)) return;

        try
        {
            bool succeeded = TerrainMinimapExporter.TryExport(_targets, CreateSettings(), _state.SmoothRadius,
                CreateMinimapOptions(), assetPath, out _message);
            _messageType = succeeded ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            _message = "미니맵 PNG 저장에 실패했습니다.\n" + exception.Message;
            _messageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    void DrawTargets()
    {
        EditorGUILayout.LabelField("대상 Terrain", EditorStyles.boldLabel);
        if (_targets.Count == 0 && _missingData.Count == 0)
        {
            EditorGUILayout.HelpBox("Hierarchy에서 Terrain을 선택하세요. 여러 개를 선택할 수 있습니다.", MessageType.Info);
            return;
        }

        foreach (Terrain target in _targets)
        {
            EditorGUILayout.LabelField(target.name, AssetDatabase.GetAssetPath(target.terrainData), EditorStyles.miniLabel);
        }

        if (_missingData.Count > 0)
            EditorGUILayout.HelpBox($"TerrainData가 없는 Terrain: {string.Join(", ", _missingData)}", MessageType.Warning);
    }

    void DrawJudgement()
    {
        EditorGUILayout.LabelField("판정 기준", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.HorizontalScope())
        {
            _state.PlayerHeight = EditorGUILayout.FloatField("플레이어 높이 (월드 Y)", _state.PlayerHeight);
            if (GUILayout.Button(_picking ? "클릭 대기 (Esc 취소)" : "스포이트", GUILayout.Width(130f)))
            {
                _picking = !_picking;
                SceneView.RepaintAll();
            }
        }

        _state.HeightTolerance = Mathf.Max(0f, EditorGUILayout.FloatField("높이 허용치", _state.HeightTolerance));
        _state.CliffAngle = EditorGUILayout.Slider("절벽 경사 각도", _state.CliffAngle, 1f, 89f);
        if (EditorGUI.EndChangeCheck()) ClearPreview();

        if (_picking) EditorGUILayout.HelpBox("Scene 뷰에서 플레이어가 다닐 평지를 클릭하세요.", MessageType.Info);
    }

    void DrawPreview()
    {
        EditorGUILayout.LabelField("판정 미리보기", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!CanRun))
        {
            if (GUILayout.Button("미리보기 갱신")) RefreshPreview();
        }

        if (_preview == null) return;

        Rect area = GUILayoutUtility.GetAspectRect((float)_preview.width / _preview.height, GUILayout.MaxWidth(320f));
        GUI.DrawTexture(area, _preview, ScaleMode.ScaleToFit);
        EditorGUILayout.LabelField("초록 평지 · 회색 절벽 · 갈색 높은 곳 · 파랑 낮은 곳", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            $"월드 범위: 원점 ({_previewWorldRect.x:0.##}, {_previewWorldRect.y:0.##}) · " +
            $"크기 ({_previewWorldRect.width:0.##}, {_previewWorldRect.height:0.##})",
            EditorStyles.miniLabel);
    }

    void DrawPaint(bool playing)
    {
        EditorGUILayout.LabelField("Terrain 칠하기", EditorStyles.boldLabel);
        _state.WalkableLayer = (TerrainLayer)EditorGUILayout.ObjectField("평지 Layer", _state.WalkableLayer, typeof(TerrainLayer), false);
        _state.CliffLayer = (TerrainLayer)EditorGUILayout.ObjectField("절벽 Layer", _state.CliffLayer, typeof(TerrainLayer), false);
        _state.HighLayer = (TerrainLayer)EditorGUILayout.ObjectField("높은 곳 Layer", _state.HighLayer, typeof(TerrainLayer), false);
        _state.LowLayer = (TerrainLayer)EditorGUILayout.ObjectField("낮은 곳 Layer", _state.LowLayer, typeof(TerrainLayer), false);
        _state.CliffExpand = Mathf.Max(0, EditorGUILayout.IntField("절벽 넓히기 (칸)", _state.CliffExpand));
        _state.BlendRadius = Mathf.Max(0, EditorGUILayout.IntField("경계 블렌딩 (칸)", _state.BlendRadius));
        EditorGUILayout.LabelField("비워둔 분류는 기존 칠하기를 유지합니다. 실행 후 Ctrl+Z로 되돌릴 수 있습니다.", EditorStyles.wordWrappedMiniLabel);

        if (playing) EditorGUILayout.HelpBox("플레이 모드를 종료한 뒤 칠하세요.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!CanRun || playing || !CreatePaintOptions().HasAnyLayer))
        {
            if (GUILayout.Button("선택 Terrain 칠하기", GUILayout.Height(30f))) Paint();
        }
    }

    void DrawMinimap()
    {
        EditorGUILayout.LabelField("미니맵 PNG", EditorStyles.boldLabel);
        _state.MinimapLongSide = Mathf.Clamp(EditorGUILayout.IntField("긴 변 픽셀", _state.MinimapLongSide), 16, 4096);
        _state.FillColor = EditorGUILayout.ColorField("채우기 색", _state.FillColor);
        _state.OutlineColor = EditorGUILayout.ColorField("테두리 색", _state.OutlineColor);
        _state.OutlineWidth = Mathf.Clamp(EditorGUILayout.IntField("테두리 두께 (px)", _state.OutlineWidth), 0, 32);
        _state.SmoothRadius = Mathf.Max(0, EditorGUILayout.IntField("경계 부드럽게 (px)", _state.SmoothRadius));
        EditorGUILayout.LabelField($"선택한 Terrain 전체를 배경이 투명한 한 장으로 {MinimapImageExporter.ExportFolder}에 저장합니다. Terrain 칠하기는 변경하지 않습니다.", EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUI.DisabledScope(!CanRun))
        {
            if (GUILayout.Button("미니맵 PNG 내보내기", GUILayout.Height(30f))) Export();
        }
    }

    void OnGUI()
    {
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox("플레이어 높이의 평지를 이동 가능 영역으로 판정합니다. 칠하기와 미니맵 내보내기는 같은 판정을 사용합니다.", MessageType.Info);

        DrawTargets();
        EditorGUILayout.Space();
        DrawJudgement();
        EditorGUILayout.Space();
        DrawPreview();
        EditorGUILayout.Space();
        DrawPaint(playing);
        EditorGUILayout.Space();
        DrawMinimap();

        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
        EditorGUILayout.EndScrollView();
    }
}
