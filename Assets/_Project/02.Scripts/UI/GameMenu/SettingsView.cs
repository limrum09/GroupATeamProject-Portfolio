using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// 로비와 세션에서 공유하는 옵션 편집 화면
/// </summary>
[DisallowMultipleComponent]
public class SettingsView : MonoBehaviour, ILobbyView
{
    [SerializeField] Slider _masterVolumeSlider;
    [SerializeField] Slider _musicVolumeSlider;
    [SerializeField] Slider _uiVolumeSlider;
    [SerializeField] Slider _effectsVolumeSlider;
    [SerializeField] Slider _ambientVolumeSlider;
    [SerializeField] Button _applyButton;
    [SerializeField] Button _backButton;
    [SerializeField] TMP_Text _statusText;

    [SerializeField] Button[] _keyButtons;
    [SerializeField] TMP_Text[] _keyLabels;
    [SerializeField] TMP_Text[] _keyDescriptions;
    [SerializeField] GameObject[] _tabContents;
    [SerializeField] Button[] _tabs;
    [SerializeField] Button _resetButton;
    [SerializeField] RectTransform _bindingPopup;
    [SerializeField] TMP_Text _candidateLabel;
    [SerializeField] TMP_Text _bindingNotice;
    [SerializeField] Button _confirmBinding;
    [SerializeField] Button _cancelBinding;

    [SerializeField] CanvasGroup _settingsContent;
    [SerializeField] GameObject _saveConfirmation;
    [SerializeField] TMP_Text _saveConfirmationText;
    [SerializeField] Button _saveConfirmationApply;
    [SerializeField] Button _saveConfirmationDiscard;

    GameObject _selectionBeforeSaveConfirmation;
    [SerializeField] Slider _cursorScaleSlider;
    [SerializeField] Slider _cursorHueSlider;
    [SerializeField] Image _cursorColorPreview;
    [SerializeField] TMP_Dropdown _combatTextOption;
    [SerializeField] TMP_Dropdown[] _graphicsOptions;

    UnityAction<int>[] _graphicsHandlers;
    List<Vector2Int> _graphicsResolutions;
    float _statusExpiresAt;
    UnityAction[] _tabHandlers;
    UnityAction[] _keyHandlers;
    int _activeTab;
    int _editingRow = -1;
    int _openedFrame;
    string _candidate;

    SettingsService _settings;
    EventSystem _eventSystem;
    SettingsData _draft;

    public event Action OnBackRequested;

    public bool IsOpen => gameObject.activeSelf;

    void OnDestroy()
    {
        ReleaseBindings();
    }

    void ShowTab(int tab)
    {
        _activeTab = tab;
        _resetButton.interactable = _draft != null;

        for (int index = 0; index < _tabs.Length; index++)
        {
            bool selected = index == tab;
            _tabContents[index].SetActive(selected);
            _tabs[index].interactable = !selected;
        }

        _eventSystem.SetSelectedGameObject(null);
    }

    void RefreshApplyState()
    {
        bool incomplete = _draft != null &&
            Array.IndexOf(_draft.BindingPaths, SettingsBindings.Unassigned) >= 0;
        _applyButton.interactable = _draft != null && !incomplete;
        if (incomplete)
            ShowStatus("키가 지정되지 않은 동작이 있습니다.");
        else if (_statusText.text == "키가 지정되지 않은 동작이 있습니다.")
            ShowStatus(string.Empty);
    }

    void SelectCursorScale(float value)
    {
        _draft.CursorScale = value;
    }

    void SelectCursorHue(float value)
    {
        _draft.CursorHue = value;
        _cursorColorPreview.color = GameplayCursor.GetColor(value);
    }

    void ShowGameplay()
    {
        bool ready = _draft != null;
        _cursorScaleSlider.interactable = ready;
        _cursorHueSlider.interactable = ready;
        _combatTextOption.interactable = ready;
        if (!ready) return;

        _cursorScaleSlider.SetValueWithoutNotify(_draft.CursorScale);
        _cursorHueSlider.SetValueWithoutNotify(_draft.CursorHue);
        _cursorColorPreview.color = GameplayCursor.GetColor(_draft.CursorHue);
        _combatTextOption.SetValueWithoutNotify((int)_draft.CombatText);
    }

    void SelectCombatText(int value)
    {
        _draft.CombatText = (CombatTextTarget)value;
    }

