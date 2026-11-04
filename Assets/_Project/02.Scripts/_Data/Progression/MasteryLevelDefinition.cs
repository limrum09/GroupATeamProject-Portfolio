using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 통합 숙련도의 레벨별 필요 경험치 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Progression/Mastery Definition", fileName = "_MasteryLevelDefinition")]
public class MasteryLevelDefinition : ScriptableObject
{
    [Header("----- 통합 레벨 설정 -----")]
    [SerializeField] List<long> _experienceToNextLevel = new List<long>();

    public int MaxLevel => _experienceToNextLevel.Count + 1;

    void OnValidate()
    {
        Validate();
    }

    /// <summary>
    /// 누적 숙련도에 대응하는 레벨 조회
    /// </summary>
    /// <param name="totalExperience">누적 숙련도 경험치</param>
    /// <returns>1부터 시작하는 숙련도 레벨</returns>
    public int GetLevel(long totalExperience)
    {
        GetProgress(totalExperience, out int level, out _, out _);

        return level;
    }

    /// <summary>
    /// 누적 숙련도를 현재 레벨과 해당 구간의 진행량으로 변환
    /// 최대 레벨에서는 구간 경험치와 다음 레벨 필요량 모두 0
    /// </summary>
    /// <param name="totalExperience">누적 숙련도 경험치</param>
    /// <param name="level">계산한 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 구간 경험치, 최대 레벨은 0</param>
    public void GetProgress(long totalExperience, out int level, out long currentExperience, out long requiredExperience)
    {
        if (totalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));

        level = 1;
        currentExperience = totalExperience;
        requiredExperience = 0;

        foreach (long required in _experienceToNextLevel)
        {
            if (currentExperience < required)
            {
                requiredExperience = required;
                return;
            }

            currentExperience -= required;
            level++;
        }

        // 최대 레벨의 표시는 원본 누적 경험치를 변경하지 않음
        currentExperience = 0;
    }

    /// <summary>
    /// 레벨 구간 존재 여부와 구간별 양수 경험치 검사
    /// </summary>
    public void Validate()
    {
        if (_experienceToNextLevel == null || _experienceToNextLevel.Count == 0)
            throw new InvalidOperationException($"{name}: 하나 이상의 레벨별 필요 숙련도 설정 필요");
    }
}