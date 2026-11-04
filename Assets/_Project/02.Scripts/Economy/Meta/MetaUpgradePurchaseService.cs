using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 메타 강화 구매 요청의 처리 상태
/// </summary>
public enum MetaPurchaseStatus
{
    Processing,
    Applied,
    Busy,
    NotAllowed,
    NotPurchasable,
    StaleSelection,
    InsufficientFunds,
    Failed,
    Faulted
}

/// <summary>
/// 한 메타 구매 요청의 처리 결과
/// </summary>
public readonly struct MetaPurchaseResult
{
    public Guid RequestId { get; }
    public MetaPurchaseStatus Status { get; }
    public string Message { get; }

    internal MetaPurchaseResult(Guid requestId, MetaPurchaseStatus status, string message)
    {
        RequestId = requestId;
        Status = status;
        Message = message;
    }
}

/// <summary>
/// 공용 지갑을 사용하는 메타 강화 구매와 저장 요청 조정
/// </summary>
public sealed class MetaUpgradePurchaseService
{
    sealed class PurchaseAttempt
    {
        public readonly Guid RequestId;
        public readonly string CharacterId;
        public readonly string UpgradeId;
        public readonly int ExpectedRank;

        public MetaPurchaseResult Result;

        public PurchaseAttempt(Guid requestId, string characterId, string upgradeId, int expectedRank)
        {
            RequestId = requestId;
            CharacterId = characterId;
            UpgradeId = upgradeId;
            ExpectedRank = expectedRank;

            Result = new MetaPurchaseResult(requestId, MetaPurchaseStatus.Processing, null);
        }

        public bool Matches(string characterId, string upgradeId, int expectedRank)
        {
            bool isEqualCharaterId = string.Equals(CharacterId, characterId, StringComparison.Ordinal);
            bool isEqualUpgradeId = string.Equals(UpgradeId, upgradeId, StringComparison.Ordinal);
            bool isEqualRank = ExpectedRank == expectedRank;
            return isEqualCharaterId && isEqualUpgradeId && isEqualRank;
        }
    }

    readonly Dictionary<Guid, PurchaseAttempt> _attempts = new Dictionary<Guid, PurchaseAttempt>();

    readonly Func<bool> _isLobbyReady;

    readonly MetaProgression _progression;
    readonly MetaWallet _wallet;
    readonly Action<MetaSavePart> _markChanged;

    bool _isProcessing;
    bool _faulted;

    public event Action OnChanged;

    public bool IsProcessing => _isProcessing;
    public bool IsFaulted => _faulted;
    public long Balance => _wallet.Balance;
    // 진행 중 거래와 미저장 결과 및 복구 실패에 따른 런 진입 차단 상태
    public bool BlocksRunEntry => _isProcessing || _faulted;

    static void ValidateRequest(Guid requestId, string characterId, string upgradeId, int expectedRank)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("구매 요청 식별자 필요", nameof(requestId));

        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 식별자 필요", nameof(characterId));

        if (string.IsNullOrWhiteSpace(upgradeId))
            throw new ArgumentException("강화 식별자 필요", nameof(upgradeId));

