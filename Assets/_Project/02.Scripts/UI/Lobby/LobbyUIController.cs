using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// 로비 버튼 상호작용과 공통 화면 및 카메라 전환
/// </summary>
[DisallowMultipleComponent]
public class LobbyUIController : MonoBehaviour
{
    [Header("----- 로비 연결 -----")]
    [SerializeField] LobbyManager _lobby;

    [Header("----- 로비 상태 표시 -----")]
    [SerializeField] LobbyPlayerInfoView _playerInfoView;
    [SerializeField] LobbyMetaCurrencyView _metaCurrencyView;

    [Header("----- 패널과 카메라 -----")]
    [SerializeField] LobbyPanel[] _panels = Array.Empty<LobbyPanel>();
    [SerializeField] LobbyCameraDirector _cameraDirector;

    [Header("----- 홈 화면 기능 -----")]
    [SerializeField] GameObject _startButtonRoot;
    [SerializeField] Button _startButton;
    [SerializeField] LobbyDifficultySelectView _difficultyView;

    [Header("----- 스토리 콘텐츠 -----")]
    [SerializeField] CharacterStoryPresenter _storyPresenter;

    // 기존 버튼 이벤트와 기능별 상태 갱신에 필요한 참조
    LobbyCharacterSelectView _characterView;
    LobbyCharacterSelectPresenter _characterPresenter;
    LobbyStatusPresenter _statusPresenter;
    CharacterStoryView _storyView;
    MetaUpgradeUIController _metaUpgradeUI;
    LobbyQuestUIController _questUI;

    LobbyPanel _currentPanel;
    LobbyPanel _returnPanel;

    [Header("----- 로비 메뉴 연결 -----")]
    [SerializeField] bool _useSceneMenu;
    [SerializeField] EventSystem _menuEventSystem;
    [SerializeField] UICancelInputRouter _cancelInput;

    LobbyMenuView _menuView;
    SettingsView _settingsView;
    bool _cancelInputBound;

    Action _afterMove;

    bool _started;
    bool _subscribed;
    bool _isTransitioning;

    public LobbyPanel CurrentPanel => _currentPanel;
    public bool IsTransitioning => _isTransitioning;
    bool CanUseHomeSelection => _subscribed && !_isTransitioning && IsCurrentPanel(LobbyPanelType.Home) && _lobby.CanChangeSelection;
    bool CanUseStart => _subscribed && !_isTransitioning && !IsCurrentPanel(LobbyPanelType.Story) && !IsCurrentPanel(LobbyPanelType.MetaUpgrade) &&
        !IsCurrentPanel(LobbyPanelType.Menu) && !IsCurrentPanel(LobbyPanelType.Settings) && _lobby.CanStartRun;

    void Awake()
    {
        ValidateBindings();

        _characterView.ValidateBindings();

        if (_storyPresenter == null)
            throw new MissingReferenceException($"{name}: 스토리 Presenter 연결 필요");

        _storyPresenter.ValidateBindings(_storyView);
        _difficultyView.ValidateBindings();

        if (_metaUpgradeUI != null)
            _metaUpgradeUI.Bind(_lobby);

        if (_questUI != null)
            _questUI.Bind(_lobby);            

        _startButton.interactable = false;

        _statusPresenter = new LobbyStatusPresenter(_lobby, _playerInfoView, _metaCurrencyView);
        _characterPresenter = new LobbyCharacterSelectPresenter(_lobby, _characterView);
    }

    void Start()
    {
        _started = true;
        Subscribe();
    }

    void LateUpdate()
    {
        if (!_subscribed || _isTransitioning || IsCurrentPanel(LobbyPanelType.Home) ||
            IsCurrentPanel(LobbyPanelType.Menu) || IsCurrentPanel(LobbyPanelType.Settings))
            return;

        Mouse mouse = Mouse.current;

        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            OpenHome();
    }

