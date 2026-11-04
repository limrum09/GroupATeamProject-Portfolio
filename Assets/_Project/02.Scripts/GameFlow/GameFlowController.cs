using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정상 런 종료 요청의 원인
/// </summary>
public enum RunEndReason
{
    Manual,
    PlayerDied,
    Cleared
}

/// <summary>
/// PlayerActor 준비와 보상 연결 및 런 시작과 종료 순서 조율
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class GameFlowController : MonoBehaviour
{
    enum RunPhase
    {
        Idle,
        Preparing,
        Running,
        EndRequested,
        Releasing,
        Finished,
        Failed
    }

    [Header("----- 공통 성장 설정 -----")]
    [SerializeField] RunLevelDefinition _levelDefinition;

    [Header("----- 플레이어 준비 담당 -----")]
    [SerializeField] PlayerActor _playerActor;

    [Header("----- 세션 연결 담당 -----")]
    [SerializeField] SessionInitializer _sessionInitializer;
    [SerializeField] RunSettlementView _settlementView;

    RunLevelProgression _runProgression;
    RunStartRequest _startRequest;

    CharacterStatus _subscribedStatus;

    RunPhase _phase;
    RunEndReason _requestedEndReason;
    bool _sceneStarted;
    bool _questScopeOpen;

    public event Action<RunLevelProgression> OnRunStarted;
    public event Action<RunEndReason, RunLevelProgression> OnRunEnded;

    public RunLevelProgression CurrentRun => _runProgression;
    public RunStartRequest StartRequest => _startRequest;
    public RunSettlementResult LastScoreResult { get; private set; }
    public RunSettlementRelay SettlementRelay { get; private set; }
    public RunSettlementView SettlementView => _settlementView;
    public bool HasSceneStarted => _sceneStarted;
    public bool IsEndRequested => _phase == RunPhase.EndRequested;

    /// <summary>
    /// 종료 요청 이후 현재 프레임의 보상 처리 구간을 포함한 런 활성 여부
    /// </summary>
    public bool IsRunning => (_phase == RunPhase.Running || _phase == RunPhase.EndRequested) && _runProgression != null && _runProgression.IsActive;

    void Awake()
    {
        ValidateSceneReferences();
        SettlementRelay = new RunSettlementRelay(this);

        _sessionInitializer.InitializeForScene();
        _playerActor.DisableControl();
    }

    void Start()
    {
        _sceneStarted = true;
    }

    void LateUpdate()
    {
        if (_phase == RunPhase.EndRequested)
            FinishRun();
    }

    void OnDisable()
    {
        StopAbnormally();
    }

    void OnDestroy()
    {
        SettlementRelay?.Dispose();
        StopAbnormally();
    }

    static void TryCleanup(Action action, List<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            // 다른 필수 정리 단계도 수행한 뒤 함께 보고
            errors.Add(exception);
        }
    }

    static void RequirePlayMode()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 실행 필요");
    }

    void ValidateSceneReferences()
    {
        if (_levelDefinition == null || _playerActor == null || _playerActor.Status == null || _sessionInitializer == null)
            throw new MissingReferenceException($"{name}: 성장 설정, PlayerActor와 SessionInitializer 연결 필요");

        if (_sessionInitializer.gameObject.scene != gameObject.scene || !_sessionInitializer.isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 같은 런 씬의 활성 SessionInitializer 연결 필요");

        if (_playerActor.gameObject.scene != gameObject.scene || !_playerActor.enabled)
            throw new InvalidOperationException($"{name}: 같은 런 씬의 PlayerActor 연결과 컴포넌트 활성 설정 필요");

        if (transform.IsChildOf(_playerActor.transform))
            throw new InvalidOperationException($"{name}: GameFlow는 비활성 PlayerRoot 외부에 배치 필요");
    }

    void RequirePreparedRun(RunLevelProgression progression)
    {
        if (!isActiveAndEnabled || _phase != RunPhase.Preparing || _runProgression != progression)
            throw new OperationCanceledException($"{name}: 런 준비 중 GameFlow 또는 런 연결 해제");

        if (!_playerActor.IsPrepared || _playerActor.RunId != progression.RunId)
            throw new InvalidOperationException($"{name}: 같은 런으로 준비된 활성 PlayerActor 필요");

        CharacterStatus status = _playerActor.Status;

        if (status == null || !status.IsInitialized || status.RunId != progression.RunId || !status.IsAlive)
            throw new InvalidOperationException($"{name}: 준비된 런의 생존 플레이어 상태 필요");
    }

    [ContextMenu("진행/런 종료 요청")]
    void RequestEndFromContext()
    {
        RequirePlayMode();
        RequestEndRun(RunEndReason.Manual);
    }

    void HandlePlayerDied()
    {
        RequestEndRun(RunEndReason.PlayerDied);
    }

    void HandleRunClearRequested()
    {
        RequestEndRun(RunEndReason.Cleared);
    }

    internal void BindSettlementMenu()
    {
        _sessionInitializer.BindSettlementMenu();
    }

    void FinishRun()
    {
        RunEndReason reason = _requestedEndReason;
        RunLevelProgression completedRun = _runProgression;

        RunSettlementResult scoreResult = null;
        List<Exception> errors = new List<Exception>();
        TryCleanup(() => scoreResult = _sessionInitializer.FinishScore(
            reason, completedRun.Level, _playerActor.Loadout), errors);

        Exception cleanupError = ReleaseRunBindings(RunPhase.Finished);
        if (cleanupError != null)
            errors.Add(cleanupError);

        if (errors.Count > 0)
            throw new AggregateException("런 결과 확정과 정리 실패", errors);

        LastScoreResult = scoreResult;
        PublishRunEnded(reason, completedRun);
    }

    void StopAbnormally()
    {
        if (_phase == RunPhase.Idle || _phase == RunPhase.Releasing || _phase == RunPhase.Finished)
            return;

        // 강제 비활성화와 씬 해제는 정상 종료 결과로 발행하지 않음
        Exception cleanupError = ReleaseRunBindings(RunPhase.Failed);

        if (cleanupError != null)
            Debug.LogException(cleanupError, this);
    }

    void PublishRunStarted(RunLevelProgression progression)
    {
        Delegate[] callbacks = OnRunStarted?.GetInvocationList();

        if (callbacks == null)
            return;

        foreach (Action<RunLevelProgression> callback in callbacks)
        {
            if (!IsRunning || _runProgression != progression)
                return;

            try
            {
                callback(progression);
            }
            catch (Exception exception)
            {
                // 구독자 오류로 준비된 런을 다시 실패 처리하지 않음
                Debug.LogException(exception, this);
            }
        }
    }

    void PublishRunEnded(RunEndReason reason, RunLevelProgression completedRun)
    {
        Delegate[] callbacks = OnRunEnded?.GetInvocationList();

        if (callbacks == null)
            return;

        foreach (Action<RunEndReason, RunLevelProgression> callback in callbacks)
        {
            try
            {
                callback(reason, completedRun);
            }
            catch (Exception exception)
            {
                // 다른 종료 구독자와 SceneFlow의 통지 누락 방지
                Debug.LogException(exception, this);
            }
        }
    }

    Exception ReleaseRunBindings(RunPhase completedPhase)
    {
        if (_phase == RunPhase.Releasing)
            return null;

        _phase = RunPhase.Releasing;

        RunLevelProgression progression = _runProgression;
        CharacterStatus subscribedStatus = _subscribedStatus;

        // 정리 콜백의 재진입에 대비한 연결 참조 선해제
        _runProgression = null;
        _subscribedStatus = null;

        List<Exception> errors = new List<Exception>();

        TryCleanup(() =>
        {
            if (_sessionInitializer != null)
                _sessionInitializer.OnRunClearRequested -= HandleRunClearRequested;
        }, errors);

        TryCleanup(() =>
        {
            if (_playerActor != null)
                _playerActor.DisableControl();
        }, errors);

        // 보상과 HUD 종료 및 적 회수 이후 플레이어 해제
        TryCleanup(() =>
        {
            if (_sessionInitializer != null)
                _sessionInitializer.Release();
        }, errors);

        TryCleanup(() =>
        {
            if (progression != null)
                progression.Stop();
        }, errors);

        TryCleanup(() =>
        {
            if (subscribedStatus != null)
                subscribedStatus.OnDied -= HandlePlayerDied;
        }, errors);

        TryCleanup(() =>
        {
            if (_playerActor != null)
                _playerActor.Release();
        }, errors);

        _phase = errors.Count == 0 ? completedPhase : RunPhase.Failed;

        if (_questScopeOpen)
        {
            _questScopeOpen = false;

            TryCleanup(() =>
            {
                // 정리 중 싱글톤 검색과 신규 생성 방지
                if (!QuestManager.TryGetInstance(out QuestManager quests))
                {
                    // 앱 종료 과정에서 이미 제거된 인스턴스
                    if (SingletonRuntime.IsQuitting)
                        return;

                    throw new MissingReferenceException($"{name}: 런 정리를 위한 기존 QuestManager 필요");
                }

                if (completedPhase == RunPhase.Finished && errors.Count == 0)
                    quests.EndRun();
                else
                    quests.AbortRun();
            }, errors);
        }

        _phase = errors.Count == 0 ? completedPhase : RunPhase.Failed;

        if (errors.Count == 0)
            return null;

        return new AggregateException($"{name}: 런 연결 정리 실패", errors);
    }

    /// <summary>
    /// 런 준비와 씬 연결 및 시작과 종료 순서 조율
    /// </summary>
    /// <param name="request">로비에서 확정한 성장 스냅샷과 난이도</param>
    public void BeginRun(RunStartRequest request)
    {
        RequirePlayMode();

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!_sceneStarted || !isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 GameFlow의 Start 진입 이후 런 준비 필요");

        if (_phase != RunPhase.Idle)
            throw new InvalidOperationException($"{name}: 새 런 또는 준비 실패 후 재시도는 런 씬 재로드 필요");

        ValidateSceneReferences();

        if (_playerActor.gameObject.activeSelf || _playerActor.IsPrepared || _playerActor.RunId != Guid.Empty)
            throw new InvalidOperationException($"{name}: 아직 준비하지 않은 비활성 PlayerRoot 필요");

        RunGrowthSnapshot growthSnapshot = request.GrowthSnapshot;

        if (!(growthSnapshot.GrowthDefinition.CharacterDefinition is PlayerDefinition playerDefinition))
            throw new InvalidOperationException($"{name}: 플레이어 캐릭터의 성장 설정 필요");

        RunLevelProgression progression = null;

        _phase = RunPhase.Preparing;
        _startRequest = request;
        LastScoreResult = null;

        try
        {
            _sessionInitializer.BindPlayerScene(_playerActor);

            progression = new RunLevelProgression(_levelDefinition, growthSnapshot, _playerActor.Status.ApplyLevelUp);

            // Actor 준비 중 취소되어도 성장 객체를 정리할 수 있도록 선연결
            _runProgression = progression;

            // 비활성 PlayerRoot의 주입과 초기화 및 활성화는 Actor에서 수행
            _playerActor.PrepareForRun(playerDefinition, progression.RunId, progression.GrowthContribution);

            RequirePreparedRun(progression);

            _subscribedStatus = _playerActor.Status;
            _subscribedStatus.OnDied += HandlePlayerDied;

            _sessionInitializer.OnRunClearRequested += HandleRunClearRequested;

            QuestManager.Instance.BeginRun();
            _questScopeOpen = true;

            _sessionInitializer.Initialize(_playerActor, progression, this);

            progression.Start();

            RequirePreparedRun(progression);

            if (!progression.IsActive)
                throw new OperationCanceledException($"{name}: 준비 중 런 성장 상태 종료");

            _playerActor.EnableControl(progression.RunId);

            RequirePreparedRun(progression);

            if (!progression.IsActive)
                throw new OperationCanceledException($"{name}: 조작 허용 중 런 성장 상태 종료");

            _phase = RunPhase.Running;
        }
        catch (Exception startError)
        {
            Exception cleanupError = ReleaseRunBindings(RunPhase.Failed);

            if (cleanupError != null)
                throw new AggregateException($"{name}: 런 준비 실패와 정리 실패", startError, cleanupError);

            throw;
        }

        PublishRunStarted(progression);
    }

    /// <summary>
    /// 첫 종료 원인을 기록하고 조작을 중단한 뒤 프레임 종료 시 정리 예약
    /// </summary>
    /// <param name="reason">런을 종료할 원인</param>
    /// <returns>첫 정상 종료 요청의 접수 여부</returns>
    public bool RequestEndRun(RunEndReason reason)
    {
        if (!Enum.IsDefined(typeof(RunEndReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        if (_phase != RunPhase.Running || !IsRunning)
            return false;

        _requestedEndReason = reason;
        _phase = RunPhase.EndRequested;

        try
        {
            // 새 조작은 중단하고 현재 프레임의 처치 보상 처리는 유지
            _sessionInitializer.BeginStopping();
            _playerActor.DisableControl();
        }
        catch (Exception stopError)
        {
            Exception cleanupError = ReleaseRunBindings(RunPhase.Failed);

            if (cleanupError != null)
                throw new AggregateException($"{name}: 조작 중단 실패와 정리 실패", stopError, cleanupError);

            throw;
        }

        return true;
    }
}
