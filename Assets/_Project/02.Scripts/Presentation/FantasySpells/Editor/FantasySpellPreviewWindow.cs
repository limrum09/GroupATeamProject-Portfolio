using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>편집 모드 스펠 재생 및 시간 이동 창</summary>
public sealed class FantasySpellPreviewWindow : EditorWindow
{
    PreviewRenderUtility _preview;
    GameObject _asset;
    GameObject _instance;
    BakedSpellPlayer _player;
    float _time;
    float _speed = 1f;
    bool _playing;
    double _lastUpdate;
    Vector2 _orbit = new Vector2(35f, -25f);
    Vector3 _center = Vector3.up * 2f;
    float _distance = 35f;
    string[] _paths = Array.Empty<string>();
    string _error;

    /// <summary>선택한 완성 스펠의 미리보기 열기</summary>
    /// <param name="asset">완성 스펠 프리팹 또는 인스턴스</param>
    public static void Open(GameObject asset)
    {
        FantasySpellPreviewWindow window = GetWindow<FantasySpellPreviewWindow>("스펠 미리보기");
        window.minSize = new Vector2(620f, 460f);
        window.Load(asset);
        window.Show();
    }

    [MenuItem("Tools/VFX/Fantasy Spells/미리보기")]
    static void OpenSelected()
    {
        GameObject asset = Selection.activeGameObject;
        if (asset == null || asset.GetComponent<BakedSpellPlayer>() == null)
        {
            string first = Directory.Exists(FantasySpellBaker.Output) ? Directory.GetFiles(FantasySpellBaker.Output, "*.prefab").OrderBy(path => path).FirstOrDefault() : null;
            asset = first == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(first.Replace('\\', '/'));
        }
        Open(asset);
    }

    void OnEnable()
    {
        EditorApplication.update += Tick;
        _lastUpdate = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        Cleanup();
    }

    void Cleanup()
    {
        if (_instance != null) Object.DestroyImmediate(_instance);
        _preview?.Cleanup();
        _preview = null;
        _instance = null;
        _player = null;
    }