    void OnEnable()
    {
        if (_started)
            Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();

        if (_characterPresenter != null)
        {
            _characterPresenter.Dispose();
            _characterPresenter = null;
        }
    }

    void ValidateBindings()
    {
        if (_lobby == null || _cameraDirector == null ||
            _startButtonRoot == null || _startButton == null || _difficultyView == null)
            throw new MissingReferenceException($"{name}: 로비, 카메라, 시작 버튼과 난이도 View 연결 필요");

        if (_panels == null || _panels.Length == 0)
            throw new MissingReferenceException($"{name}: 패널 목록 연결 필요");

        HashSet<LobbyPanelType> types = new HashSet<LobbyPanelType>();

        for (int i = 0; i < _panels.Length; i++)
        {
            LobbyPanel panel = _panels[i];

            if (panel == null)
                throw new MissingReferenceException($"{name}: 빈 패널 항목 제거 필요");

            panel.ValidateBindings();

            if (!types.Add(panel.Type))
                throw new InvalidOperationException($"{name}: 중복 패널 종류: {panel.Type}");
        }

        GetPanel(LobbyPanelType.Home);

        _characterView = GetPanel(LobbyPanelType.Character).ViewRoot.GetComponent<LobbyCharacterSelectView>();

        _storyView = GetPanel(LobbyPanelType.Story).ViewRoot.GetComponent<CharacterStoryView>();

        if (_characterView == null || _storyView == null)
            throw new MissingReferenceException($"{name}: 캐릭터와 스토리 패널 루트에 해당 View 연결 필요");

        if (!_characterView.enabled || !_storyView.enabled)
            throw new InvalidOperationException($"{name}: 캐릭터와 스토리 View 활성 설정 필요");

        LobbyPanel metaPanel = FindPanel(LobbyPanelType.MetaUpgrade);

        if (metaPanel != null)
        {
            // 기존 강화 기능을 이전하기 전까지 Bind와 닫기 검사 유지
            _metaUpgradeUI = metaPanel.ViewRoot
                .GetComponent<MetaUpgradeUIController>();

            if (_metaUpgradeUI == null)
                throw new MissingReferenceException($"{name}: 강화 패널 루트에 MetaUpgradeUIController 연결 필요");

            if (!_metaUpgradeUI.enabled)
                throw new InvalidOperationException($"{name}: MetaUpgradeUIController 활성 설정 필요");
        }

        LobbyPanel questPanel = FindPanel(LobbyPanelType.Quest);

        if (questPanel != null)
        {
            _questUI = questPanel.ViewRoot.GetComponent<LobbyQuestUIController>();

            if (_questUI == null)
                throw new MissingReferenceException($"{name}: 퀘스트 패널 루트에 LobbyQuestUIController 연결 필요");

            if (!_questUI.enabled)
                throw new InvalidOperationException($"{name}: LobbyQuestUIController 활성 설정 필요");
        }
    }

    LobbyPanel FindPanel(LobbyPanelType type)
    {
        foreach (LobbyPanel panel in _panels)
        {
            if (panel != null && panel.Type == type)
                return panel;
        }

        return null;
    }

    LobbyPanel GetPanel(LobbyPanelType type)
    {
        LobbyPanel panel = FindPanel(type);

        if (panel == null)
            throw new MissingReferenceException($"{name}: {type} 패널 등록 필요");

        return panel;
    }

    bool IsCurrentPanel(LobbyPanelType type)
    {
        return _currentPanel != null && _currentPanel.Type == type;
    }

