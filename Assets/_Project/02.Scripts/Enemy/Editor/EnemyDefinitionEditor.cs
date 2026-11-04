using UnityEditor;

/// <summary>
/// CSV 관리 능력치를 제외한 적 정의 Inspector
/// </summary>
[CustomEditor(typeof(EnemyDefinition))]
[CanEditMultipleObjects]
public class EnemyDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        }

        DrawPropertiesExcluding(serializedObject, "m_Script", "_maxHealth", "_attackPower",
            "_defense", "_spellPower", "_moveSpeed", "_baseAttackSpeed",
            "_criticalChance", "_criticalDamageMultiplier");
        serializedObject.ApplyModifiedProperties();
    }
}