    void Load(GameObject asset)
    {
        Cleanup();
        _asset = asset;
        _time = 0f;
        _error = null;
        _playing = false;
        _paths = Directory.Exists(FantasySpellBaker.Output) ? Directory.GetFiles(FantasySpellBaker.Output, "*.prefab").Select(path => path.Replace('\\', '/')).OrderBy(path => path).ToArray() : Array.Empty<string>();
        if (asset == null) return;
        if (asset.GetComponent<BakedSpellPlayer>() == null)
        {
            _error = "Baked Spell Player가 있는 완성 프리팹 선택 필요";
            return;
        }
        _preview = new PreviewRenderUtility();
        _preview.cameraFieldOfView = 35f;
        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.065f, 0.075f, 0.095f);
        _preview.camera.nearClipPlane = 0.05f;
        _preview.camera.farClipPlane = 2000f;
        _preview.ambientColor = Color.gray;
        _preview.lights[0].intensity = 1.2f;
        _preview.lights[0].transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        _preview.lights[1].intensity = 0.8f;
        _instance = Object.Instantiate(asset);
        _instance.hideFlags = HideFlags.HideAndDontSave;
        _instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _preview.AddSingleGO(_instance);
        _player = _instance.GetComponent<BakedSpellPlayer>();
        _player.Restart(true);
        _player.PreviewAt(Mathf.Min(1f, _player.Definition.EndSeconds));
        FrameBounds();
        _player.Restart(true);
        Repaint();
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float delta = Mathf.Min(0.1f, (float)(now - _lastUpdate));
        _lastUpdate = now;
        if (!_playing || _player == null) return;
        _time += delta * _speed;
        if (!_player.Definition.IsPersistent && _time > _player.Definition.EndSeconds)
        {
            _time = _player.Definition.EndSeconds;
            _playing = false;
        }
        _player.PreviewAt(_time);
        Repaint();
    }

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GameObject selected = (GameObject)EditorGUILayout.ObjectField(_asset, typeof(GameObject), false);
            if (selected != _asset) Load(selected);
            if (GUILayout.Button("이전", EditorStyles.toolbarButton, GUILayout.Width(45))) SelectOffset(-1);
            if (GUILayout.Button("다음", EditorStyles.toolbarButton, GUILayout.Width(45))) SelectOffset(1);
        }
        if (_error != null) { EditorGUILayout.HelpBox(_error, MessageType.Error); return; }
        if (_player == null) { EditorGUILayout.HelpBox("완성 프리팹을 선택하면 Play Mode 없이 재생 가능", MessageType.Info); return; }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "일시 정지" : "재생", GUILayout.Width(85))) _playing = !_playing;
            if (GUILayout.Button("처음으로", GUILayout.Width(85))) { _time = 0f; _player.Restart(true); }
            if (GUILayout.Button("화면 맞춤", GUILayout.Width(85))) FrameBounds();
            _speed = EditorGUILayout.Slider("속도", _speed, 0.1f, 2f);
        }
        BakedSpellDefinition definition = _player.Definition;
        float horizon = definition.IsPersistent ? Mathf.Max(30f, _time + 1f, definition.EndSeconds) : definition.EndSeconds;
        float time = EditorGUILayout.Slider("재생 위치 (초)", _time, 0f, horizon);
        if (Math.Abs(time - _time) > 0.0001f)
        {
            _playing = false;
            _time = time;
            _player.PreviewAt(time);
        }
        EditorGUILayout.LabelField(definition.IsPersistent ? "지속형 · 종료 없음" : $"최대 종료 {definition.EndSeconds:0.###}초", $"마지막 생성 {definition.LastSpawnSeconds:0.###}초");
        EditorGUILayout.LabelField("드래그: 회전  ·  휠: 확대/축소  ·  메시와 이동도 함께 재생", EditorStyles.miniLabel);
        Rect rect = GUILayoutUtility.GetRect(100f, 10000f, 200f, 10000f, GUILayout.ExpandHeight(true));
        Event current = Event.current;
        if (rect.Contains(current.mousePosition))
        {
            if (current.type == EventType.MouseDrag && current.button == 0) { _orbit += current.delta * 0.5f; current.Use(); Repaint(); }
            if (current.type == EventType.ScrollWheel) { _distance = Mathf.Clamp(_distance * Mathf.Exp(current.delta.y * 0.05f), 0.5f, 1000f); current.Use(); Repaint(); }
        }
        if (current.type != EventType.Repaint) return;
        _preview.BeginPreview(rect, GUIStyle.none);
        Quaternion rotation = Quaternion.Euler(-_orbit.y, _orbit.x, 0f);
        _preview.camera.transform.SetPositionAndRotation(_center + rotation * Vector3.back * _distance, rotation);
        _preview.Render(true);
        Texture texture = _preview.EndPreview();
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
    }

    void FrameBounds()
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
        foreach (Renderer renderer in _instance.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled && renderer.bounds.size.sqrMagnitude < 1000000f) bounds.Encapsulate(renderer.bounds);
        }
        _center = bounds.center;
        _distance = Mathf.Max(8f, bounds.extents.magnitude * 2.7f);
    }

    void SelectOffset(int offset)
    {
        if (_paths.Length == 0) return;
        int current = Array.IndexOf(_paths, AssetDatabase.GetAssetPath(_asset));
        int next = (Mathf.Max(current, 0) + offset + _paths.Length) % _paths.Length;
        Load(AssetDatabase.LoadAssetAtPath<GameObject>(_paths[next]));
    }
}

/// <summary>완성 스펠의 재생 정보와 미리보기 진입점</summary>
[CustomEditor(typeof(BakedSpellPlayer))]
public sealed class BakedSpellPlayerEditor : Editor
{
    /// <summary>계산 결과와 편집 모드 재생 버튼 표시</summary>
    public override void OnInspectorGUI()
    {
        BakedSpellPlayer player = (BakedSpellPlayer)target;
        if (GUILayout.Button("스펠 미리보기 열기", GUILayout.Height(32f))) FantasySpellPreviewWindow.Open(player.gameObject);
        if (player.Definition != null)
        {
            BakedSpellDefinition data = player.Definition;
            EditorGUILayout.LabelField("파티클", data.Particles.Length.ToString());
            EditorGUILayout.LabelField("메시", data.Surfaces.Length.ToString());
            EditorGUILayout.LabelField("마지막 생성", $"{data.LastSpawnSeconds:0.###}초");
            EditorGUILayout.LabelField("최대 종료", data.IsPersistent ? "지속형" : $"{data.EndSeconds:0.###}초");
        }
        EditorGUILayout.HelpBox("파츠는 모두 미리 조립됨. 이동·재질 소멸을 함께 확인하려면 위 미리보기 사용", MessageType.Info);
        using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
    }
}