    void ConnectSceneMenu()
    {
        if (!_useSceneMenu) return;

        if (_menuEventSystem == null || _cancelInput == null)
            throw new MissingReferenceException($"{name}: 로비 EventSystem과 취소 입력 연결 필요");
        if (_menuEventSystem.gameObject.scene != gameObject.scene ||
            _cancelInput.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 로비 입력 구성 필요");

        LobbyMenuView menu = GetPanel(LobbyPanelType.Menu).ViewRoot.GetComponent<LobbyMenuView>();
        SettingsView settings = GetPanel(LobbyPanelType.Settings).ViewRoot.GetComponent<SettingsView>();
        if (menu == null || settings == null)
            throw new MissingReferenceException($"{name}: 메뉴와 설정 패널 View 연결 필요");

        settings.Bind(SettingsService.Instance, _menuEventSystem);
        _menuView = menu;
        _settingsView = settings;
        _menuView.OnContinueRequested += OpenHome;
        _menuView.OnSettingsRequested += OpenSettings;
        _settingsView.OnBackRequested += OpenMenu;

        _cancelInput.Bind(CanHandleMenuInput, HandleMenuCancel);
        _cancelInputBound = true;
    }

    void DisconnectSceneMenu()
    {
        if (_cancelInputBound)
        {
            _cancelInputBound = false;
            if (_cancelInput != null)
                _cancelInput.Unbind();
        }

        if (_menuView != null)
        {
            _menuView.OnContinueRequested -= OpenHome;
            _menuView.OnSettingsRequested -= OpenSettings;
        }

        if (_settingsView != null)
        {
            _settingsView.OnBackRequested -= OpenMenu;
            _settingsView.Unbind();
        }

        _menuView = null;
        _settingsView = null;
    }

    bool CanHandleMenuInput()
    {
        return _subscribed && !_isTransitioning;
    }

    void OpenMenu()
    {
        OpenPanel(LobbyPanelType.Menu);
    }

    void OpenSettings()
    {
        OpenPanel(LobbyPanelType.Settings);
    }

    void HandleMenuCancel()
    {
        if (IsCurrentPanel(LobbyPanelType.Settings) && _settingsView.TryHandleBack()) return;

        if (IsCurrentPanel(LobbyPanelType.Home) || IsCurrentPanel(LobbyPanelType.Settings))
        {
            OpenMenu();
            return;
        }

        OpenHome();
    }

    void Subscribe()
    {
        if (_subscribed)
            return;

        if (_lobby.Characters == null || _lobby.Characters.Count == 0)
            throw new InvalidOperationException($"{name}: 로비 캐릭터 초기화 이후 연결 필요");

        LobbyPanel homePanel = GetPanel(LobbyPanelType.Home);

        _currentPanel = homePanel;
        _returnPanel = homePanel; ;

        _afterMove = null;
        _isTransitioning = false;

        foreach(LobbyPanel panel in _panels)
        {
            panel.SetVisible(false);
        }

        homePanel.SetVisible(true);
        _cameraDirector.SnapToMainView();

        _lobby.OnChanged += Refresh;
        _startButton.onClick.AddListener(HandleStartClicked);

        _difficultyView.SelectionRequested += HandleDifficultySelectionRequested;

        _characterView.CloseRequested += OpenHome;

        _storyView.CloseRequested += OpenHome;

        if (_metaUpgradeUI != null)
        {
            _metaUpgradeUI.OnVisibilityChanged += HandleMetaVisibilityChanged;
        }

        _cameraDirector.OnMoveCompleted += CompleteTransition;

        _subscribed = true;
        try
        {
            _statusPresenter.Activate();

            ConnectSceneMenu();
            Refresh();
        }
        catch (Exception bindingError)
        {
            try
            {
                Unsubscribe();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("로비 메뉴 연결 및 정리 실패", bindingError, cleanupError);
            }

            throw;
        }
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        // 화면 닫기 중 발생하는 전환 요청 차단
        _subscribed = false;
        DisconnectSceneMenu();

        _characterPresenter.Deactivate();

        if (_lobby != null)
        {
            _lobby.OnChanged -= Refresh;
        }            

        if (_cameraDirector != null)
        {
            _cameraDirector.OnMoveCompleted -= CompleteTransition;
            _cameraDirector.CancelMove();
        }

        _afterMove = null;
        _isTransitioning = false;

        if (_startButton != null)
        {
            _startButton.onClick.RemoveListener(HandleStartClicked);
            _startButton.interactable = false;
        }

        if (_difficultyView != null)
        {
            _difficultyView.SelectionRequested -= HandleDifficultySelectionRequested;

            _difficultyView.SetInteraction(false);
        }

        if (_characterView != null)
        {
            _characterView.CloseRequested -= OpenHome;
        }

        if (_storyView != null)
        {
            _storyView.CloseRequested -= OpenHome;

            if (_storyPresenter != null)
                _storyPresenter.Clear();
        }            

        if (_metaUpgradeUI != null)
        {
            _metaUpgradeUI.OnVisibilityChanged -= HandleMetaVisibilityChanged;
        }

        foreach (LobbyPanel panel in _panels)
        {
            // 씬 해제 중 먼저 파괴된 화면 제외
            if (panel.ViewRoot != null)
                panel.SetVisible(false);
        }

        if (_statusPresenter != null)
            _statusPresenter.Deactivate();

        _currentPanel = null;
        _returnPanel = null;
    }

    async void HandleStartClicked()
    {
        if (!CanUseStart)
            return;

        try
        {
            bool started = await _lobby.TryStartRunAsync();

            if (!started && _subscribed)
            {
                Refresh();

                if (!string.IsNullOrEmpty(_lobby.MetaSaveError))
                    Debug.LogWarning(_lobby.MetaSaveError, this);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);

            if (_subscribed)
                Refresh();
        }
    }

    void HandleSelectionRequested(int direction)
    {
        if (!_subscribed || _isTransitioning || !IsCurrentPanel(LobbyPanelType.Character) || !_lobby.CanChangeSelection)
            return;

        IReadOnlyList<LobbyCharacterDefinition> characters = _lobby.Characters;

        if (characters.Count <= 1)
            return;

        int currentIndex = -1;

        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] == _lobby.SelectedCharacter)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
            throw new InvalidOperationException($"{name}: 선택 캐릭터가 로비 목록에 포함되어야 함");

