using UnityEditor;

/// <summary>
/// 목표 종류에 필요한 설정만 표시하는 퀘스트 Inspector
/// </summary>
[CustomEditor(typeof(QuestDefinition))]
[CanEditMultipleObjects]
public class QuestDefinitionEditor : Editor
{
    SerializedProperty _script;
    SerializedProperty _questId;
    SerializedProperty _displayName;
    SerializedProperty _description;
    SerializedProperty _kind;
    SerializedProperty _goalType;
    SerializedProperty _targetValue;
    SerializedProperty _anyEnemyRank;
    SerializedProperty _enemyRank;
    SerializedProperty _goldSources;
    SerializedProperty _nodeTypes;
    SerializedProperty _metaGoldReward;

    void OnEnable()
    {
        _script = serializedObject.FindProperty("m_Script");
        _questId = serializedObject.FindProperty("_questId");
        _displayName = serializedObject.FindProperty("_displayName");
        _description = serializedObject.FindProperty("_description");
        _kind = serializedObject.FindProperty("_kind");
        _goalType = serializedObject.FindProperty("_goalType");
        _targetValue = serializedObject.FindProperty("_targetValue");
        _anyEnemyRank = serializedObject.FindProperty("_anyEnemyRank");
        _enemyRank = serializedObject.FindProperty("_enemyRank");
        _goldSources = serializedObject.FindProperty("_goldSources");
        _nodeTypes = serializedObject.FindProperty("_nodeTypes");
        _metaGoldReward = serializedObject.FindProperty("_metaGoldReward");
    }

    void DrawGoalConditions()
    {
        if (_goalType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("선택한 퀘스트들의 목표 종류가 다릅니다. " + "종류를 통일하면 해당 조건을 함께 편집할 수 있습니다.", MessageType.Info);

            return;
        }

        QuestGoalType goalType = (QuestGoalType)_goalType.intValue;

        switch (goalType)
        {
            case QuestGoalType.EnemyKill:
                EditorGUILayout.PropertyField(_anyEnemyRank);

                if (_anyEnemyRank.hasMultipleDifferentValues || !_anyEnemyRank.boolValue)
                    EditorGUILayout.PropertyField(_enemyRank);
                break;
            case QuestGoalType.GoldAcquired:
                EditorGUILayout.PropertyField(_goldSources, true);
                break;
            case QuestGoalType.GoldHeld:
                break;
            case QuestGoalType.NodeCleared:
            case QuestGoalType.NodeRankCleared:
                EditorGUILayout.PropertyField(_nodeTypes, true);
                break;
        }
    }

    /// <summary>
    /// 공용 설정과 선택한 목표의 조건 표시
    /// </summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(_script);
        }

        EditorGUILayout.PropertyField(_questId);
        EditorGUILayout.PropertyField(_displayName);
        EditorGUILayout.PropertyField(_description);
        EditorGUILayout.PropertyField(_kind);
        EditorGUILayout.PropertyField(_goalType);
        EditorGUILayout.PropertyField(_targetValue);

        DrawGoalConditions();

        EditorGUILayout.PropertyField(_metaGoldReward);

        serializedObject.ApplyModifiedProperties();
    }
}