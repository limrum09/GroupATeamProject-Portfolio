using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 영구 데이터의 변경 및 저장 항목
/// </summary>
[Flags]
public enum MetaSavePart
{
    None = 0,
    MetaGold = 1,
    Purchases = 2,
    Loadouts = 4,
    Mastery = 8,
    DailyQuests = 16,

    All = MetaGold | Purchases | Loadouts | Mastery | DailyQuests
}

/// <summary>
/// 한 저장 작업에서 사용할 항목별 데이터 복사본
/// </summary>
public class MetaSaveChanges
{
    readonly Dictionary<string, JToken> _fields;

    public MetaSavePart Parts { get; }

    MetaSaveChanges(MetaSavePart parts, JObject fields)
    {
        Parts = parts;
        _fields = new Dictionary<string, JToken>(StringComparer.Ordinal);

        foreach (JProperty field in fields.Properties())
        {
            _fields.Add(field.Name, field.Value.DeepClone());
        }
    }

    static JsonSerializer CreateSerializer()
    {
        return new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.None
        };
    }

    /// <summary>
    /// 지정된 영구 데이터만 저장용으로 복사
    /// </summary>
    /// <param name="parts">이번에 저장할 항목</param>
    /// <param name="balance">저장할 최종 잔액</param>
    /// <param name="purchases">캐릭터별 강화 구매 기록</param>
    /// <param name="loadouts">캐릭터별 장착 기록</param>
    /// <param name="mastery">캐릭터별 숙련도 기록</param>
    /// <param name="dailyQuests">현재 주기의 일일 퀘스트 기록</param>
    public MetaSaveChanges(MetaSavePart parts, long? balance = null, IReadOnlyList<MetaUpgradeRecord> purchases = null, IReadOnlyList<MetaLoadoutRecord> loadouts = null, IReadOnlyList<MetaMasteryRecord> mastery = null, MetaProfileData.DailyQuestData dailyQuests = null)
    {
        if ((parts & ~MetaSavePart.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(parts));

        Parts = parts;
        _fields = new Dictionary<string, JToken>(StringComparer.Ordinal);

        JsonSerializer serializer = CreateSerializer();

        if ((parts & MetaSavePart.MetaGold) != 0)
        {
            if (!balance.HasValue || balance.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(balance));

            _fields["metaGold"] = balance.Value;
        }

        if ((parts & MetaSavePart.Purchases) != 0)
        {
            if (purchases == null)
                throw new ArgumentNullException(nameof(purchases));

            _fields["purchases"] = JArray.FromObject(
                purchases.Select(record => new MetaProfileData.PurchaseData
                {
                    CharacterId = record.CharacterId,
                    UpgradeId = record.UpgradeId,
                    Rank = record.Rank
                }).ToArray(),
                serializer);
        }

        if ((parts & MetaSavePart.Loadouts) != 0)
        {
            if (loadouts == null)
                throw new ArgumentNullException(nameof(loadouts));

            _fields["loadouts"] = JArray.FromObject(
                loadouts.Select(record => new MetaProfileData.LoadoutData
                {
                    CharacterId = record.CharacterId,
                    Slots = record.Slots.ToArray()
                }).ToArray(),
                serializer);
        }

        if ((parts & MetaSavePart.Mastery) != 0)
        {
            if (mastery == null)
                throw new ArgumentNullException(nameof(mastery));

            _fields["mastery"] = JArray.FromObject(
                mastery.Select(record => new MetaProfileData.MasteryData
                {
                    CharacterId = record.CharacterId,
                    TotalExperience = record.TotalExperience
                }).ToArray(),
                serializer);
        }

        if ((parts & MetaSavePart.DailyQuests) != 0)
        {
            if (dailyQuests == null)
                throw new ArgumentNullException(nameof(dailyQuests));

            _fields["dailyQuests"] = JObject.FromObject(dailyQuests, serializer);
        }
    }

    /// <summary>
    /// 초기 생성 또는 형식 이전에 사용할 전체 항목 복사
    /// </summary>
    /// <param name="profile">모든 필드를 갖춘 공통 프로필</param>
    /// <returns>전체 항목을 포함한 저장 데이터</returns>
    public static MetaSaveChanges FromProfile(MetaProfileData profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        return new MetaSaveChanges(MetaSavePart.All, JObject.FromObject(profile, CreateSerializer()));
    }

    /// <summary>
    /// 저장 후보와 내부 데이터까지 분리된 필드 복사본 제공
    /// </summary>
    /// <returns>원본 저장 후보와 분리된 필드 묶음</returns>
    public Dictionary<string, JToken> CopyFields()
    {
        Dictionary<string, JToken> copy = new Dictionary<string, JToken>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, JToken> field in _fields)
        {
            copy.Add(field.Key, field.Value.DeepClone());
        }

        return copy;
    }
}