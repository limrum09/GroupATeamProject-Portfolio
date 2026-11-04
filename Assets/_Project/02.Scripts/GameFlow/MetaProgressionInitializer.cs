using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

/// <summary>
/// 로컬 메타 프로필 준비 상태
/// </summary>
public enum MetaInitializationState
{
    NotStarted,
    Loading,
    Ready,
    Failed
}

/// <summary>
/// 명시적인 초기화 요청에 따른 메타 성장 생성과 수명 관리
/// </summary>
[DisallowMultipleComponent]
public sealed class MetaProgressionInitializer : MonoBehaviour
{
    // 최초 시작 씬 외의 중복 배치 검사
    static MetaProgressionInitializer _activeInitializer;

    [Header("----- 메타 강화 JSON -----")]
    [SerializeField] TextAsset _upgradesJson;
    [SerializeField] TextAsset _ranksJson;
    [SerializeField] TextAsset _effectsJson;

    [Header("----- 프로필 저장소 -----")]
    [SerializeField] MetaProfileStoreProvider _storeProvider;

    [Header("----- 숙련도 레벨표 -----")]
    [SerializeField] MasteryLevelDefinition _integratedMasteryDefinition;
    [SerializeField] MasteryLevelDefinition _characterMasteryDefinition;

    [Header("----- 캐릭터 성장 설정 -----")]
    [SerializeField]
    CharacterGrowthDefinition[] _characters = Array.Empty<CharacterGrowthDefinition>();

    MetaProgression _progression;
    MetaWallet _wallet;
    MetaUpgradePurchaseService _purchases;
    MetaLoadoutService _loadouts;
    QuestActionService _questActions;

    QuestManager _quests;
    MetaProfileSaveCoordinator _saves;
    ApplicationExitService _exit;
    IMetaProfileStore _store;
    LocalStoryUnlockStore _storyUnlocks;

    MetaProfileData _initialProfile;
    string _boundUserId;

    bool _destroyed;

    public event Action OnStateChanged;

    public MetaInitializationState State { get; private set; }
    public string ErrorMessage { get; private set; }
    public static LocalStoryUnlockStore ActiveStoryUnlockStore =>
        _activeInitializer != null && _activeInitializer.IsReady ? _activeInitializer._storyUnlocks : null;

    public bool IsReady => State == MetaInitializationState.Ready;
    public MetaProfileSaveCoordinator Saves => _saves;
    public bool IsExiting => _exit != null && _exit.IsProcessing;
    public string SaveError => _exit?.ErrorMessage ?? _saves?.ErrorMessage ?? _questActions?.ErrorMessage;

    // 메타 재화 잔액을 소유한 전용 지갑
    public MetaWallet Wallet
    {
        get
        {
            if (_wallet == null)
                throw new InvalidOperationException($"{name}: 메타 구매 초기화 완료 후 사용 필요");

            return _wallet;
        }
    }
    
    // 준비된 메타 강화 구매 서비스
    public MetaUpgradePurchaseService Purchases
    {
        get
        {
            if (_purchases == null)
                throw new InvalidOperationException($"{name}: 메타 구매 초기화 완료 후 사용 필요");

            return _purchases;
        }
    }

    // 정의 로딩과 구매 기록 복원을 완료한 메타 성장 인스턴스
    public MetaProgression Progression
    {
        get
        {
            if (_progression == null)
                throw new InvalidOperationException($"{name}: 메타 성장 초기화 완료 후 사용 필요");

            return _progression;
        }
    }

    public MetaLoadoutService Loadouts
    {
        get
        {
            if (_loadouts == null)
                throw new InvalidOperationException($"{name}: 메타 프로필 초기화 완료 후 사용 필요");

            return _loadouts;
        }
    }

    public QuestActionService QuestActions
    {
        get
        {
            if (_questActions == null)
                throw new InvalidOperationException(
                    "메타 프로필 초기화 완료 후 사용 필요");

            return _questActions;
        }
    }

    void Awake()
    {
        if (_activeInitializer != null && _activeInitializer != this)
            throw new InvalidOperationException($"{name}: MetaProgressionInitializer는 최초 시작 씬에 하나만 배치 필요");

        _activeInitializer = this;
        DontDestroyOnLoad(gameObject);

        _exit = new ApplicationExitService(SaveBeforeExitAsync, QuitApplication);

        _exit.OnChanged += PublishStateChanged;
    }

    void OnDestroy()
    {
        _destroyed = true;

        if (_exit != null)
        {
            _exit.OnChanged -= PublishStateChanged;
            _exit.Dispose();
        }

        if (_saves != null)
            _saves.Dispose();

        if (_questActions != null)
            _questActions.Dispose();

        _questActions = null;

        if (_activeInitializer == this)
            _activeInitializer = null;

        _progression = null;
        _purchases = null;
        _wallet = null;
        _loadouts = null;
    }

