using System;
using UnityEngine;

/// <summary>
/// 중도 포기 버튼과 정상 런 종료 요청 연결
/// </summary>
[DisallowMultipleComponent]
public class RunExitUIController : MonoBehaviour
{
    [Header("----- 런 종료 담당 -----")]
    [SerializeField] GameFlowController _gameFlow;

    [Header("----- 중도 포기 버튼 -----")]
    [SerializeField] UnityEngine.UI.Button _exitButton;

    bool _subscribed;

    bool CanRequestExit => _subscribed && _gameFlow.IsRunning && !_gameFlow.IsEndRequested;

    void Awake()
    {
        if (_gameFlow == null || _exitButton == null)
            throw new MissingReferenceException($"{name}: GameFlowController와 중도 포기 버튼 연결 필요");

        if (_gameFlow.gameObject.scene != gameObject.scene || _exitButton.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 GameFlowController와 버튼 연결 필요");

        _exitButton.interactable = false;
    }

    void OnEnable()
    {
        _gameFlow.OnRunStarted += HandleRunStarted;
        _gameFlow.OnRunEnded += HandleRunEnded;
        _exitButton.onClick.AddListener(HandleExitClicked);

        _subscribed = true;
        Refresh();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _subscribed = false;

        if (_gameFlow != null)
        {
            _gameFlow.OnRunStarted -= HandleRunStarted;
            _gameFlow.OnRunEnded -= HandleRunEnded;
        }

        if (_exitButton != null)
        {
            _exitButton.onClick.RemoveListener(HandleExitClicked);
            _exitButton.interactable = false;
        }
    }

    void HandleRunStarted(RunLevelProgression progression)
    {
        Refresh();
    }

    void HandleRunEnded(
        RunEndReason reason,
        RunLevelProgression progression)
    {
        Refresh();
    }

    void HandleExitClicked()
    {
        if (!CanRequestExit)
        {
            Refresh();
            return;
        }

        _exitButton.interactable = false;

        try
        {
            _gameFlow.RequestEndRun(RunEndReason.Manual);
        }
        finally
        {
            Refresh();
        }
    }

    void Refresh()
    {
        _exitButton.interactable = CanRequestExit;
    }
}