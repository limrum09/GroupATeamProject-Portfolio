using System;
using UnityEngine;

/// <summary>
/// 퀘스트 하나의 조건 비교와 진행 및 완료 상태
/// </summary>
public class QuestProgress
{
    readonly QuestDefinition _definition;
    readonly string _periodKey;

    float _currentValue;
    bool _isCompleted;

    public QuestDefinition Definition => _definition;
    public string PeriodKey => _periodKey;
    public float CurrentValue => _currentValue;
    public bool IsCompleted => _isCompleted;

    bool AcceptsGoldSource(GoldGainSource source)
    {
        foreach (GoldGainSource accepted in _definition.GoldSources)
        {
            if (accepted == source)
                return true;
        }

        return false;
    }

    bool AcceptsNodeType(MapBindings.MapType nodeType)
    {
        foreach (MapBindings.MapType accepted in _definition.NodeTypes)
        {
            if (accepted == nodeType)
                return true;
        }

        return false;
    }

    bool SetValue(float value, ObjectiveUpdateMode mode = ObjectiveUpdateMode.Set)
    {
        return ObjectiveEvaluator.Apply(ref _currentValue, ref _isCompleted, _definition.TargetValue, value, mode);
    }

    /// <summary>
    /// 정의와 발급 주기를 사용하는 진행 상태 생성
    /// </summary>
    /// <param name="definition">등록할 퀘스트 정의</param>
    /// <param name="periodKey">일일 주기 또는 돌발 발급 기록 키</param>
    /// <param name="currentValue">복원할 진행값</param>
    /// <param name="isCompleted">기존 완료 여부</param>
    public QuestProgress(QuestDefinition definition, string periodKey = null, float currentValue = 0f, bool isCompleted = false)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (definition.Kind == QuestKind.Daily && string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("발급 기록 키 필요", nameof(periodKey));

        if (float.IsNaN(currentValue) || float.IsInfinity(currentValue) || currentValue < 0f)
            throw new ArgumentOutOfRangeException(nameof(currentValue));

        definition.Validate();

        _definition = definition;
        _periodKey = definition.Kind == QuestKind.Daily ? periodKey : null;

        _currentValue = isCompleted ? definition.TargetValue : Mathf.Clamp(currentValue, 0f, definition.TargetValue);

        _isCompleted = isCompleted || _currentValue >= definition.TargetValue;
    }

    /// <summary>
    /// 등급 조건에 맞는 적 처치 반영
    /// </summary>
    /// <param name="enemy">처치한 적의 정의</param>
    /// <returns>진행 변경 여부</returns>
    public bool ReportEnemyKilled(EnemyDefinition enemy)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));

        if (_isCompleted || _definition.GoalType != QuestGoalType.EnemyKill)
            return false;

        if (!_definition.AnyEnemyRank && _definition.EnemyRank != enemy.RankType)
            return false;

        return SetValue(1f, ObjectiveUpdateMode.Add);
    }

    /// <summary>
    /// 획득 사유에 맞는 골드 획득량 누적
    /// </summary>
    /// <param name="source">골드 획득 사유</param>
    /// <param name="amount">실제 획득량</param>
    /// <returns>진행 변경 여부</returns>
    public bool ReportGoldAcquired(GoldGainSource source, float amount)
    {
        if (_isCompleted || _definition.GoalType != QuestGoalType.GoldAcquired)
            return false;

        if (!AcceptsGoldSource(source))
            return false;

        return SetValue(amount, ObjectiveUpdateMode.Add);
    }

    /// <summary>
    /// 미완료 보유 목표에 현재 골드 잔액 반영
    /// </summary>
    /// <param name="currentGold">확정된 현재 잔액</param>
    /// <returns>진행 변경 여부</returns>
    public bool ReportGoldChanged(float currentGold)
    {
        if (_isCompleted || _definition.GoalType != QuestGoalType.GoldHeld)
            return false;

        return SetValue(currentGold);
    }

    /// <summary>
    /// 노드 종류에 따른 클리어 횟수 또는 최고 Rank 반영
    /// </summary>
    /// <param name="nodeType">클리어한 노드 종류</param>
    /// <param name="rank">클리어한 노드의 Rank</param>
    /// <returns>진행 변경 여부</returns>
    public bool ReportNodeCleared(MapBindings.MapType nodeType, int rank)
    {
        if (_isCompleted)
            return false;

        QuestGoalType goalType = _definition.GoalType;

        if (goalType != QuestGoalType.NodeCleared && goalType != QuestGoalType.NodeRankCleared)
            return false;

        if (!AcceptsNodeType(nodeType))
            return false;

        if (goalType == QuestGoalType.NodeCleared)
            return SetValue(1f, ObjectiveUpdateMode.Add);

        return SetValue(rank, ObjectiveUpdateMode.Maximum);
    }
}