using System;

/// <summary>
/// 메타 재화 잔액과 거래용 차감 및 복구
/// </summary>
public sealed class MetaWallet
{
    long _balance;
    long _lastPublishedBalance;

    public event Action<long> OnBalanceChanged;

    public long Balance => _balance;

    static void ValidateAmount(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "0 이상의 재화 금액 필요");
        }
    }

    /// <summary>
    /// 충분한 잔액이 있을 때 재화 차감
    /// </summary>
    /// <param name="amount">차감할 금액</param>
    /// <returns>차감 성공 여부</returns>
    internal bool TryDebit(long amount)
    {
        ValidateAmount(amount);

        if (_balance < amount)
            return false;

        _balance -= amount;

        // 강화 적용까지 완료된 뒤 거래 서비스에서 변경 알림
        return true;
    }

    /// <summary>
    /// 직전 차감 이후 잔액이 유지된 경우 차감 복구
    /// </summary>
    /// <param name="amount">복구할 차감 금액</param>
    /// <param name="expectedBalance">직전 차감 직후의 예상 잔액</param>
    internal void RefundDebit(long amount, long expectedBalance)
    {
        ValidateAmount(amount);

        if (_balance != expectedBalance)
        {
            throw new InvalidOperationException(
                "직전 차감 이후 잔액이 변경되어 거래 복구 불가");
        }

        long restoredBalance = checked(_balance + amount);
        _balance = restoredBalance;
    }

    /// <summary>
    /// 거래 반영 후 실제 잔액 변경 전달
    /// </summary>
    internal void PublishChanged()
    {
        long balance = _balance;

        if (_lastPublishedBalance == balance)
            return;

        OnBalanceChanged?.Invoke(balance);

        _lastPublishedBalance = balance;
    }

    /// <summary>
    /// 복원된 잔액으로 메타 지갑 구성
    /// </summary>
    /// <param name="initialBalance">복원된 잔액 또는 신규 프로필 초기 잔액</param>
    public MetaWallet(long initialBalance)
    {
        ValidateAmount(initialBalance);

        _balance = initialBalance;
        _lastPublishedBalance = initialBalance;
    }

    /// <summary>
    /// 검증된 거래 후보 잔액 반영
    /// </summary>
    /// <param name="balance">검증을 마친 메타 재화 잔액</param>
    public void ApplyPreparedBalance(long balance)
    {
        ValidateAmount(balance);
        _balance = balance;
    }

    /// <summary>
    /// 상태 변경 없이 보상 반영 후 잔액 검증
    /// </summary>
    /// <param name="amount">추가할 메타 재화</param>
    /// <param name="balance">검증된 후보 잔액</param>
    /// <returns>보유 범위 내의 보상 반영 가능 여부</returns>
    public bool TryPrepareCredit(long amount, out long balance)
    {
        ValidateAmount(amount);

        balance = _balance;

        if (amount > long.MaxValue - _balance)
            return false;

        balance += amount;
        return true;
    }
}