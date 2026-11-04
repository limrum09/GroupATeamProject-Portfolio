using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 현재 일일 퀘스트의 읽기 전용 표시 정보
/// </summary>
public readonly struct DailyQuestInfo
{
    public QuestDefinition Definition { get; }
    public string PeriodKey { get; }
    public float CurrentValue { get; }
    public bool IsCompleted { get; }
    public bool IsClaimed { get; }
    public long MetaGoldReward { get; }
    public bool CanClaim { get; }

    /// <summary>
    /// 일일 퀘스트의 현재 표시 정보 구성
    /// </summary>
    /// <param name="definition">표시할 퀘스트 정의</param>
    /// <param name="periodKey">수령 요청에 사용할 일일 주기 키</param>
    /// <param name="currentValue">현재 진행값</param>
    /// <param name="isCompleted">목표 완료 여부</param>
    /// <param name="isClaimed">보상 수령 여부</param>
    /// <param name="metaGoldReward">실제 수령 대상 메타 보상량</param>
    /// <param name="canClaim">현재 수령 요청 가능 여부</param>
    public DailyQuestInfo(QuestDefinition definition, string periodKey, float currentValue, bool isCompleted, bool isClaimed, long metaGoldReward, bool canClaim)
    {
        Definition = definition;
        PeriodKey = periodKey;
        CurrentValue = currentValue;
        IsCompleted = isCompleted;
        IsClaimed = isClaimed;
        MetaGoldReward = metaGoldReward;
        CanClaim = canClaim;
    }
}

