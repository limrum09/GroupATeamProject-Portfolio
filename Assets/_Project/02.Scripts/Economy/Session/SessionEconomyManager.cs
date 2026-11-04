using System;
using UnityEngine;

/// <summary>
/// 세션 골드 지급 사유
/// </summary>
public enum GoldGainSource
{
    EnemyReward,
    SkillSale,
    TestGrant
}

/// <summary>세션 골드 잔액 관리</summary>
public sealed class SessionEconomyManager : MonoBehaviour
{
    float _gold;

    public event Action<float> OnGoldChanged;
    public event Action<GoldGainSource, float> OnGoldAcquired;

    public float Gold => _gold;

    /// <summary>새 세션의 잔액 초기화</summary>
    public void ResetSession()
    {
        _gold = 0f;
        PublishGoldChanged();
    }

    /// <summary>확정된 골드 보상 반영</summary>
    /// <param name="amount">지급할 골드</param>
    public void AddGold(float amount, GoldGainSource source)
    {
        ValidateAmount(amount);

        float nextGold = _gold + amount;

        if (float.IsInfinity(nextGold))
            throw new OverflowException("세션 골드 범위 초과");

        _gold = nextGold;

        OnGoldAcquired?.Invoke(source, amount);
        QuestManager.Instance.ReportGoldAcquired(source, amount);
        PublishGoldChanged();
    }

    internal bool TryDebit(float amount)
    {
        ValidateAmount(amount);

        if (_gold < amount)
            return false;

        _gold -= amount;
        return true;
    }

    internal void RefundDebit(float amount)
    {
        _gold += amount;
    }

    internal void PublishGoldChanged()
    {
        QuestManager.Instance.ReportGoldChanged(_gold);
        OnGoldChanged?.Invoke(_gold);
    }

    static void ValidateAmount(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(nameof(amount));
    }
}
