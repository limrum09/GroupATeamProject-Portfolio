using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 스킬바 화면의 연결, 개폐 및 Presenter 수명 관리
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class SkillBarUIController : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] SkillBarView _view;

    [Header("----- 선택 기능: 유료 스킬 강화 -----")]
    [SerializeField] bool _enableSkillUpgrades;
    [SerializeField] SessionSkillUpgradeService _upgrades;

    SkillLoadout _loadout;
    SessionSkillUpgradeService _boundUpgrades;
    SkillBarPresenter _presenter;
    bool _started;

    public bool IsBound => _loadout != null && _presenter != null;
    public bool IsOpen => gameObject.activeInHierarchy;

    void Awake()
    {
        ValidateView();
    }

    void Start()
    {
        // 모든 View의 Awake 이후 선택 기능 초기화
        if (_enableSkillUpgrades)
            _view.InitializeUpgradeBindings();

        _started = true;
        ActivatePresenterIfReady();
    }

    void OnEnable()
    {
        _view.OnSlotUpgradeRequested += HandleSlotUpgradeRequested;

        ActivatePresenterIfReady();
    }

    void OnDisable()
    {
        if (_view != null)
            _view.OnSlotUpgradeRequested -= HandleSlotUpgradeRequested;

        if (_presenter != null)
            _presenter.Deactivate();
    }

    void Update()
    {
        if (_presenter != null)
            _presenter.SetUpgradeMode(IsUpgradeHeld());
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && _presenter != null)
            _presenter.SetUpgradeMode(false);
    }

    bool IsUpgradeHeld()
    {
        Keyboard keyboard = Keyboard.current;

        return _enableSkillUpgrades && Application.isFocused && keyboard != null && keyboard.leftAltKey.isPressed;
    }

    void HandleSlotUpgradeRequested(int slot)
    {
        if (_presenter == null)
            return;

        // 키 해제와 같은 프레임에 전달된 클릭 재검사
        if (!IsUpgradeHeld())
        {
            _presenter.SetUpgradeMode(false);
            return;
        }

        _presenter.RequestUpgrade(slot);
    }

    void LateUpdate()
    {
        if (_presenter == null)
            return;

        // 연결된 캐릭터 제거 시 해당 화면 종료
        if (_loadout == null)
        {
            Unbind();
            Close();
            return;
        }

        _presenter.Tick();
    }

    void OnDestroy()
    {
        Unbind();
    }

    void ValidateView()
    {
        if (_view == null)
            throw new MissingReferenceException($"{name}: SkillBarView 연결 필요");

        if (_view.gameObject != gameObject || !_view.enabled)
            throw new InvalidOperationException($"{name}: 같은 화면 루트의 활성 SkillBarView 연결 필요");

        if (_enableSkillUpgrades && _upgrades == null)
            throw new MissingReferenceException($"{name}: 강화 기능을 사용할 SessionSkillUpgradeService 연결 필요");
    }

    void ActivatePresenterIfReady()
    {
        if (!_started || !isActiveAndEnabled || _presenter == null)
            return;

        if (_loadout == null)
        {
            Unbind();
            Close();
            return;
        }

        _presenter.Activate();
        _presenter.Tick();
    }

    /// <summary>
    /// 현재 런의 로드아웃과 선택적 강화 서비스를 스킬바에 연결
    /// </summary>
    /// <param name="loadout">표시할 플레이어의 스킬 로드아웃</param>
    /// <param name="economy">유료 강화에 사용할 세션 지갑</param>
    public void Bind(SkillLoadout loadout, SessionEconomyManager economy)
    {
        if (loadout == null)
            throw new ArgumentNullException(nameof(loadout));

        ValidateView();

        SessionSkillUpgradeService upgrades = _enableSkillUpgrades ? _upgrades : null;

        if (upgrades != null && economy == null)
            throw new ArgumentNullException(nameof(economy));

        if (_presenter != null && _loadout == loadout && _boundUpgrades == upgrades && (upgrades == null || upgrades.Economy == economy))
            return;

        Unbind();

        if (upgrades != null)
        {
            upgrades.Bind(loadout, economy);
            _boundUpgrades = upgrades;
        }

        try
        {
            _loadout = loadout;
            _presenter = new SkillBarPresenter(loadout, _view, upgrades);
            ActivatePresenterIfReady();
        }
        catch
        {
            Unbind();
            throw;
        }
    }

    /// <summary>
    /// Presenter 구독과 현재 런의 강화 서비스 연결 해제
    /// </summary>
    public void Unbind()
    {
        SkillBarPresenter presenter = _presenter;
        SessionSkillUpgradeService upgrades = _boundUpgrades;

        _presenter = null;
        _loadout = null;
        _boundUpgrades = null;

        try
        {
            if (presenter != null)
                presenter.Dispose();
        }
        finally
        {
            if (upgrades != null)
                upgrades.Unbind();
        }
    }

    /// <summary>연결된 스킬바 화면 열기</summary>
    public void Open()
    {
        if (!IsBound)
            throw new MissingReferenceException($"{name}: 표시할 SkillLoadout 연결 필요");

        gameObject.SetActive(true);
    }

    /// <summary>스킬바 화면 닫기</summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }
}