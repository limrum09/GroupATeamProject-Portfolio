using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프로젝트 스킬 Definition의 빈 UID와 중복 UID 검사
/// </summary>
public static class SkillUIDValidator
{
    [MenuItem("Tools/Skills/Validate UIDs")]
    static void ValidateAll()
    {
        Dictionary<string, string> registered = new Dictionary<string, string>(StringComparer.Ordinal);

        string[] assetGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Project" });

        int errorCount = 0;
        int skillCount = 0;

        foreach (string assetGuid in assetGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(assetGuid);

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is SkillDefinition definition))
                    continue;

                skillCount++;

                try
                {
                    definition.ValidateSkillUID();
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogError($"{path}: {exception.Message}", definition);

                    errorCount++;
                    continue;
                }

                string location = $"{path} ({definition.name})";

                if (registered.TryGetValue(definition.SkillUID, out string existingLocation))
                {
                    Debug.LogError($"중복 스킬 UID: {definition.SkillUID}\n" + $"{existingLocation}\n{location}", definition);

                    errorCount++;
                    continue;
                }

                registered.Add(definition.SkillUID, location);
            }
        }

        if (errorCount == 0)
        {
            Debug.Log($"스킬 UID 검사 완료: {skillCount}개");
        }
        else
        {
            Debug.LogError($"스킬 UID 검사 실패: {errorCount}개 오류");
        }
    }
}