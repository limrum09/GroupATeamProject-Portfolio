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

    [Header("----- 로딩 화면 -----")]
    [Tooltip("DontDestroyOnLoad 유지를 위해 이 오브젝트의 자식으로 배치")]
    [SerializeField] LoadingScreenView _loadingScreen;

    // 로딩 진행률 구간: 프로필 준비 완료 지점
    const float ProfileReadyProgress = 0.4f;

    // 시작 지점부터 남은 구간 중 씬 로드 비중, 런 진입은 동기 월드 생성 비중 반영
    const float MetaSceneLoadShare = 0.8f;
    const float RunSceneLoadShare = 0.6f;

    // 로비 복귀 시 퀘스트 결과, 런 정산 재화와 숙련도를 함께 저장하는 범위
    const MetaSavePart ReturnSaveParts = MetaSavePart.DailyQuests | MetaSavePart.MetaGold | MetaSavePart.Mastery;

    GameFlowController _currentRunFlow;
    RunSettlementView _settlementView;

    // 런 씬 해제 후에도 저장 완료까지 유지하는 정산 결과와 단계별 반영 완료 여부
    RunSettlementService _settlement;
    bool _settlementQuestApplied;
    bool _settlementExperienceApplied;

    bool _isTransitioning;

    // 로딩 화면 표시부터 해제까지의 전체 준비 구간, 씬 로드 이후 저장 단계 포함
    bool _isPreparingScene;

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
    public bool HasPendingSettlement => _settlement != null;

    void LateUpdate()
    {
        if (_returnToMetaRequested && !_isPreparingScene)
        {
            // 런 종료 알림의 다른 구독자 처리 이후 씬 전환
            _returnToMetaRequested = false;
            TryReturnToMetaScene();
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

    bool CanEnterRunScene()
    {
        // 로비 복귀 후 정산 저장 단계에서도 다음 런 진입 차단
        if (!isActiveAndEnabled || _isPreparingScene || !IsInMetaScene)
            return false;

        // 명시적으로 선택한 임시 테스트에서만 메타 준비 조건 제외
        if (_useCharacterTestMode)
            return true;

        if (!_metaProgression.IsReady)
            return false;

        // 반영 도중 중단된 정산도 저장 완료 전까지 다음 런 진입 차단
        return _settlement == null && !_metaProgression.Purchases.BlocksRunEntry && !_metaProgression.Loadouts.BlocksRunEntry && !_metaProgression.QuestActions.BlocksMetaOperations && _metaProgression.Saves.CanAcceptChanges;
    }

    void HandleRunEnded(RunEndReason reason, RunLevelProgression completedRun)
    {
        Debug.Log($"[SceneFlow] 런 종료 사유: {reason}", this);
        _returnToMetaRequested = true;
        _saveQuestOnReturn = !_useCharacterTestMode;

        RunSettlementResult result = _currentRunFlow.LastScoreResult;
        if (result.IsRewardEligible)
        {
            // 런 씬 해제 전에 결과와 캐릭터 식별자 보관
            if (!_useCharacterTestMode)
                PrepareSettlement(result, _currentRunFlow.StartRequest.CharacterId);

            _currentRunFlow.BindSettlementMenu();
            _settlementView.Show(result, RequestSettlementReturn);
            _returnToMetaRequested = false;
        }
    }

    void RequestSettlementReturn()
    {
        _returnToMetaRequested = true;
    }

    void PrepareSettlement(RunSettlementResult result, string characterId)
    {
        // 저장 대기 정산이 남은 상태의 런 진입은 CanEnterRunScene에서 차단
        if (_settlement != null)
            throw new InvalidOperationException($"{name}: 이전 런 정산의 저장 완료 필요");

        _settlementQuestApplied = false;
        _settlementExperienceApplied = false;
        _settlement = new RunSettlementService(result, settled => CommitSettlementAsync(settled, characterId));
    }

    async Task<bool> CommitSettlementAsync(RunSettlementResult result, string characterId)
    {
        MetaProgression progression = _metaProgression.Progression;
        long experience = 0;

        // 지급 전 실패 가능한 숙련도 범위 검증, 재시도에서는 반영 완료 단계 제외
        if (!_settlementExperienceApplied &&
            !progression.TryPrepareMasteryExperience(characterId, result.MetaExperience, out experience))
            throw new InvalidOperationException("숙련도 경험치의 보유 가능 범위 초과");

        if (!_settlementQuestApplied)
        {
            if (!await _metaProgression.QuestActions.ApplyRunResultAsync(result.MetaGold))
                return false;

            _settlementQuestApplied = true;
        }

        if (!_settlementExperienceApplied)
        {
            // 반영 실패 시 변경 없는 값의 저장만 남도록 반영 전에 저장 대상 표시
            _metaProgression.Saves.MarkChanged(MetaSavePart.Mastery);
            progression.ApplyPreparedMasteryExperience(characterId, experience);
            _settlementExperienceApplied = true;

            progression.PublishChanged();
        }

        return await _metaProgression.SaveMetaChangesAsync(ReturnSaveParts);
    }

    async Task<bool> SaveSettlementAsync()
    {
        RunSettlementService settlement = _settlement;

        bool saved = settlement.HasPendingSave
            ? await settlement.RetrySaveAsync()
            : await settlement.SaveAsync();

        if (saved)
        {
            _settlement = null;
            return true;
        }

        // 정산 서비스가 보관한 반영 예외의 기록, 결과는 재시도를 위해 유지
        if (settlement.LastError != null && this != null)
            Debug.LogException(settlement.LastError, this);

        return false;
    }

    void BindLobby(Scene scene)
    {
        LobbyManager lobby = FindRootEntry<LobbyManager>(scene);
        lobby.Bind(_metaProgression, this);
    }

    void BeginSceneTransition(int sceneIndex, RunStartRequest request, float startProgress)
    {
        if (!isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 활성 SceneFlowController 필요");

        _isTransitioning = true;
        _isPreparingScene = true;

        StartCoroutine(LoadSceneRoutine(sceneIndex, request, startProgress));

        OnStateChanged?.Invoke();
    }

    void DetachRunFlow()
    {
        if (_currentRunFlow != null)
            _currentRunFlow.OnRunEnded -= HandleRunEnded;

        _currentRunFlow = null;
        _settlementView = null;
    }

    IEnumerator LoadSceneRoutine(int sceneIndex, RunStartRequest request, float startProgress)
    {
        // 시작 조건이 없는 전환은 로비 복귀 경로
        bool returnsToMeta = request == null;
        string loadStage = returnsToMeta ? "로비 불러오는 중..." : "모험 불러오는 중...";
        float loadedProgress = Mathf.Lerp(startProgress, 1f, returnsToMeta ? MetaSceneLoadShare : RunSceneLoadShare);

        try
        {
            try
            {
                DetachRunFlow();

                if (_loadingScreen.IsVisible)
                    _loadingScreen.SetStage(loadStage, startProgress);
                else
                    _loadingScreen.Show(loadStage, startProgress);

                // 현재 씬 해제 전 로딩 화면 렌더링
                yield return null;

                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);

                if (operation == null)
                    throw new InvalidOperationException($"{name}: 씬 로드 작업 생성 실패");

                while (!operation.isDone)
                {
                    // Unity 로드 진행률 0.9 이후는 씬 활성화 구간
                    float ratio = Mathf.Clamp01(operation.progress / 0.9f);
                    _loadingScreen.SetStage(loadStage, Mathf.Lerp(startProgress, loadedProgress, ratio));
                    yield return null;
                }

                Scene loadedScene = SceneManager.GetSceneByBuildIndex(sceneIndex);

                if (returnsToMeta)
                {
                    _loadingScreen.SetStage("로비 준비 중...", loadedProgress);
                    BindLobby(loadedScene);
                }
                else
                {
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

                    _loadingScreen.SetStage("월드 생성 중...", loadedProgress);

                    // 동기 월드 생성 중 정지되는 표시를 씬 로드 완료 지점까지 미리 채움
                    while (!_loadingScreen.IsFillSettled)
                        yield return null;

                    // 동기 월드 생성 전 단계 문구 렌더링
                    yield return null;

                    if (runFlow == null || !runFlow.isActiveAndEnabled)
                        throw new InvalidOperationException($"{name}: 런 초기화 전 진입점 해제 또는 비활성화");

                    _currentRunFlow = runFlow;
                    _currentRunFlow.OnRunEnded += HandleRunEnded;
                    _currentRunFlow.BeginRun(request);

                    // 활성 런을 가리지 않도록 보간 없이 완료 표시 후 한 프레임만 유지
                    _loadingScreen.CompleteFill();
                    yield return null;
                }
            }
            finally
            {
                _isTransitioning = false;

                OnStateChanged?.Invoke();
            }

            if (returnsToMeta)
            {
                // 로비 복귀 정산 저장, IsInMetaScene 상태에서만 저장 허용이므로 전환 해제 이후 실행
                if (_saveQuestOnReturn)
                {
                    _saveQuestOnReturn = false;
                    _loadingScreen.SetStage("모험 결과 저장 중...", loadedProgress);

                    Task<bool> save = RetryReturnedQuestSaveAsync();

                    while (!save.IsCompleted)
                        yield return null;

                    // 실패 시 결과 유지, 로비의 기존 재시도 상태로 처리
                    if (save.IsFaulted)
                        Debug.LogException(save.Exception.GetBaseException(), this);
                    else if (save.IsCanceled || !save.Result)
                        Debug.LogWarning(_metaProgression.SaveError ?? "런 정산 결과를 저장하지 못했습니다.", this);
                }

                // 정산 저장 이후 서버 날짜 기준 일일 주기 확인, 실패 시 기존 주기 유지 후 다음 로비 진입에서 재확인
                if (!_useCharacterTestMode)
                {
                    _loadingScreen.SetStage("일일 퀘스트 확인 중...", loadedProgress);

                    Task<bool> refresh = _metaProgression.RefreshDailyQuestsAsync();

                    while (!refresh.IsCompleted)
                        yield return null;

                    if (refresh.IsFaulted)
                        Debug.LogException(refresh.Exception.GetBaseException(), this);
                    else if (refresh.IsCanceled || !refresh.Result)
                        Debug.LogWarning(_metaProgression.SaveError ?? "일일 퀘스트 주기를 갱신하지 못했습니다.", this);
                }

                // 준비가 끝난 로비 표시 전 진행 바 완료 연출
                _loadingScreen.SetStage("준비 완료", 1f);

                while (!_loadingScreen.IsFillSettled)
                    yield return null;
            }
        }
        finally
        {
            // 예외 경로는 완료 연출 없이 즉시 해제
            _isPreparingScene = false;
            _loadingScreen.Hide();

            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 사용자별 초기화 작업 공유와 명시적인 실패 재시도
    /// </summary>
    /// <param name="retryFailed">완료된 실패 작업을 다시 시작할지 여부</param>
    /// <param name="userId">Firebase UID, 생략 시 기존 작업 또는 로컬 테스트 프로필 사용</param>
    /// <returns>초기화 성공 여부</returns>
    Task<bool> EnsureInitializedAsync(bool retryFailed = false, string userId = null)
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

        if (_loadingScreen == null)
            throw new MissingReferenceException($"{name}: LoadingScreenView 연결 필요");

        if (!_loadingScreen.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 씬 전환 후 유지를 위해 LoadingScreenView를 자식으로 배치 필요");

        int metaIndex = GetSceneIndex(_metaScenePath);
        int runIndex = GetSceneIndex(_runScenePath);

        if (metaIndex == runIndex)
            throw new InvalidOperationException($"{name}: 서로 다른 메타 씬과 런 씬 지정 필요");

        DontDestroyOnLoad(gameObject);

        // 모든 구성 검사가 성공한 뒤 준비 완료 처리
        _isFlowInitialized = true;
    }

    /// <summary>
    /// 로딩 화면 아래에서 사용자 프로필 준비 후 로비 씬 전환 시작
    /// </summary>
    /// <param name="userId">Firebase UID, null이면 로컬 테스트 프로필</param>
    /// <returns>프로필 준비 성공과 로비 전환 시작 여부</returns>
    public async Task<bool> EnterMetaSceneAsync(string userId = null)
    {
        RequirePlayMode();
        Initialize();

        if (_isPreparingScene || (_currentRunFlow != null && _currentRunFlow.IsRunning))
            throw new InvalidOperationException($"{name}: 진행 중인 씬 준비 또는 런 종료 후 요청 필요");

        _isPreparingScene = true;
        _loadingScreen.Show("프로필 불러오는 중...", 0f);

        bool transitionStarted = false;

        try
        {
            if (!await EnsureInitializedAsync(retryFailed: true, userId: userId) || this == null)
                return false;

            BeginSceneTransition(GetSceneIndex(_metaScenePath), null, ProfileReadyProgress);
            transitionStarted = true;
            return true;
        }
        finally
        {
            // 전환 시작 이후의 화면 해제는 LoadSceneRoutine 담당
            if (!transitionStarted && this != null)
            {
                _isPreparingScene = false;
                _loadingScreen.Hide();

                OnStateChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 정상 런 결과와 런 정산 보상 반영 후 일일 기록, 메타 재화, 숙련도 저장
    /// 정산 대기 중이면 같은 정산 결과로 저장 재시도
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
            if (_settlement != null)
                return await SaveSettlementAsync();

            // 보상 대상이 아닌 종료는 퀘스트 결과만 반영
            if (actions.HasPendingRunResult && !await actions.ApplyRunResultAsync())
                return false;

            return await _metaProgression.SaveMetaChangesAsync(ReturnSaveParts);
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

        BeginSceneTransition(sceneIndex, request, 0f);
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

        if (_isTransitioning || _isPreparingScene)
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

        BeginSceneTransition(sceneIndex, null, 0f);
        return true;
    }
}
