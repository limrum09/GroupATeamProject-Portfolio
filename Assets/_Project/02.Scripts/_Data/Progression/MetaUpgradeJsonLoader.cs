using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 세 JSON 표를 연결하여 메타 강화 정의 구성
/// </summary>
public static class MetaUpgradeJsonLoader
{
    static JArray ReadRows(TextAsset asset)
    {
        try
        {
            using (StringReader textReader = new StringReader(asset.text))
            using (JsonTextReader reader = new JsonTextReader(textReader))
            {
                reader.DateParseHandling = DateParseHandling.None;

                JsonLoadSettings settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                };

                JObject document = JObject.Load(reader, settings);

                // 오류에 표시할 원본 JSON 이름
                document.AddAnnotation(asset.name);

                if (reader.Read())
                    throw Error(document, "JSON 루트 뒤에 추가 내용 존재");

                if (!(document["Rows"] is JArray rows))
                    throw Error(document, "Rows 배열 필요");

                if (rows.Count == 0)
                    throw Error(document, "Rows 배열에 데이터 필요");

                return rows;
            }
        }
        catch (JsonException exception)
        {
            throw new FormatException($"{asset.name}: {exception.Message}", exception);
        }
    }

    static JObject ReadObject(JToken token)
    {
        if (!(token is JObject row))
            throw Error(token, "행은 JSON 객체 형태 필요");

        return row;
    }

    static string ReadText(JObject row, string field, bool allowEmpty = false)
    {
        if (!row.TryGetValue(field, StringComparison.Ordinal, out JToken token) || token.Type != JTokenType.String)
            throw Error(row, $"{field}: 문자열 필드 필요");

        string value = (string)token;

        if (value == null || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw Error(row, $"{field}: 값 누락");

        return value;
    }

    static string ReadId(JObject row, string field)
    {
        string value = ReadText(row, field);

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw Error(row, $"{field}: ID에 공백 또는 제어 문자 사용 불가");
        }

        return value;
    }

    static long ReadLong(JObject row, string field)
    {
        if (!long.TryParse(ReadText(row, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            throw Error(row, $"{field}: 64비트 정수 필요");

        return value;
    }

    static int ReadInt(JObject row, string field, int minimum)
    {
        long value = ReadLong(row, field);

        if (value < minimum || value > int.MaxValue)
            throw Error(row, $"{field}: {minimum} 이상 32비트 정수 필요");

        return (int)value;
    }

    static float ReadFloat(JObject row, string field)
    {
        if (!double.TryParse(ReadText(row, field), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))
            throw Error(row, $"{field}: 유한한 숫자 필요");

        float value = (float)number;

        if (float.IsNaN(value) || float.IsInfinity(value) || (number != 0d && value == 0f))
            throw Error(row, $"{field}: float로 표현 가능한 값 필요");

        return value;
    }

    static bool ReadBoolean(JObject row, string field)
    {
        if (!bool.TryParse(ReadText(row, field), out bool value))
            throw Error(row, $"{field}: TRUE 또는 FALSE 필요");

        return value;
    }

    static FormatException Error(JToken token, string message)
    {
        string source = token.Root.Annotation<string>() ?? "JSON";

        return new FormatException($"{source}: {token.Path}: {message}");
    }

    /// <summary>
    /// 강화 목록과 구매 단계 및 효과 JSON 읽기
    /// </summary>
    /// <param name="upgradesJson">강화 목록 JSON</param>
    /// <param name="ranksJson">구매 단계 JSON</param>
    /// <param name="effectsJson">효과 JSON</param>
    /// <returns>강화 ID로 조회하는 읽기 전용 데이터</returns>
    public static IReadOnlyDictionary<string, MetaUpgradeData> Load(TextAsset upgradesJson, TextAsset ranksJson, TextAsset effectsJson)
    {
        if (upgradesJson == null)
            throw new ArgumentNullException(nameof(upgradesJson));

        if (ranksJson == null)
            throw new ArgumentNullException(nameof(ranksJson));

        if (effectsJson == null)
            throw new ArgumentNullException(nameof(effectsJson));

        JArray upgradeRows = ReadRows(upgradesJson);
        JArray rankRows = ReadRows(ranksJson);
        JArray effectRows = ReadRows(effectsJson);

        Dictionary<string, SortedDictionary<int, JObject>> ranksByUpgrade = new Dictionary<string, SortedDictionary<int, JObject>>(StringComparer.Ordinal);

        Dictionary<(string UpgradeId, int Rank), List<MetaUpgradeEffectData>> effectsByRank = new Dictionary<(string UpgradeId, int Rank), List<MetaUpgradeEffectData>>();

        // 강화 ID 등록
        foreach (JToken token in upgradeRows)
        {
            JObject row = ReadObject(token);
            string upgradeId = ReadId(row, "UpgradeId");

            if (ranksByUpgrade.ContainsKey(upgradeId))
                throw Error(row, $"중복 강화 ID: {upgradeId}");

            ranksByUpgrade.Add(upgradeId, new SortedDictionary<int, JObject>());
        }

        // 강화 ID를 기준으로 구매 단계 연결
        foreach (JToken token in rankRows)
        {
            JObject row = ReadObject(token);
            string upgradeId = ReadId(row, "UpgradeId");
            int rank = ReadInt(row, "Rank", 1);

            if (!ranksByUpgrade.TryGetValue(upgradeId, out SortedDictionary<int, JObject> ranks))
                throw Error(row, $"존재하지 않는 강화 ID: {upgradeId}");

            if (ranks.ContainsKey(rank))
                throw Error(row, $"중복 구매 단계: {upgradeId}/{rank}");

            ranks.Add(rank, row);

            effectsByRank.Add((upgradeId, rank), new List<MetaUpgradeEffectData>());
        }

        // 강화 ID와 구매 단계를 기준으로 효과 연결
        foreach (JToken token in effectRows)
        {
            JObject row = ReadObject(token);
            string upgradeId = ReadId(row, "UpgradeId");
            int rank = ReadInt(row, "Rank", 1);

            if (!effectsByRank.TryGetValue((upgradeId, rank), out List<MetaUpgradeEffectData> effects))
                throw Error(row, $"존재하지 않는 구매 단계 참조: {upgradeId}/{rank}");

            string statId = ReadId(row, "StatId");
            string applyTiming = ReadId(row, "ApplyTiming");
            string modifierType = ReadId(row, "ModifierType");
            float value = ReadFloat(row, "Value");

            bool duplicated = effects.Exists(effect => effect.StatId == statId && effect.ApplyTiming == applyTiming && effect.ModifierType == modifierType);

            if (duplicated)
                throw Error(row, $"중복 효과: {upgradeId}/{rank}/" + $"{statId}/{applyTiming}/{modifierType}");

            effects.Add(new MetaUpgradeEffectData(statId, applyTiming, modifierType, value));
        }

        Dictionary<string, MetaUpgradeData> result = new Dictionary<string, MetaUpgradeData>(StringComparer.Ordinal);

        // 연결된 표에서 최종 읽기 전용 데이터 구성
        foreach (JToken token in upgradeRows)
        {
            JObject row = ReadObject(token);
            string upgradeId = ReadId(row, "UpgradeId");
            int maxRank = ReadInt(row, "MaxRank", 1);

            SortedDictionary<int, JObject> sourceRanks = ranksByUpgrade[upgradeId];

            if (sourceRanks.Count != maxRank)
                throw Error(row, $"{upgradeId}: MaxRank와 구매 단계 수 불일치 " + $"(MaxRank={maxRank}, 단계 수={sourceRanks.Count})");

            List<MetaUpgradeRankData> ranks = new List<MetaUpgradeRankData>();

            foreach (KeyValuePair<int, JObject> entry in sourceRanks)
            {
                int expectedRank = ranks.Count + 1;

                if (entry.Key != expectedRank)
                    throw Error(entry.Value, $"{upgradeId}: {expectedRank}단계 누락");

                List<MetaUpgradeEffectData> effects = effectsByRank[(upgradeId, entry.Key)];

                if (effects.Count == 0)
                    throw Error(entry.Value, $"{upgradeId}/{entry.Key}: 효과 없음");

                long cost = ReadLong(entry.Value, "Cost");

                if (cost < 0)
                    throw Error(entry.Value, "Cost는 0 이상 필요");

                ranks.Add(new MetaUpgradeRankData(entry.Key, cost, effects));
            }

            MetaUpgradeData upgrade = new MetaUpgradeData(upgradeId, ReadText(row, "DisplayName"), ReadText(row, "Description"), ReadText(row, "IconId", true), ReadId(row, "Category"), ReadId(row, "TargetCharacterId"), ReadInt(row, "SortOrder", 0), ReadBoolean(row, "Enabled"), ranks);

            result.Add(upgradeId, upgrade);
        }

        return new ReadOnlyDictionary<string, MetaUpgradeData>(result);
    }
}