        int nextIndex = (currentIndex + direction + characters.Count) % characters.Count;

        if (!_lobby.TrySelectCharacter(characters[nextIndex].CharacterId))
            Refresh();
    }

    void HandleDifficultySelectionRequested(Difficulty difficulty)
    {
        if (!CanUseHomeSelection)
            return;

        if (!_lobby.TrySelectDifficulty(difficulty))
            Refresh();
    }

    void HandleMetaVisibilityChanged(bool isOpen)
    {
        if (!_subscribed || _isTransitioning || isOpen || !IsCurrentPanel(LobbyPanelType.MetaUpgrade))
            return;

        // 강화 화면 자체의 닫기 검사를 통과한 뒤 이전 화면으로 복귀
        LobbyPanel returnPanel = _returnPanel;

        _cameraDirector.CancelMove();
        CompleteTransition();

        OpenPanel(returnPanel);
    }

    void CompleteTransition()
    {
        if (!_subscribed || !_isTransitioning)
            return;

        Action afterMove = _afterMove;
        _afterMove = null;

        try
        {
            afterMove?.Invoke();
        }
        finally
        {
            _isTransitioning = false;
            Refresh();
        }
    }

    void Refresh()
    {
        if (!_subscribed)
            return;

        // 카메라와 패널 전환 상태만 Presenter에 전달
        _characterPresenter.SetInteractionEnabled(!_isTransitioning && IsCurrentPanel(LobbyPanelType.Character));

        _storyView.SetInteraction(!_isTransitioning && IsCurrentPanel(LobbyPanelType.Story));

        bool showHudControls = !_storyView.gameObject.activeSelf;
        _startButtonRoot.SetActive(showHudControls);
        _difficultyView.gameObject.SetActive(showHudControls);

        _difficultyView.SetInteraction(CanUseHomeSelection);
        _difficultyView.Show(_lobby.SelectedDifficulty);

        _startButton.interactable = CanUseStart;
    }

    /// <summary>
    /// 필요한 저장을 완료한 뒤 패널과 카메라 전환
    /// </summary>
    /// <param name="panel">등록된 전환 대상 패널</param>
    public async void OpenPanel(LobbyPanel panel)
    {
        if (panel == null)
            throw new MissingReferenceException("전환할 패널 필요");

        if (Array.IndexOf(_panels, panel) < 0)
            throw new InvalidOperationException("등록된 패널만 전환 가능");

        if (!_subscribed || _isTransitioning || panel == _currentPanel)
            return;

        bool needsLobbySelection = panel.Type == LobbyPanelType.Character || panel.Type == LobbyPanelType.Story;

        if (needsLobbySelection && !_lobby.CanChangeSelection)
            return;

        LobbyPanel previous = _currentPanel;

        _isTransitioning = true;
        _afterMove = null;
        Refresh();

        try
        {
            if (previous != null && previous.Type == LobbyPanelType.MetaUpgrade && _metaUpgradeUI.IsOpen)
            {
                bool closed = await _metaUpgradeUI.TryCloseAsync();

                if (!_subscribed)
                    return;

                if (!closed)
                {
                    _isTransitioning = false;
                    Refresh();
                    return;
                }
            }

            if (panel.Type == LobbyPanelType.Story)
            {
                int? masteryLevel = null;
                long currentExperience = 0;
                long requiredExperience = 0;
                if (_lobby.IsMetaReady && !_lobby.IsCharacterTestMode)
                {
                    _lobby.GetSelectedCharacterMasteryProgress(
                        out int level, out currentExperience, out requiredExperience);
                    masteryLevel = level;
                }

                _storyPresenter.Show(
                    _lobby.SelectedCharacter, masteryLevel, currentExperience, requiredExperience,
                    _lobby.IsCharacterTestMode ? null : MetaProgressionInitializer.ActiveStoryUnlockStore);
            }

            _currentPanel = panel;
            _returnPanel = previous;

            LobbyPanel closingStoryPanel = null;

            if (previous != null && previous.Type == LobbyPanelType.Story)
            {
                _storyView.BeginClosing();
                _storyPresenter.Clear();

                closingStoryPanel = previous;
                _afterMove = () => previous.SetVisible(false);
            }

            _characterPresenter.Deactivate();

            foreach (LobbyPanel other in _panels)
            {
                if (other != panel && other != closingStoryPanel)
                    other.SetVisible(false);
            }

            if (panel.Type == LobbyPanelType.Character)
                _characterPresenter.Activate();

            panel.SetVisible(true);
            Refresh();

            if (panel.CameraTarget != null)
                _cameraDirector.MoveTo(panel.CameraTarget);
            else
                CompleteTransition();
        }
        catch (Exception exception)
        {
            _afterMove = null;
            _isTransitioning = false;

            Debug.LogException(exception, this);

            if (_subscribed)
                Refresh();
        }
    }

    /// <summary>
    /// 화면 종류에 해당하는 등록 패널로 전환
    /// </summary>
    /// <param name="type">전환할 화면 종류</param>
    public void OpenPanel(LobbyPanelType type)
    {
        OpenPanel(GetPanel(type));
    }

    /// <summary>
    /// 지정 화면으로 전환하거나 현재 화면이면 Home으로 복귀
    /// </summary>
    /// <param name="type">LobbyPanelType에 정의된 화면 종류 번호</param>
    public void OpenPanelByIndex(int type)
    {
        if (!Enum.IsDefined(typeof(LobbyPanelType), type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "정의된 로비 화면 종류 번호 필요");

        LobbyPanelType targetType = (LobbyPanelType)type;

        if (IsCurrentPanel(targetType))
            targetType = LobbyPanelType.Home;

        OpenPanel(targetType);
    }

    /// <summary>
    /// 기본 로비 화면으로 복귀
    /// </summary>
    public void OpenHome()
    {
        OpenPanel(LobbyPanelType.Home);
    }
}
