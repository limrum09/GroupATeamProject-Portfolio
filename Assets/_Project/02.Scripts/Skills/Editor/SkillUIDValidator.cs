using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프로젝트 스킬 Definition의 빈 UID와 중복 UID 검사
/// </summary>
public static class SkillUIDValidator
{
    public const string SearchRoot = "Assets/_Project";

    /// <summary>
    /// 문제가 있는 스킬과 UID 검사 메시지
    /// </summary>
    public readonly struct Issue
    {
        public SkillDefinition Definition { get; }
        public string Message { get; }

        /// <summary>
        /// UID 검사 결과 생성
        /// </summary>
        /// <param name="definition">문제가 있는 스킬</param>
        /// <param name="message">오류 설명</param>
        public Issue(SkillDefinition definition, string message)
        {
            Definition = definition;
            Message = message;
        }
    }

    [MenuItem("Tools/Skills/Validate UIDs")]
    static void ValidateAll()
    {
        List<SkillDefinition> definitions = FindDefinitions();
        List<Issue> issues = Validate(definitions);

        foreach (Issue issue in issues)
            Debug.LogError(issue.Message, issue.Definition);

        if (issues.Count == 0)
            Debug.Log($"스킬 UID 검사 완료: {definitions.Count}개");
        else
            Debug.LogError($"스킬 UID 검사 실패: {issues.Count}개 오류");
    }

    /// <summary>
    /// 프로젝트 소유 스킬 에셋과 하위 에셋 조회
    /// </summary>
    /// <returns>경로와 이름으로 정렬한 스킬 목록</returns>
    public static List<SkillDefinition> FindDefinitions()
    {
        List<SkillDefinition> definitions = new List<SkillDefinition>();

        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { SearchRoot });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is SkillDefinition definition)
                    definitions.Add(definition);
            }
        }

        definitions.Sort((left, right) =>
        {
            int pathOrder = StringComparer.Ordinal.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right));

            return pathOrder != 0 ? pathOrder : StringComparer.Ordinal.Compare(left.name, right.name);
        });

        return definitions;
    }

    /// <summary>
    /// 빈 UID와 중복 UID 검사
    /// </summary>
    /// <param name="definitions">검사할 전체 스킬 목록</param>
    /// <returns>문제가 있는 스킬과 오류 설명</returns>
    public static List<Issue> Validate(IReadOnlyList<SkillDefinition> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        List<Issue> issues = new List<Issue>();
        Dictionary<string, string> registered = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (SkillDefinition definition in definitions)
        {
            // 에디터에서 삭제된 목록 항목 제외
            if (definition == null)
                continue;

            string location = $"{AssetDatabase.GetAssetPath(definition)} ({definition.name})";

            try
            {
                definition.ValidateSkillUID();
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(new Issue(definition, $"{location}\n{exception.Message}"));

                continue;
            }

            if (registered.TryGetValue(definition.SkillUID, out string existing))
            {
                issues.Add(new Issue(definition, $"중복 UID: {definition.SkillUID}\n{existing}\n{location}"));

                continue;
            }

            registered.Add(definition.SkillUID, location);
        }

        return issues;
    }
}