using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 공통 메뉴의 표시 상태와 선택 포커스 관리
/// </summary>
[DisallowMultipleComponent]
public class GameMenuController : MonoBehaviour
{
    [Header("----- 메뉴 화면 -----")]
    [SerializeField] GameObject _menuPanel;
    [SerializeField] GameObject _menuWindow;
    [SerializeField] SettingsView _settingsView;
    [SerializeField] Button _settingsButton;
    [SerializeField] Button _initialSelectedButton;

    [Header("----- 메뉴 버튼 -----")]
    [SerializeField] Button _continueButton;
    [SerializeField] Button _returnToLobbyButton;

    EventSystem _eventSystem;
    GameObject _previousSelection;

    public event Action<bool> OnVisibilityChanged;
    public event Action OnReturnToLobbyRequested;

    public bool IsOpen => _menuPanel.activeSelf;
    public bool IsBound => _eventSystem != null;

    void HandleContinueClicked()
    {
        if (!IsBound || !IsOpen || _settingsView.IsOpen) return;

        Close();
    }

    void HandleSettingsClicked()
    {
        if (!IsBound || !IsOpen || _settingsView.IsOpen) return;

        _menuWindow.SetActive(false);
        _settingsView.Open();
    }

    void HandleSettingsBack()
    {
        _settingsView.Close();
        _menuWindow.SetActive(true);
        _eventSystem.SetSelectedGameObject(_settingsButton.gameObject);
    }

    void HandleReturnToLobbyClicked()
    {
        if (!IsBound || !IsOpen || _settingsView.IsOpen) return;

        OnReturnToLobbyRequested?.Invoke();
    }

    void CloseCore(bool restoreSelection)
    {
        if (_menuPanel == null || !IsOpen) return;

        GameObject previousSelection = _previousSelection;
        _previousSelection = null;
        if (_settingsView != null)
            _settingsView.Close();

        _menuPanel.SetActive(false);
        if (_menuWindow != null)
            _menuWindow.SetActive(true);

        if (_eventSystem != null)
        {
            GameObject selection = restoreSelection && previousSelection != null && previousSelection.activeInHierarchy
                ? previousSelection
                : null;
            _eventSystem.SetSelectedGameObject(selection);
        }

        OnVisibilityChanged?.Invoke(false);
    }

    void Awake()
    {
        if (_menuPanel == null || _menuWindow == null || _settingsView == null ||
            _settingsButton == null || _initialSelectedButton == null ||
            _continueButton == null || _returnToLobbyButton == null)
            throw new MissingReferenceException($"{name}: 메뉴 패널과 최초 선택 및 기능 버튼 연결 필요");

        if (_menuPanel == gameObject || !_menuPanel.transform.IsChildOf(transform) ||
            _menuWindow.transform.parent != _menuPanel.transform ||
            _settingsView.transform.parent != _menuPanel.transform ||
            !_settingsButton.transform.IsChildOf(_menuWindow.transform) ||
            !_initialSelectedButton.transform.IsChildOf(_menuPanel.transform) ||
            !_continueButton.transform.IsChildOf(_menuPanel.transform) ||
            !_returnToLobbyButton.transform.IsChildOf(_menuPanel.transform) ||
            _continueButton == _returnToLobbyButton)
            throw new InvalidOperationException($"{name}: 루트 하위 메뉴 패널과 패널 하위의 서로 다른 기능 버튼 필요");

        _settingsView.Close();
        _menuPanel.SetActive(false);
        _settingsButton.onClick.AddListener(HandleSettingsClicked);
        _settingsView.OnBackRequested += HandleSettingsBack;
        _continueButton.onClick.AddListener(HandleContinueClicked);
        _returnToLobbyButton.onClick.AddListener(HandleReturnToLobbyClicked);
    }

    void OnDestroy()
    {
        if (_settingsButton != null)
            _settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        if (_settingsView != null)
            _settingsView.OnBackRequested -= HandleSettingsBack;

        if (_continueButton != null)
            _continueButton.onClick.RemoveListener(HandleContinueClicked);

        if (_returnToLobbyButton != null)
            _returnToLobbyButton.onClick.RemoveListener(HandleReturnToLobbyClicked);
    }

    /// <summary>
    /// 애플리케이션의 공통 종료 절차에 데스크톱 종료 요청 전달
    /// </summary>
    public void RequestExitToDesktop()
    {
        Application.Quit();
    }

    /// <summary>
    /// 현재 세션의 UI 선택 담당자 연결
    /// </summary>
    /// <param name="eventSystem">현재 세션의 활성 EventSystem</param>
    /// <param name="settings">공유 옵션 담당자</param>
    /// <param name="allowRunExit">메뉴의 런 종료 요청 허용 여부</param>
    public void Bind(EventSystem eventSystem, SettingsService settings, bool allowRunExit = true)
    {
        if (eventSystem == null) throw new ArgumentNullException(nameof(eventSystem));
        if (IsBound || IsOpen) throw new InvalidOperationException($"{name}: 이전 메뉴 연결 해제 필요");
        if (!isActiveAndEnabled || !eventSystem.isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 메뉴와 EventSystem 필요");

        _settingsView.Bind(settings, eventSystem);
        _returnToLobbyButton.interactable = allowRunExit;
        _eventSystem = eventSystem;
    }

    /// <summary>
    /// 메뉴 표시와 최초 버튼 선택
    /// </summary>
    public void Open()
    {
        if (!IsBound) throw new InvalidOperationException($"{name}: 메뉴 표시 전 세션 연결 필요");
        if (IsOpen) return;

        _previousSelection = _eventSystem.currentSelectedGameObject;
        _menuPanel.SetActive(true);
        _eventSystem.SetSelectedGameObject(_initialSelectedButton.gameObject);
        OnVisibilityChanged?.Invoke(true);
    }

    /// <summary>
    /// 설정 화면 복귀 우선 처리와 메뉴 종료
    /// </summary>
    /// <returns>취소 입력 소비 여부</returns>
    public bool TryHandleCancel()
    {
        if (!IsOpen) return false;

        if (_settingsView.TryHandleBack()) return true;

        Close();

        return true;
    }

    /// <summary>
    /// 메뉴 숨김과 이전 선택 복원
    /// </summary>
    public void Close()
    {
        CloseCore(true);
    }

    /// <summary>
    /// 이전 선택 복원 없는 메뉴 종료와 세션 연결 해제
    /// </summary>
    public void Unbind()
    {
        try
        {
            CloseCore(false);
        }
        finally
        {
            if (_settingsView != null)
                _settingsView.Unbind();

            _eventSystem = null;
            _previousSelection = null;
        }
    }
}
