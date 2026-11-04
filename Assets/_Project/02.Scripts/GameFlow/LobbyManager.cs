using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// 로비 선택 상태와 메타 성장 조회 및 런 시작 요청 조율
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyManager : MonoBehaviour
{
    [Header("----- 선택 가능한 캐릭터 -----")]
    [SerializeField]
    LobbyCharacterDefinition[] _characters = Array.Empty<LobbyCharacterDefinition>();

    [Header("----- 최초 선택 -----")]
    [SerializeField, Min(0)] int _defaultCharacterIndex;
    [SerializeField] Difficulty _defaultDifficulty = Difficulty.Normal;

    IReadOnlyList<LobbyCharacterDefinition> _availableCharacters;

    MetaProgressionInitializer _metaInitializer;
    SceneFlowController _sceneFlow;
    MetaProgression _progression;

    MetaUpgradePurchaseService _purchases;
    MetaLoadoutService _loadouts;
    QuestActionService _questActions;
    MetaProfileSaveCoordinator _saves;
    LobbyCharacterDefinition _selectedCharacter;
    Difficulty _selectedDifficulty;

    bool _savingBoundary;
    bool _startingRun;
    string _boundaryError;

    public event Action OnChanged;

    const MetaSavePart QuestSaveParts = MetaSavePart.DailyQuests | MetaSavePart.MetaGold;

    public IReadOnlyList<LobbyCharacterDefinition> Characters => _availableCharacters;
    public LobbyCharacterDefinition SelectedCharacter => _selectedCharacter;
    public Difficulty SelectedDifficulty => _selectedDifficulty;
    public bool IsMetaReady => _metaInitializer != null && _metaInitializer.IsReady && _progression != null && _purchases != null && _loadouts != null && _questActions != null && _saves != null;
    public bool HasMetaLoadFailed => _metaInitializer != null && _metaInitializer.State == MetaInitializationState.Failed;
    public bool IsCharacterTestMode => _sceneFlow != null && _sceneFlow.IsCharacterTestMode;
    public bool CanChangeSelection => isActiveAndEnabled && !_savingBoundary && !_startingRun && _sceneFlow != null && _sceneFlow.IsInMetaScene && (IsCharacterTestMode || (IsMetaReady && !_metaInitializer.IsExiting && !_purchases.BlocksRunEntry && !_loadouts.BlocksRunEntry));
    public bool CanStartRun => CanChangeSelection && _sceneFlow.CanStartRun;
    public bool CanEditMetaLoadout => CanChangeSelection && !IsCharacterTestMode && _saves.CanAcceptChanges && !_questActions.BlocksMetaOperations;
    public string MetaLoadoutError => IsMetaReady ? _loadouts.ErrorMessage : null;
    public string MetaSaveError => _boundaryError ?? (IsMetaReady ? _metaInitializer.SaveError : null);
    public long MetaBalance => _purchases.Balance;
    public bool IsMetaProcessing => _savingBoundary || _startingRun || (IsMetaReady && (_purchases.IsProcessing || _loadouts.IsProcessing || _questActions.IsProcessing || _saves.IsSaving || _metaInitializer.IsExiting));
    public bool HasPendingMetaSave => IsMetaReady && _saves.HasPendingSave;
    public bool HasMetaOperationFault => IsMetaReady && (_purchases.IsFaulted || _loadouts.IsFaulted || _questActions.IsFaulted);
    public bool CanRetryMetaSave => IsMetaReady && !IsCharacterTestMode && _sceneFlow.IsInMetaScene && !IsMetaProcessing && !HasMetaOperationFault && (_saves.HasChanges || _questActions.HasPendingRunResult);
    public bool CanRetryQuestSave => CanRetryMetaSave && (_saves.PendingParts & ~QuestSaveParts) == MetaSavePart.None && (_questActions.HasPendingRunResult || (_saves.ChangedParts & QuestSaveParts) != MetaSavePart.None);

    public QuestActionService QuestActions
    {
        get
        {
            RequireMetaReady();
            return _questActions;
        }
    }

    /// <summary>
    /// 메타 강화 화면에 표시할 계정 공통 숙련도 레벨
    /// </summary>
    public int IntegratedMasteryLevel
    {
        get
        {
            RequireMetaReady();
            return _progression.IntegratedMasteryLevel;
        }
    }

    void Awake()
    {
        if (_characters == null || _characters.Length == 0)
            throw new InvalidOperationException($"{name}: 선택 가능한 캐릭터 설정 필요");

        if (_defaultCharacterIndex < 0 || _defaultCharacterIndex >= _characters.Length)
            throw new InvalidOperationException($"{name}: 기본 캐릭터 인덱스 범위 확인 필요");

        HashSet<string> characterIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (LobbyCharacterDefinition character in _characters)
        {
            if (character == null)
                throw new MissingReferenceException($"{name}: 캐릭터 선택 설정 연결 필요");

            character.Validate();

            if (!characterIds.Add(character.CharacterId))
                throw new InvalidOperationException($"{name}: 중복 캐릭터 ID: {character.CharacterId}");
        }

        RunStartRequest.ValidateDifficulty(_defaultDifficulty);

        _availableCharacters = Array.AsReadOnly(_characters);
        _selectedCharacter = _characters[_defaultCharacterIndex];
        _selectedDifficulty = _defaultDifficulty;
    }

    void OnDestroy()
    {
        if (_metaInitializer != null)
            _metaInitializer.OnStateChanged -= HandleMetaStateChanged;

        if (_sceneFlow != null)
            _sceneFlow.OnStateChanged -= HandleChanged;

        if (_progression != null)
            _progression.OnChanged -= HandleChanged;

        if (_purchases != null)
            _purchases.OnChanged -= HandleChanged;

        if (_loadouts != null)
            _loadouts.OnChanged -= HandleChanged;

        if (_questActions != null)
            _questActions.OnChanged -= HandleChanged;

        if (_saves != null)
            _saves.OnChanged -= HandleChanged;
    }

    void HandleMetaStateChanged()
    {
        if (_saves == null && _metaInitializer.IsReady)
        {
            _saves = _metaInitializer.Saves;
            _saves.OnChanged += HandleChanged;
        }

        if (_progression == null && _metaInitializer.IsReady)
        {
            MetaProgression progression = _metaInitializer.Progression;

            // 같은 ID에 다른 성장 에셋을 연결한 구성 오류 검사
            foreach (LobbyCharacterDefinition character in _characters)
            {
                RunGrowthSnapshot snapshot = progression.CreateRunSnapshot(character.CharacterId);

                if (snapshot.GrowthDefinition != character.GrowthDefinition)
                    throw new InvalidOperationException($"{name}: 메타와 로비의 성장 에셋 불일치: " + character.CharacterId);
            }

            _progression = progression;
            _progression.OnChanged += HandleChanged;
        }

        if (_purchases == null && _metaInitializer.IsReady)
        {
            _purchases = _metaInitializer.Purchases;
            _purchases.OnChanged += HandleChanged;
        }

        if (_loadouts == null && _metaInitializer.IsReady)
        {
            _loadouts = _metaInitializer.Loadouts;
            _loadouts.OnChanged += HandleChanged;
        }

        if (_questActions == null && _metaInitializer.IsReady)
        {
            _questActions = _metaInitializer.QuestActions;
            _questActions.OnChanged += HandleChanged;
        }

        HandleChanged();
    }

    void HandleChanged()
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
                Debug.LogException(exception, this);
            }
        }
    }

    void RequireMetaReady()
    {
        if (!IsMetaReady)
            throw new InvalidOperationException($"{name}: 메타 성장 초기화 완료 후 조회 필요");
    }

    /// <summary>
    /// 로비 씬에 애플리케이션 수명의 메타와 씬 전환 담당 연결
    /// </summary>
    /// <param name="metaInitializer">기존 메타 인스턴스를 소유한 초기화 담당</param>
    /// <param name="sceneFlow">로비와 런 씬 전환 담당</param>
    public void Bind(MetaProgressionInitializer metaInitializer, SceneFlowController sceneFlow)
    {
        if (sceneFlow == null)
            throw new ArgumentNullException(nameof(sceneFlow));        

        if (_sceneFlow != null)
        {
            if (_metaInitializer != metaInitializer || _sceneFlow != sceneFlow)
                throw new InvalidOperationException($"{name}: 다른 메타 또는 씬 전환 담당으로 재연결 불가");

            return;
        }

        _metaInitializer = metaInitializer;
        _sceneFlow = sceneFlow;

        _sceneFlow.OnStateChanged += HandleChanged;

        if(_metaInitializer != null)
        {
            _metaInitializer.OnStateChanged += HandleMetaStateChanged;

            // 로비보다 먼저 초기화된 경우에도 현재 상태 연결
            HandleMetaStateChanged();
        }
        else
        {
            HandleChanged();
        }
    }

    /// <summary>
    /// 캐릭터 선택 변경과 변경 알림
    /// </summary>
    /// <param name="characterId">선택할 캐릭터 식별자</param>
    /// <returns>선택 요청 수락 여부</returns>
    public bool TrySelectCharacter(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 ID 필요", nameof(characterId));

        if (!CanChangeSelection)
            return false;

        foreach (LobbyCharacterDefinition character in _characters)
        {
            if (character.CharacterId != characterId)
                continue;

            if (_selectedCharacter == character)
                return true;

            _selectedCharacter = character;

            HandleChanged();
            return true;
        }

        throw new ArgumentException($"로비에 등록되지 않은 캐릭터: {characterId}", nameof(characterId));
    }

    /// <summary>
    /// 이번 런의 난이도 선택 변경
    /// </summary>
    /// <param name="difficulty">선택할 난이도</param>
    /// <returns>선택 요청 수락 여부</returns>
    public bool TrySelectDifficulty(Difficulty difficulty)
    {
        RunStartRequest.ValidateDifficulty(difficulty);

        if (!CanChangeSelection)
            return false;

        if (_selectedDifficulty == difficulty)
            return true;

        _selectedDifficulty = difficulty;

        HandleChanged();
        return true;
    }

    /// <summary>
    /// 현재 선택한 캐릭터의 숙련도 레벨과 구간 진행량 조회
    /// </summary>
    /// <param name="level">캐릭터 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 숙련도</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 숙련도, 최대 레벨은 0</param>
    public void GetSelectedCharacterMasteryProgress(out int level, out long currentExperience, out long requiredExperience)
    {
        RequireMetaReady();

        _progression.GetCharacterMasteryProgress(_selectedCharacter.CharacterId, out level, out currentExperience, out requiredExperience);
    }

    /// <summary>
    /// 통합 숙련도 레벨과 현재 경험치 구간의 진행량 조회
    /// </summary>
    /// <param name="level">통합 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨 필요 경험치, 최대 레벨은 0</param>
    public void GetIntegratedMasteryProgress(out int level, out long currentExperience, out long requiredExperience)
    {
        RequireMetaReady();

        _progression.GetIntegratedMasteryProgress(out level, out currentExperience, out requiredExperience);
    }

    /// <summary>
    /// 선택 캐릭터에게 표시할 메타 강화 목록 조회
    /// </summary>
    /// <returns>표시 순서로 정렬된 강화 목록</returns>
    public IReadOnlyList<MetaUpgradeData> GetUpgrades()
    {
        RequireMetaReady();

        return _progression.GetUpgrades(_selectedCharacter.CharacterId);
    }

    /// <summary>
    /// 선택 캐릭터의 현재 강화 구매 단계 조회
    /// </summary>
    /// <param name="upgradeId">조회할 강화 식별자</param>
    /// <returns>현재 구매 단계, 미구매 상태는 0</returns>
    public int GetPurchasedRank(string upgradeId)
    {
        RequireMetaReady();

        return _progression.GetPurchasedRank(_selectedCharacter.CharacterId, upgradeId);
    }

    /// <summary>
    /// 실행 모드에 따른 선택 캐릭터의 런 성장 스냅샷 생성
    /// </summary>
    /// <returns>기본 성장과 적용할 메타 보너스를 포함한 스냅샷</returns>
    public RunGrowthSnapshot GetGrowthPreview()
    {
        if (IsCharacterTestMode)
        {
            return new RunGrowthSnapshot(_selectedCharacter.GrowthDefinition, default, default);
        }

        RequireMetaReady();

        return _progression.CreateRunSnapshot(_selectedCharacter.CharacterId);
    }

    /// <summary>
    /// 선택 캐릭터의 장착 슬롯 조회
    /// </summary>
    /// <returns>슬롯 0부터 5까지의 강화 식별자, 빈 슬롯은 null</returns>
    public IReadOnlyList<string> GetEquippedUpgradeIds()
    {
        RequireMetaReady();

        return _progression.GetEquippedUpgradeIds(_selectedCharacter.CharacterId);
    }

    public Task<MetaEquipStatus> RequestEquipAsync(int slotIndex, string upgradeId, string expectedUpgradeId)
    {
        return _loadouts.RequestEquipAsync(_selectedCharacter.CharacterId, slotIndex, upgradeId, expectedUpgradeId);
    }

    /// <summary>
    /// 선택 캐릭터의 장착 슬롯 이동 또는 교환 요청
    /// </summary>
    /// <param name="sourceSlot">출발 슬롯 번호</param>
    /// <param name="destinationSlot">도착 슬롯 번호</param>
    /// <param name="expectedSource">화면에서 확인한 출발 강화</param>
    /// <param name="expectedDestination">화면에서 확인한 도착 강화</param>
    /// <returns>이동 또는 교환 처리 결과</returns>
    public Task<MetaEquipStatus> RequestSwapAsync(int sourceSlot, int destinationSlot, string expectedSource, string expectedDestination)
    {
        return _loadouts.RequestSwapAsync(_selectedCharacter.CharacterId, sourceSlot, destinationSlot, expectedSource, expectedDestination);
    }

    /// <summary>
    /// 선택 캐릭터의 강화 구매와 저장 요청
    /// </summary>
    /// <param name="requestId">이번 구매 요청 식별자</param>
    /// <param name="upgradeId">구매할 강화 식별자</param>
    /// <param name="expectedRank">화면에서 확인한 구매 단계</param>
    /// <returns>구매 처리 결과</returns>
    public Task<MetaPurchaseResult> RequestMetaPurchaseAsync(Guid requestId, string upgradeId, int expectedRank)
    {
        return _purchases.RequestPurchaseAsync(requestId, _selectedCharacter.CharacterId, upgradeId, expectedRank);
    }

    /// <summary>
    /// 일일 보상 수령 처리 후 일일 기록과 재화 저장
    /// </summary>
    /// <param name="questId">수령할 퀘스트 UID</param>
    /// <param name="periodKey">수령할 일일 주기</param>
    /// <returns>수령 처리와 저장 완료 여부</returns>
    public async Task<bool> RequestQuestClaimAsync(string questId, string periodKey)
    {
        RequireMetaReady();

        if (!CanChangeSelection || IsMetaProcessing || !_questActions.CanClaimRewards)
            return false;

        _savingBoundary = true;
        _boundaryError = null;
        HandleChanged();

        try
        {
            if (!await _questActions.RequestClaimAsync(questId, periodKey))
            {
                _boundaryError = _questActions.ErrorMessage;
                return false;
            }

            bool saved = await _metaInitializer.SaveMetaChangesAsync(QuestSaveParts);

            if (!saved)
            {
                _boundaryError = _metaInitializer.SaveError ?? "보상은 반영되었지만 저장하지 못했습니다. 저장을 재시도해 주세요.";
            }

            return saved;
        }
        catch (Exception exception)
        {
            _boundaryError = exception.Message;
            Debug.LogException(exception, this);
            return false;
        }
        finally
        {
            _savingBoundary = false;

            if (this != null)
                HandleChanged();
        }
    }

    /// <summary>
    /// 명시적인 저장 지점의 변경 기록과 실패 저장 재시도
    /// </summary>
    /// <param name="requestedParts">이번 저장 지점에서 허용한 대상</param>
    /// <returns>요청 대상의 저장 완료 여부</returns>
    public async Task<bool> SaveMetaChangesAsync(MetaSavePart requestedParts = MetaSavePart.All)
    {
        if (IsCharacterTestMode)
            return true;

        RequireMetaReady();

        if (IsMetaProcessing || HasMetaOperationFault)
            return false;

        _savingBoundary = true;
        _boundaryError = null;
        HandleChanged();

        try
        {
            if (_questActions.HasPendingRunResult)
            {
                if ((requestedParts & QuestSaveParts) != QuestSaveParts)
                {
                    _boundaryError = "런 정산 결과를 먼저 저장해 주세요.";
                    return false;
                }

                if (!await _sceneFlow.RetryReturnedQuestSaveAsync())
                {
                    _boundaryError = _metaInitializer.SaveError ?? "런 정산 결과를 저장하지 못했습니다.";

                    return false;
                }
            }

            bool saved = await _metaInitializer.SaveMetaChangesAsync(requestedParts);

            if (!saved)
            {
                _boundaryError = _metaInitializer.SaveError ?? "변경 내용을 저장하지 못했습니다.";
            }

            return saved;
        }
        catch (Exception exception)
        {
            _boundaryError = exception.Message;
            Debug.LogException(exception, this);
            return false;
        }
        finally
        {
            _savingBoundary = false;

            if (this != null)
                HandleChanged();
        }
    }

    /// <summary>
    /// 선택 캐릭터의 다음 강화 단계와 비용 조회
    /// </summary>
    /// <param name="upgradeId">조회할 강화 식별자</param>
    /// <param name="nextRank">다음 단계의 비용과 효과</param>
    /// <returns>구매 가능한 다음 단계 존재 여부</returns>
    public bool TryGetNextRank(string upgradeId, out MetaUpgradeRankData nextRank)
    {
        RequireMetaReady();

        return _progression.TryGetNextRank(_selectedCharacter.CharacterId, upgradeId, out nextRank);
    }

    /// <summary>
    /// 영구 변경 저장 이후 시작 데이터를 확정하여 런 진입
    /// </summary>
    /// <returns>씬 전환 요청 접수 여부</returns>
    public async Task<bool> TryStartRunAsync()
    {
        if (!CanStartRun)
            return false;

        _startingRun = true;
        _boundaryError = null;
        HandleChanged();

        try
        {
            if (!IsCharacterTestMode)
            {
                bool saved = await _metaInitializer.SaveMetaChangesAsync();

                if (!saved)
                {
                    _boundaryError = _metaInitializer.SaveError ?? "런 시작 전 저장을 완료하지 못했습니다.";

                    return false;
                }
            }

            if (this == null || !isActiveAndEnabled || !_sceneFlow.IsInMetaScene)
                return false;

            RunStartRequest request = new RunStartRequest(GetGrowthPreview(), _selectedDifficulty);

            return _sceneFlow.TryEnterRunScene(request);
        }
        catch (Exception exception)
        {
            _boundaryError = exception.Message;
            Debug.LogException(exception, this);

            return false;
        }
        finally
        {
            _startingRun = false;

            if (this != null)
                HandleChanged();
        }
    }
}