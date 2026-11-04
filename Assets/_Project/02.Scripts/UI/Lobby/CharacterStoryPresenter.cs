using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 캐릭터별 콘텐츠 캐시와 스토리 화면 연결
/// </summary>
public class CharacterStoryPresenter : MonoBehaviour
{
    [Header("----- 스토리 화면과 풀 -----")]
    [SerializeField] CharacterStoryView _view;
    [SerializeField] CharacterStoryViewPool _pool;

    [Header("----- 스토리 해금 테스트 -----")]
    [SerializeField] bool _useTestMasteryLevel;
    [SerializeField, Min(0)] int _testMasteryLevel = 1;

    readonly HashSet<(string CharacterId, string StoryId)> _testUnlocks =
        new HashSet<(string CharacterId, string StoryId)>();

    CharacterStoryContent _visibleContent;
    LocalStoryUnlockStore _unlockStore;
    CharacterStoryContent.Story _selectedStory;
    string _characterId;
    int? _masteryLevel;
    bool _isMasteryTest;

    readonly Dictionary<CharacterStoryDefinition, CharacterStoryContent> _contents =
        new Dictionary<CharacterStoryDefinition, CharacterStoryContent>();

    CharacterStoryItemView _selectedItem;
    bool _validated;

    void OnEnable()
    {
        _view.UnlockRequested += HandleUnlockRequested;
    }

    void OnDisable()
    {
        _view.UnlockRequested -= HandleUnlockRequested;
        if (_validated)
            Clear();
    }

    void HandleStorySelected(CharacterStoryItemView item, CharacterStoryContent.Story story)
    {
        if (_selectedItem != item)
        {
            ClearSelection();
            _selectedItem = item;
            _selectedStory = story;
            _selectedItem.SetSelected(true);
        }

        RefreshSelectedStory();
    }

    void ClearSelection()
    {
        if (_selectedItem != null)
            _selectedItem.SetSelected(false);

        _selectedItem = null;
        _selectedStory = null;
    }

    bool HasUnlockRecord(CharacterStoryContent.Story story)
    {
        return _isMasteryTest
            ? _testUnlocks.Contains((_characterId, story.StoryId))
            : _unlockStore?.Contains(_characterId, story.StoryId) == true;
    }

    bool CanUnlock(CharacterStoryContent.Story story)
    {
        bool canSave = _isMasteryTest || (_unlockStore != null && _unlockStore.LoadError == null);
        return canSave && !HasUnlockRecord(story)
            && CharacterStoryProgression.MeetsRequirements(story, _masteryLevel);
    }

    string GetLockedMessage(CharacterStoryContent.Story story)
    {
        if (CharacterStoryProgression.CanRead(story, _masteryLevel, HasUnlockRecord(story)))
            return null;

        if (CharacterStoryProgression.MeetsRequirements(story, _masteryLevel))
        {
            if (!_isMasteryTest && _unlockStore == null)
                return "로컬 해금 기록을 사용할 수 없습니다.";

            return (_isMasteryTest ? null : _unlockStore.LoadError)
                ?? "해금 버튼을 눌러 스토리를 열 수 있습니다.";
        }

        string heading = story.UnlockMode == StoryUnlockMode.Any
            ? "다음 조건 중 하나를 충족하면 열립니다."
            : "다음 조건을 모두 충족하면 열립니다.";
        List<string> lines = new List<string> { heading };

        foreach (CharacterStoryUnlockCondition condition in story.UnlockConditions)
            lines.Add(condition.Description);

        if (!_masteryLevel.HasValue)
            lines.Add("현재 캐릭터 숙련도를 확인할 수 없습니다.");

        return string.Join("\n", lines);
    }

    void RefreshSelectedStory(string error = null)
    {
        if (_selectedStory == null) return;

        string body = _selectedItem.LockedMessage ?? _selectedStory.Body;
        if (error != null)
            body += "\n\n" + error;

        _view.ShowStory(_selectedStory.Title, body);
        _view.SetUnlockButtonVisible(CanUnlock(_selectedStory));
    }

    void RefreshStoryList()
    {
        for (int index = 0; index < _visibleContent.Stories.Count; index++)
        {
            CharacterStoryContent.Story story = _visibleContent.Stories[index];
            _pool.Items[index].SetLockedMessage(GetLockedMessage(story));
        }

        RefreshSelectedStory();
    }