/// <summary>
/// 퀘스트 등록과 결과 전달 및 정산과 보상 수령 조율
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class QuestManager : Singleton<QuestManager>
{
    [Header("----- 전체 퀘스트 설정 -----")]
    [SerializeField] QuestDefinitionCatalog _definitionCatalog;

    readonly List<QuestProgress> _quests = new List<QuestProgress>();
    List<QuestRecord> _committed = new List<QuestRecord>();
    List<QuestDefinition> _dailyDefinitions = new List<QuestDefinition>();

#if UNITY_EDITOR
    Func<string, Task> _refreshDailyFromEditor;
#endif

    const int MaxDailyQuestCount = 4;

    string _dailyPeriod;
    long _pendingSuddenReward;
    bool _hasRunResult;
    bool _initialized;
    bool _testMode;
    bool _isRunning;

    public event Action OnChanged;
    public event Action OnDailyDataChanged;

    public int QuestCount => _quests.Count;
    public bool IsInitialized => _initialized;
    public bool CanActivateSuddenQuests => _initialized && _isRunning;
    // 퀘스트 초기화와 런 진행 및 미정산 결과에 따른 작업 차단 상태
    public bool BlocksMetaOperations => !_initialized || _isRunning || _hasRunResult;
    // 정산과 다른 메타 작업을 마친 로비의 보상 수령 가능 상태
    public long PendingSuddenReward => _pendingSuddenReward;
    public bool IsTestMode => _testMode;
    public string DailyPeriod => _dailyPeriod;
    public bool HasPendingRunResult => _hasRunResult;

    void ValidateDefinitions()
    {
        if (_definitionCatalog == null)
            throw new MissingReferenceException("QuestDefinitionCatalog 연결 필요");

        _definitionCatalog.Validate();
    }

    void RequireInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("퀘스트 초기화 필요");
    }

    void ApplyReport(Func<QuestProgress, bool> report)
    {
        RequireInitialized();

        bool changed = false;

        for (int i = _quests.Count - 1; i >= 0; i--)
        {
            QuestProgress quest = _quests[i];

            if (quest.Definition.Kind == QuestKind.Sudden && !_isRunning)
                continue;

            changed |= report(quest);
            changed |= CollectCompletedSuddenQuest(i);
        }

        if (changed)
            PublishChanged();
    }

    void RemoveSuddenQuestAt(int index)
    {
        QuestDefinition runtimeDefinition = _quests[index].Definition;

        _quests.RemoveAt(index);
        Destroy(runtimeDefinition);
    }

    void ClearSuddenQuests()
    {
        for (int i = _quests.Count - 1; i >= 0; i--)
        {
            if (_quests[i].Definition.Kind == QuestKind.Sudden)
                RemoveSuddenQuestAt(i);
        }
    }

    bool CollectCompletedSuddenQuest(int index)
    {
        QuestProgress quest = _quests[index];

        if (quest.Definition.Kind != QuestKind.Sudden || !quest.IsCompleted)
            return false;

        _pendingSuddenReward = checked(_pendingSuddenReward + quest.Definition.MetaGoldReward);

        RemoveSuddenQuestAt(index);
        return true;
    }

    QuestRecord CreateRecord(QuestProgress quest)
    {
        QuestDefinition definition = quest.Definition;

        return new QuestRecord(definition.QuestId, quest.PeriodKey, definition.Kind, definition.DisplayName, quest.CurrentValue, quest.IsCompleted, false, definition.MetaGoldReward);
    }

    List<QuestRecord> BuildProgressRecords()
    {
        List<QuestRecord> records = new List<QuestRecord>(_committed);

        foreach (QuestProgress quest in _quests)
        {
            if (quest.Definition.Kind != QuestKind.Daily)
                continue;

            int index = records.FindIndex(record => record.QuestId == quest.Definition.QuestId && record.PeriodKey == quest.PeriodKey);

            // 기존 완료 및 수령 기록 보존
            if (index >= 0 && records[index].IsCompleted)
                continue;

            QuestRecord updated = CreateRecord(quest);

            if (index >= 0)
                records[index] = updated;
            else
                records.Add(updated);
        }

        return records;
    }

    void RestoreDailyPeriod()
    {
        // 아직 일일 기록이 없는 수동 개발 단계의 최초 주기
        _dailyPeriod = "D:manual-initial";

        for (int i = _committed.Count - 1; i >= 0; i--)
        {
            QuestRecord record = _committed[i];

            if (record.Kind != QuestKind.Daily)
                continue;

            _dailyPeriod = record.PeriodKey;
            return;
        }
    }

    List<QuestRecord> CreateDailyRecords(string periodKey)
    {
        List<QuestRecord> records = _committed.FindAll(record => record.Kind == QuestKind.Daily && record.PeriodKey == periodKey);

        if (records.Count > 0)
            return records;

        List<QuestDefinition> candidates = new List<QuestDefinition>();

        foreach (QuestDefinition definition in _definitionCatalog.Definitions)
        {
            if (definition.Kind == QuestKind.Daily)
                candidates.Add(definition);
        }

        int count = Math.Min(candidates.Count, MaxDailyQuestCount);

        if (candidates.Count > MaxDailyQuestCount)
        {
            // 중복 없는 일일 퀘스트 선택
            for (int i = 0; i < count; i++)
            {
                int randomIndex = UnityEngine.Random.Range(i, candidates.Count);

                QuestDefinition temporary = candidates[i];
                candidates[i] = candidates[randomIndex];
                candidates[randomIndex] = temporary;
            }
        }

        for (int i = 0; i < count; i++)
        {
            QuestDefinition definition = candidates[i];

            records.Add(new QuestRecord(definition.QuestId, periodKey, QuestKind.Daily, definition.DisplayName, 0f, false, false, definition.MetaGoldReward));
        }

        return records;
    }

    void ApplyDailySelection(string periodKey, IReadOnlyList<QuestRecord> records)
    {
        List<QuestDefinition> selected = new List<QuestDefinition>();

        foreach (QuestRecord record in records)
        {
            if (record.Kind != QuestKind.Daily || record.PeriodKey != periodKey)
                continue;

            if (selected.Count == MaxDailyQuestCount)
                break;

            QuestDefinition definition = _definitionCatalog.GetDefinition(record.QuestId);

            if (definition.Kind != QuestKind.Daily)
                throw new InvalidOperationException( $"일일 퀘스트 정의 확인 필요: {record.QuestId}");

            selected.Add(definition);
        }

        _dailyPeriod = periodKey;
        _dailyDefinitions = selected;

        RebuildDailyQuests();
    }

    void RebuildDailyQuests()
    {
        List<QuestProgress> dailyQuests = new List<QuestProgress>(_dailyDefinitions.Count);

        foreach (QuestDefinition definition in _dailyDefinitions)
        {
            QuestRecord record = FindCommitted(definition.QuestId, _dailyPeriod);

            // 최초 발급 후 아직 저장 기록이 없는 경우 진행값 0으로 시작
            dailyQuests.Add(new QuestProgress(definition, _dailyPeriod, record != null ? record.CurrentValue : 0f, record != null && record.IsCompleted));
        }

        _quests.RemoveAll(quest => quest.Definition.Kind == QuestKind.Daily);
        _quests.AddRange(dailyQuests);
    }

    QuestRecord FindCommitted(string questId, string periodKey)
    {
        return _committed.Find(record => record.QuestId == questId && record.PeriodKey == periodKey);
    }

