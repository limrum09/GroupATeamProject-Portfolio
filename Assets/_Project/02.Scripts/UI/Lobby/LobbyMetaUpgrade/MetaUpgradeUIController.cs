using System;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// 메타 화면 개폐와 Presenter 수명 관리
/// </summary>
[DisallowMultipleComponent]
public class MetaUpgradeUIController : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] MetaUpgradeView _view;

    [Header("----- 뒤쪽 로비 입력 차단 -----")]
    [SerializeField] CanvasGroup _lobbyControls;

    LobbyManager _lobby;
    MetaUpgradePresenter _presenter;

    bool _previousInteractable;
    bool _previousBlocksRaycasts;
    bool _closing;

    public event Action<bool> OnVisibilityChanged;

    public bool IsOpen => isActiveAndEnabled;

    void Start()
    {
        if (_lobby == null)
            throw new InvalidOperationException($"{name}: 화면 초기화 전에 LobbyManager Bind 필요");

        // 로비 연결과 View의 Awake 완료 이후 Presenter 생성
        _presenter = new MetaUpgradePresenter(_lobby, _view);
        _view.CloseRequested += RequestClose;
        _presenter.Activate();
    }

    void OnEnable()
    {
        _previousInteractable = _lobbyControls.interactable;
        _previousBlocksRaycasts = _lobbyControls.blocksRaycasts;

        _lobbyControls.interactable = false;
        _lobbyControls.blocksRaycasts = false;

        if (_presenter != null)
            _presenter.Activate();

        OnVisibilityChanged?.Invoke(true);
    }

    void OnDisable()
    {
        if (_presenter != null)
            _presenter.Deactivate();

        if (_lobbyControls != null)
        {
            _lobbyControls.interactable = _previousInteractable;
            _lobbyControls.blocksRaycasts = _previousBlocksRaycasts;
        }

        OnVisibilityChanged?.Invoke(false);
    }

    void OnDestroy()
    {
        if (_view != null)
            _view.CloseRequested -= RequestClose;

        if (_presenter != null)
        {
            _presenter.Dispose();
            _presenter = null;
        }
    }

    async void RequestClose()
    {
        await TryCloseAsync();
    }

#if UNITY_EDITOR
    [ContextMenu("메타 UI 연결 검사")]
    void ValidateInspectorBindings()
    {
        if (_lobby == null || _view == null || _lobbyControls == null)
            throw new MissingReferenceException($"{name}: LobbyManager, View, HUD CanvasGroup 연결 필요");

        _view.ValidateBindings();

        Debug.Log("메타 UI 연결 확인 완료", this);
    }
#endif

    /// <summary>
    /// 화면에서 사용할 로비 상태 연결
    /// </summary>
    /// <param name="lobby">부모 UI가 전달한 로비 상태와 요청 담당</param>
    public void Bind(LobbyManager lobby)
    {
        if (lobby == null)
            throw new ArgumentNullException(nameof(lobby));

        if (_lobby == lobby)
            return;

        if (_lobby != null || _presenter != null)
            throw new InvalidOperationException($"{name}: 다른 로비 대상으로 재연결 불가");

        _lobby = lobby;
    }

    /// <summary>
    /// 메타 강화 화면 열기
    /// </summary>
    public void Open()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 승인된 화면 전환 또는 수명 정리에서 사용하는 표시 종료
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 편집 결과 저장 이후 화면 종료
    /// </summary>
    /// <returns>화면 종료 완료 여부</returns>
    public async Task<bool> TryCloseAsync()
    {
        if (!IsOpen)
            return true;

        if (_closing || _presenter == null || !_presenter.CanClose)
            return false;

        _closing = true;

        try
        {
            if (!await _lobby.SaveMetaChangesAsync())
                return false;

            if (this == null || _lobby == null)
                return false;

            Close();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
        finally
        {
            _closing = false;
        }
    }
}