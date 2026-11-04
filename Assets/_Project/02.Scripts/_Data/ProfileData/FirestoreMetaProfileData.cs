using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// 게임 프로필과 Firestore 필드 사이의 형식 변환
/// </summary>
public static class FirestoreMetaProfileData
{
    static Dictionary<string, object> WriteMap(JObject map)
    {
        return map.Properties().ToDictionary(
            property => property.Name,
            property => WriteValue(property.Value));
    }

    static object WriteValue(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                return WriteMap((JObject)token);
            case JTokenType.Array:
                return ((JArray)token).Select(WriteValue).ToList();
            case JTokenType.Integer:
                return token.Value<long>();
            case JTokenType.Float:
                return token.Value<double>();
            case JTokenType.Boolean:
                return token.Value<bool>();
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Null:
                // 빈 장착 슬롯의 위치 보존
                return null;
            default:
                throw new FormatException($"지원하지 않는 프로필 값 형식: {token.Type}");
        }
    }

    static T ReadField<T>(IDictionary<string, object> fields, string name)
    {
        if (fields == null || !fields.TryGetValue(name, out object value) || !(value is T result))
            throw new FormatException($"프로필 필드 누락 또는 형식 오류: {name}");

        return result;
    }

    static T[] ReadRows<T>(IDictionary<string, object> fields, string name, Func<IDictionary<string, object>, T> readRow)
    {
        IList rows = ReadField<IList>(fields, name);
        T[] result = new T[rows.Count];

        for (int i = 0; i < rows.Count; i++)
        {
            if (!(rows[i] is IDictionary<string, object> row))
                throw new FormatException($"프로필 항목 형식 오류: {name}[{i}]");

            result[i] = readRow(row);
        }

        return result;
    }

    static MetaProfileData.QuestData ReadQuest(IDictionary<string, object> row)
    {
        object storedValue = ReadField<object>(row, "currentValue");

        // Firestore의 정수와 실수 진행값을 게임의 float로 복원
        if (!(storedValue is long) && !(storedValue is double))
            throw new FormatException("currentValue는 숫자 필요");

        float currentValue = Convert.ToSingle(storedValue);

        if (float.IsNaN(currentValue) || float.IsInfinity(currentValue) || currentValue < 0f)
            throw new FormatException("유효한 퀘스트 진행값 필요");

        return new MetaProfileData.QuestData
        {
            QuestId = ReadField<string>(row, "questId"),
            CurrentValue = currentValue,
            IsCompleted = ReadField<bool>(row, "isCompleted"),
            IsClaimed = ReadField<bool>(row, "isClaimed")
        };
    }

    /// <summary>
    /// 변경된 항목을 Firestore 쓰기용 필드로 변환
    /// </summary>
    /// <param name="changes">저장 조율자가 고정한 변경 데이터</param>
    /// <returns>이번 요청에 포함할 Firestore 필드</returns>
    public static Dictionary<string, object> ToFields(MetaSaveChanges changes)
    {
        if (changes == null)
            throw new ArgumentNullException(nameof(changes));

        Dictionary<string, object> fields = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, JToken> field in changes.CopyFields())
        {
            fields.Add(field.Key, WriteValue(field.Value));
        }

        return fields;
    }

    /// <summary>
    /// 서버 문서를 게임 초기화에 사용할 공통 프로필로 복원
    /// </summary>
    /// <param name="fields">Firestore 문서의 필드</param>
    /// <returns>기존 메타 초기화에 전달할 프로필</returns>
    public static MetaProfileData ReadProfile(IDictionary<string, object> fields)
    {
        if (fields == null)
            throw new ArgumentNullException(nameof(fields));

        long balance = ReadField<long>(fields, "metaGold");

        if (balance < 0)
            throw new FormatException("음수 메타 골드 기록");

        IDictionary<string, object> daily = ReadField<IDictionary<string, object>>(fields, "dailyQuests");

        return new MetaProfileData
        {
            MetaGold = balance,

            Purchases = ReadRows(fields, "purchases",
                row => new MetaProfileData.PurchaseData
                {
                    CharacterId = ReadField<string>(row, "characterId"),
                    UpgradeId = ReadField<string>(row, "upgradeId"),
                    Rank = checked((int)ReadField<long>(row, "rank"))
                }),

            Loadouts = ReadRows(fields, "loadouts",
                row => new MetaProfileData.LoadoutData
                {
                    CharacterId = ReadField<string>(row, "characterId"),
                    Slots = ReadField<IList>(row, "slots").Cast<string>().ToArray()
                }),

            Mastery = ReadRows(fields, "mastery",
                row => new MetaProfileData.MasteryData
                {
                    CharacterId = ReadField<string>(row, "characterId"),
                    TotalExperience = ReadField<long>(row, "totalExperience")
                }),

            DailyQuests = new MetaProfileData.DailyQuestData
            {
                PeriodKey = ReadField<string>(daily, "periodKey"),
                Entries = ReadRows(daily, "entries", ReadQuest)
            }
        };
    }

    /// <summary>
    /// 최초 프로필 저장 재시도 시 기존 문서와 후보 값 비교
    /// </summary>
    /// <param name="stored">서버에서 확인한 필드</param>
    /// <param name="candidate">최초 저장 후보 필드</param>
    /// <returns>두 프로필의 필드와 값 일치 여부</returns>
    public static bool HasSameFields(IDictionary<string, object> stored, IDictionary<string, object> candidate)
    {
        return JToken.DeepEquals(JObject.FromObject(stored), JObject.FromObject(candidate));
    }
}