using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

/// <summary>
/// 로비 의존성 연결과 런 시작 조건 전달 및 씬 전환 조율
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneFlowController : MonoBehaviour
{
    [Header("----- 씬 경로 -----")]
    [Tooltip("빌드 씬 목록에 등록한 Assets/.../*.unity 경로")]
    [SerializeField] string _metaScenePath;

    [Tooltip("빌드 씬 목록에 등록한 Assets/.../*.unity 경로")]
    [SerializeField] string _runScenePath;

    [Header("----- 메타 구매 준비 -----")]
    [SerializeField] MetaProgressionInitializer _metaProgression;

    [Header("----- 임시 캐릭터 테스트 -----")]
    [SerializeField] bool _useCharacterTestMode;

    GameFlowController _currentRunFlow;
    RunSettlementView _settlementView;

    bool _isTransitioning;
    bool _returnToMetaRequested;
    bool _saveQuestOnReturn;
    bool _isFlowInitialized;
    Task<bool> _initializationTask;
    string _initializationUserId;

    public event Action OnStateChanged;

    public bool IsTransitioning => _isTransitioning;
    public bool CanStartRun => CanEnterRunScene();
    public bool IsInMetaScene => !_isTransitioning && !_returnToMetaRequested && (_currentRunFlow == null || !_currentRunFlow.IsRunning) && SceneManager.GetActiveScene().path == _metaScenePath;
    public bool IsCharacterTestMode => _useCharacterTestMode;

    void LateUpdate()
    {
        if (_returnToMetaRequested && !_isTransitioning)
        {
            // 런 종료 알림의 다른 구독자 처리 이후 씬 전환
            _returnToMetaRequested = false;
            TryReturnToMetaScene();
        }

        if(_saveQuestOnReturn && IsInMetaScene)
        {
            _saveQuestOnReturn = false;
            SaveReturnedQuestProgress();
        }
    }

    void OnDestroy()
    {
        DetachRunFlow();
    }

    static T FindRootEntry<T>(Scene scene) where T : MonoBehaviour
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("로드가 완료된 씬 필요");

        T found = null;

        // 지정 씬의 루트에 배치된 단일 진입점이라는 명시적 구성 계약
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T candidate = root.GetComponent<T>();

            if (candidate == null)
                continue;

            if (found != null)
                throw new InvalidOperationException($"{scene.name}: {typeof(T).Name}은 루트에 하나만 배치 필요");

            found = candidate;
        }

        if (found == null)
            throw new MissingReferenceException($"{scene.name}: 루트 GameObject의 {typeof(T).Name} 필요");

        if (!found.isActiveAndEnabled)
            throw new InvalidOperationException($"{scene.name}: 활성 {typeof(T).Name} 필요");

        return found;
    }

    static int GetSceneIndex(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            throw new InvalidOperationException("빌드 씬 목록에 등록한 씬 경로 필요");

        int index = SceneUtility.GetBuildIndexByScenePath(scenePath);

        if (index < 0)
            throw new InvalidOperationException($"빌드 씬 목록에서 찾을 수 없는 씬: {scenePath}");

        return index;
    }

    static void RequirePlayMode()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 실행 필요");
    }

    async void SaveReturnedQuestProgress()
    {
        try
        {
            bool saved = await RetryReturnedQuestSaveAsync();

            if (!saved && this != null)
            {
                Debug.LogWarning(_metaProgression.SaveError ?? "런 정산 결과를 저장하지 못했습니다.", this);
            }
        }
        catch (Exception exception)
        {
            if (this != null)
                Debug.LogException(exception, this);
        }
    }

    bool CanEnterRunScene()
    {
        if (!isActiveAndEnabled || !IsInMetaScene)
            return false;

        // 명시적으로 선택한 임시 테스트에서만 메타 준비 조건 제외
        if (_useCharacterTestMode)
            return true;

        if (!_metaProgression.IsReady)
            return false;

        return !_metaProgression.Purchases.BlocksRunEntry && !_metaProgression.Loadouts.BlocksRunEntry && !_metaProgression.QuestActions.BlocksMetaOperations && _metaProgression.Saves.CanAcceptChanges;
    }

    void HandleRunEnded(RunEndReason reason, RunLevelProgression completedRun)
    {
        Debug.Log($"[SceneFlow] 런 종료 사유: {reason}", this);
        _returnToMetaRequested = true;
        _saveQuestOnReturn = !_useCharacterTestMode;

        RunSettlementResult result = _currentRunFlow.LastScoreResult;
        if (result.IsRewardEligible)
        {
            _currentRunFlow.BindSettlementMenu();
            _settlementView.Show(result, RequestSettlementReturn);
            _returnToMetaRequested = false;
        }
    }

    void RequestSettlementReturn()
    {
        _returnToMetaRequested = true;
    }

    void BindLobby(Scene scene)
    {
        LobbyManager lobby = FindRootEntry<LobbyManager>(scene);
        lobby.Bind(_metaProgression, this);
    }

    void BeginSceneTransition(int sceneIndex, RunStartRequest request)
    {
        if (!isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 SceneFlowController 필요");

        _isTransitioning = true;

        StartCoroutine(LoadSceneRoutine(sceneIndex, request));

        OnStateChanged?.Invoke();
    }

    void DetachRunFlow()
    {
        if (_currentRunFlow != null)
            _currentRunFlow.OnRunEnded -= HandleRunEnded;

        _currentRunFlow = null;
        _settlementView = null;
    }

    IEnumerator LoadSceneRoutine(int sceneIndex, RunStartRequest request)
    {
        try
        {
            DetachRunFlow();

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);

            if (operation == null)
                throw new InvalidOperationException($"{name}: 씬 로드 작업 생성 실패");

            yield return operation;

            Scene loadedScene = SceneManager.GetSceneByBuildIndex(sceneIndex);

            // 시작 조건이 없는 전환은 로비 복귀 경로
            if (request == null)
            {
                BindLobby(loadedScene);
                yield break;
            }

            GameFlowController runFlow = FindRootEntry<GameFlowController>(loadedScene);

            while (true)
            {
                if (runFlow == null || !runFlow.isActiveAndEnabled)
                    throw new InvalidOperationException($"{name}: 런 초기화 전 진입점 해제 또는 비활성화");

                if (runFlow.HasSceneStarted)
                    break;

                yield return null;
            }

            _settlementView = runFlow.SettlementView;
            if (_settlementView == null || _settlementView.gameObject.scene != loadedScene)
                throw new MissingReferenceException($"{loadedScene.name}: 같은 씬의 런 결산 화면 연결 필요");

            _currentRunFlow = runFlow;
            _currentRunFlow.OnRunEnded += HandleRunEnded;
            _currentRunFlow.BeginRun(request);
        }
        finally
        {
            _isTransitioning = false;

            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 씬 흐름 구성 검사와 애플리케이션 수명 준비
    /// </summary>
    public void Initialize()
    {
        RequirePlayMode();

        if (!isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 SceneFlowController 필요");

        if (_isFlowInitialized)
            return;

        if (transform.parent != null)
            throw new InvalidOperationException($"{name}: 독립된 루트 오브젝트 필요");

        if (_useCharacterTestMode)
        {
            if (_metaProgression != null)
                throw new InvalidOperationException($"{name}: 임시 테스트에서는 메타 연결을 비워 둘 필요");
        }
        else if (_metaProgression == null)
        {
            throw new MissingReferenceException($"{name}: MetaProgressionInitializer 연결 필요");
        }

        int metaIndex = GetSceneIndex(_metaScenePath);
        int runIndex = GetSceneIndex(_runScenePath);

        if (metaIndex == runIndex)
            throw new InvalidOperationException($"{name}: 서로 다른 메타 씬과 런 씬 지정 필요");

        DontDestroyOnLoad(gameObject);

        // 모든 구성 검사가 성공한 뒤 준비 완료 처리
        _isFlowInitialized = true;
    }

    /// <summary>
    /// 사용자별 초기화 작업 공유와 명시적인 실패 재시도
    /// </summary>
    /// <param name="retryFailed">완료된 실패 작업을 다시 시작할지 여부</param>
    /// <param name="userId">Firebase UID, 생략 시 기존 작업 또는 로컬 테스트 프로필 사용</param>
    /// <returns>초기화 성공 여부</returns>
    public Task<bool> EnsureInitializedAsync(bool retryFailed = false, string userId = null)
    {
        Initialize();

        if (_initializationTask != null)
        {
            if (userId != null && !string.Equals(userId, _initializationUserId, StringComparison.Ordinal))
                throw new InvalidOperationException("다른 사용자 프로필로 변경하려면 현재 실행 종료 필요");

            // 기존 Context 테스트의 재요청도 현재 프로필에 유지
            userId = _initializationUserId;
        }

        if (retryFailed && _initializationTask != null && _initializationTask.IsCompleted && (_initializationTask.IsFaulted || _initializationTask.IsCanceled || !_initializationTask.Result))
            _initializationTask = null;

        if (_initializationTask == null)
        {
            _initializationUserId = userId;

            _initializationTask = _useCharacterTestMode ? Task.FromResult(true) : _metaProgression.InitializeAsync( () => this != null && isActiveAndEnabled && IsInMetaScene, userId);
        }

        return _initializationTask;
    }

    /// <summary>
    /// 정상 런 결과 반영과 일일 기록 및 메타 재화 저장
    /// </summary>
    /// <returns>정산 결과 저장 완료 여부</returns>
    public async Task<bool> RetryReturnedQuestSaveAsync()
    {
        if (_useCharacterTestMode)
            return true;

        if (!IsInMetaScene || !_metaProgression.IsReady)
            return false;

        QuestActionService actions = _metaProgression.QuestActions;

        try
        {
            if (actions.HasPendingRunResult && !await actions.ApplyRunResultAsync())
                return false;

            return await _metaProgression.SaveMetaChangesAsync(
                MetaSavePart.DailyQuests | MetaSavePart.MetaGold);
        }
        finally
        {
            if (this != null)
                OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 로비에서 확정한 시작 조건으로 런 씬 전환 요청
    /// </summary>
    /// <param name="request">캐릭터 성장과 난이도를 포함한 시작 조건</param>
    /// <returns>씬 전환 요청 접수 여부</returns>
    public bool TryEnterRunScene(RunStartRequest request)
    {
        RequirePlayMode();

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!CanEnterRunScene())
            return false;

        // 로비의 저장 대기 절차를 우회한 직접 진입 차단
        if (!_useCharacterTestMode && _metaProgression.Saves.HasChanges)
            return false;

        int sceneIndex = GetSceneIndex(_runScenePath);

        BeginSceneTransition(sceneIndex, request);
        return true;
    }

    /// <summary>
    /// 활성 런 종료 요청 또는 로비 씬 전환
    /// </summary>
    /// <param name="reloadCurrentScene">현재 로비 씬도 다시 로드할지 여부</param>
    /// <returns>종료 또는 씬 전환 요청 접수 여부</returns>
    public bool TryReturnToMetaScene(bool reloadCurrentScene = false)
    {
        RequirePlayMode();

        if (_isTransitioning)
            return false;

        int sceneIndex = GetSceneIndex(_metaScenePath);

        if (_currentRunFlow != null && _currentRunFlow.IsRunning)
            return _currentRunFlow.RequestEndRun(RunEndReason.Manual);

        _returnToMetaRequested = false;

        if (!reloadCurrentScene && SceneManager.GetActiveScene().buildIndex == sceneIndex)
        {
            DetachRunFlow();
            BindLobby(SceneManager.GetActiveScene());

            OnStateChanged?.Invoke();
            return true;
        }

        BeginSceneTransition(sceneIndex, null);
        return true;
    }
}
