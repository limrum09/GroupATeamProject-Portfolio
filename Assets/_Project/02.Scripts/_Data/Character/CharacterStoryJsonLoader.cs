using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

/// <summary>
/// CSV 변환 JSON의 문자열 행 검증과 스토리 콘텐츠 구성
/// </summary>
public static class CharacterStoryJsonLoader
{
    static JArray ReadRows(string json, string source)
    {
        JObject document = JObject.Parse(json);
        if (!(document["Rows"] is JArray rows))
            throw new FormatException($"{source}: Rows 배열 필요");

        return rows;
    }

    static string ReadField(JToken row, string field, string source, int index, bool allowEmpty = false)
    {
        if (!(row is JObject values) || values[field]?.Type != JTokenType.String)
            throw new FormatException($"{source}: Rows[{index}].{field} 문자열 필요");

        string value = (string)values[field];
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new FormatException($"{source}: Rows[{index}].{field} 빈 값 사용 불가");

        return value;
    }

    static T ReadEnum<T>(JToken row, string field, string source, int index) where T : struct, Enum
    {
        string value = ReadField(row, field, source, index);
        if (!Enum.TryParse(value, out T result) ||
            !Enum.IsDefined(typeof(T), result) || value != result.ToString())
        {
            throw new FormatException($"{source}: Rows[{index}].{field} 알 수 없는 값: {value}");
        }

        return result;
    }

    static CharacterStoryUnlockCondition ReadCondition(JToken row, string source, int index)
    {
        string id = ReadField(row, "ConditionId", source, index);
        StoryUnlockConditionType type = ReadEnum<StoryUnlockConditionType>(row, "Type", source, index);
        string targetId = ReadField(row, "TargetId", source, index, true);
        string amountText = ReadField(row, "RequiredAmount", source, index, true);
        string description = ReadField(row, "Description", source, index);
        long? requiredAmount = null;

        if (type == StoryUnlockConditionType.MasteryLevel || type == StoryUnlockConditionType.CurrencyAmount)
        {
            if (!long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out long amount) || amount <= 0)
                throw new FormatException($"{source}: Rows[{index}].RequiredAmount 양의 long 정수 필요");

            requiredAmount = amount;
        }
        else if (amountText.Length != 0)
        {
            throw new FormatException($"{source}: Rows[{index}] {type}의 RequiredAmount는 빈 값 필요");
        }

        if (type == StoryUnlockConditionType.MasteryLevel)
        {
            if (targetId.Length != 0)
                throw new FormatException($"{source}: Rows[{index}] 현재 캐릭터 숙련도 조건의 TargetId는 빈 값 필요");
        }
        else if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new FormatException($"{source}: Rows[{index}] {type}의 TargetId 필요");
        }

        return new CharacterStoryUnlockCondition(id, type, targetId, requiredAmount, description);
    }

    static void ValidateConditionCount(StoryUnlockMode mode, int count, string source, string ownerId)
    {
        if ((mode == StoryUnlockMode.None && count != 0) || (mode != StoryUnlockMode.None && count == 0))
            throw new FormatException($"{source}: {ownerId}의 {mode}와 해금 조건 수 {count} 불일치");
    }

    /// <summary>
    /// 콘텐츠와 해금 조건 JSON을 검증하고 행 순서대로 콘텐츠 생성
    /// </summary>
    /// <param name="definition">콘텐츠와 해금 조건 JSON을 참조하는 스토리 정의</param>
    /// <returns>검증을 마친 읽기 전용 콘텐츠</returns>
    public static CharacterStoryContent Load(CharacterStoryDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        definition.Validate();
        string storySource = definition.StoriesJson.name;
        string conditionSource = definition.UnlockConditionsJson.name;
        JArray storyRows = ReadRows(definition.StoriesJson.text, storySource);
        JArray conditionRows = ReadRows(definition.UnlockConditionsJson.text, conditionSource);
        Dictionary<string, List<CharacterStoryUnlockCondition>> storyConditions =
            new Dictionary<string, List<CharacterStoryUnlockCondition>>(StringComparer.Ordinal);
        List<(string StoryId, string Title, string Body, StoryUnlockMode Mode)> storyData =
            new List<(string StoryId, string Title, string Body, StoryUnlockMode Mode)>();

        for (int index = 0; index < storyRows.Count; index++)
        {
            string id = ReadField(storyRows[index], "StoryId", storySource, index);
            string title = ReadField(storyRows[index], "Title", storySource, index);
            string body = ReadField(storyRows[index], "Body", storySource, index);
            StoryUnlockMode mode = ReadEnum<StoryUnlockMode>(storyRows[index], "UnlockMode", storySource, index);
            if (storyConditions.ContainsKey(id))
                throw new FormatException($"{storySource}: Rows[{index}] 중복 StoryId: {id}");

            storyConditions.Add(id, new List<CharacterStoryUnlockCondition>());
            storyData.Add((id, title, body, mode));
        }

        HashSet<string> conditionIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < conditionRows.Count; index++)
        {
            JToken row = conditionRows[index];
            CharacterStoryUnlockCondition condition = ReadCondition(row, conditionSource, index);
            if (!conditionIds.Add(condition.ConditionId))
                throw new FormatException($"{conditionSource}: Rows[{index}] 중복 ConditionId: {condition.ConditionId}");

            string ownerType = ReadField(row, "OwnerType", conditionSource, index);
            string ownerId = ReadField(row, "OwnerId", conditionSource, index);
            if (ownerType != "Story")
                throw new FormatException($"{conditionSource}: Rows[{index}] OwnerType은 Story 필요");
            if (!storyConditions.TryGetValue(ownerId, out List<CharacterStoryUnlockCondition> conditions))
                throw new FormatException($"{conditionSource}: Rows[{index}] 알 수 없는 Story 소유 대상: {ownerId}");

            conditions.Add(condition);
        }

        List<CharacterStoryContent.Story> stories = new List<CharacterStoryContent.Story>(storyData.Count);
        foreach (var data in storyData)
        {
            List<CharacterStoryUnlockCondition> conditions = storyConditions[data.StoryId];
            ValidateConditionCount(data.Mode, conditions.Count, storySource, data.StoryId);
            stories.Add(new CharacterStoryContent.Story(data.StoryId, data.Title, data.Body, data.Mode, conditions));
        }

        return new CharacterStoryContent(stories);
    }
}
