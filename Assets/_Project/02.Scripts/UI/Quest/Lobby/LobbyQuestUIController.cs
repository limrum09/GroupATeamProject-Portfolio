using System;
using UnityEngine;

/// <summary>
/// 퀘스트 화면 개폐와 로비 연결 및 Presenter 수명 관리
/// </summary>
[DisallowMultipleComponent]
public class LobbyQuestUIController : MonoBehaviour, ILobbyView
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] LobbyQuestView _view;

    LobbyManager _lobby;
    LobbyQuestPresenter _presenter;

    void Awake()
    {
        if (_view == null)
            throw new MissingReferenceException($"{name}: LobbyQuestView 연결 필요");

        if (_view.gameObject != gameObject)
            throw new InvalidOperationException($"{name}: LobbyQuestView와 Controller를 같은 패널 루트에 배치 필요");
    }

    void Start()
    {
        if (_lobby == null)
            throw new InvalidOperationException($"{name}: 화면 초기화 전에 LobbyManager Bind 필요");

        // 부모 UI의 Bind와 View의 Awake 완료 이후 Presenter 생성
        _presenter = new LobbyQuestPresenter(_view, _lobby, QuestManager.Instance);
        _presenter.Activate();
    }

    void OnEnable()
    {
        // 최초 활성화는 Start에서 생성 이후 처리
        if (_presenter != null)
            _presenter.Activate();
    }

    void OnDisable()
    {
        if (_presenter != null)
            _presenter.Deactivate();
    }

    void OnDestroy()
    {
        if (_presenter != null)
        {
            _presenter.Dispose();
            _presenter = null;
        }
    }

    /// <summary>
    /// 퀘스트 화면에서 사용할 로비 상태 연결
    /// </summary>
    /// <param name="lobby">부모 UI가 전달한 로비 상태 조회 대상</param>
    public void Bind(LobbyManager lobby)
    {
        if (lobby == null)
            throw new ArgumentNullException(nameof(lobby));

        if (_lobby == lobby)
            return;

        if (_lobby != null || _presenter != null)
            throw new InvalidOperationException(
                $"{name}: 다른 로비 대상으로 재연결 불가");

        _lobby = lobby;
    }

    /// <summary>
    /// 퀘스트 화면 열기
    /// </summary>
    public void Open()
    {
        _view.SetVisible(true);
    }

    /// <summary>
    /// 퀘스트 화면 닫기
    /// </summary>
    public void Close()
    {
        _view.SetVisible(false);
    }
}