using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// 로컬 메타 프로필의 JSON 저장 형식
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class MetaProfileData
{
    [JsonProperty("metaGold", Required = Required.Always)]
    public long MetaGold { get; set; }

    [JsonProperty("purchases", Required = Required.Always)]
    public PurchaseData[] Purchases { get; set; }

    [JsonProperty("loadouts", Required = Required.Always)]
    public LoadoutData[] Loadouts { get; set; }

    [JsonProperty("mastery", Required = Required.Always)]
    public MasteryData[] Mastery { get; set; }

    [JsonProperty("dailyQuests", Required = Required.Always)]
    public DailyQuestData DailyQuests { get; set; }

    /// <summary>
    /// 저장 파일의 캐릭터별 강화 구매 항목
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class PurchaseData
    {
        [JsonProperty("characterId", Required = Required.Always)]
        public string CharacterId { get; set; }

        [JsonProperty("upgradeId", Required = Required.Always)]
        public string UpgradeId { get; set; }

        [JsonProperty("rank", Required = Required.Always)]
        public int Rank { get; set; }
    }

    /// <summary>
    /// 저장 파일의 캐릭터별 강화 장착 슬롯
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class LoadoutData
    {
        [JsonProperty("characterId", Required = Required.Always)]
        public string CharacterId { get; set; }

        [JsonProperty("slots", Required = Required.Always)]
        public string[] Slots { get; set; }
    }

    /// <summary>
    /// 저장 파일의 캐릭터별 누적 숙련도 경험치
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class MasteryData
    {
        [JsonProperty("characterId", Required = Required.Always)]
        public string CharacterId { get; set; }

        [JsonProperty("totalExperience", Required = Required.Always)]
        public long TotalExperience { get; set; }
    }

    /// <summary>
    /// 현재 일일 주기와 선택된 퀘스트 기록
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DailyQuestData
    {
        [JsonProperty("periodKey", Required = Required.Always)]
        public string PeriodKey { get; set; }

        [JsonProperty("entries", Required = Required.Always)]
        public QuestData[] Entries { get; set; }

        /// <summary>
        /// 실행 기록에서 지정한 일일 주기의 저장 필드만 추출
        /// </summary>
        /// <param name="periodKey">저장할 일일 주기</param>
        /// <param name="records">기능에서 제공한 퀘스트 기록</param>
        /// <returns>현재 주기의 저장 데이터</returns>
        public static DailyQuestData FromRecords(string periodKey, IReadOnlyList<QuestRecord> records)
        {
            if (string.IsNullOrWhiteSpace(periodKey))
                throw new ArgumentException("일일 주기 필요", nameof(periodKey));

            if (records == null)
                throw new ArgumentNullException(nameof(records));

            List<QuestData> entries = new List<QuestData>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (QuestRecord record in records)
            {
                if (record == null)
                    throw new ArgumentException("빈 퀘스트 기록", nameof(records));

                if (record.Kind != QuestKind.Daily || record.PeriodKey != periodKey)
                    continue;

                if (!ids.Add(record.QuestId))
                    throw new ArgumentException("중복 일일 퀘스트", nameof(records));

                entries.Add(new QuestData
                {
                    QuestId = record.QuestId,
                    CurrentValue = record.CurrentValue,
                    IsCompleted = record.IsCompleted,
                    IsClaimed = record.IsClaimed
                });
            }

            if (entries.Count > 4)
                throw new ArgumentException("일일 퀘스트는 최대 네 개", nameof(records));

            return new DailyQuestData
            {
                PeriodKey = periodKey,
                Entries = entries.ToArray()
            };
        }
    }

    /// <summary>
    /// 일일 퀘스트 하나의 사용자 진행 상태
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class QuestData
    {
        [JsonProperty("questId", Required = Required.Always)]
        public string QuestId { get; set; }

        [JsonProperty("currentValue", Required = Required.Always)]
        public float CurrentValue { get; set; }

        [JsonProperty("isCompleted", Required = Required.Always)]
        public bool IsCompleted { get; set; }

        [JsonProperty("isClaimed", Required = Required.Always)]
        public bool IsClaimed { get; set; }
    }
}