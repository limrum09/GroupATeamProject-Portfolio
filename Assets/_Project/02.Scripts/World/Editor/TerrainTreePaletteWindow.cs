using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기준 Terrain의 나무 목록 일괄 등록 편집 창
/// </summary>
public class TerrainTreePaletteWindow : EditorWindow
{
    [SerializeField] Terrain _sourceTerrain;
    [SerializeField] List<Terrain> _targetTerrains = new List<Terrain>();

    SerializedObject _windowState;
    SerializedProperty _sourceProperty;
    SerializedProperty _targetsProperty;
    Vector2 _scroll;
    string _message;
    MessageType _messageType = MessageType.Info;

    [MenuItem("Tools/Terrain/나무 목록 일괄 등록")]
    static void OpenWindow()
    {
        TerrainTreePaletteWindow window = GetWindow<TerrainTreePaletteWindow>("나무 목록 일괄 등록");
        window.minSize = new Vector2(480f, 360f);
        window.Show();
    }

    void OnEnable()
    {
        _windowState = new SerializedObject(this);
        _sourceProperty = _windowState.FindProperty("_sourceTerrain");
        _targetsProperty = _windowState.FindProperty("_targetTerrains");
        Undo.undoRedoPerformed += Repaint;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        _windowState.Dispose();
    }

    void OnSelectionChange()
    {
        Repaint();
    }

    void OnProjectChange()
    {
        Repaint();
    }

    void SetSourceFromSelection()
    {
        GameObject selected = Selection.activeGameObject;
        Terrain terrain = selected != null ? selected.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            _message = "Hierarchy에서 기준으로 사용할 Terrain을 선택하세요.";
            _messageType = MessageType.Warning;
            return;
        }

        Undo.RecordObject(this, "기준 Terrain 지정");
        _sourceTerrain = terrain;
        _targetTerrains.RemoveAll(target => target == terrain ||
            (target != null && target.terrainData == terrain.terrainData));
        _windowState.Update();
        _message = string.Empty;
    }

    void AddSelectionToTargets()
    {
        List<Terrain> selectedTerrains = new List<Terrain>();
        foreach (GameObject selected in Selection.gameObjects)
        {
            Terrain terrain = selected.GetComponent<Terrain>();
            if (terrain == null || _targetTerrains.Contains(terrain)) continue;
            if (_sourceTerrain != null && terrain.terrainData == _sourceTerrain.terrainData) continue;
            selectedTerrains.Add(terrain);
        }

        if (selectedTerrains.Count == 0)
        {
            _message = "Hierarchy에서 기준과 다른 대상 Terrain을 선택하세요. 여러 개를 선택할 수 있습니다.";
            _messageType = MessageType.Warning;
            return;
        }

        Undo.RecordObject(this, "대상 Terrain 추가");
        _targetTerrains.AddRange(selectedTerrains);
        _windowState.Update();
        _message = string.Empty;
    }

    bool DrawPreview(out int totalAdded)
    {
        totalAdded = 0;
        bool valid = _targetTerrains.Count > 0;
        HashSet<TerrainData> visited = new HashSet<TerrainData>();

        foreach (Terrain target in _targetTerrains)
        {
            if (!TerrainTreePaletteUtility.TryGetAdditionCount(_sourceTerrain, target, out int addedCount, out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
                valid = false;
                continue;
            }

            if (!visited.Add(target.terrainData))
            {
                EditorGUILayout.LabelField(target.name, "같은 TerrainData · 한 번만 처리");
                continue;
            }

            int currentCount = target.terrainData.treePrototypes.Length;
            EditorGUILayout.LabelField(target.name, $"{currentCount}종 → {currentCount + addedCount}종 (+{addedCount})");
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(target.terrainData), EditorStyles.wordWrappedMiniLabel);
            totalAdded += addedCount;
        }

        return valid;
    }

    void AppendMissing()
    {
        try
        {
            bool succeeded = TerrainTreePaletteUtility.TryAppendMissing(_sourceTerrain, _targetTerrains, out _message);
            _messageType = succeeded ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            _message = "등록에 실패했습니다. 실행 중 변경은 되돌렸습니다.\n" + exception.Message;
            _messageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    void OnGUI()
    {
        _windowState.Update();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox("기준 Terrain에 등록된 나무 종류를 대상 목록 끝에 추가합니다. 같은 프리팹과 같은 설정은 중복 등록하지 않습니다.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_sourceProperty, new GUIContent("기준 Terrain"));
        EditorGUILayout.PropertyField(_targetsProperty, new GUIContent("대상 Terrain"), true);
        if (EditorGUI.EndChangeCheck()) _message = string.Empty;
        _windowState.ApplyModifiedProperties();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("선택 Terrain을 기준으로")) SetSourceFromSelection();
            if (GUILayout.Button("선택 Terrain을 대상에 추가")) AddSelectionToTargets();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("등록 미리보기", EditorStyles.boldLabel);
        bool valid = DrawPreview(out int totalAdded);
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        if (playing)
        {
            EditorGUILayout.HelpBox("플레이 모드를 종료한 뒤 등록하세요.", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!valid || totalAdded == 0 || playing))
        {
            if (GUILayout.Button($"누락된 나무 종류 등록 ({totalAdded}종)", GUILayout.Height(30f))) AppendMissing();
        }

        EditorGUILayout.LabelField("기존 나무 배치와 목록 순서를 유지합니다. 등록 후 Ctrl+Z로 되돌릴 수 있습니다.", EditorStyles.wordWrappedMiniLabel);
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
        EditorGUILayout.EndScrollView();
    }
}
