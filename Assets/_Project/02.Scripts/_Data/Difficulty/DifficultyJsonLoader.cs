using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// CSV 변환 JSON 검증과 선택 난이도 추출
/// </summary>
public static class DifficultyJsonLoader
{
    static JArray ReadRows(string json, string source)
    {
        if (json == null)
            throw new ArgumentNullException(nameof(json));

        using (StringReader input = new StringReader(json))
        using (JsonTextReader reader = new JsonTextReader(input))
        {
            reader.DateParseHandling = DateParseHandling.None;
            JObject document = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });

            if (reader.Read() || !(document["Rows"] is JArray rows) || rows.Count == 0)
                throw new FormatException($"{source}: 비어 있지 않은 Rows 배열과 단일 루트 필요");

            return rows;
        }
    }

    static string ReadText(JObject row, string column)
    {
        if (!(row[column] is JValue value) || value.Type != JTokenType.String)
            throw new FormatException($"{column}: 문자열 셀 필요");

        string result = (string)value;
        if (string.IsNullOrWhiteSpace(result) || result != result.Trim())
            throw new FormatException($"{column}: 빈 값 또는 앞뒤 공백 사용 불가");

        return result;
    }

    static string ReadId(JObject row, string column)
    {
        string id = ReadText(row, column);
        foreach (char character in id)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new FormatException($"{column}: 식별자에 공백 또는 제어 문자 사용 불가");
        }

        return id;
    }

    static int ReadInt(JObject row, string column)
    {
        if (!int.TryParse(ReadText(row, column), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int result) || result < 0)
        {
            throw new FormatException($"{column}: 0 이상의 정수 필요");
        }

        return result;
    }

    static float ReadFloat(JObject row, string column)
    {
        if (!float.TryParse(ReadText(row, column), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float result) ||
            float.IsNaN(result) || float.IsInfinity(result) || result < 0f)
        {
            throw new FormatException($"{column}: 유한한 0 이상의 실수 필요");
        }

        return result;
    }

    static Difficulty ReadDifficulty(JObject row)
    {
        string text = ReadText(row, "Difficulty");
        if (!Enum.TryParse(text, out Difficulty value) ||
            !Enum.IsDefined(typeof(Difficulty), value) ||
            value == Difficulty.Count || text != value.ToString())
        {
            throw new FormatException("Difficulty: Easy, Normal, Hard, Nightmare 중 하나 필요");
        }

        return value;
    }

    static EnemyDifficultyDefinition ReadEnemy(JObject row, string id)
    {
        CharacterStatValues stats = new CharacterStatValues(
            ReadFloat(row, "MaxHealth"),
            ReadFloat(row, "AttackPower"),
            ReadFloat(row, "Defense"),
            ReadFloat(row, "SpellPower"),
            ReadFloat(row, "MoveSpeed"),
            ReadFloat(row, "AttackSpeed"),
            ReadFloat(row, "CriticalChance"),
            ReadFloat(row, "CriticalDamageMultiplier"));

        return new EnemyDifficultyDefinition(id, stats,
            ReadFloat(row, "Exp"), ReadFloat(row, "Score"), ReadFloat(row, "SessionGold"));
    }

    static IReadOnlyDictionary<TKey, TValue> LoadSelected<TKey, TValue>(
        string json, string source, Difficulty selected,
        Func<JObject, TKey> readKey, Func<JObject, TKey, TValue> readValue)
    {
        if (!Enum.IsDefined(typeof(Difficulty), selected) || selected == Difficulty.Count)
            throw new ArgumentOutOfRangeException(nameof(selected));

        JArray rows;
        try
        {
            rows = ReadRows(json, source);
        }
        catch (JsonException error)
        {
            throw new FormatException($"{source}: JSON 형식 오류", error);
        }

        Dictionary<TKey, HashSet<Difficulty>> coverage = new Dictionary<TKey, HashSet<Difficulty>>();
        Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>();
        for (int index = 0; index < rows.Count; index++)
        {
            try
            {
                if (!(rows[index] is JObject row))
                    throw new FormatException("객체 형식의 행 필요");

                TKey key = readKey(row);
                Difficulty difficulty = ReadDifficulty(row);
                if (!coverage.TryGetValue(key, out HashSet<Difficulty> difficulties))
                {
                    difficulties = new HashSet<Difficulty>();
                    coverage.Add(key, difficulties);
                }

                if (!difficulties.Add(difficulty))
                    throw new FormatException($"중복 설정 {key}, {difficulty}");

                TValue value = readValue(row, key);
                if (difficulty == selected)
                    result.Add(key, value);
            }
            catch (Exception error) when (
                error is FormatException || error is ArgumentException || error is OverflowException)
            {
                throw new FormatException($"{source}: Rows[{index}] {error.Message}", error);
            }
        }

        foreach (KeyValuePair<TKey, HashSet<Difficulty>> entry in coverage)
        {
            if (entry.Value.Count != (int)Difficulty.Count)
                throw new FormatException($"{source}: {entry.Key}의 난이도 행 누락");
        }

        return new ReadOnlyDictionary<TKey, TValue>(result);
    }

    /// <summary>
    /// 선택 난이도의 적 설정 로드
    /// </summary>
    /// <param name="json">적 설정 JSON</param>
    /// <param name="source">원본 이름</param>
    /// <param name="selected">선택 난이도</param>
    /// <returns>적 ID별 최종 설정</returns>
    public static IReadOnlyDictionary<string, EnemyDifficultyDefinition> LoadEnemies(
        string json, string source, Difficulty selected)
    {
        return LoadSelected<string, EnemyDifficultyDefinition>(
            json, source, selected, row => ReadId(row, "EnemyId"), ReadEnemy);
    }

    /// <summary>
    /// 선택 난이도의 웨이브 설정 로드
    /// </summary>
    /// <param name="json">웨이브 설정 JSON</param>
    /// <param name="source">원본 이름</param>
    /// <param name="selected">선택 난이도</param>
    /// <returns>구역 ID와 순번별 최종 수량</returns>
    public static IReadOnlyDictionary<(string EncounterId, int WaveIndex), WaveDifficultyDefinition>
        LoadWaves(string json, string source, Difficulty selected)
    {
        return LoadSelected<(string EncounterId, int WaveIndex), WaveDifficultyDefinition>(
            json, source, selected,
            row => (ReadId(row, "EncounterId"), ReadInt(row, "WaveIndex")),
            (row, key) => new WaveDifficultyDefinition(
                ReadInt(row, "NormalCount"), ReadInt(row, "EpicCount"),
                ReadInt(row, "MidBossCount"), ReadInt(row, "BossCount")));
    }
}
