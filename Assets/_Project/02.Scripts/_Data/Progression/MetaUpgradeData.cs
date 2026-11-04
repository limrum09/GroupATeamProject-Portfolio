using System;
using System.Collections.Generic;

/// <summary>
/// 메타 강화 한 단계에 포함되는 효과 데이터
/// </summary>
public sealed class MetaUpgradeEffectData
{
    public string StatId { get; }
    public string ApplyTiming { get; }
    public string ModifierType { get; }
    public float Value { get; }

    internal MetaUpgradeEffectData(
        string statId,
        string applyTiming,
        string modifierType,
        float value)
    {
        StatId = statId;
        ApplyTiming = applyTiming;
        ModifierType = modifierType;
        Value = value;
    }
}

/// <summary>
/// 메타 강화 한 단계의 비용과 전체 효과
/// </summary>
public sealed class MetaUpgradeRankData
{
    public int Rank { get; }
    public long Cost { get; }
    public IReadOnlyList<MetaUpgradeEffectData> Effects { get; }

    public MetaUpgradeRankData(int rank, long cost, List<MetaUpgradeEffectData> effects)
    {
        Rank = rank;
        Cost = cost;

        Effects = new List<MetaUpgradeEffectData>(effects).AsReadOnly();
    }
}

/// <summary>
/// 표시 정보와 구매 단계들을 포함하는 메타 강화 정의
/// </summary>
public sealed class MetaUpgradeData
{
    public string UpgradeId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string IconId { get; }
    public string Category { get; }
    public string TargetCharacterId { get; }
    public int SortOrder { get; }
    public bool Enabled { get; }

    public IReadOnlyList<MetaUpgradeRankData> Ranks { get; }

    // 검증된 연속 구매 단계 수로 결정되는 최대 단계
    public int MaxRank => Ranks.Count;

    internal MetaUpgradeData(string upgradeId, string displayName, string description, string iconId, string category, string targetCharacterId, int sortOrder, bool enabled, List<MetaUpgradeRankData> ranks)
    {
        UpgradeId = upgradeId;
        DisplayName = displayName;
        Description = description;
        IconId = iconId;
        Category = category;
        TargetCharacterId = targetCharacterId;
        SortOrder = sortOrder;
        Enabled = enabled;

        Ranks = new List<MetaUpgradeRankData>(ranks).AsReadOnly();
    }

    /// <summary>
    /// 지정 구매 단계의 비용과 전체 효과 조회
    /// </summary>
    /// <param name="rank">1부터 최대 단계 사이의 구매 단계</param>
    /// <returns>해당 단계의 데이터</returns>
    public MetaUpgradeRankData GetRank(int rank)
    {
        if (rank < 1 || rank > MaxRank)
            throw new ArgumentOutOfRangeException(nameof(rank));

        return Ranks[rank - 1];
    }
}