#if UNITY_EDITOR
    [ContextMenu("퀘스트/일일 퀘스트 초기화")]
    async void ResetDailyQuestsFromContext()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Play 모드의 로비에서 실행 필요");

        RequireInitialized();

        string periodKey = "D:manual-" + Guid.NewGuid().ToString("N");

        if (_testMode)
        {
            if (BlocksMetaOperations)
            {
                Debug.LogWarning("런과 정산을 마친 뒤 초기화할 수 있습니다.", this);
                return;
            }

            ApplyPersistentRecords(PrepareDailyRefresh(periodKey), periodKey);
            PublishChanged();
            return;
        }

        if (_refreshDailyFromEditor == null)
            throw new InvalidOperationException("일일 퀘스트 명령 연결 필요");

        await _refreshDailyFromEditor(periodKey);
    }
#endif

    /// <summary>
    /// 실행 중 생성한 돌발 퀘스트와 런 상태 정리
    /// </summary>
    protected override void OnSingletonDestroy()
    {
        ClearSuddenQuests();

        _pendingSuddenReward = 0;
        _hasRunResult = false;
        _isRunning = false;
        _initialized = false;

#if UNITY_EDITOR
        _refreshDailyFromEditor = null;
#endif
    }

    static void ValidateGold(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// 저장과 보상 지급을 사용하지 않는 독립 테스트 초기화
    /// </summary>
    public void InitializeForTests()
    {
        if (_initialized)
        {
            if (!_testMode)
                throw new InvalidOperationException("실제 프로필로 초기화된 퀘스트를 테스트 모드로 변경 불가");

            return;
        }

        ValidateDefinitions();

        RestoreDailyPeriod();
        ApplyDailySelection(_dailyPeriod, CreateDailyRecords(_dailyPeriod));

        _testMode = true;
        _initialized = true;

        PublishChanged();
    }



    /// <summary>
    /// 저장된 일일 주기와 선택 목록으로 실행 상태 복원
    /// </summary>
    /// <param name="records">복원할 현재 주기의 기록</param>
    /// <param name="periodKey">저장된 일일 주기</param>
    public void Initialize(IReadOnlyList<QuestRecord> records, string periodKey)
    {
        if (_initialized)
            throw new InvalidOperationException("퀘스트 프로필 재연결 불가");

        if (records == null)
            throw new ArgumentNullException(nameof(records));

        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("일일 주기 필요", nameof(periodKey));

        if (records.Count > MaxDailyQuestCount)
            throw new ArgumentException("일일 퀘스트 개수 초과", nameof(records));

        ValidateDefinitions();

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (QuestRecord record in records)
        {
            if (record == null || record.Kind != QuestKind.Daily || record.PeriodKey != periodKey || !ids.Add(record.QuestId))
                throw new ArgumentException("일일 기록과 주기 불일치", nameof(records));
        }

        _committed = new List<QuestRecord>(records);

        // 빈 선택 목록도 저장된 결과로 취급하여 재추첨 방지
        ApplyDailySelection(periodKey, _committed);

        _initialized = true;
        PublishChanged();
    }

    /// <summary>
    /// 비정상 종료 시 미저장 진행 폐기
    /// </summary>
    public void AbortRun()
    {
        RequireInitialized();

        _isRunning = false;

        ClearSuddenQuests();

        _pendingSuddenReward = 0;
        _hasRunResult = false;
        _quests.Clear();

        RebuildDailyQuests();
        PublishChanged();
    }

    /// <summary>
    /// 현재 활성 일일 퀘스트의 표시 정보 조회
    /// </summary>
    /// <param name="canClaimRewards">작업 담당자가 확인한 수령 허용 상태</param>
    /// <returns>진행값과 수령 상태를 포함한 일일 퀘스트 목록</returns>
    public IReadOnlyList<DailyQuestInfo> GetDailyQuests(bool canClaimRewards)
    {
        RequireInitialized();

        List<DailyQuestInfo> result = new List<DailyQuestInfo>();

        canClaimRewards &= !_testMode && !BlocksMetaOperations;

        foreach (QuestProgress quest in _quests)
        {
            QuestDefinition definition = quest.Definition;

            if (definition.Kind != QuestKind.Daily)
                continue;

            QuestRecord saved = FindCommitted(definition.QuestId, quest.PeriodKey);

            bool isClaimed = saved != null && saved.IsClaimed;
            long reward = definition.MetaGoldReward;

            result.Add(new DailyQuestInfo(definition, quest.PeriodKey, quest.CurrentValue, quest.IsCompleted, isClaimed, reward, canClaimRewards && quest.IsCompleted && !isClaimed));
        }

        return result;
    }

    /// <summary>
    /// 확정된 퀘스트 상태의 관찰자 알림
    /// </summary>
    public void PublishChanged()
    {
        Delegate[] callbacks = OnChanged?.GetInvocationList();

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
                // 화면 오류 이후에도 확정된 게임 결과와 다른 알림 처리 유지
                Debug.LogException(exception, this);
            }
        }
    }

    /// <summary>
    /// 새 일일 주기의 선택 기록 준비
    /// </summary>
    /// <param name="periodKey">선택할 일일 주기</param>
    /// <returns>지정한 주기의 일일 기록</returns>
    public IReadOnlyList<QuestRecord> PrepareDailyRefresh(string periodKey)
    {
        RequireInitialized();

        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("일일 주기 필요", nameof(periodKey));

        return CreateDailyRecords(periodKey);
    }

    /// <summary>
    /// 정상 종료 시 확정할 일일 진행 기록 준비
    /// </summary>
    /// <returns>현재 진행을 포함한 후보 기록</returns>
    public IReadOnlyList<QuestRecord> CaptureProgressRecords()
    {
        RequireInitialized();
        return BuildProgressRecords();
    }

    /// <summary>
    /// 완료된 일일 퀘스트의 수령 기록과 보상 준비
    /// </summary>
    /// <param name="questId">퀘스트 UID</param>
    /// <param name="periodKey">일일 주기</param>
    /// <param name="records">수령 표시를 반영한 후보 기록</param>
    /// <param name="reward">지급할 메타 보상량</param>
    /// <param name="message">수령 거절 사유</param>
    /// <returns>후보 준비 성공 여부</returns>
    public bool TryPrepareClaim(string questId, string periodKey, out IReadOnlyList<QuestRecord> records, out long reward, out string message)
    {
        RequireInitialized();

        records = null;
        reward = 0;
        message = null;

        if (periodKey != _dailyPeriod)
        {
            message = "일일 퀘스트 주기가 변경되었습니다.";
            return false;
        }

        List<QuestRecord> candidate = BuildProgressRecords();

        int index = candidate.FindIndex(item => item.Kind == QuestKind.Daily && item.QuestId == questId && item.PeriodKey == periodKey);

        if (index < 0)
        {
            message = "수령할 퀘스트 기록이 없습니다.";
            return false;
        }

        QuestRecord record = candidate[index];

        if (!record.IsCompleted || record.IsClaimed)
        {
            message = "완료된 미수령 퀘스트만 수령할 수 있습니다.";
            return false;
        }

        QuestDefinition definition = _definitionCatalog.GetDefinition(questId);

        candidate[index] = record.CreateClaimedRecord();

        records = candidate;
        reward = definition.MetaGoldReward;
        return true;
    }

    /// <summary>
    /// 정산 또는 수령으로 확정된 일일 기록 반영과 변경 알림
    /// </summary>
    /// <param name="records">이 객체가 준비한 현재 주기의 후보 기록</param>
    /// <param name="dailyPeriod">새 일일 주기, 생략 시 현재 주기</param>
    public void ApplyPersistentRecords(IReadOnlyList<QuestRecord> records, string dailyPeriod = null)
    {
        RequireInitialized();

        if (_isRunning)
            throw new InvalidOperationException("런 종료 후 기록 확정 필요");

        if (records == null)
            throw new ArgumentNullException(nameof(records));

        string periodKey = dailyPeriod ?? _dailyPeriod;
        bool changed = _dailyPeriod != periodKey || _committed.Count != records.Count;

        if (!changed)
        {
            foreach (QuestRecord record in records)
            {
                QuestRecord previous = FindCommitted(record.QuestId, periodKey);

                if (previous == null || previous.CurrentValue != record.CurrentValue || previous.IsCompleted != record.IsCompleted || previous.IsClaimed != record.IsClaimed)
                {
                    changed = true;
                    break;
                }
            }
        }

        _committed = new List<QuestRecord>(records);

        // 주기 변경에만 선택 목록과 진행 객체 재구성
        if (dailyPeriod != null)
            ApplyDailySelection(dailyPeriod, _committed);

        _pendingSuddenReward = 0;
        _hasRunResult = false;

        if (changed && !_testMode)
            OnDailyDataChanged?.Invoke();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 기존 Inspector 메뉴의 일일 갱신 요청 연결
    /// </summary>
    /// <param name="refresh">초기화 담당자가 연결한 갱신 요청</param>
    public void BindEditorDailyRefresh(Func<string, Task> refresh)
    {
        _refreshDailyFromEditor = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }
#endif

    /// <summary>
    /// 돌발 퀘스트의 활성화와 진행 허용
    /// </summary>
    public void BeginRun()
    {
        RequireInitialized();

        if (_isRunning)
            return;

        if(BlocksMetaOperations)
            throw new InvalidOperationException("이전 퀘스트 결과의 저장 완료 필요");

        _isRunning = true;
        PublishChanged();
    }

    /// <summary>
    /// 돌발 동작 종료와 완료 결과 보관
    /// </summary>
    public void EndRun()
    {
        RequireInitialized();

        if (!_isRunning)
            return;

        _isRunning = false;
        ClearSuddenQuests();

        _hasRunResult = !_testMode;

        if (_testMode)
            _pendingSuddenReward = 0;

        PublishChanged();
    }

    /// <summary>
    /// 등록된 돌발 퀘스트의 무작위 선택과 실행용 복제
    /// </summary>
    /// <param name="currentGold">발생 당시 현재 골드 잔액</param>
    /// <returns>돌발 퀘스트 생성 여부</returns>
    public bool ActivateRandomSuddenQuest(float currentGold)
    {
        RequireInitialized();

        if (!_isRunning)
            return false;

        ValidateGold(currentGold);

        List<QuestDefinition> candidates = new List<QuestDefinition>();

        foreach (QuestDefinition definition in _definitionCatalog.Definitions)
        {
            if (definition.Kind != QuestKind.Sudden)
                continue;

            bool alreadyActive = _quests.Exists(quest => quest.Definition.Kind == QuestKind.Sudden && quest.Definition.QuestId == definition.QuestId);

            if (!alreadyActive)
                candidates.Add(definition);
        }

        if (candidates.Count == 0)
            return false;

        int selectedIndex = UnityEngine.Random.Range(0, candidates.Count);

        QuestDefinition runtimeDefinition = Instantiate(candidates[selectedIndex]);
        QuestProgress quest = new QuestProgress(runtimeDefinition);

        quest.ReportGoldChanged(currentGold);
        _quests.Add(quest);

        CollectCompletedSuddenQuest(_quests.Count - 1);

        PublishChanged();
        return true;
    }

    /// <summary>
    /// 외부에서 진행 상태를 변경할 수 없는 표시 정보 조회
    /// </summary>
    /// <param name="index">퀘스트 목록 순번</param>
    /// <returns>설정과 현재 진행값 및 목표 완료 여부</returns>
    public void GetQuest(int index, out QuestDefinition definition, out float value, out bool isCompleted)
    {
        QuestProgress quest = _quests[index];

        definition = quest.Definition;
        value = quest.CurrentValue;
        isCompleted = quest.IsCompleted;
    }

    /// <summary>
    /// ID에 해당하는 활성 퀘스트의 진행 상태 조회
    /// </summary>
    /// <param name="questId">조회할 퀘스트 ID</param>
    /// <param name="currentValue">현재 진행값, 없으면 0</param>
    /// <param name="isCompleted">완료 여부</param>
    /// <returns>퀘스트가 없으면 false</returns>
    public bool TryGetQuest(string questId, out float currentValue, out bool isCompleted, out bool isClaimed)
    {
        QuestProgress quest = _quests.Find(quest => quest.Definition.QuestId ==  questId);

        currentValue = 0;
        isCompleted = false;
        isClaimed = false;

        if (quest == null)
            return false;

        currentValue = quest.CurrentValue;
        isCompleted = quest.IsCompleted;

        QuestRecord record = FindCommitted(questId, quest.PeriodKey);
        isClaimed = record != null && record.IsClaimed;

        return true;
    }

    /// <summary>
    /// 다른 프로필 저장에서도 유지할 확정 기록 복사본 제공
    /// </summary>
    /// <returns>진행 중 미확정 변경을 제외한 저장 기록</returns>
    public IReadOnlyList<QuestRecord> CaptureCommittedRecords()
    {
        if (!_initialized)
            throw new InvalidOperationException("퀘스트 기록 초기화 필요");

        return _committed.ToArray();
    }

    /// <summary>
    /// 미정산 런 진행을 제외한 현재 일일 기록 제공
    /// </summary>
    /// <returns>현재 주기의 확정된 저장 데이터</returns>
    public MetaProfileData.DailyQuestData CaptureDailyData()
    {
        RequireInitialized();

        return MetaProfileData.DailyQuestData.FromRecords(_dailyPeriod, _committed);
    }

    /// <summary>
    /// 최초 프로필 생성 전에 저장할 일일 목록 준비
    /// </summary>
    /// <param name="periodKey">최초 일일 주기</param>
    /// <returns>최초 저장용 선택 목록</returns>
    public MetaProfileData.DailyQuestData PrepareInitialDailyData(string periodKey)
    {
        if (_initialized)
            throw new InvalidOperationException("최초 퀘스트 준비 시점 오류");

        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("일일 주기 필요", nameof(periodKey));

        ValidateDefinitions();

        return MetaProfileData.DailyQuestData.FromRecords(periodKey, CreateDailyRecords(periodKey));
    }

    /// <summary>
    /// 저장된 UID와 진행 상태에 현재 정의를 연결하여 기록 복원
    /// </summary>
    /// <param name="data">불러온 일일 퀘스트 데이터</param>
    /// <returns>현재 주기의 실행 기록</returns>
    public IReadOnlyList<QuestRecord> RestoreDailyData(MetaProfileData.DailyQuestData data)
    {
        if (data == null || data.Entries == null)
            throw new ArgumentNullException(nameof(data));

        if (string.IsNullOrWhiteSpace(data.PeriodKey))
            throw new ArgumentException("일일 주기 필요", nameof(data));

        ValidateDefinitions();

        List<QuestRecord> records = new List<QuestRecord>();

        foreach (MetaProfileData.QuestData row in data.Entries)
        {
            if (row == null)
                throw new ArgumentException("빈 일일 기록", nameof(data));

            QuestDefinition definition = _definitionCatalog.GetDefinition(row.QuestId);

            if (definition.Kind != QuestKind.Daily)
                throw new InvalidOperationException($"일일 퀘스트 정의 확인 필요: {row.QuestId}");

            records.Add(new QuestRecord(row.QuestId, data.PeriodKey, QuestKind.Daily, definition.DisplayName, row.CurrentValue, row.IsCompleted, row.IsClaimed, definition.MetaGoldReward));
        }

        return records;
    }

    /// <summary>
    /// 확정된 적 처치 결과 보고
    /// </summary>
    /// <param name="enemy">처치된 적의 정의</param>
    public void ReportEnemyKilled(EnemyDefinition enemy)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));

        ApplyReport(quest => quest.ReportEnemyKilled(enemy));
    }

    /// <summary>
    /// 확정된 골드 획득 결과 보고
    /// </summary>
    /// <param name="source">골드 획득 사유</param>
    /// <param name="amount">실제 획득량</param>
    public void ReportGoldAcquired(GoldGainSource source, float amount)
    {
        ValidateGold(amount);

        if (!Enum.IsDefined(typeof(GoldGainSource), source))
            throw new ArgumentOutOfRangeException(nameof(source));

        if (source == GoldGainSource.TestGrant || amount == 0f)
            return;

        ApplyReport(quest => quest.ReportGoldAcquired(source, amount));
    }

    /// <summary>
    /// 확정된 현재 골드 잔액 보고
    /// </summary>
    /// <param name="currentGold">현재 골드 잔액</param>
    public void ReportGoldChanged(float currentGold)
    {
        ValidateGold(currentGold);

        ApplyReport(quest => quest.ReportGoldChanged(currentGold));
    }

    /// <summary>
    /// 최초 완료된 노드의 클리어 결과 보고
    /// </summary>
    /// <param name="nodeType">클리어한 노드 종류</param>
    /// <param name="rank">클리어한 노드의 Rank</param>
    public void ReportNodeCleared(MapBindings.MapType nodeType, int rank)
    {
        if (!Enum.IsDefined(typeof(MapBindings.MapType), nodeType))
            throw new ArgumentOutOfRangeException(nameof(nodeType));

        if (rank < 0)
            throw new ArgumentOutOfRangeException(nameof(rank));

        ApplyReport(quest => quest.ReportNodeCleared(nodeType, rank));
    }
}