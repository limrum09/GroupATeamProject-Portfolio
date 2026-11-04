using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Runtime.ExceptionServices;

/// <summary>
/// 플레이어 씬 의존성과 세션 기능 및 보상과 UI의 연결 및 해제 조율
/// </summary>
[DisallowMultipleComponent]
public class SessionInitializer : MonoBehaviour
{
    [Header("----- 세션 UI -----")]
    [SerializeField] SessionUIController _ui;

    [Header("----- 플레이어 씬 연결 -----")]
    [SerializeField] Camera _inputCamera;
    [SerializeField] EventSystem _eventSystem;
    [SerializeField] CombatEffect _effectPlayer;

    [Header("----- 월드와 적 -----")]
    [SerializeField] SessionProgressManager _world;
    [SerializeField] EnemySpawnManager _enemies;

    [Header("----- 상점 연결 -----")]
    [SerializeField] SessionEconomyManager _economy;
    [SerializeField] SessionShopPurchaseService _purchases;
    [SerializeField] SessionShop _shop;

    [Header("----- 보상 수신 연결 -----")]
    [SerializeField] EnemyExpRewardReceiver _expReceiver;
    [SerializeField] EnemyGoldRewardReceiver _goldReceiver;
    [SerializeField] EnemyScoreRewardReceiver _scoreReceiver;

    [Header("----- 난이도 -----")]
    [SerializeField] RunDifficultyInitializer _difficultyInitializer;

    RunScoreSession _scoreSession;

    PlayerActor _player;
    Guid _playerRunId;

    bool _connected;
    bool _ready;
    bool _releasing;
    bool _handlingPlacement;
    bool _failurePending;

    [Header("----- 세션 메뉴 -----")]
    [SerializeField] bool _useSceneMenu;
    [SerializeField] UICancelInputRouter _cancelInput;
    [SerializeField] GameMenuController _gameMenu;
    SessionControlService _controls;
    GameMenuActionController _menuActions;

    bool _acceptUIInput;
    bool _routerBound;
    bool _menuBound;
    bool _menuSubscribed;

    public event Action OnWorldCompleted;
    public event Action OnRunClearRequested;
    public event Action OnProgressFailed;
    public bool IsReady => _ready && _world.IsReady;

    void LateUpdate()
    {
        // 준비 이전과 정리 이후에는 제어 담당자 없음
        if (_controls != null)
            _controls.EndFrame();
    }

    void OnDestroy()
    {
        Release();
    }

    bool CanHandleCancelInput()
    {
        return isActiveAndEnabled && _acceptUIInput && _connected && !_releasing &&
            IsReady && !_handlingPlacement && !_world.IsTransitioning && !_world.HasFailure &&
            _ui.IsBound && !_controls.IsStopped && _player.Status.IsAlive;
    }

