using System;

/// <summary>
/// 정산 또는 보상 수령으로 확정할 퀘스트 기록
/// </summary>
public class QuestRecord
{
    public string QuestId { get; }
    public string PeriodKey { get; }
    public QuestKind Kind { get; }
    public string DisplayName { get; }
    public float CurrentValue { get; }
    public bool IsCompleted { get; }
    public bool IsClaimed { get; }
    public long MetaGoldReward { get; }

    /// <summary>
    /// 저장할 진행과 완료 및 보상 수령 기록 구성
    /// </summary>
    /// <param name="questId">퀘스트 식별자</param>
    /// <param name="periodKey">일일 날짜 또는 런 식별 정보</param>
    /// <param name="kind">퀘스트 운영 종류</param>
    /// <param name="displayName">기록에 보관할 표시 이름</param>
    /// <param name="currentValue">기록할 진행값</param>
    /// <param name="isCompleted">목표 완료 여부</param>
    /// <param name="isClaimed">보상 수령 여부</param>
    /// <param name="metaGoldReward">기록에 보관할 보상량</param>
    public QuestRecord(string questId, string periodKey, QuestKind kind, string displayName, float currentValue, bool isCompleted, bool isClaimed, long metaGoldReward)
    {
        if (string.IsNullOrWhiteSpace(questId))
            throw new ArgumentException("Quest ID 필요", nameof(questId));

        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("진행 주기 식별 정보 필요", nameof(periodKey));

        if (!Enum.IsDefined(typeof(QuestKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("표시 이름 필요", nameof(displayName));

        if (float.IsNaN(currentValue) || float.IsInfinity(currentValue) || currentValue < 0f)
            throw new ArgumentOutOfRangeException(nameof(currentValue));

        if (isClaimed && !isCompleted)
            throw new ArgumentException("완료한 퀘스트만 수령 기록 가능");

        if (metaGoldReward < 0)
            throw new ArgumentOutOfRangeException(nameof(metaGoldReward));

        QuestId = questId;
        PeriodKey = periodKey;
        Kind = kind;
        DisplayName = displayName;
        CurrentValue = currentValue;
        IsCompleted = isCompleted;
        IsClaimed = isClaimed;
        MetaGoldReward = metaGoldReward;
    }

    /// <summary>
    /// 기존 기록을 변경하지 않는 보상 수령 후보 구성
    /// </summary>
    /// <returns>수령 표시를 반영한 새 기록</returns>
    public QuestRecord CreateClaimedRecord()
    {
        if (!IsCompleted || IsClaimed)
            throw new InvalidOperationException("완료된 미수령 기록 필요");

        return new QuestRecord(QuestId, PeriodKey, Kind, DisplayName, CurrentValue, true, true, MetaGoldReward);
    }
}