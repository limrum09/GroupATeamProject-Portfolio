using System;

/// <summary>
/// 저장 계층에 전달하거나 복원할 캐릭터별 메타 강화 구매 기록
/// </summary>
public readonly struct MetaUpgradeRecord
{
    public string CharacterId { get; }
    public string UpgradeId { get; }
    public int Rank { get; }

    /// <summary>
    /// 캐릭터와 강화 식별자에 대응하는 구매 기록 구성
    /// </summary>
    /// <param name="characterId">강화 대상 캐릭터 식별자</param>
    /// <param name="upgradeId">메타 강화 식별자</param>
    /// <param name="rank">구매 완료 단계, 미구매 상태는 0</param>
    public MetaUpgradeRecord(string characterId, string upgradeId, int rank)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 식별자 필요", nameof(characterId));

        if (string.IsNullOrWhiteSpace(upgradeId))
            throw new ArgumentException("강화 식별자 필요", nameof(upgradeId));

        if (rank < 0)
            throw new ArgumentOutOfRangeException(nameof(rank), "0 이상의 구매 단계 필요");

        CharacterId = characterId;
        UpgradeId = upgradeId;
        Rank = rank;
    }
}