    void PublishStateChanged()
    {
        if (_destroyed)
            return;

        Delegate[] callbacks = OnStateChanged?.GetInvocationList();

        if (callbacks == null)
            return;

        foreach (Action callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    bool HasBlockingOperation()
    {
        return _purchases.IsProcessing || _loadouts.IsProcessing || _questActions.IsProcessing || _purchases.IsFaulted || _loadouts.IsFaulted || _questActions.IsFaulted || _saves.IsSaving;
    }

    async Task<bool> SaveBeforeExitAsync()
    {
        if (State == MetaInitializationState.Loading)
            return false;

        if (!IsReady)
            return true;

        if (HasBlockingOperation() || _quests.HasPendingRunResult)
            return false;

        // 종료로 현재 런을 정산하지 않고 이미 확정된 변경만 저장
        if (_saves.HasPendingSave && !await _saves.RetryAsync())
            return false;

        return await _saves.SaveAsync();
    }
   
    void ValidateReferences()
    {
        if (_storeProvider == null)
            throw new MissingReferenceException($"{name}: 프로필 저장소 제공자 연결 필요");

        if (_upgradesJson == null || _ranksJson == null || _effectsJson == null)
            throw new MissingReferenceException($"{name}: 강화 목록, 구매 단계, 효과 JSON 연결 필요");

        if (_characters == null || _characters.Length == 0)
            throw new InvalidOperationException($"{name}: 하나 이상의 캐릭터 성장 설정 필요");

        for (int i = 0; i < _characters.Length; i++)
        {
            if (_characters[i] == null)
                throw new MissingReferenceException($"{name}: Characters[{i}] 성장 설정 연결 필요");
        }

        if (_integratedMasteryDefinition == null || _characterMasteryDefinition == null)
            throw new MissingReferenceException($"{name}: 통합 숙련도와 캐릭터 숙련도 레벨표 연결 필요");

        _integratedMasteryDefinition.Validate();
        _characterMasteryDefinition.Validate();
    }

    /// <summary>
    /// 선택한 저장소의 프로필 복원과 메타 기능 초기화
    /// </summary>
    /// <param name="isLobbyReady">현재 로비의 작업 허용 여부 확인 함수</param>
    /// <param name="userId">사용자 UID, null이면 로컬 테스트 프로필</param>
    /// <returns>전체 초기화 성공 여부</returns>
    public async Task<bool> InitializeAsync(Func<bool> isLobbyReady, string userId = null)
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 초기화 필요");

        if (_activeInitializer != this)
            throw new InvalidOperationException(
                $"{name}: 정상 등록된 초기화 오브젝트에서 호출 필요");

        if (isLobbyReady == null)
            throw new ArgumentNullException(nameof(isLobbyReady));

        if (State == MetaInitializationState.Ready)
        {
            if (!string.Equals(_boundUserId, userId, StringComparison.Ordinal))
                throw new InvalidOperationException("실행 중 프로필 사용자 변경 불가");

            return true;
        }

        if (State == MetaInitializationState.Loading)
            throw new InvalidOperationException($"{name}: 기존 초기화 완료 후 요청 필요");

        if (userId != null && string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("유효한 사용자 ID 필요", nameof(userId));

        State = MetaInitializationState.Loading;
        ErrorMessage = null;

        try
        {
            PublishStateChanged();
            ValidateReferences();

            IReadOnlyDictionary<string, MetaUpgradeData> upgrades = MetaUpgradeJsonLoader.Load(_upgradesJson, _ranksJson, _effectsJson);

            MetaProgression progression = new MetaProgression(_characters, upgrades, _integratedMasteryDefinition, _characterMasteryDefinition);

            if (_store == null)
            {
                _boundUserId = userId;
                _store = _storeProvider.CreateStore(userId) ?? throw new InvalidOperationException("프로필 저장소 생성 결과 필요");
            }
            else if (!string.Equals(_boundUserId, userId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("초기화 재시도 중 사용자 변경 불가");
            }

            _storyUnlocks = new LocalStoryUnlockStore(LocalMetaProfileStoreProvider.GetProfileDirectory(userId));

            QuestManager quests = QuestManager.Instance;

            if (quests.IsInitialized)
                throw new InvalidOperationException("이미 초기화된 퀘스트에 재연결 불가");

            MetaProfileData data = _initialProfile;
            bool requiresSave = data != null;

            if (data == null)
            {
                MetaProfileLoadResult loaded = await _store.LoadAsync();

                if (_destroyed)
                    return false;

                if (loaded.Status == MetaProfileLoadStatus.Missing)
                {
                    data = new MetaProfileData
                    {
                        MetaGold = 0,
                        Purchases = Array.Empty<MetaProfileData.PurchaseData>(),
                        Loadouts = Array.Empty<MetaProfileData.LoadoutData>(),
                        Mastery = Array.Empty<MetaProfileData.MasteryData>(),
                        DailyQuests = quests.PrepareInitialDailyData("D:manual-initial")
                    };

                    requiresSave = true;
                }
                else if (loaded.Status == MetaProfileLoadStatus.Loaded)
                {
                    data = loaded.Data;
                    requiresSave = loaded.RequiresMigration;

                    if (loaded.RequiresMigration && data.DailyQuests.Entries.Length == 0)
                        data.DailyQuests = quests.PrepareInitialDailyData(data.DailyQuests.PeriodKey);
                }
                else
                {
                    throw new InvalidOperationException($"프로필 불러오기 실패 ({loaded.Status}): {loaded.Message}");
                }

                if (requiresSave)
                    _initialProfile = data;
            }

            progression.RestorePurchases(data.Purchases.Select(row => new MetaUpgradeRecord( row.CharacterId, row.UpgradeId, row.Rank)).ToArray());
            progression.RestoreLoadouts(data.Loadouts.Select(row => new MetaLoadoutRecord(row.CharacterId, row.Slots)).ToArray());
            progression.RestoreMastery(data.Mastery.Select(row => new MetaMasteryRecord(row.CharacterId, row.TotalExperience)).ToArray());

            IReadOnlyList<QuestRecord> restoredQuests = quests.RestoreDailyData(data.DailyQuests);

            if (requiresSave)
            {
                if (!await _store.SaveAsync(MetaSaveChanges.FromProfile(data)))
                    throw new IOException("최초 프로필 또는 형식 이전 저장 실패");

                if (_destroyed)
                    return false;

                _initialProfile = null;
            }

            MetaWallet wallet = new MetaWallet(data.MetaGold);

            quests.Initialize(restoredQuests, data.DailyQuests.PeriodKey);

            MetaProfileSaveCoordinator saves = new MetaProfileSaveCoordinator(progression, wallet, quests, _store.SaveAsync, () => !_destroyed && !_exit.IsProcessing);

            MetaUpgradePurchaseService purchases = new MetaUpgradePurchaseService(progression, wallet, saves.MarkChanged, () => !_destroyed && IsReady && saves.CanAcceptChanges && !Loadouts.BlocksRunEntry && !QuestActions.BlocksMetaOperations && isLobbyReady());

            MetaLoadoutService loadouts = new MetaLoadoutService(progression, purchases, saves.MarkChanged, () => !_destroyed && IsReady && saves.CanAcceptChanges && !QuestActions.BlocksMetaOperations && isLobbyReady());

            QuestActionService questActions = new QuestActionService(quests, wallet, saves, () => !_destroyed && IsReady && isLobbyReady() && !Purchases.BlocksRunEntry && !Loadouts.BlocksRunEntry);

#if UNITY_EDITOR
            quests.BindEditorDailyRefresh(async periodKey =>
            {
                if (!await questActions.RefreshDailyQuestsAsync(periodKey))
                {
                    Debug.LogWarning(questActions.ErrorMessage, quests);
                    return;
                }

                if (!await SaveMetaChangesAsync(MetaSavePart.DailyQuests))
                    Debug.LogWarning(SaveError, quests);
            });
#endif

            _progression = progression;
            _wallet = wallet;
            _quests = quests;
            _saves = saves;
            _purchases = purchases;
            _loadouts = loadouts;
            _questActions = questActions;

            State = MetaInitializationState.Ready;
        }
        catch (Exception exception)
        {
            if (_destroyed)
                return false;

            ErrorMessage = exception.Message;
            State = MetaInitializationState.Failed;

            Debug.LogException(exception, this);
        }

        PublishStateChanged();
        return IsReady;
    }

    /// <summary>
    /// 지정한 저장 대상의 미저장 변경 기록
    /// </summary>
    /// <param name="requestedParts">이번 저장 지점에서 허용한 대상</param>
    /// <returns>요청 대상의 저장 완료 여부</returns>
    public async Task<bool> SaveMetaChangesAsync(MetaSavePart requestedParts = MetaSavePart.All)
    {
        if (!IsReady || HasBlockingOperation() || _quests.BlocksMetaOperations)
            return false;

        if (_saves.HasPendingSave)
        {
            if ((_saves.PendingParts & ~requestedParts) != MetaSavePart.None)
                return false;

            if (!await _saves.RetryAsync())
                return false;
        }

        return await _saves.SaveAsync(requestedParts);
    }

    /// <summary>
    /// 명시적인 저장 후 애플리케이션 종료 요청
    /// </summary>
    [ContextMenu("프로필/저장 후 게임 종료")]
    public async void RequestApplicationExit()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 중에 종료 요청 필요");

        await _exit.RequestExitAsync();
    }
}