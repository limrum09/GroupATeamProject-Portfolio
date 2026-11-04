using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 퀘스트 정의 등록과 UID 조회
/// </summary>
[CreateAssetMenu(menuName = "Project/Quests/Quest Definition Catalog", fileName = "QuestDefinitionCatalog")]
public class QuestDefinitionCatalog : ScriptableObject
{
    [Header("----- 전체 퀘스트 정의 -----")]
    [SerializeField] QuestDefinition[] _definitions = Array.Empty<QuestDefinition>();

    public IReadOnlyList<QuestDefinition> Definitions => _definitions;

    /// <summary>
    /// 초기 구성의 필수 설정과 UID 중복 검사
    /// </summary>
    public void Validate()
    {
        if (_definitions == null)
            throw new InvalidOperationException("퀘스트 정의 목록 필요");

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (QuestDefinition definition in _definitions)
        {
            if (definition == null)
                throw new MissingReferenceException("퀘스트 정의의 빈 항목 제거 필요");

            definition.Validate();

            if (!ids.Add(definition.QuestId))
                throw new InvalidOperationException($"중복 Quest ID: {definition.QuestId}");
        }
    }

    /// <summary>
    /// UID에 대응하는 퀘스트 정의 조회
    /// </summary>
    /// <param name="questId">조회할 퀘스트 UID</param>
    /// <returns>등록된 퀘스트 정의</returns>
    public QuestDefinition GetDefinition(string questId)
    {
        foreach (QuestDefinition definition in _definitions)
        {
            if (definition.QuestId == questId)
                return definition;
        }

        throw new KeyNotFoundException($"퀘스트 정의 확인 필요: {questId}");
    }
}