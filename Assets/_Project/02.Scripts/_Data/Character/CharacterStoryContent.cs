using System;
using System.Collections.Generic;

/// <summary>
/// CSV 행 순서를 보존한 읽기 전용 캐릭터 스토리 콘텐츠
/// </summary>
public class CharacterStoryContent
{
    /// <summary>
    /// 개별 스토리의 원문과 자체 해금 조건
    /// </summary>
    public class Story
    {
        public string StoryId { get; }
        public string Title { get; }
        public string Body { get; }
        public StoryUnlockMode UnlockMode { get; }
        public IReadOnlyList<CharacterStoryUnlockCondition> UnlockConditions { get; }

        /// <summary>
        /// 스토리 콘텐츠 구성
        /// </summary>
        /// <param name="storyId">스토리 식별자</param>
        /// <param name="title">스토리 제목</param>
        /// <param name="body">줄바꿈을 포함한 본문 원문</param>
        /// <param name="unlockMode">자체 조건의 조합 방식</param>
        /// <param name="unlockConditions">로더에서 검증한 자체 해금 조건</param>
        public Story(
            string storyId,
            string title,
            string body,
            StoryUnlockMode unlockMode,
            IReadOnlyList<CharacterStoryUnlockCondition> unlockConditions)
        {
            if (string.IsNullOrWhiteSpace(storyId))
                throw new ArgumentException("스토리 ID 필요", nameof(storyId));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("스토리 제목 필요", nameof(title));
            if (string.IsNullOrWhiteSpace(body))
                throw new ArgumentException("스토리 본문 필요", nameof(body));
            if (unlockConditions == null)
                throw new ArgumentNullException(nameof(unlockConditions));

            StoryId = storyId;
            Title = title;
            Body = body;
            UnlockMode = unlockMode;
            UnlockConditions = new List<CharacterStoryUnlockCondition>(unlockConditions).AsReadOnly();
        }
    }

    public IReadOnlyList<Story> Stories { get; }

    /// <summary>
    /// 스토리 목록의 복사본으로 전체 콘텐츠 구성
    /// </summary>
    /// <param name="stories">CSV 행 순서의 스토리 목록</param>
    public CharacterStoryContent(IReadOnlyList<Story> stories)
    {
        if (stories == null)
            throw new ArgumentNullException(nameof(stories));

        Stories = new List<Story>(stories).AsReadOnly();
    }
}
