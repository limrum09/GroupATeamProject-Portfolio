using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 스토리 화면과 본문 표시 및 닫기 요청 전달
/// </summary>
[DisallowMultipleComponent]
public class CharacterStoryView : MonoBehaviour, ILobbyView
{
    [Header("----- 화면 표시 -----")]
    [SerializeField] CanvasGroup _canvasGroup;
    [SerializeField] Image _inputBlocker;

    [Header("----- 닫기 -----")]
    [SerializeField] Button _closeButton;

    [Header("----- 스토리 목록 -----")]
    [SerializeField] ScrollRect _storyListScroll;

    [Header("----- 숙련도 표시 -----")]
    [SerializeField] TextMeshProUGUI _masteryLevelText;
    [SerializeField] Image _masteryGaugeFill;

    [Header("----- 스토리 본문 창 -----")]
    [SerializeField] GameObject _storyContentWindow;
    [SerializeField] TextMeshProUGUI _storyTitleText;
    [SerializeField] TextMeshProUGUI _storyBodyText;
    [SerializeField] ScrollRect _storyBodyScroll;
    [SerializeField] Button _closeContentButton;
    [SerializeField] Button _unlockButton;

    public event Action CloseRequested;
    public event Action UnlockRequested;

    public RectTransform StoryListContent => _storyListScroll.content;

    void OnEnable()
    {
        _closeButton.onClick.AddListener(HandleCloseClicked);
        _closeContentButton.onClick.AddListener(HideStory);
        _unlockButton.onClick.AddListener(HandleUnlockClicked);
    }

    void OnDisable()
    {
        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(HandleCloseClicked);

        if (_closeContentButton != null)
            _closeContentButton.onClick.RemoveListener(HideStory);

        if (_unlockButton != null)
            _unlockButton.onClick.RemoveListener(HandleUnlockClicked);
    }

    void HandleUnlockClicked()
    {
        UnlockRequested?.Invoke();
    }

    void HandleCloseClicked()
    {
        CloseRequested?.Invoke();
    }