    void HandleUnlockRequested()
    {
        if (_selectedStory == null || !CanUnlock(_selectedStory)) return;

        try
        {
            if (_isMasteryTest)
                _testUnlocks.Add((_characterId, _selectedStory.StoryId));
            else
                _unlockStore.SaveUnlock(_characterId, _selectedStory.StoryId);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            RefreshSelectedStory("해금 기록을 저장하지 못했습니다. 다시 시도해 주세요.");
            return;
        }

        RefreshStoryList();
    }

#if UNITY_EDITOR
    [ContextMenu("스토리 해금 테스트/레벨 +1")]
    void IncreaseTestMasteryLevel()
    {
        if (!Application.isPlaying || !_useTestMasteryLevel || _testMasteryLevel == int.MaxValue)
            return;

        _testMasteryLevel++;
        if (!_isMasteryTest || _visibleContent == null) return;

        _masteryLevel = _testMasteryLevel;
        _view.ShowMastery(_masteryLevel, 0, null);
        RefreshStoryList();
    }
#endif

    /// <summary>
    /// 로비 전환에서 사용할 화면과 풀의 필수 참조 검사
    /// </summary>
    /// <param name="view">로비가 제어하는 스토리 화면</param>
    public void ValidateBindings(CharacterStoryView view)
    {
        if (_view == null || _pool == null || _view != view)
            throw new MissingReferenceException($"{name}: 로비와 동일한 스토리 화면 및 풀 연결 필요");
        if (!enabled || !_pool.enabled)
            throw new InvalidOperationException($"{name}: Presenter와 풀 컴포넌트 활성 설정 필요");

        _view.ValidateBindings();
        _pool.ValidateBindings(_view.StoryListContent);
        _validated = true;
    }

    /// <summary>
    /// 선택 캐릭터의 숙련도와 스토리 목록 표시
    /// </summary>
    /// <param name="character">로비에서 선택한 캐릭터</param>
    /// <param name="masteryLevel">로비에 로드된 캐릭터 숙련도 레벨</param>
    /// <param name="currentExperience">현재 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨 필요 경험치</param>
    /// <param name="unlockStore">현재 사용자의 로컬 해금 기록 저장소</param>
    public void Show(
        LobbyCharacterDefinition character,
        int? masteryLevel,
        long currentExperience,
        long requiredExperience,
        LocalStoryUnlockStore unlockStore)
    {
        if (!_validated)
            throw new InvalidOperationException($"{name}: 스토리 참조 검사 이후 표시 필요");
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        CharacterStoryDefinition definition = character.StoryDefinition;
        if (definition == null)
            throw new MissingReferenceException($"{name}: 선택 캐릭터의 스토리 SO 연결 필요");

        // 데이터 오류가 있는 경우 현재 로비 화면을 유지하도록 풀 변경 전에 로딩
        if (!_contents.TryGetValue(definition, out CharacterStoryContent content))
        {
            content = CharacterStoryJsonLoader.Load(definition);
            _contents.Add(definition, content);
        }

        _view.HideStory();
        ClearSelection();
        _characterId = character.CharacterId;
        _unlockStore = unlockStore;
        _isMasteryTest = false;
#if UNITY_EDITOR
        _isMasteryTest = _useTestMasteryLevel && Application.isPlaying;
#endif
        _masteryLevel = _isMasteryTest ? _testMasteryLevel : masteryLevel;

        _pool.Prepare(content.Stories.Count);
        for (int index = 0; index < content.Stories.Count; index++)
        {
            CharacterStoryContent.Story story = content.Stories[index];
            _pool.Items[index].Bind(story, GetLockedMessage(story), HandleStorySelected);
        }

        _pool.Activate(content.Stories.Count);
        _view.ShowMastery(
            _masteryLevel,
            currentExperience,
            _isMasteryTest || !masteryLevel.HasValue ? null : requiredExperience);
        _visibleContent = content;
    }

    /// <summary>
    /// 캐시와 풀 객체를 유지하며 화면의 이전 선택 및 이벤트 해제
    /// </summary>
    public void Clear()
    {
        _visibleContent = null;
        _unlockStore = null;
        _characterId = null;
        _masteryLevel = null;
        _isMasteryTest = false;

        _view.HideStory();
        ClearSelection();
        _pool.Clear();
    }
}
