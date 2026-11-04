using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 퀘스트의 단일 목표 종류
/// </summary>
public enum QuestGoalType
{
    EnemyKill,
    GoldAcquired,
    GoldHeld,
    NodeCleared,
    NodeRankCleared
}

/// <summary>
/// 퀘스트의 활성화와 진행 유지 범위
/// </summary>
public enum QuestKind
{
    Daily,
    Sudden
}

/// <summary>
/// 퀘스트의 표시 정보와 단일 목표 및 보상 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Quests/Quest Definition", fileName = "_QuestDefinition")]
public class QuestDefinition : ScriptableObject
{
    [Header("----- 기본 정보 -----")]
    [SerializeField] string _questId;
    [SerializeField] string _displayName;
    [SerializeField, TextArea(2, 5)] string _description;

    [Header("----- 운영 종류 -----")]
    [SerializeField] QuestKind _kind;

    [Header("----- 목표 설정 -----")]
    [SerializeField] QuestGoalType _goalType;
    [SerializeField] float _targetValue = 1f;

    [Header("----- 적 처치 조건 -----")]
    [SerializeField] bool _anyEnemyRank = true;
    [SerializeField] EnemyRankType _enemyRank;

    [Header("----- 골드 획득 조건 -----")]
    [SerializeField]
    GoldGainSource[] _goldSources =
    {
        GoldGainSource.EnemyReward,
        GoldGainSource.SkillSale
    };

    [Header("----- 노드 조건 -----")]
    [SerializeField]
    MapBindings.MapType[] _nodeTypes =
    {
        MapBindings.MapType.Combat,
        MapBindings.MapType.Boss
    };

    [Header("----- 메타 보상 설정 -----")]
    [SerializeField] long _metaGoldReward;

    public string QuestId => _questId;
    public string DisplayName => _displayName;
    public string Description => _description;
    public QuestKind Kind => _kind;
    public QuestGoalType GoalType => _goalType;
    public float TargetValue => _targetValue;
    public bool AnyEnemyRank => _anyEnemyRank;
    public EnemyRankType EnemyRank => _enemyRank;
    public IReadOnlyList<GoldGainSource> GoldSources => _goldSources;
    public IReadOnlyList<MapBindings.MapType> NodeTypes => _nodeTypes;
    public long MetaGoldReward => _metaGoldReward;

    /// <summary>
    /// 목표 종류에 따른 필수 설정 검사
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(_questId))
            throw new InvalidOperationException($"{name}: Quest ID 설정 필요");

        if (string.IsNullOrWhiteSpace(_displayName))
            throw new InvalidOperationException($"{name}: 퀘스트 이름 설정 필요");

        if (!Enum.IsDefined(typeof(QuestKind), _kind))
            throw new InvalidOperationException($"{name}: 유효한 운영 종류 필요");

        if (!Enum.IsDefined(typeof(QuestGoalType), _goalType))
            throw new InvalidOperationException($"{name}: 유효한 목표 종류 필요");

        if (float.IsNaN(_targetValue) || float.IsInfinity(_targetValue) || _targetValue <= 0f)
            throw new InvalidOperationException($"{name}: 목표 수치는 유한한 양수 필요");

        bool integerGoal = _goalType == QuestGoalType.EnemyKill || _goalType == QuestGoalType.NodeCleared || _goalType == QuestGoalType.NodeRankCleared;

        if (integerGoal && Mathf.Floor(_targetValue) != _targetValue)
            throw new InvalidOperationException($"{name}: 횟수와 Rank 목표는 정수 필요");

        if (_metaGoldReward < 0)
            throw new InvalidOperationException($"{name}: 메타 보상은 0 이상 필요");

        if (_goalType == QuestGoalType.EnemyKill && !_anyEnemyRank && !Enum.IsDefined(typeof(EnemyRankType), _enemyRank))
            throw new InvalidOperationException($"{name}: 유효한 적 등급 필요");

        if (_goalType == QuestGoalType.GoldAcquired)
        {
            if (_goldSources == null || _goldSources.Length == 0)
                throw new InvalidOperationException($"{name}: 골드 획득 사유 설정 필요");

            foreach (GoldGainSource source in _goldSources)
            {
                if (source != GoldGainSource.EnemyReward && source != GoldGainSource.SkillSale)
                    throw new InvalidOperationException($"{name}: 실제 플레이의 골드 획득 사유만 설정 가능");
            }
        }

        if (_goalType == QuestGoalType.NodeCleared || _goalType == QuestGoalType.NodeRankCleared)
        {
            if (_nodeTypes == null || _nodeTypes.Length == 0)
                throw new InvalidOperationException($"{name}: 대상 노드 종류 설정 필요");

            foreach (MapBindings.MapType nodeType in _nodeTypes)
            {
                if (!Enum.IsDefined(typeof(MapBindings.MapType), nodeType))
                    throw new InvalidOperationException($"{name}: 유효한 노드 종류 필요");
            }
        }
    }
}