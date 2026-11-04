using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AAA Projectiles Vol 2의 편집 모드 정면 발사 테스트 창
/// </summary>
public class ProjectileTestWindow : EditorWindow
{
    [SerializeField] string _selectedPath;
    [SerializeField] Vector3 _position = new Vector3(0f, 1f, 0f);
    [SerializeField] Vector3 _eulerAngles;
    [SerializeField] float _speed = 10f;
    [SerializeField] float _duration = 2f;
    [SerializeField] float _repeatInterval = 0.5f;
    [SerializeField] bool _showTrajectory;

    string[] _paths = Array.Empty<string>();
    string[] _names = Array.Empty<string>();
    int _selectedIndex = -1;
    GameObject _prefab;
    ProjectilePreviewSession _session;
    Vector2 _scroll;

    [MenuItem("Tools/VFX/AAA Projectiles Vol 2/정면 발사 테스트")]
    static void Open()
    {
        ProjectileTestWindow window = GetWindow<ProjectileTestWindow>("탄환 발사 테스트");
        window.Show();
    }

    void OnEnable()
    {
        minSize = new Vector2(440f, 480f);
        _session = new ProjectilePreviewSession();
        _session.OnChanged += Repaint;
        SceneView.duringSceneGui += DrawSceneGuide;
        RefreshCatalogue();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DrawSceneGuide;
        if (_session != null)
        {
            _session.OnChanged -= Repaint;
            _session.Dispose();
            _session = null;
        }
    }

    void OnProjectChange()
    {
        RefreshCatalogue();
        Repaint();
    }

    void OnSelectionChange()
    {
        Repaint();
    }

