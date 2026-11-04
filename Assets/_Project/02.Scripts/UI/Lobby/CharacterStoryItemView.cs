using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스토리 패널 제목 표시와 선택 전달
/// </summary>
public class CharacterStoryItemView : MonoBehaviour
{
    [Header("----- 패널 참조 -----")]
    [SerializeField] Button _button;
    [SerializeField] TextMeshProUGUI _titleText;
    [SerializeField] GameObject _selectionVisual;
    [SerializeField] GameObject _lockIcon;

    static readonly Color32 SelectedTitleColor = new Color32(224, 224, 224, 255);
    static readonly Color32 UnselectedTitleColor = new Color32(145, 145, 145, 255);

    CharacterStoryContent.Story _story;
    Action<CharacterStoryItemView, CharacterStoryContent.Story> _onSelected;

    public string LockedMessage { get; private set; }

    void OnEnable()
    {
        _button.onClick.AddListener(HandleClicked);
    }

    void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleClicked);
    }

    void HandleClicked()
    {
        _onSelected?.Invoke(this, _story);
    }

    /// <summary>
    /// 비활성 템플릿을 포함한 필수 참조 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_button == null || _titleText == null || _lockIcon == null)
            throw new MissingReferenceException($"{name}: 스토리 버튼과 제목 및 자물쇠 연결 필요");
        if (_button.gameObject != gameObject || !_titleText.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 같은 오브젝트의 버튼과 하위 제목 연결 필요");
        if (!enabled || _button.onClick.GetPersistentEventCount() != 0)
            throw new InvalidOperationException($"{name}: 패널 컴포넌트 활성 설정과 기존 OnClick 연결 제거 필요");
    }

    /// <summary>
    /// 재사용 패널에 콘텐츠와 선택 처리 연결
    /// </summary>
    /// <param name="story">표시할 스토리</param>
    /// <param name="lockedMessage">잠금 안내, 해금 상태는 null</param>
    /// <param name="onSelected">선택 처리</param>
    public void Bind(CharacterStoryContent.Story story, string lockedMessage, Action<CharacterStoryItemView, CharacterStoryContent.Story> onSelected)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
        _titleText.text = story.Title;
        SetLockedMessage(lockedMessage);
        SetSelected(false);
        _button.interactable = true;
    }

    /// <summary>
    /// 이전 콘텐츠와 선택 처리 해제
    /// </summary>
    public void Unbind()
    {
        SetSelected(false);
        SetLockedMessage(null);
        _button.interactable = false;
        _titleText.text = string.Empty;
        _story = null;
        _onSelected = null;
    }

    /// <summary>
    /// 잠금 안내와 자물쇠 표시 갱신
    /// </summary>
    /// <param name="lockedMessage">잠금 안내, 해금 상태는 null</param>
    public void SetLockedMessage(string lockedMessage)
    {
        LockedMessage = lockedMessage;
        _lockIcon.SetActive(lockedMessage != null);
    }

    /// <summary>
    /// 스토리 선택 효과 표시 상태 설정
    /// </summary>
    /// <param name="selected">선택 여부</param>
    public void SetSelected(bool selected)
    {
        _titleText.color = selected ? SelectedTitleColor : UnselectedTitleColor;
        if (_selectionVisual != null)
            _selectionVisual.SetActive(selected);
    }
}
