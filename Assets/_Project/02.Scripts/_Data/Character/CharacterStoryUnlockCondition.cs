/// <summary>
/// 연결된 해금 조건의 조합 방식
/// </summary>
public enum StoryUnlockMode
{
    None,
    All,
    Any
}

/// <summary>
/// 스토리 해금 조건의 종류
/// </summary>
public enum StoryUnlockConditionType
{
    MasteryLevel,
    CurrencyAmount,
    QuestCompleted
}

/// <summary>
/// 해금 조건의 정적 정의와 안내 문구
/// </summary>
public class CharacterStoryUnlockCondition
{
    public string ConditionId { get; }
    public StoryUnlockConditionType Type { get; }
    public string TargetId { get; }
    public long? RequiredAmount { get; }
    public string Description { get; }

    /// <summary>
    /// 로더에서 검증한 조건 데이터 구성
    /// </summary>
    /// <param name="conditionId">캐릭터 내 고유 조건 식별자</param>
    /// <param name="type">조건 종류</param>
    /// <param name="targetId">참조 대상 식별자 또는 현재 캐릭터를 뜻하는 빈 문자열</param>
    /// <param name="requiredAmount">요구 수치 또는 수치를 사용하지 않는 조건의 null</param>
    /// <param name="description">잠금 화면의 조건 안내 문구</param>
    public CharacterStoryUnlockCondition(
        string conditionId,
        StoryUnlockConditionType type,
        string targetId,
        long? requiredAmount,
        string description)
    {
        ConditionId = conditionId;
        Type = type;
        TargetId = targetId;
        RequiredAmount = requiredAmount;
        Description = description;
    }
}