    void SelectGraphics(int row, int index)
    {
        if (_draft == null) return;

        switch (row)
        {
            case 0:
                Vector2Int resolution = _graphicsResolutions[index];
                _draft.ResolutionWidth = resolution.x;
                _draft.ResolutionHeight = resolution.y;
                break;
            case 1:
                _draft.ScreenMode = SettingsService.ScreenModes[index];
                RefreshGraphicsResolutions();
                break;
            case 2:
                _draft.FrameLimit = SettingsService.FrameLimits[index];
                break;
        }
    }

    void RefreshGraphicsResolutions()
    {
        _graphicsResolutions = SettingsService.GetResolutions(_draft.ScreenMode);
        List<string> labels = new List<string>(_graphicsResolutions.Count);
        foreach (Vector2Int resolution in _graphicsResolutions)
            labels.Add($"{resolution.x} × {resolution.y}");

        TMP_Dropdown dropdown = _graphicsOptions[0];
        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
        Vector2Int selected = SettingsService.ResolveResolution(_draft, _graphicsResolutions);
        dropdown.SetValueWithoutNotify(_graphicsResolutions.IndexOf(selected));
        dropdown.RefreshShownValue();
    }

    void RefreshGraphics()
    {
        foreach (TMP_Dropdown dropdown in _graphicsOptions)
            dropdown.interactable = _draft != null;

        if (_draft == null) return;

        RefreshGraphicsResolutions();
        _graphicsOptions[1].SetValueWithoutNotify(Array.IndexOf(SettingsService.ScreenModes, _draft.ScreenMode));
        _graphicsOptions[2].SetValueWithoutNotify(Array.IndexOf(SettingsService.FrameLimits, _draft.FrameLimit));
    }

    void ShowStatus(string message)
    {
        _statusText.text = message ?? string.Empty;
        _statusExpiresAt = string.IsNullOrEmpty(message) ? 0f : Time.unscaledTime + 2f;
    }

    void ShowAudio(SettingsData data)
    {
        _masterVolumeSlider.SetValueWithoutNotify(data.MasterVolume);
        _musicVolumeSlider.SetValueWithoutNotify(data.MusicVolume);
        _uiVolumeSlider.SetValueWithoutNotify(data.UIVolume);
        _effectsVolumeSlider.SetValueWithoutNotify(data.EffectsVolume);
        _ambientVolumeSlider.SetValueWithoutNotify(data.AmbientVolume);
    }

    void ReadAudio()
    {
        _draft.MasterVolume = _masterVolumeSlider.value;
        _draft.MusicVolume = _musicVolumeSlider.value;
        _draft.UIVolume = _uiVolumeSlider.value;
        _draft.EffectsVolume = _effectsVolumeSlider.value;
        _draft.AmbientVolume = _ambientVolumeSlider.value;
    }

