using UnityEngine;

/// <summary>
/// 캐릭터 숙련도 기반 스토리 해금 판정
/// </summary>
public class CharacterStoryProgression : MonoBehaviour
{
    /// <summary>
    /// 스토리 조건 조합에 따른 해금 여부 판정
    /// </summary>
    /// <param name="story">해금 조건을 확인할 스토리</param>
    /// <param name="masteryLevel">캐릭터 숙련도 레벨, 조회 불가 시 null</param>
    /// <returns>스토리 해금 여부</returns>
    public static bool MeetsRequirements(CharacterStoryContent.Story story, int? masteryLevel)
    {
        if (story.UnlockMode == StoryUnlockMode.None) return true;

        bool requireAll = story.UnlockMode == StoryUnlockMode.All;
        foreach (CharacterStoryUnlockCondition condition in story.UnlockConditions)
        {
            bool met = condition.Type == StoryUnlockConditionType.MasteryLevel
                && masteryLevel is int level
                && condition.RequiredAmount is long requiredLevel
                && level >= requiredLevel;

            if (requireAll && !met) return false;
            if (!requireAll && met) return true;
        }

        return requireAll;
    }

    /// <summary>
    /// 현재 숙련도 조건과 해금 완료 기록에 따른 본문 열람 판정
    /// </summary>
    /// <param name="story">열람할 스토리</param>
    /// <param name="masteryLevel">캐릭터 숙련도 레벨</param>
    /// <param name="hasUnlockRecord">해금 완료 기록 여부</param>
    /// <returns>본문 열람 가능 여부</returns>
    public static bool CanRead(CharacterStoryContent.Story story, int? masteryLevel, bool hasUnlockRecord)
    {
        return hasUnlockRecord && MeetsRequirements(story, masteryLevel);
    }
}
