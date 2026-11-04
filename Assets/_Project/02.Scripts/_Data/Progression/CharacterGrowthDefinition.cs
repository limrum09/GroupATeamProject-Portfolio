using System;
using UnityEngine;

/// <summary>
/// 한 단계 캐릭터 레벨업의 스탯 증가량과 추가 회복량
/// </summary>
[Serializable]
public struct CharacterLevelGrowth
{
    [Header("----- 스탯 증가량 -----")]
    [SerializeField] CharacterStatValues _statIncrease;

    [Header("----- 최대 체력 증가분 외 추가 회복량 -----")]
    [SerializeField, Min(0f)] float _additionalHealing;

    public CharacterStatValues StatIncrease => _statIncrease;
    public float AdditionalHealing => _additionalHealing;

    /// <summary>
    /// 스탯 증가량과 추가 회복량의 유효성 검사
    /// </summary>
    public void Validate()
    {
        _statIncrease.Validate();

        if (float.IsNaN(_additionalHealing) || float.IsInfinity(_additionalHealing) || _additionalHealing < 0f)
            throw new InvalidOperationException("레벨업 추가 회복량은 유한한 0 이상의 값 필요");
    }
}

/// <summary>
/// 캐릭터별 기본 정의와 레벨별 성장 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Progression/Character Growth Definition", fileName = "_CharacterGrowthDefinition")]
public sealed class CharacterGrowthDefinition : ScriptableObject
{
    [Header("----- 캐릭터 식별 -----")]
    [SerializeField] string _characterId;

    [Header("----- 기본 캐릭터 설정 -----")]
    [SerializeField] CharacterDefinition _characterDefinition;

    [Header("----- 2레벨부터의 레벨별 성장 -----")]
    [SerializeField]
    CharacterLevelGrowth[] _levelGrowth = Array.Empty<CharacterLevelGrowth>();

    public string CharacterId => _characterId;
    public CharacterDefinition CharacterDefinition => _characterDefinition;

    // 성장 행 수로 표현되는 최대 레벨, 공통 런 최대 레벨과 일치 필요
    public int MaxLevel => _levelGrowth.Length + 1;


    void OnValidate()
    {
        Validate();
    }

    /// <summary>
    /// 지정 레벨에 도달할 때 적용할 성장 설정 조회
    /// </summary>
    /// <param name="reachedLevel">새로 도달한 2 이상의 레벨</param>
    /// <returns>해당 레벨의 스탯 증가량과 추가 회복량</returns>
    public CharacterLevelGrowth GetGrowthForLevel(int reachedLevel)
    {
        if (reachedLevel < 2 || reachedLevel > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(reachedLevel), "성장 설정에 포함된 2 이상의 레벨 필요");

        return _levelGrowth[reachedLevel - 2];
    }

    /// <summary>
    /// 캐릭터 식별자와 기본 정의 및 성장 행 검사
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(_characterId))
            throw new InvalidOperationException($"{name}: 저장에 사용할 Character ID 필요");

        if (_characterDefinition == null)
            throw new MissingReferenceException($"{name}: CharacterDefinition 연결 필요");

        if (_levelGrowth == null)
            throw new InvalidOperationException($"{name}: 레벨별 성장 배열 필요");

        foreach (CharacterLevelGrowth growth in _levelGrowth)
        {
            growth.Validate();
        }
    }

    /// <summary>
    /// 공통 경험치 표와 캐릭터 성장 표의 레벨 범위 검사
    /// </summary>
    /// <param name="levelDefinition">이번 런에서 사용할 공통 경험치 표</param>
    public void ValidateFor(RunLevelDefinition levelDefinition)
    {
        if (levelDefinition == null)
            throw new ArgumentNullException(nameof(levelDefinition));

        Validate();
        levelDefinition.Validate();

        if (MaxLevel != levelDefinition.MaxLevel)
            throw new InvalidOperationException($"{name}: 성장 최대 레벨 {MaxLevel}과 공통 최대 레벨 " + $"{levelDefinition.MaxLevel}의 일치 필요");
    }
}