    void ResetTab()
    {
        if (_draft == null) return;

        if (_activeTab == 0)
        {
            SettingsData defaults = new SettingsData();
            _draft.CursorScale = defaults.CursorScale;
            _draft.CursorHue = defaults.CursorHue;
            _draft.CombatText = defaults.CombatText;
            ShowGameplay();
        }
        else if (_activeTab == 1)
        {
            Array.Clear(_draft.BindingPaths, 0, _draft.BindingPaths.Length);
            RefreshBindings();
        }
        else if (_activeTab == 2)
        {
            ShowAudio(new SettingsData());
            ReadAudio();
        }

        if (_activeTab == 3)
        {
            SettingsData defaults = new SettingsData();
            _draft.ResolutionWidth = defaults.ResolutionWidth;
            _draft.ResolutionHeight = defaults.ResolutionHeight;
            _draft.ScreenMode = defaults.ScreenMode;
            _draft.FrameLimit = defaults.FrameLimit;
            RefreshGraphics();
        }

        SettingsData saved = _settings.CreateDraft();
        bool differsFromSaved = _activeTab == 0 && !_draft.HasSameGameplay(saved);
        if (_activeTab == 1)
        {
            for (int row = 0; row < _draft.BindingPaths.Length; row++)
            {
                if (string.Equals(SettingsBindings.EffectivePath(_draft.BindingPaths, row),
                    SettingsBindings.EffectivePath(saved.BindingPaths, row),
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                differsFromSaved = true;
                break;
            }
        }
        else if (_activeTab == 2)
        {
            differsFromSaved = !_draft.HasSameAudio(saved);
        }

        else if (_activeTab == 3)
        {
            differsFromSaved = !_draft.HasSameGraphics(saved);
        }

        ShowStatus(differsFromSaved
            ? "기본값으로 초기화했습니다. 적용을 눌러 저장하세요."
            : string.Empty);
        RefreshApplyState();
    }

    void OpenBinding(int row)
    {
        if (_draft == null || _editingRow >= 0) return;

        _editingRow = row;
        _openedFrame = Time.frameCount;
        _candidate = null;
        _candidateLabel.text = string.Empty;
        _bindingNotice.text = "변경할 키를 입력하세요.";
        _confirmBinding.interactable = false;
        _tabContents[_activeTab].SetActive(false);
        _masterVolumeSlider.interactable = false;
        _applyButton.interactable = false;
        _backButton.interactable = false;
        _resetButton.interactable = false;
        foreach (Button tab in _tabs) tab.interactable = false;
        _bindingPopup.gameObject.SetActive(true);
        _eventSystem.SetSelectedGameObject(null);
    }

    void Update()
    {
        if (_statusExpiresAt > 0f && Time.unscaledTime >= _statusExpiresAt)
            ShowStatus(string.Empty);

        if (_editingRow < 0 || Time.frameCount <= _openedFrame) return;

        string path = ReadBinding();
        if (path == null) return;

        bool allowed = SettingsBindings.IsAllowed(path);
        int conflict = SettingsBindings.FindConflict(_draft.BindingPaths, _editingRow, path);
        _candidate = allowed ? path : null;
        _confirmBinding.interactable = allowed;
        _candidateLabel.text = FormatBindingLabel(path);
        _bindingNotice.text = !allowed ? "지원하지 않는 키입니다."
            : conflict < 0 ? string.Empty
            : $"이미 {_keyDescriptions[conflict].text}에 지정된 키입니다. 적용 시 기존 연결이 해제됩니다.";
    }

    static string ReadBinding()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            foreach (KeyControl key in keyboard.allKeys)
            {
                if (key.keyCode != Key.Escape && key.wasPressedThisFrame)
                    return $"<Keyboard>/{key.name}";
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return null;

        if (mouse.rightButton.wasPressedThisFrame) return "<Mouse>/rightButton";
        if (mouse.middleButton.wasPressedThisFrame) return "<Mouse>/middleButton";
        if (mouse.backButton.wasPressedThisFrame) return "<Mouse>/backButton";
        if (mouse.forwardButton.wasPressedThisFrame) return "<Mouse>/forwardButton";
        return null;
    }

    void ConfirmBinding()
    {
        if (_editingRow < 0 || _candidate == null) return;

        int conflict = SettingsBindings.FindConflict(_draft.BindingPaths, _editingRow, _candidate);
        if (conflict >= 0)
            _draft.BindingPaths[conflict] = SettingsBindings.Unassigned;

        _draft.BindingPaths[_editingRow] = _candidate;
        RefreshBindings();
        TryCancelBinding();
    }

    static string FormatBindingLabel(string path)
    {
        switch (path)
        {
            case "<Mouse>/rightButton": return "RMB";
            case "<Mouse>/leftButton": return "LMB";
            default:
                return InputControlPath.ToHumanReadableString(path,
                    InputControlPath.HumanReadableStringOptions.OmitDevice);
        }
    }

    void RefreshBindings()
    {
        for (int row = 0; row < _keyButtons.Length; row++)
        {
            _keyButtons[row].interactable = _draft != null;
            string path = _draft == null ? SettingsBindings.Defaults[row]
                : SettingsBindings.EffectivePath(_draft.BindingPaths, row);
            _keyLabels[row].text = path == SettingsBindings.Unassigned
                ? "미지정" : FormatBindingLabel(path);
        }
    }

    /// <summary>
    /// 열린 키 지정 팝업 우선 취소
    /// </summary>
    /// <returns>취소 입력 소비 여부</returns>
    public bool TryCancelBinding()
    {
        if (_editingRow < 0) return false;

        int row = _editingRow;
        _editingRow = -1;
        _candidate = null;
        _bindingPopup.gameObject.SetActive(false);
        ShowTab(_activeTab);
        _masterVolumeSlider.interactable = true;
        RefreshApplyState();
        _backButton.interactable = true;
        _eventSystem.SetSelectedGameObject(_keyButtons[row].gameObject);
        return true;
    }

    void CancelBinding()
    {
        TryCancelBinding();
    }

    void ReleaseBindings()
    {
        if (_applyButton != null)
            _applyButton.onClick.RemoveListener(Apply);
        if (_backButton != null)
            _backButton.onClick.RemoveListener(RequestBack);

        if (_settings != null)
        {
            _saveConfirmationApply.onClick.RemoveListener(ApplyAndGoBack);
            _saveConfirmationDiscard.onClick.RemoveListener(DiscardAndGoBack);
            _resetButton.onClick.RemoveListener(ResetTab);
            _combatTextOption.onValueChanged.RemoveListener(SelectCombatText);
            _cursorScaleSlider.onValueChanged.RemoveListener(SelectCursorScale);
            _cursorHueSlider.onValueChanged.RemoveListener(SelectCursorHue);
            for (int row = 0; row < _graphicsOptions.Length; row++)
                _graphicsOptions[row].onValueChanged.RemoveListener(_graphicsHandlers[row]);
            _confirmBinding.onClick.RemoveListener(ConfirmBinding);
            _cancelBinding.onClick.RemoveListener(CancelBinding);

            for (int tab = 0; tab < _tabs.Length; tab++)
                _tabs[tab].onClick.RemoveListener(_tabHandlers[tab]);
            for (int row = 0; row < _keyButtons.Length; row++)
                _keyButtons[row].onClick.RemoveListener(_keyHandlers[row]);
        }

        _settings = null;
        _eventSystem = null;
        _draft = null;
    }

    void Apply()
    {
        if (_draft == null) return;

        ReadAudio();
        ShowStatus(_settings.TryApply(_draft, out string error)
            ? "저장했습니다."
            : error);
    }

    bool HasUnsavedChanges()
    {
        if (_draft == null) return false;

        SettingsData saved = _settings.CreateDraft();
        ReadAudio();
        if (!_draft.HasSameGameplay(saved) ||
            !_draft.HasSameAudio(saved) || !_draft.HasSameGraphics(saved)) return true;

        for (int row = 0; row < _draft.BindingPaths.Length; row++)
        {
            if (!string.Equals(SettingsBindings.EffectivePath(_draft.BindingPaths, row),
                SettingsBindings.EffectivePath(saved.BindingPaths, row),
                StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    void RequestBack()
    {
        if (TryCancelBinding()) return;

        if (_combatTextOption.IsExpanded)
        {
            _combatTextOption.Hide();
            return;
        }

        foreach (TMP_Dropdown dropdown in _graphicsOptions)
        {
            if (!dropdown.IsExpanded) continue;

            dropdown.Hide();
            return;
        }

        if (_saveConfirmation.activeSelf)
        {
            CloseSaveConfirmation();
            return;
        }

        if (!HasUnsavedChanges())
        {
            OnBackRequested?.Invoke();
            return;
        }

        _selectionBeforeSaveConfirmation = _eventSystem.currentSelectedGameObject;
        _saveConfirmationText.text = "변경된 설정이 존재합니다. 저장하시겠습니까?";
        _settingsContent.interactable = false;
        _settingsContent.blocksRaycasts = false;
        _saveConfirmation.SetActive(true);
        _eventSystem.SetSelectedGameObject(_saveConfirmationApply.gameObject);
    }

    void CloseSaveConfirmation()
    {
        if (!_saveConfirmation.activeSelf) return;

        _saveConfirmation.SetActive(false);
        _settingsContent.interactable = true;
        _settingsContent.blocksRaycasts = true;
        _eventSystem.SetSelectedGameObject(_selectionBeforeSaveConfirmation);
        _selectionBeforeSaveConfirmation = null;
    }

    void DiscardAndGoBack()
    {
        CloseSaveConfirmation();
        OnBackRequested?.Invoke();
    }

    void ApplyAndGoBack()
    {
        ReadAudio();
        if (!_settings.TryApply(_draft, out string error))
        {
            _saveConfirmationText.text = error;
            return;
        }

        CloseSaveConfirmation();
        OnBackRequested?.Invoke();
    }

    /// <summary>
    /// 설정 화면의 취소 입력과 저장 확인 처리
    /// </summary>
    /// <returns>취소 입력 소비 여부</returns>
    public bool TryHandleBack()
    {
        if (!IsOpen) return false;

        RequestBack();
        return true;
    }

    /// <summary>
    /// 공유 옵션과 현재 씬의 선택 담당자 연결
    /// </summary>
    /// <param name="settings">앱 수명의 옵션 담당자</param>
    /// <param name="eventSystem">현재 씬의 EventSystem</param>
    public void Bind(SettingsService settings, EventSystem eventSystem)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (eventSystem == null) throw new ArgumentNullException(nameof(eventSystem));
        if (_settings != null) throw new InvalidOperationException("이전 설정 화면 연결 해제 필요");

        if (_settingsContent == null || _saveConfirmation == null || _saveConfirmationText == null ||
            _saveConfirmationApply == null || _saveConfirmationDiscard == null || _masterVolumeSlider == null ||
            _musicVolumeSlider == null || _uiVolumeSlider == null || _effectsVolumeSlider == null ||
            _ambientVolumeSlider == null || _applyButton == null ||
            _bindingPopup == null || _candidateLabel == null || _bindingNotice == null ||
            _resetButton == null || _confirmBinding == null || _cancelBinding == null ||
            _tabs == null || _tabs.Length != 4 || Array.Exists(_tabs, button => button == null) ||
            _tabContents == null || _tabContents.Length != _tabs.Length ||
            Array.Exists(_tabContents, content => content == null) ||
            _cursorScaleSlider == null || _cursorHueSlider == null || _cursorColorPreview == null ||
            _combatTextOption == null || _combatTextOption.template == null ||
            _graphicsOptions == null || _graphicsOptions.Length != 3 ||
            Array.Exists(_graphicsOptions, dropdown => dropdown == null || dropdown.template == null) ||
            _keyButtons == null || _keyButtons.Length != SettingsBindings.Names.Length ||
            Array.Exists(_keyButtons, button => button == null) ||
            _keyLabels == null || _keyLabels.Length != _keyButtons.Length ||
            Array.Exists(_keyLabels, label => label == null) ||
            _keyDescriptions == null || _keyDescriptions.Length != _keyButtons.Length ||
            Array.Exists(_keyDescriptions, label => label == null) ||
            _backButton == null || _statusText == null)
            throw new MissingReferenceException($"{name}: 옵션 화면 참조 연결 필요");

        if (!eventSystem.isActiveAndEnabled || eventSystem.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 활성 EventSystem 필요");

        _bindingPopup.gameObject.SetActive(false);
        settings.Initialize();
        _settings = settings;
        _eventSystem = eventSystem;
        _applyButton.onClick.AddListener(Apply);
        _backButton.onClick.AddListener(RequestBack);
        _saveConfirmationApply.onClick.AddListener(ApplyAndGoBack);
        _saveConfirmationDiscard.onClick.AddListener(DiscardAndGoBack);
        _resetButton.onClick.AddListener(ResetTab);
        _combatTextOption.onValueChanged.AddListener(SelectCombatText);
        _cursorScaleSlider.onValueChanged.AddListener(SelectCursorScale);
        _cursorHueSlider.onValueChanged.AddListener(SelectCursorHue);
        _graphicsHandlers = new UnityAction<int>[_graphicsOptions.Length];
        for (int row = 0; row < _graphicsOptions.Length; row++)
        {
            int index = row;
            _graphicsHandlers[row] = value => SelectGraphics(index, value);
            _graphicsOptions[row].onValueChanged.AddListener(_graphicsHandlers[row]);
        }

        _confirmBinding.onClick.AddListener(ConfirmBinding);
        _cancelBinding.onClick.AddListener(CancelBinding);
        _tabHandlers = new UnityAction[_tabs.Length];
        _keyHandlers = new UnityAction[_keyButtons.Length];

        for (int tab = 0; tab < _tabs.Length; tab++)
        {
            int index = tab;
            _tabHandlers[tab] = () => ShowTab(index);
            _tabs[tab].onClick.AddListener(_tabHandlers[tab]);
        }

        for (int row = 0; row < _keyButtons.Length; row++)
        {
            int index = row;
            _keyHandlers[row] = () => OpenBinding(index);
            _keyButtons[row].onClick.AddListener(_keyHandlers[row]);
        }
    }

    /// <summary>
    /// 현재 적용값의 편집본 표시
    /// </summary>
    public void Open()
    {
        if (_settings == null) throw new InvalidOperationException("설정 화면 연결 필요");

        gameObject.SetActive(true);
        bool ready = _settings.IsReady;
        _masterVolumeSlider.interactable = ready;
        _musicVolumeSlider.interactable = ready;
        _uiVolumeSlider.interactable = ready;
        _effectsVolumeSlider.interactable = ready;
        _ambientVolumeSlider.interactable = ready;
        _applyButton.interactable = ready;
        _draft = ready ? _settings.CreateDraft() : null;
        RefreshBindings();
        RefreshGraphics();
        ShowGameplay();
        ShowStatus(ready ? string.Empty : _settings.ErrorMessage);

        if (ready)
            ShowAudio(_draft);

        ShowTab(0);
        _eventSystem.SetSelectedGameObject(_backButton.gameObject);
    }

    /// <summary>
    /// 미적용 편집본 폐기와 화면 숨김
    /// </summary>
    public void Close()
    {
        if (_settings != null) CloseSaveConfirmation();
        TryCancelBinding();
        _draft = null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 설정 화면의 씬 연결 해제
    /// </summary>
    public void Unbind()
    {
        Close();
        ReleaseBindings();
    }
}
