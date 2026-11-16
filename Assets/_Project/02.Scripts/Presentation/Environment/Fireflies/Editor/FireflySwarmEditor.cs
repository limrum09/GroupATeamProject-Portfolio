using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 반딧불 경로 편집, 설정 진단 및 선택 영역의 부유 범위 표시
/// </summary>
[CustomEditor(typeof(FireflySwarm))]
public class FireflySwarmEditor : Editor
{
    readonly List<PathPreview> _previews = new List<PathPreview>();
    readonly Vector3[] _circle = new Vector3[25];
    SerializedProperty _script;
    SerializedProperty _profile;
    SerializedProperty _prefab;
    SerializedProperty _seed;
    SerializedProperty _paths;
    double _nextRefresh;
    string _error;
    long _total;

    class PathPreview
    {
        public Vector3[] Points;
        public float Horizontal;
        public float Vertical;
        public string Label;
    }

    void OnEnable()
    {
        _script = serializedObject.FindProperty("m_Script");
        _profile = serializedObject.FindProperty("_profile");
        _prefab = serializedObject.FindProperty("_prefab");
        _seed = serializedObject.FindProperty("_seed");
        _paths = serializedObject.FindProperty("_paths");
    }

    void RefreshPreview()
    {
        _nextRefresh = EditorApplication.timeSinceStartup + 0.5d;
        FireflySwarm swarm = (FireflySwarm)target;
        FireflyValidation.Validate(swarm, out _error);
        _total = 0;
        _previews.Clear();
        if (swarm.Paths == null) return;
        for (int i = 0; i < swarm.Paths.Count; i++)
        {
            FireflyPathSettings settings = swarm.Paths[i];
            if (settings == null) continue;
            _total += settings.Count;
            if (settings.Count == 0 || !FireflyValidation.ValidatePath(settings, out _, out _)) continue;
            using (FireflySplinePath path = new FireflySplinePath(settings, Allocator.Temp))
            {
                Vector3[] points = new Vector3[97];
                for (int j = 0; j < points.Length; j++)
                    points[j] = path.Evaluate(path.Length * (double)j / (points.Length - 1));
                _previews.Add(new PathPreview
                {
                    Points = points,
                    Horizontal = settings.HorizontalSway,
                    Vertical = settings.VerticalSway,
                    Label = $"경로 {i + 1}: {settings.Count}마리 / {path.Length:F2} / {(path.Closed ? "순환" : "왕복") }"
                });
            }
        }
    }

    void DrawEnvelope(Vector3 center, float horizontal, float vertical)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < _circle.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / (_circle.Length - 1);
                _circle[i] = center + new Vector3(Mathf.Cos(angle) * horizontal, vertical * side,
                    Mathf.Sin(angle) * horizontal);
            }
            Handles.DrawAAPolyLine(_circle);
        }
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * horizontal, 0f, Mathf.Sin(angle) * horizontal);
            Handles.DrawLine(center + offset - Vector3.up * vertical, center + offset + Vector3.up * vertical);
        }
    }

    void OnSceneGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (EditorApplication.timeSinceStartup >= _nextRefresh) RefreshPreview();
        Color previous = Handles.color;
        foreach (PathPreview preview in _previews)
        {
            Handles.color = new Color(0.6f, 1f, 0.2f, 0.9f);
            Handles.DrawAAPolyLine(2f, preview.Points);
            Handles.Label(preview.Points[0], preview.Label);
            Handles.color = new Color(0.6f, 1f, 0.2f, 0.3f);
            for (int i = 0; i < preview.Points.Length; i += 8)
                DrawEnvelope(preview.Points[i], preview.Horizontal, preview.Vertical);
        }
        Handles.color = previous;
    }

    /// <summary>
    /// 직렬화 속성 기반 경로 목록과 공통 검증 결과 표시
    /// </summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(_script);
        EditorGUILayout.HelpBox("경로 목록의 + / - 로 추가·제거하세요. 간격은 같은 스플라인의 이웃 간 경로상 거리입니다. " +
            "다른 경로와 교차점의 충돌은 검사하지 않습니다. 수평 반경·수직 높이 전체가 안전한 영역에 들어오도록 배치하세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            EditorGUILayout.PropertyField(_profile);
            EditorGUILayout.PropertyField(_prefab);
            EditorGUILayout.PropertyField(_seed);
            EditorGUILayout.PropertyField(_paths, new GUIContent("경로 목록"), true);
        }
        if (serializedObject.ApplyModifiedProperties())
        {
            _nextRefresh = 0d;
            SceneView.RepaintAll();
        }
        if (EditorApplication.timeSinceStartup >= _nextRefresh) RefreshPreview();
        EditorGUILayout.LabelField("전체 개체 수", _total.ToString());
        if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);
        else EditorGUILayout.HelpBox("현재 설정으로 생성 가능합니다. 0마리 경로는 제외되며, 1마리 경로에는 이웃 간격 제약이 없습니다.", MessageType.Info);
        EditorGUILayout.HelpBox("경로·Transform·프로필 설정은 재생 전에 편집하세요. 실행 중에는 시작 시점의 설정을 사용합니다. " +
            "닫힌 경로의 연결부는 Tangent를 매끄럽게 맞추세요. 빛 번짐은 카메라의 Bloom 설정에 따릅니다.", MessageType.None);
    }
}