    void RefreshCatalogue()
    {
        _session.Clear();
        List<string> paths = new List<string>();
        if (AssetDatabase.IsValidFolder(ProjectilePreviewSession.PrefabFolder))
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ProjectilePreviewSession.PrefabFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string directory = Path.GetDirectoryName(path).Replace('\\', '/');
                if (directory == ProjectilePreviewSession.PrefabFolder &&
                    Path.GetFileNameWithoutExtension(path).StartsWith("Projectile ", StringComparison.Ordinal))
                    paths.Add(path);
            }
        }

        paths.Sort(CompareProjectilePaths);
        _paths = paths.ToArray();
        _names = new string[_paths.Length];
        for (int i = 0; i < _paths.Length; i++)
            _names[i] = Path.GetFileNameWithoutExtension(_paths[i]);

        // 최초 창 열기에서만 기본 선택, 삭제된 선택 항목은 자동 대체하지 않음
        if (string.IsNullOrEmpty(_selectedPath) && _paths.Length > 0)
            _selectedPath = _paths[0];
        _selectedIndex = Array.IndexOf(_paths, _selectedPath);
        _prefab = _selectedIndex < 0 ? null : AssetDatabase.LoadAssetAtPath<GameObject>(_selectedPath);
    }

    int CompareProjectilePaths(string left, string right)
    {
        string[] leftWords = Path.GetFileNameWithoutExtension(left).Split(' ');
        string[] rightWords = Path.GetFileNameWithoutExtension(right).Split(' ');
        if (leftWords.Length > 1 && rightWords.Length > 1 &&
            int.TryParse(leftWords[1], out int leftNumber) && int.TryParse(rightWords[1], out int rightNumber))
        {
            int comparison = leftNumber.CompareTo(rightNumber);
            if (comparison != 0) return comparison;
        }
        return string.Compare(left, right, StringComparison.Ordinal);
    }

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField("AAA Projectiles Vol 2 · 정면 발사", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Temp_Skill_Test_Scene의 Scene 뷰에서 확인하세요. Play Mode 없이 탄환 이동과 파티클을 재생합니다.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("탄환", _selectedIndex, _names);
            if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < _paths.Length)
            {
                _selectedIndex = selected;
                _selectedPath = _paths[selected];
                _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_selectedPath);
                _session.Clear();
            }
            if (GUILayout.Button("새로고침", GUILayout.Width(75f))) RefreshCatalogue();
        }

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("선택 프리팹", _prefab, typeof(GameObject), false);

        if (_paths.Length == 0)
            EditorGUILayout.HelpBox("지정된 Prefabs 폴더에서 Projectile 프리팹을 찾지 못했습니다.", MessageType.Warning);
        else if (_prefab == null)
            EditorGUILayout.HelpBox("저장된 선택 항목을 찾지 못했습니다. 탄환을 다시 선택하세요.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("발사 설정", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _position = EditorGUILayout.Vector3Field("발사 위치 (월드)", _position);
        _eulerAngles = EditorGUILayout.Vector3Field("발사 회전 (정면 +Z)", _eulerAngles);
        _speed = EditorGUILayout.FloatField("이동 속도 (unit/s)", _speed);
        _duration = EditorGUILayout.FloatField("최대 재생 시간 (초)", _duration);
        _repeatInterval = EditorGUILayout.FloatField("반복 간격 (초)", _repeatInterval);
        if (EditorGUI.EndChangeCheck()) _session.Clear();

        Transform selectedTransform = Selection.activeTransform;
        using (new EditorGUI.DisabledScope(selectedTransform == null ||
            selectedTransform.gameObject.scene.path != ProjectilePreviewSession.ScenePath))
        {
            if (GUILayout.Button("선택한 씬 오브젝트의 위치 · 정면 가져오기"))
            {
                _position = selectedTransform.position;
                _eulerAngles = selectedTransform.eulerAngles;
                _session.Clear();
            }
        }

        EditorGUI.BeginChangeCheck();
        _showTrajectory = EditorGUILayout.Toggle("경로 표시 (대기 중)", _showTrajectory);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

        string contextError = ProjectilePreviewSession.GetContextError();
        if (contextError != null) EditorGUILayout.HelpBox(contextError, MessageType.Info);

        using (new EditorGUI.DisabledScope(contextError != null || _prefab == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("단발 발사", "기존 미리보기를 정리하고 한 발 발사"), GUILayout.Height(30f)))
                    Play(false);
                if (GUILayout.Button(_session.IsRepeating ? "반복 발사 중지" : "반복 발사 시작", GUILayout.Height(30f)))
                {
                    if (_session.IsRepeating) _session.StopRepeating();
                    else Play(true);
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("전체 정리")) _session.Clear();
            using (new EditorGUI.DisabledScope(contextError != null || !HasFiniteTrajectory()))
            {
                if (GUILayout.Button("발사 구간 화면 맞춤")) FrameTrajectory();
            }
        }

        EditorGUILayout.LabelField("재생 상태", _session.IsRepeating ? "반복 발사 중" : "단발 / 대기");
        EditorGUILayout.LabelField("남아 있는 탄환", _session.ActiveCount.ToString());
        if (_session.Error != null) EditorGUILayout.HelpBox(_session.Error, MessageType.Error);
        EditorGUILayout.HelpBox("설정 변경 시 현재 미리보기 정리 · 속도 0 초과~500 · 시간 0.1~30초 · 간격 0.1~10초\n" +
            "동시 " + ProjectilePreviewSession.MaximumActiveShots + "발 초과 시 가장 오래된 탄환 정리\n" +
            "창 닫기, 씬 저장·전환, Play Mode 진입 시 자동 정리 · 충돌과 피해 판정 제외", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    void Play(bool repeat)
    {
        _session.Play(_prefab, _position, _eulerAngles, _speed, _duration, _repeatInterval, repeat);
    }

    bool HasFiniteTrajectory()
    {
        float sum = _position.sqrMagnitude + _eulerAngles.sqrMagnitude + _speed * _speed + _duration * _duration;
        return !float.IsNaN(sum) && !float.IsInfinity(sum) && _speed > 0f && _speed <= 500f &&
            _duration >= 0.1f && _duration <= 30f;
    }

    void DrawSceneGuide(SceneView sceneView)
    {
        // 탄환 관찰을 가리지 않도록 재생 중에는 모든 경로 안내 숨김
        if (!_showTrajectory || _session.IsRepeating || _session.ActiveCount > 0) return;

        if (ProjectilePreviewSession.GetContextError() != null || !HasFiniteTrajectory() ||
            Event.current.type != EventType.Repaint) return;

        Quaternion rotation = Quaternion.Euler(_eulerAngles);
        Vector3 end = _position + rotation * Vector3.forward * (_speed * _duration);
        using (new Handles.DrawingScope(Color.cyan))
        {
            Handles.DrawDottedLine(_position, end, 5f);
            Handles.ArrowHandleCap(0, _position, rotation, HandleUtility.GetHandleSize(_position), EventType.Repaint);
            Handles.Label(_position + Vector3.up * 0.3f, "탄환 발사 위치 (+Z)");
            Handles.Label(end, "최대 재생 시간 종료 위치");
        }
    }

    void FrameTrajectory()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null) sceneView = GetWindow<SceneView>();
        Vector3 end = _position + Quaternion.Euler(_eulerAngles) * Vector3.forward * (_speed * _duration);
        Bounds bounds = new Bounds(_position, Vector3.one * 4f);
        bounds.Encapsulate(end);
        sceneView.Frame(bounds, true);
        sceneView.Show();
        sceneView.Repaint();
    }
}