    void ResetContent()
    {
        HideStory();

        LayoutRebuilder.MarkLayoutForRebuild(_storyListScroll.content);
        _storyListScroll.StopMovement();
        _storyListScroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// 초기 비활성 상태에서도 사용할 화면 참조 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_canvasGroup == null || _inputBlocker == null || _closeButton == null)
            throw new MissingReferenceException($"{name}: CanvasGroup과 입력 차단 배경 및 닫기 버튼 연결 필요");

        if (_canvasGroup.gameObject != gameObject)
            throw new InvalidOperationException($"{name}: CanvasGroup은 스토리 화면 루트에 배치 필요");

        if (!_inputBlocker.transform.IsChildOf(transform) || !_closeButton.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 배경과 닫기 버튼은 스토리 화면 내부에 배치 필요");

        if (!_inputBlocker.raycastTarget)
            throw new InvalidOperationException($"{name}: 입력 차단 배경의 Raycast Target 활성 설정 필요");

        if (_storyListScroll == null ||
            _storyContentWindow == null ||
            _storyTitleText == null ||
            _storyBodyText == null ||
            _storyBodyScroll == null ||
            _closeContentButton == null ||
            _unlockButton == null)
        {
            throw new MissingReferenceException($"{name}: 스토리 목록과 스토리 본문 참조 연결 필요");
        }

        if (_storyContentWindow == gameObject || !_storyContentWindow.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 본문 창은 전체 스토리 화면의 하위에 배치 필요");

        if (!_closeContentButton.transform.IsChildOf(_storyContentWindow.transform))
            throw new InvalidOperationException($"{name}: 본문 닫기 버튼은 본문 창 내부에 배치 필요");

        if (!_unlockButton.transform.IsChildOf(_storyContentWindow.transform))
            throw new InvalidOperationException($"{name}: 해금 버튼은 본문 창 내부에 배치 필요");

        if (_storyListScroll.content == null ||
            _storyListScroll.viewport == null ||
            _storyBodyScroll.content == null ||
            _storyBodyScroll.viewport == null)
        {
            throw new MissingReferenceException($"{name}: 목록과 본문 ScrollRect의 Content 및 Viewport 연결 필요");
        }

        if (!_storyBodyText.transform.IsChildOf(_storyBodyScroll.content))
            throw new InvalidOperationException($"{name}: 본문 텍스트는 본문 ScrollRect의 Content 내부에 배치 필요");

        if (_masteryLevelText == null || _masteryGaugeFill == null)
            throw new MissingReferenceException($"{name}: 숙련도 레벨 텍스트와 게이지 연결 필요");
    }

    /// <summary>
    /// 선택 캐릭터의 숙련도 레벨과 현재 구간 진행률 표시
    /// </summary>
    /// <param name="level">숙련도 레벨, 조회 불가 시 null</param>
    /// <param name="currentExperience">현재 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨 필요 경험치, 최대 레벨은 0, 조회 불가 시 null</param>
    public void ShowMastery(int? level, long currentExperience, long? requiredExperience)
    {
        _masteryLevelText.text = level.HasValue ? $"Lv. {level.Value:N0}" : "Lv. —";
        if (!level.HasValue || !requiredExperience.HasValue)
        {
            _masteryGaugeFill.fillAmount = 0f;
            return;
        }

        _masteryGaugeFill.fillAmount = requiredExperience.Value == 0
            ? 1f
            : Mathf.Clamp01((float)((double)currentExperience / requiredExperience.Value));
    }

    /// <summary>
    /// 스토리 화면 활성 여부와 초기 입력 상태 설정
    /// </summary>
    /// <param name="visible">화면 표시 여부</param>
    public void SetVisible(bool visible)
    {
        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = visible;
        _closeButton.interactable = false;

        ResetContent();
        gameObject.SetActive(visible);
    }

    /// <summary>
    /// 스토리 화면 입력 허용 변경
    /// </summary>
    /// <param name="interactable">입력 허용 여부</param>
    public void SetInteraction(bool interactable)
    {
        _canvasGroup.interactable = interactable;
        _closeButton.interactable = interactable;
    }

    /// <summary>
    /// 선택한 스토리 본문 표시와 스크롤 초기화
    /// </summary>
    /// <param name="title">표시할 스토리 제목</param>
    /// <param name="body">표시할 스토리 본문</param>
    public void ShowStory(string title, string body)
    {
        _storyTitleText.text = title;
        _storyBodyText.text = body;
        _storyContentWindow.SetActive(true);

        // 본문 길이에 따른 레이아웃 반영 후 상단 배치
        Canvas.ForceUpdateCanvases();
        _storyBodyScroll.StopMovement();
        _storyBodyScroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// 상세 창 해금 버튼 표시 상태 설정
    /// </summary>
    /// <param name="visible">해금 버튼 표시 여부</param>
    public void SetUnlockButtonVisible(bool visible)
    {
        _unlockButton.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 목록을 유지하고 스토리 본문 창 닫기
    /// </summary>
    public void HideStory()
    {
        SetUnlockButtonVisible(false);
        _storyContentWindow.SetActive(false);
        _storyTitleText.text = string.Empty;
        _storyBodyText.text = string.Empty;
        _storyBodyScroll.StopMovement();
    }

    /// <summary>
    /// 카메라 복귀 중 화면을 숨기고 뒤쪽 입력 차단 유지
    /// </summary>
    public void BeginClosing()
    {
        SetInteraction(false);

        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = true;
    }

    /// <summary>
    /// 스토리 화면 열기
    /// </summary>
    public void Open()
    {
        SetVisible(true);
    }

    /// <summary>
    /// 스토리 화면 닫기
    /// </summary>
    public void Close()
    {
        SetVisible(false);
    }
}
