using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 퀘스트 보상 수령과 일일 갱신 및 정상 종료 결과의 작업 조율
/// </summary>
public class QuestActionService : IDisposable
{
    readonly QuestManager _quests;
    readonly MetaWallet _wallet;
    readonly MetaProfileSaveCoordinator _saves;
    readonly Func<bool> _canUseLobby;

    bool _isProcessing;
    bool _faulted;
    bool _disposed;

    public event Action OnChanged;

    public string ErrorMessage { get; private set; }
    public bool IsProcessing => _isProcessing;
    public bool IsFaulted => _faulted; public bool HasPendingRunResult => _quests.HasPendingRunResult;
    public bool BlocksMetaOperations => _disposed || _quests.BlocksMetaOperations || _isProcessing || _faulted || !_saves.CanAcceptChanges;
    public bool CanClaimRewards => !BlocksMetaOperations && !_quests.IsTestMode && _canUseLobby() && (_saves.ChangedParts & MetaSavePart.Purchases) == MetaSavePart.None;

    void RequireReady()
    {
        if (_disposed || !_quests.IsInitialized)
            throw new InvalidOperationException("준비된 퀘스트 작업 담당 필요");
    }

    void PublishChanged()
    {
        if (_disposed)
            return;

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
                // 관찰자 오류와 작업 결과의 분리
                Debug.LogException(exception);
            }
        }
    }

    Task<bool> Reject(string message)
    {
        ErrorMessage = message;
        PublishChanged();
        return Task.FromResult(false);
    }

    Task<bool> ApplyChanges(IReadOnlyList<QuestRecord> records, long balance, string dailyPeriod = null)
    {
        _isProcessing = true;
        ErrorMessage = null;
        PublishChanged();

        try
        {
            _quests.ApplyPersistentRecords(records, dailyPeriod);
            _wallet.ApplyPreparedBalance(balance);

            _wallet.PublishChanged();
            _quests.PublishChanged();

            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            // 예상하지 못한 반영 또는 필수 변경 전달 오류 이후 추가 지급 차단
            _faulted = true;
            ErrorMessage = $"퀘스트 결과 반영 오류: {exception.Message}";
            Debug.LogException(exception);

            return Task.FromResult(false);
        }
        finally
        {
            _isProcessing = false;
            PublishChanged();
        }
    }

    /// <summary>
    /// 퀘스트와 재화 및 공통 저장 절차 연결
    /// </summary>
    /// <param name="quests">퀘스트 상태 소유자</param>
    /// <param name="wallet">메타 재화 소유자</param>
    /// <param name="saves">프로필 수집과 저장 담당</param>
    /// <param name="canUseLobby">자기 저장 대기를 제외한 로비 작업 허용 조건</param>
    public QuestActionService(QuestManager quests, MetaWallet wallet, MetaProfileSaveCoordinator saves, Func<bool> canUseLobby)
    {
        _quests = quests;
        _wallet = wallet;
        _saves = saves;
        _canUseLobby = canUseLobby;
    }

    /// <summary>
    /// 정상 런의 일일 진행과 돌발 보상 반영
    /// </summary>
    /// <returns>정산 반영 성공 여부</returns>
    public Task<bool> ApplyRunResultAsync()
    {
        RequireReady();

        if (_isProcessing)
            return Task.FromResult(false);

        if (_faulted)
            return Reject("이전 결과 반영 오류로 추가 작업을 진행할 수 없습니다.");

        // 저장 재시도에서 정산 보상 재적용 제외
        if (!_quests.HasPendingRunResult)
            return Task.FromResult(true);

        if (!_saves.CanAcceptChanges || !_canUseLobby())
            return Reject("로비에서 다른 작업을 마친 뒤 정산할 수 있습니다.");

        if (!_wallet.TryPrepareCredit(_quests.PendingSuddenReward, out long balance))
            return Reject("메타 재화의 보유 가능 범위를 초과합니다.");

        return ApplyChanges(_quests.CaptureProgressRecords(), balance);
    }

    /// <summary>
    /// 완료한 일일 퀘스트의 수령 상태와 메타 보상 반영
    /// </summary>
    /// <param name="questId">수령할 퀘스트 UID</param>
    /// <param name="periodKey">수령할 일일 주기</param>
    /// <returns>수령 반영 성공 여부</returns>
    public Task<bool> RequestClaimAsync(string questId, string periodKey)
    {
        RequireReady();

        if (_isProcessing)
            return Task.FromResult(false);

        if (!CanClaimRewards)
            return Reject("로비에서 다른 작업과 저장을 마친 후 수령할 수 있습니다.");

        if (!_quests.TryPrepareClaim(questId, periodKey, out IReadOnlyList<QuestRecord> records, out long reward, out string message))
            return Reject(message);

        if (!_wallet.TryPrepareCredit(reward, out long balance))
            return Reject("메타 재화의 보유 가능 범위를 초과합니다.");

        return ApplyChanges(records, balance);
    }

    /// <summary>
    /// 새 일일 퀘스트 선택과 상태 반영
    /// </summary>
    /// <param name="periodKey">새 일일 주기</param>
    /// <returns>새 주기 적용 성공 여부</returns>
    public Task<bool> RefreshDailyQuestsAsync(string periodKey)
    {
        RequireReady();

        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("일일 주기 필요", nameof(periodKey));

        if (BlocksMetaOperations || !_canUseLobby())
            return Reject("런과 정산 및 다른 작업을 마친 뒤 초기화할 수 있습니다.");

        if (periodKey == _quests.DailyPeriod)
            return Task.FromResult(true);

        return ApplyChanges(_quests.PrepareDailyRefresh(periodKey), _wallet.Balance, periodKey);
    }

    /// <summary>
    /// 작업 담당 수명 종료와 비동기 결과의 후속 반영 차단
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}