using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 메타 프로필 수집과 저장 및 동일 프로필 재시도
/// Unity 메인 스레드 호출 전용
/// </summary>
public class MetaProfileSaveCoordinator : IDisposable
{
    readonly MetaProgression _progression;
    readonly MetaWallet _wallet;
    readonly QuestManager _quests;
    readonly Func<MetaSaveChanges, Task<bool>> _writeAsync;
    readonly Func<bool> _canChange;

    MetaSavePart _dirty;
    MetaSaveChanges _pending;

    bool _writing;
    bool _disposed;

    public event Action OnChanged;

    public string ErrorMessage { get; private set; }
    public bool HasChanges => _dirty != MetaSavePart.None;
    public bool HasPendingSave => _pending != null;
    public bool IsSaving => _writing;
    public bool CanAcceptChanges => !_disposed && !_writing && _pending == null && _canChange();
    public MetaSavePart ChangedParts => _dirty;
    public MetaSavePart PendingParts => _pending?.Parts ?? MetaSavePart.None;

    void RequireAlive()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MetaProfileSaveCoordinator));
    }

    void RequireIdle()
    {
        RequireAlive();

        if (_writing || _pending != null)
            throw new InvalidOperationException("기존 저장 작업 완료 필요");
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
                // 표시 오류와 저장 결과의 분리
                Debug.LogException(exception);
            }
        }
    }

    void HandleDailyDataChanged()
    {
        MarkChanged(MetaSavePart.DailyQuests);
    }

    void HandleBalanceChanged(long balance)
    {
        MarkChanged(MetaSavePart.MetaGold);
    }

    MetaSaveChanges Capture(MetaSavePart parts)
    {
        long? balance = null;
        IReadOnlyList<MetaUpgradeRecord> purchases = null;
        IReadOnlyList<MetaLoadoutRecord> loadouts = null;
        IReadOnlyList<MetaMasteryRecord> mastery = null;
        MetaProfileData.DailyQuestData dailyQuests = null;

        if ((parts & MetaSavePart.MetaGold) != 0)
            balance = _wallet.Balance;

        if ((parts & MetaSavePart.Purchases) != 0)
            purchases = _progression.CapturePurchases();

        if ((parts & MetaSavePart.Loadouts) != 0)
            loadouts = _progression.CaptureLoadouts();

        if ((parts & MetaSavePart.Mastery) != 0)
            mastery = _progression.CaptureMastery();

        if ((parts & MetaSavePart.DailyQuests) != 0)
            dailyQuests = _quests.CaptureDailyData();

        return new MetaSaveChanges(parts, balance: balance, purchases: purchases, loadouts: loadouts, mastery: mastery, dailyQuests: dailyQuests);
    }

    async Task<bool> WritePendingAsync()
    {
        _writing = true;
        ErrorMessage = null;
        PublishChanged();

        bool saved = false;

        try
        {
            saved = await _writeAsync(_pending);

            if (saved)
            {
                _dirty &= ~_pending.Parts;
                _pending = null;
            }
            else
            {
                ErrorMessage = "변경 내용을 저장하지 못했습니다.";
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            if (!_disposed)
                Debug.LogException(exception);
        }
        finally
        {
            _writing = false;
            PublishChanged();
        }

        return saved && !_disposed;
    }

    /// <summary>
    /// 데이터 소유자와 변경 알림 및 저장 함수 연결
    /// </summary>
    /// <param name="progression">메타 성장 데이터 소유자</param>
    /// <param name="wallet">메타 재화 소유자</param>
    /// <param name="quests">일일 기록 소유자</param>
    /// <param name="writeAsync">실제 저장 완료 함수</param>
    /// <param name="canChange">외부 흐름의 변경 허용 조건</param>
    public MetaProfileSaveCoordinator(MetaProgression progression, MetaWallet wallet, QuestManager quests, Func<MetaSaveChanges, Task<bool>> writeAsync, Func<bool> canChange)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        _quests = quests ?? throw new ArgumentNullException(nameof(quests));
        _writeAsync = writeAsync ?? throw new ArgumentNullException(nameof(writeAsync));
        _canChange = canChange ?? throw new ArgumentNullException(nameof(canChange));

        _quests.OnDailyDataChanged += HandleDailyDataChanged;
        _wallet.OnBalanceChanged += HandleBalanceChanged;
    }

    /// <summary>
    /// 데이터 수집이나 쓰기 없이 변경 항목만 표시
    /// </summary>
    /// <param name="parts">변경된 영구 데이터 항목</param>
    public void MarkChanged(MetaSavePart parts)
    {
        RequireAlive();

        if ((parts & ~MetaSavePart.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(parts));

        if (parts == MetaSavePart.None)
            return;

        if (!CanAcceptChanges)
            throw new InvalidOperationException("현재 영구 데이터 변경 불가");

        _dirty |= parts;
        PublishChanged();
    }

    /// <summary>
    /// 요청 대상 중 변경된 항목만 수집하여 저장
    /// </summary>
    /// <param name="requestedParts">이번 저장 지점에서 허용한 대상</param>
    /// <returns>저장 성공 여부, 대상 변경이 없으면 true</returns>
    public Task<bool> SaveAsync(MetaSavePart requestedParts = MetaSavePart.All)
    {
        RequireIdle();

        if ((requestedParts & ~MetaSavePart.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(requestedParts));

        MetaSavePart parts = _dirty & requestedParts;

        if (parts == MetaSavePart.None)
        {
            ErrorMessage = null;
            return Task.FromResult(true);
        }

        _pending = Capture(parts);
        return WritePendingAsync();
    }

    /// <summary>
    /// 실패한 저장의 고정된 데이터 재기록
    /// </summary>
    /// <returns>저장 성공 여부</returns>
    public Task<bool> RetryAsync()
    {
        RequireAlive();

        if (_writing)
            throw new InvalidOperationException("현재 저장 완료 후 재시도 필요");

        if (_pending == null)
            throw new InvalidOperationException("재시도할 저장 데이터 필요");

        return WritePendingAsync();
    }

    /// <summary>
    /// 변경 알림 구독 해제와 후속 작업 차단
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _quests.OnDailyDataChanged -= HandleDailyDataChanged;
        _wallet.OnBalanceChanged -= HandleBalanceChanged;

        OnChanged = null;
    }
}