    void ConnectSceneUI()
    {
        if (!_useSceneMenu) return;

        if (_cancelInput == null || _gameMenu == null)
            throw new MissingReferenceException($"{name}: 설정된 공통 메뉴와 입력 객체 필요");

        if (_cancelInput.gameObject.scene != gameObject.scene ||
            _gameMenu.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 입력 객체와 메뉴 필요");

        if (_cancelInput.IsBound || _gameMenu.IsBound)
            throw new InvalidOperationException($"{name}: 이전 세션의 앱 UI 연결 해제 필요");

        _gameMenu.Bind(_eventSystem, SettingsService.Instance);
        _menuBound = true;
        _gameMenu.OnVisibilityChanged += _controls.SetMenuOpen;
        _menuSubscribed = true;
        _cancelInput.Bind(CanHandleCancelInput, HandleCancelInput);
        _routerBound = true;
        _ui.SetCancelInputRouted(true);
    }

    void HandleCancelInput()
    {
        _controls.ConsumeCancelInput();

        if (_gameMenu.TryHandleCancel()) return;
        if (_ui.TryHandleCancel()) return;

        _gameMenu.Open();
    }

    bool CanHandleSettlementCancel()
    {
        return isActiveAndEnabled && _menuBound && !_releasing;
    }

    void HandleSettlementCancel()
    {
        if (!_gameMenu.TryHandleCancel())
            _gameMenu.Open();
    }

    void DisconnectSceneUI()
    {
        UICancelInputRouter cancelInput = _cancelInput;
        GameMenuController gameMenu = _gameMenu;
        SessionControlService controls = _controls;
        GameMenuActionController menuActions = _menuActions;
        bool routerBound = _routerBound;
        bool menuBound = _menuBound;
        bool menuSubscribed = _menuSubscribed;

        // 정리 콜백의 재진입에 대비한 참조와 소유 상태 선해제
        _menuActions = null;
        _routerBound = false;
        _menuBound = false;
        _menuSubscribed = false;

        List<Exception> errors = new List<Exception>();
        TryCleanup(() =>
        {
            if (menuActions != null)
                menuActions.Unbind();
        }, errors);
        TryCleanup(() =>
        {
            if (routerBound && cancelInput != null)
                cancelInput.Unbind();
        }, errors);
        TryCleanup(() =>
        {
            if (routerBound && _ui != null)
                _ui.SetCancelInputRouted(false);
        }, errors);
        TryCleanup(() =>
        {
            if (menuSubscribed && gameMenu != null)
                gameMenu.OnVisibilityChanged -= controls.SetMenuOpen;
        }, errors);
        TryCleanup(() =>
        {
            if (menuBound && gameMenu != null)
                gameMenu.Unbind();
        }, errors);

        if (errors.Count > 0)
            throw new AggregateException($"{name}: 앱 UI 연결 정리 실패", errors);
    }

    static void StopPlayerForRun(PlayerActor player, Guid runId)
    {
        if (player == null || !player.IsPrepared || player.RunId != runId)
            return;

        player.DisableControl();

        // 조작 중단 콜백 이후에도 같은 런인 경우에만 배치 차단 정리
        if (player != null && player.IsPrepared && player.RunId == runId)
            player.SetPlacementBlocked(false);
    }

    static void TryCleanup(Action operation, List<Exception> errors)
    {
        try { operation(); }
        catch (Exception exception) { errors.Add(exception); }
    }

    bool IsPlayerConnected(PlayerActor player, Guid runId)
    {
        return _connected && !_releasing && player != null && _player == player && _playerRunId == runId;
    }

    void ValidateReferences()
    {
        if (_world == null || _enemies == null || _enemies.Spawner == null || _economy == null || _purchases == null || _shop == null || _ui == null ||
            _inputCamera == null || _eventSystem == null || _effectPlayer == null || _expReceiver == null || _goldReceiver == null)
        {
            throw new MissingReferenceException(
                $"{name}: 월드·적·상점·UI·플레이어 씬 의존성·보상 연결 필요");
        }

        if (_difficultyInitializer == null || _scoreReceiver == null || _scoreReceiver.Source != _enemies)
            throw new MissingReferenceException($"{name}: 난이도 초기화와 동일 세션의 점수 수신기 연결 필요");

        Behaviour[] components =
        {
            this,
            _world,
            _enemies,
            _enemies.Spawner,
            _difficultyInitializer,
            _scoreReceiver,
            _economy,
            _purchases,
            _ui,
            _inputCamera,
            _eventSystem,
            _effectPlayer
        };


        foreach (Behaviour component in components)
        {
            if (component.gameObject.scene != gameObject.scene)
                throw new InvalidOperationException($"{name}: 같은 씬의 세션 기능 연결 필요: {component.name}");

            if (!component.isActiveAndEnabled)
                throw new InvalidOperationException($"{name}: 세션 기능 활성화 필요: {component.name}");
        }

        if (!_ui.HasSkillHud || !_expReceiver.gameObject.activeInHierarchy || !_goldReceiver.gameObject.activeInHierarchy)
            throw new InvalidOperationException("정식 런의 스킬 HUD와 활성 보상 수신 오브젝트 필요");
    }

    void HandleWorldCompleted()
    {
        OnWorldCompleted?.Invoke();
    }

    void HandleRunClearRequested()
    {
        if (OnRunClearRequested == null)
            throw new InvalidOperationException("런 클리어 종료 담당자 연결 필요");
        OnRunClearRequested.Invoke();
    }

    void HandleProgressFailed()
    {
        if (!_connected || _releasing)
            return;

        // World의 실패 응답 중 이전 맵 복구와 플레이어 복구를 먼저 완료
        if (_handlingPlacement)
        {
            _failurePending = true;
            return;
        }

        // 새 월드 생성 단계 실패도 기존 런의 조작 재개 없이 정지
        if (_ready && !_world.IsReady)
        {
            _ready = false;
            StopPlayerForRun(_player, _playerRunId);
        }
        OnProgressFailed?.Invoke();
    }

    void HandlePlacement(long requestId, Pose destination)
    {
        if(_handlingPlacement)
            throw new InvalidOperationException("이전 플레이어 배치 처리 완료 필요");

        PlayerActor player = _player;
        Guid runId = _playerRunId;
        Exception failure = null;

        _handlingPlacement = true;
        _failurePending = false;

        try
        {
            if (!IsPlayerConnected(player, runId) || !player.IsPrepared || player.RunId != runId)
                throw new InvalidOperationException("현재 세션과 같은 런의 준비된 플레이어 필요");

            bool hadPreviousNode = _world.Progress.HasCurrentNode;
            Pose previousPose = player.CurrentPose;

            player.SetPlacementBlocked(true);


            bool canContinue = IsPlayerConnected(player, runId);
            bool succeed = false;

            if (canContinue)
            {
                succeed = player.TryPlace(destination);

                if (!_world.CompletePlacement(requestId, succeed))
                    throw new InvalidOperationException("현재 배치 응답 접수 실패");

                canContinue = IsPlayerConnected(player, runId) && player.IsPrepared && player.RunId == runId && player.Status.IsAlive;
            }

            if (!canContinue)
            {
                StopPlayerForRun(player, runId);
            }
            else if(!succeed && (!hadPreviousNode || !player.TryPlace(previousPose)))
            {
                throw new InvalidOperationException("플레이어 배치 또는 이전 위치 복구 실패");
            }
            else
            {
                player.SetPlacementBlocked(false);
            }
        }
        catch(Exception exception)
        {
            failure = exception;

            if (IsPlayerConnected(player, runId))
                _ready = false;

            try
            {
                StopPlayerForRun(player, runId);
            }
            catch(Exception stopError)
            {
                failure = new AggregateException("플레이어 배치 처리 및 조작 중단 실패", failure, stopError);
            }
        }
        finally
        {
            _handlingPlacement = false;
        }

        if (IsPlayerConnected(player, runId) && _failurePending)
        {
            _failurePending = false;

            try
            {
                OnProgressFailed?.Invoke();
            }
            catch (Exception notificationError)
            {
                failure = failure == null ? notificationError : new AggregateException("플레이어 배치 처리 및 진행 실패 알림 오류", failure, notificationError);
            }
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (!_connected || _releasing)
            return;

        QuestManager.Instance.ReportEnemyKilled(enemy.Definition);
    }

    /// <summary>
    /// 비활성 플레이어 준비에 필요한 씬 의존성 연결
    /// </summary>
    /// <param name="player">이번 런으로 준비할 플레이어</param>
    public void BindPlayerScene(PlayerActor player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        ValidateReferences();

        if (_connected || _releasing || _expReceiver.IsBound || _ui.IsBound)
            throw new InvalidOperationException("이전 런 연결 해제 필요");

        player.BindScene(_inputCamera, _eventSystem, _effectPlayer);
    }

    /// <summary>
    /// 씬 연결 검사와 런 시작 전 보상 및 HUD 중단
    /// </summary>
    public void InitializeForScene()
    {
        ValidateReferences();

        if (_connected || _releasing)
            throw new InvalidOperationException("이전 세션 해제 필요");

        _expReceiver.enabled = false;
        _goldReceiver.enabled = false;
        _ui.InitializeForScene();
    }

    /// <summary>
    /// 준비된 플레이어의 세션과 배치 및 보상과 UI 연결
    /// </summary>
    /// <param name="player">이번 런으로 준비된 플레이어</param>
    /// <param name="progression">이번 런의 성장 객체</param>
    /// <param name="runFlow">현재 세션의 런 종료 담당자</param>
    public void Initialize(PlayerActor player, RunLevelProgression progression, GameFlowController runFlow)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        if (progression == null)
            throw new ArgumentNullException(nameof(progression));

        if (runFlow == null)
            throw new ArgumentNullException(nameof(runFlow));

        if (!runFlow.isActiveAndEnabled || runFlow.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 세션 씬의 활성 런 종료 담당자 필요");

        ValidateReferences();

        if (!player.IsPrepared || player.RunId != progression.RunId || player.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬과 런으로 준비된 플레이어 필요");

        if (_connected || _releasing || _handlingPlacement || _controls != null || _scoreSession != null || _difficultyInitializer.HasBindings || _world.IsReady || _world.Graph.IsGenerated || _expReceiver.IsBound || _ui.IsBound)
            throw new InvalidOperationException("이전 배치 처리 완료와 세션 및 월드와 UI 해제 필요");

        PlayerDefinition playerDefinition = player.Status.Definition as PlayerDefinition;

        if (playerDefinition == null)
            throw new InvalidOperationException("준비된 플레이어의 PlayerDefinition 필요");

        CombatTarget target = player.Target;

        if (!target.IsAlive || _enemies.EnemyCount != 0 || _enemies.Spawner.PlayerTarget != null)
            throw new InvalidOperationException("생존 플레이어와 잔여 적 정리 확인 필요");

        _player = player;
        _playerRunId = player.RunId;
        _connected = true;
        _acceptUIInput = false;

        try
        {
            _controls = new SessionControlService(player, new GamePauseService());

            _world.OnPlacementRequested += HandlePlacement;
            _world.OnWorldCompleted += HandleWorldCompleted;
            _world.OnRunClearRequested += HandleRunClearRequested;
            _world.OnProgressFailed += HandleProgressFailed;

            _economy.ResetSession();

            _enemies.Spawner.BindPlayerTarget(target);
            _enemies.OnEnemyDied += HandleEnemyDied;

            _purchases.BindWorld(_world);

            _shop.BindPurchases(_purchases);

            _ui.OnShopStateChanged += _controls.SetShopOpen;
            _ui.Bind(_world, _shop, _purchases, player.Loadout, playerDefinition, progression, player.Status, _economy);

            _controls.SetShopOpen(_ui.IsShopOpen);

            _difficultyInitializer.Prepare(
                runFlow.StartRequest.SelectedDifficulty, _enemies.Spawner, _world.CombatArea);

            _scoreSession = new RunScoreSession(_scoreReceiver, _world, _economy, target);
            _scoreSession.Begin(progression.RunId, runFlow.StartRequest.SelectedDifficulty);
            _world.PrepareWorld();

            if (!_connected || !_world.IsReady || _world.IsTransitioning || _world.HasFailure || !_purchases.HasWorld)
                throw new InvalidOperationException("시작 맵 배치와 월드 및 상점 준비 완료 필요");

            _expReceiver.Bind(progression);
            _expReceiver.enabled = true;
            _goldReceiver.enabled = true;

            _ui.OpenHud();

            if (!_expReceiver.IsListening || !_goldReceiver.IsListening || !_ui.IsSkillHudBound)
                throw new InvalidOperationException("보상 수신과 스킬 HUD 연결 완료 필요");

            ConnectSceneUI();

            if (_useSceneMenu)
                _menuActions = new GameMenuActionController(_gameMenu, runFlow);

            _ready = true;
            _acceptUIInput = true;
        }
        catch (Exception preparationError)
        {
            try
            {
                Release();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("세션 준비 및 정리 실패", preparationError, cleanupError);
            }

            throw;
        }
    }

    /// <summary>
    /// 세션 종료 시작에 따른 입력 차단과 메뉴 연결 정리
    /// </summary>
    public void BeginStopping()
    {
        _acceptUIInput = false;
        List<Exception> errors = new List<Exception>();

        if (_controls != null)
            TryCleanup(_controls.Stop, errors);

        if (_scoreSession != null)
            TryCleanup(_scoreSession.StopReceiving, errors);

        TryCleanup(DisconnectSceneUI, errors);

        if (errors.Count > 0)
            throw new AggregateException($"{name}: 세션 제어 중단 실패", errors);
    }

    /// <summary>
    /// 세션 제어와 보상 및 화면 중단 후 월드와 적 및 카메라 연결 정리
    /// </summary>
    public void Release()
    {
        if (_releasing)
            return;

        bool hadConnection = _connected;
        _ready = false;
        _acceptUIInput = false;
        _releasing = true;

        List<Exception> errors = new List<Exception>();

        try
        {
            TryCleanup(BeginStopping, errors);

            if (hadConnection)
            {
                if (_ui != null && _controls != null)
                    _ui.OnShopStateChanged -= _controls.SetShopOpen;

                TryCleanup(() => _enemies.OnEnemyDied -= HandleEnemyDied, errors);
                // 적 회수와 월드 해제 이전 보상 수신 중단
                TryCleanup(() => _expReceiver.enabled = false, errors);
                TryCleanup(() => _goldReceiver.enabled = false, errors);
                TryCleanup(_expReceiver.UnBind, errors);
                TryCleanup(_ui.CloseHud, errors);

                _world.OnPlacementRequested -= HandlePlacement;
                _world.OnWorldCompleted -= HandleWorldCompleted;
                _world.OnRunClearRequested -= HandleRunClearRequested;
                _world.OnProgressFailed -= HandleProgressFailed;

                TryCleanup(() => StopPlayerForRun(_player, _playerRunId), errors);
                TryCleanup(_ui.Unbind, errors);
                TryCleanup(_shop.Close, errors);
                TryCleanup(_world.ReleaseWorld, errors);
                TryCleanup(_purchases.UnbindWorld, errors);
                TryCleanup(_enemies.ClearSessionEnemies, errors);
                TryCleanup(_enemies.Spawner.UnbindPlayerTarget, errors);
            }

            if (_scoreSession != null)
            {
                TryCleanup(() =>
                {
                    _scoreSession.Release();
                    _scoreSession = null;
                }, errors);
            }

            if (_difficultyInitializer != null)
                TryCleanup(_difficultyInitializer.Release, errors);
        }
        finally
        {
            _player = null;
            _playerRunId = Guid.Empty;
            _connected = false;
            _releasing = false;
            _failurePending = false;
            _controls = null;
        }

        if (errors.Count > 0)
            throw new AggregateException("세션 정리 실패", errors);
    }

    internal void BindSettlementMenu()
    {
        if (!_useSceneMenu)
            return;

        _gameMenu.Bind(_eventSystem, SettingsService.Instance, allowRunExit: false);
        _menuBound = true;

        try
        {
            _cancelInput.Bind(CanHandleSettlementCancel, HandleSettlementCancel);
            _routerBound = true;
        }
        catch
        {
            DisconnectSceneUI();
            throw;
        }
    }

    /// <summary>
    /// 현재 런의 정상 종료 점수 결과 확정
    /// </summary>
    /// <param name="reason">확정된 종료 사유</param>
    /// <param name="playerLevel">종료 시점 세션 레벨</param>
    /// <param name="loadout">종료 시점 스킬 슬롯</param>
    /// <returns>런 점수 정산 결과</returns>
    public RunSettlementResult FinishScore(RunEndReason reason, int playerLevel, SkillLoadout loadout)
    {
        if (_scoreSession == null)
            throw new InvalidOperationException("점수 세션 시작 필요");

        return _scoreSession.Finish(reason, playerLevel, loadout);
    }
}
