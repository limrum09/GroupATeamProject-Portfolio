using System;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 선택 NavMeshSurface의 이동 가능 영역 미니맵 PNG 내보내기 편집 창
/// </summary>
public class NavMeshMinimapWindow : EditorWindow
{
    const string PrefsKey = "GroupATeamProject.NavMeshMinimapWindow";
    const int PreviewLongSide = 256;

    // 투명 영역 확인용 미리보기 배경
    static readonly Color PreviewBackground = new Color(0.02f, 0.07f, 0.18f);

    [Serializable]
    class SavedState
    {
        public int MinimapLongSide = 512;
        public Color FillColor = MinimapImageOptions.DefaultFillColor;
        public Color OutlineColor = MinimapImageOptions.DefaultOutlineColor;
        public int OutlineWidth = MinimapImageOptions.DefaultOutlineWidth;
    }

    SavedState _state = new SavedState();
    NavMeshSurface _surface;
    Texture2D _preview;
    Vector2 _scroll;
    string _message;
    MessageType _messageType = MessageType.Info;

    [MenuItem("Tools/Terrain/NavMesh 미니맵")]
    static void OpenWindow()
    {
        NavMeshMinimapWindow window = GetWindow<NavMeshMinimapWindow>("NavMesh 미니맵");
        window.minSize = new Vector2(380f, 460f);
        window.Show();
    }

    void OnEnable()
    {
        // 창 재생성과 Unity 재시작 후에도 마지막 이미지 설정 유지, 씬 오브젝트는 저장하지 않음
        string json = EditorPrefs.GetString(PrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(json)) EditorJsonUtility.FromJsonOverwrite(json, _state);
        PickSurfaceFromSelection();
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PrefsKey, EditorJsonUtility.ToJson(_state));
        ClearPreview();
    }

    void OnSelectionChange()
    {
        PickSurfaceFromSelection();
        Repaint();
    }

    void PickSurfaceFromSelection()
    {
        // 선택에 Surface가 없으면 기존 지정 유지
        if (Selection.activeGameObject == null) return;
        if (!Selection.activeGameObject.TryGetComponent(out NavMeshSurface surface)) return;

        _surface = surface;
        ClearPreview();
    }

    void ClearPreview()
    {
        if (_preview != null) DestroyImmediate(_preview);
        _preview = null;
    }

    MinimapImageOptions CreateOptions()
    {
        return new MinimapImageOptions(_state.MinimapLongSide, _state.FillColor, _state.OutlineColor,
            _state.OutlineWidth);
    }

    float[] BuildCoverage(int longSide, out Rect worldRect, out Vector2Int size)
    {
        worldRect = NavMeshMinimapRasterizer.GetWorldRect(_surface);
        size = MinimapImageExporter.GetImageSize(worldRect, longSide);
        return NavMeshMinimapRasterizer.Rasterize(_surface, worldRect, size.x, size.y);
    }

    void RefreshPreview()
    {
        ClearPreview();
        float[] coverage = BuildCoverage(PreviewLongSide, out _, out Vector2Int size);

        // 미리보기 테두리 두께를 내보내기 해상도 비율로 축소, 테두리 사용 시 최소 1px
        int outlineWidth = Mathf.RoundToInt(_state.OutlineWidth * (float)PreviewLongSide / _state.MinimapLongSide);
        if (_state.OutlineWidth > 0) outlineWidth = Mathf.Max(1, outlineWidth);
        MinimapImageOptions options = new MinimapImageOptions(PreviewLongSide, _state.FillColor, _state.OutlineColor,
            outlineWidth);

        _preview = MinimapImageExporter.CreateTexture(coverage, size.x, size.y, options);
        _preview.hideFlags = HideFlags.HideAndDontSave;
    }

    void Export()
    {
        string assetPath = MinimapImageExporter.AskSavePath($"{_surface.name}_Minimap");

        // 저장 대화상자 취소
        if (string.IsNullOrEmpty(assetPath)) return;

        try
        {
            float[] coverage = BuildCoverage(_state.MinimapLongSide, out Rect worldRect, out Vector2Int size);
            bool succeeded = MinimapImageExporter.TryExport(coverage, size.x, size.y, worldRect, CreateOptions(),
                assetPath, out _message);
            _messageType = succeeded ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            _message = "미니맵 PNG 저장에 실패했습니다.\n" + exception.Message;
            _messageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    bool DrawSurface()
    {
        EditorGUILayout.LabelField("대상 NavMeshSurface", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _surface = (NavMeshSurface)EditorGUILayout.ObjectField("Surface", _surface, typeof(NavMeshSurface), true);
        if (EditorGUI.EndChangeCheck()) ClearPreview();

        if (!NavMeshMinimapRasterizer.TryValidateSurface(_surface, out string error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Warning);
            return false;
        }

        Rect rect = NavMeshMinimapRasterizer.GetWorldRect(_surface);
        EditorGUILayout.LabelField("Agent", NavMesh.GetSettingsNameFromID(_surface.agentTypeID), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            $"월드 범위: 원점 ({rect.x:0.##}, {rect.y:0.##}) · 크기 ({rect.width:0.##}, {rect.height:0.##})",
            EditorStyles.miniLabel);
        return true;
    }

    void DrawImageOptions()
    {
        EditorGUILayout.LabelField("미니맵 PNG", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _state.MinimapLongSide = Mathf.Clamp(EditorGUILayout.IntField("긴 변 픽셀", _state.MinimapLongSide), 16, 4096);
        _state.FillColor = EditorGUILayout.ColorField("채우기 색", _state.FillColor);
        _state.OutlineColor = EditorGUILayout.ColorField("테두리 색", _state.OutlineColor);
        _state.OutlineWidth = Mathf.Clamp(EditorGUILayout.IntField("테두리 두께 (px)", _state.OutlineWidth), 0, 32);
        if (EditorGUI.EndChangeCheck()) ClearPreview();
    }

    void DrawPreview(bool canRun)
    {
        using (new EditorGUI.DisabledScope(!canRun))
        {
            if (GUILayout.Button("미리보기 갱신")) RefreshPreview();
        }

        if (_preview == null) return;

        Rect area = GUILayoutUtility.GetAspectRect((float)_preview.width / _preview.height, GUILayout.MaxWidth(320f));
        EditorGUI.DrawRect(area, PreviewBackground);
        GUI.DrawTexture(area, _preview, ScaleMode.ScaleToFit);
    }

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox("Bake된 NavMesh 중 Surface Agent가 다닐 수 있는 영역을 투명 배경 미니맵으로 저장합니다.", MessageType.Info);

        bool canRun = DrawSurface();
        EditorGUILayout.Space();
        DrawImageOptions();
        EditorGUILayout.Space();
        DrawPreview(canRun);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"저장 위치: {MinimapImageExporter.ExportFolder}", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(!canRun))
        {
            if (GUILayout.Button("미니맵 PNG 내보내기", GUILayout.Height(30f))) Export();
        }

        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
        EditorGUILayout.EndScrollView();
    }
}
