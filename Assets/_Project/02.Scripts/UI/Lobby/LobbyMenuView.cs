using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 메뉴 표시와 화면 전환 요청
/// </summary>
[DisallowMultipleComponent]
public class LobbyMenuView : MonoBehaviour, ILobbyView
{
    [SerializeField] Button _continueButton;
    [SerializeField] Button _settingsButton;

    public event Action OnContinueRequested;
    public event Action OnSettingsRequested;

    void Awake()
    {
        if (_continueButton == null || _settingsButton == null)
            throw new MissingReferenceException($"{name}: 로비 메뉴 버튼 연결 필요");

        _continueButton.onClick.AddListener(HandleContinue);
        _settingsButton.onClick.AddListener(HandleSettings);
    }

    void OnDestroy()
    {
        if (_continueButton != null)
            _continueButton.onClick.RemoveListener(HandleContinue);
        if (_settingsButton != null)
            _settingsButton.onClick.RemoveListener(HandleSettings);
    }

    void HandleContinue() => OnContinueRequested?.Invoke();
    void HandleSettings() => OnSettingsRequested?.Invoke();

    /// <summary>
    /// 로비 메뉴 표시와 최초 버튼 선택
    /// </summary>
    public void Open()
    {
        gameObject.SetActive(true);
        _continueButton.Select();
    }

    /// <summary>
    /// 로비 메뉴 숨김
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }
}
