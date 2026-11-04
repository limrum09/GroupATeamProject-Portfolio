using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// 한 런의 결산 통계 누적
/// </summary>
public class RunStatistics
{
    readonly Dictionary<EnemyRankType, int> _killsByRank = new Dictionary<EnemyRankType, int>();
    readonly Dictionary<EnemyRankType, decimal> _scoreByRank = new Dictionary<EnemyRankType, decimal>();
    readonly HashSet<(EnemyWorldType World, int NodeId)> _visitedNodes = new HashSet<(EnemyWorldType, int)>();
    readonly HashSet<EnemyWorldType> _visitedWorlds = new HashSet<EnemyWorldType>();

    public double DamageDealt { get; private set; }
    public double DamageTaken { get; private set; }
    public float MaxSingleDamage { get; private set; }
    public double SessionGoldEarned { get; private set; }
    public int VisitedNodeCount => _visitedNodes.Count;
    public int VisitedWorldCount => _visitedWorlds.Count;

    internal void RecordKill(EnemyRankType rank, decimal score)
    {
        _killsByRank.TryGetValue(rank, out int count);
        _scoreByRank.TryGetValue(rank, out decimal total);
        _killsByRank[rank] = count + 1;
        _scoreByRank[rank] = total + score;
    }

    internal void RecordDamageDealt(float appliedDamage)
    {
        DamageDealt += appliedDamage;
        MaxSingleDamage = Math.Max(MaxSingleDamage, appliedDamage);
    }

    internal void RecordDamageTaken(float appliedDamage)
    {
        DamageTaken += appliedDamage;
    }

    internal void RecordGoldAcquired(GoldGainSource source, float amount)
    {
        if (source == GoldGainSource.EnemyReward || source == GoldGainSource.SkillSale)
            SessionGoldEarned += amount;
    }

    internal void RecordVisit(EnemyWorldType world, int nodeId)
    {
        _visitedNodes.Add((world, nodeId));
        _visitedWorlds.Add(world);
    }

    internal IReadOnlyDictionary<EnemyRankType, int> CaptureKillsByRank()
    {
        return new ReadOnlyDictionary<EnemyRankType, int>(new Dictionary<EnemyRankType, int>(_killsByRank));
    }

    internal IReadOnlyDictionary<EnemyRankType, decimal> CaptureScoreByRank()
    {
        return new ReadOnlyDictionary<EnemyRankType, decimal>(new Dictionary<EnemyRankType, decimal>(_scoreByRank));
    }
}
