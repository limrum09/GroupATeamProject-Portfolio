using System;
using UnityEngine;

/// <summary>
/// 목표 진행값의 반영 방식
/// </summary>
public enum ObjectiveUpdateMode
{
    Add,
    Set,
    Maximum
}

/// <summary>
/// 퀘스트와 업적의 공통 진행 계산 및 완료 판정
/// </summary>
public static class ObjectiveEvaluator
{
    /// <summary>
    /// 미완료 목표의 진행값 반영과 완료 상태 전환
    /// </summary>
    /// <param name="currentValue">현재 진행값</param>
    /// <param name="isCompleted">달성 이후 유지할 완료 상태</param>
    /// <param name="targetValue">목표 수치</param>
    /// <param name="reportedValue">보고된 수치</param>
    /// <param name="mode">수치 반영 방식</param>
    /// <returns>진행값 또는 완료 상태의 변경 여부</returns>
    public static bool Apply(ref float currentValue, ref bool isCompleted, float targetValue, float reportedValue, ObjectiveUpdateMode mode)
    {
        if (isCompleted)
            return false;

        float nextValue;

        switch (mode)
        {
            case ObjectiveUpdateMode.Add:
                nextValue = currentValue + reportedValue;
                break;
            case ObjectiveUpdateMode.Set:
                nextValue = reportedValue;
                break;
            case ObjectiveUpdateMode.Maximum:
                nextValue = Mathf.Max(currentValue, reportedValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        nextValue = Mathf.Clamp(nextValue, 0f, targetValue);
        bool completed = nextValue >= targetValue;

        if (currentValue == nextValue && isCompleted == completed)
            return false;

        currentValue = nextValue;
        isCompleted = completed;
        return true;
    }
}