        if (expectedRank < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRank));
    }

    static MetaPurchaseResult SetResult(PurchaseAttempt attempt, MetaPurchaseStatus status, string message = null)
    {
        attempt.Result = new MetaPurchaseResult(attempt.RequestId, status, message);

        return attempt.Result;
    }

    static void Notify(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception exception)
        {
            // 관찰자 오류와 확정된 거래 결과의 분리
            Debug.LogException(exception);
        }
    }

    void NotifyChanged()
    {
        Notify(() => OnChanged?.Invoke());
    }

    void Rollback(PurchaseAttempt attempt, bool rankApplied, long cost, long balanceAfterDebit)
    {
        try
        {
            if (rankApplied)
                _progression.RollbackIncrease(attempt.CharacterId, attempt.UpgradeId, attempt.ExpectedRank);

            _wallet.RefundDebit(cost, balanceAfterDebit);
        }
        catch
        {
            // 복구 실패 이후 추가 거래와 런 진입 차단
            _faulted = true;
            throw;
        }
    }

    MetaPurchaseStatus ApplyPurchase(PurchaseAttempt attempt)
    {
        if (!_isLobbyReady())
            return MetaPurchaseStatus.NotAllowed;

        if (!_progression.TryGetNextRank(attempt.CharacterId, attempt.UpgradeId, out MetaUpgradeRankData nextRank))
            return MetaPurchaseStatus.NotPurchasable;

        if (nextRank.Rank - 1 != attempt.ExpectedRank)
            return MetaPurchaseStatus.StaleSelection;

        if (!_wallet.TryDebit(nextRank.Cost))
            return MetaPurchaseStatus.InsufficientFunds;

        long balanceAfterDebit = _wallet.Balance;
        bool rankApplied = false;

        try
        {
            rankApplied = _progression.TryIncreaseRank(attempt.CharacterId, attempt.UpgradeId, attempt.ExpectedRank);

            if (rankApplied)
            {
                _markChanged(MetaSavePart.Purchases);
                return MetaPurchaseStatus.Applied;
            }
        }
        catch (Exception applyException)
        {
            try
            {
                Rollback(attempt, rankApplied, nextRank.Cost, balanceAfterDebit);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException("메타 구매 적용과 거래 복구 실패", applyException, rollbackException);
            }

            throw;
        }

        Rollback(attempt, false, nextRank.Cost, balanceAfterDebit);
        return MetaPurchaseStatus.StaleSelection;
    }

    /// <summary>
    /// 메타 구매 규칙과 변경 알림 연결
    /// </summary>
    /// <param name="progression">강화 규칙과 상태 소유자</param>
    /// <param name="wallet">재화 상태 소유자</param>
    /// <param name="markChanged">필수 변경 알림</param>
    /// <param name="isLobbyReady">현재 구매 허용 조건</param>
    public MetaUpgradePurchaseService(MetaProgression progression, MetaWallet wallet, Action<MetaSavePart> markChanged, Func<bool> isLobbyReady)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        _markChanged = markChanged ?? throw new ArgumentNullException(nameof(markChanged));
        _isLobbyReady = isLobbyReady ?? throw new ArgumentNullException(nameof(isLobbyReady));
    }

    /// <summary>
    /// 구매 적용과 변경 전달 및 동일 구매 요청의 중복 실행 방지
    /// </summary>
    /// <param name="requestId">동일 구매 의도에 유지할 식별자</param>
    /// <param name="characterId">대상 캐릭터</param>
    /// <param name="upgradeId">대상 강화</param>
    /// <param name="expectedRank">화면에서 확인한 기존 단계</param>
    /// <returns>게임 상태 적용 또는 거절 결과</returns>
    public Task<MetaPurchaseResult> RequestPurchaseAsync(Guid requestId, string characterId, string upgradeId, int expectedRank)
    {
        ValidateRequest(requestId, characterId, upgradeId, expectedRank);

        if (_attempts.TryGetValue(requestId, out PurchaseAttempt existing))
        {
            if (!existing.Matches(characterId, upgradeId, expectedRank))
                throw new InvalidOperationException("구매 요청 ID 재사용 오류");

            return Task.FromResult(existing.Result);
        }

        if (_faulted)
        {
            return Task.FromResult(new MetaPurchaseResult(
                requestId,MetaPurchaseStatus.Faulted, "이전 거래 반영 오류"));
        }

        if (_isProcessing)
        {
            return Task.FromResult(new MetaPurchaseResult(requestId, MetaPurchaseStatus.Busy, "기존 구매 처리 중"));
        }

        PurchaseAttempt attempt = new PurchaseAttempt(requestId, characterId, upgradeId, expectedRank);

        _attempts.Add(requestId, attempt);
        _isProcessing = true;

        try
        {
            MetaPurchaseStatus status = ApplyPurchase(attempt);
            SetResult(attempt, status);

            if (status == MetaPurchaseStatus.Applied)
            {
                // 필수 재화 변경 전달 실패는 거래 성공으로 처리하지 않음
                _wallet.PublishChanged();

                Notify(_progression.PublishChanged);
            }

            return Task.FromResult(attempt.Result);
        }
        catch (Exception exception)
        {
            if (attempt.Result.Status == MetaPurchaseStatus.Applied)
                _faulted = true;

            Debug.LogException(exception);

            return Task.FromResult(SetResult(attempt, _faulted ? MetaPurchaseStatus.Faulted : MetaPurchaseStatus.Failed, exception.Message));
        }
        finally
        {
            _isProcessing = false;
            NotifyChanged();
        }
    }
}