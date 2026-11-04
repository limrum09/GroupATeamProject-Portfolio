using System;
using UnityEngine;

/// <summary>
/// 모든 캐릭터가 공유하는 런 레벨별 필요 경험치 정의
/// </summary>
[CreateAssetMenu(menuName = "Project/Progression/Run Level Definition", fileName = "_RunLevelDefinition")]
public sealed class RunLevelDefinition : ScriptableObject
{
    [Header("----- 다음 레벨까지 필요한 경험치 -----")]
    [SerializeField]
    float[] _experienceToNextLevel = Array.Empty<float>();

    /// <summary>
    /// 시작 레벨 1과 경험치 구간 수로 결정되는 최대 레벨
    /// </summary>
    public int MaxLevel => _experienceToNextLevel.Length + 1;

    /// <summary>
    /// 현재 레벨에서 다음 레벨까지 필요한 경험치 조회
    /// </summary>
    /// <param name="currentLevel">최대 레벨 미만의 현재 레벨</param>
    /// <returns>다음 레벨까지 필요한 경험치</returns>
    public float GetRequiredExperience(int currentLevel)
    {
        if (currentLevel < 1 || currentLevel >= MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(currentLevel), "다음 레벨이 존재하는 현재 레벨 필요");

        return _experienceToNextLevel[currentLevel - 1];
    }

    /// <summary>
    /// 경험치 배열과 각 구간의 양수 유한값 검사
    /// </summary>
    public void Validate()
    {
        if (_experienceToNextLevel == null)
            throw new InvalidOperationException($"{name}: 레벨별 필요 경험치 배열 필요");
    }

    void OnValidate()
    {
        Validate();
    }
}