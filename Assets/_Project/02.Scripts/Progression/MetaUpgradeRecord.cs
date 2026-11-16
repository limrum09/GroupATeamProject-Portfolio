using System;

/// <summary>
/// 저장 계층에 전달하거나 복원할 캐릭터별 메타 강화 구매 기록
/// </summary>
public readonly struct MetaUpgradeRecord
{
    public string UpgradeId { get; }
    public int Rank { get; }

    /// <summary>
    /// 캐릭터와 강화 식별자에 대응하는 구매 기록 구성
    /// </summary>
    /// <param name="upgradeId">메타 강화 식별자</param>
    /// <param name="rank">구매 완료 단계, 미구매 상태는 0</param>
    public MetaUpgradeRecord(string upgradeId, int rank)
    {
        if (string.IsNullOrWhiteSpace(upgradeId))
            throw new ArgumentException("강화 식별자 필요", nameof(upgradeId));

        if (rank < 0)
            throw new ArgumentOutOfRangeException(nameof(rank), "0 이상의 구매 단계 필요");

        UpgradeId = upgradeId;
        Rank = rank;
    }
}