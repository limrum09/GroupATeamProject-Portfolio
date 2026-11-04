using System;
using System.Collections.Generic;

/// <summary>
/// 한 저장 작업에 고정된 메타 잔액과 구매 기록
/// </summary>
public sealed class MetaProfileSnapshot
{
    public Guid OperationId { get; }
    public long Balance { get; }
    public IReadOnlyList<MetaUpgradeRecord> Purchases { get; }
    public IReadOnlyList<MetaLoadoutRecord> Loadouts { get; }
    public IReadOnlyList<MetaMasteryRecord> Mastery { get; }
    public IReadOnlyList<QuestRecord> Quests { get; }

    /// <summary>
    /// 동일 저장 작업의 재시도에 사용할 데이터 복사본 구성
    /// </summary>
    /// <param name="operationId">저장 작업 식별자</param>
    /// <param name="balance">저장할 메타 잔액</param>
    /// <param name="purchases">기능 소유자가 캡처한 구매 기록</param>
    /// <param name="loadouts">캐릭터별 장착 기록</param>
    /// <param name="mastery">캐릭터별 확정 숙련도 기록</param>
    /// <param name="quests">정산 또는 수령으로 확정할 퀘스트 기록</param>
    public MetaProfileSnapshot(Guid operationId, long balance, IReadOnlyList<MetaUpgradeRecord> purchases, IReadOnlyList<MetaLoadoutRecord> loadouts, IReadOnlyList<MetaMasteryRecord> mastery, IReadOnlyList<QuestRecord> quests)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("저장 작업 식별자 필요", nameof(operationId));

        if (balance < 0)
            throw new ArgumentOutOfRangeException(nameof(balance));

        if (purchases == null)
            throw new ArgumentNullException(nameof(purchases));

        if (loadouts == null)
            throw new ArgumentNullException(nameof(loadouts));

        if (mastery == null)
            throw new ArgumentNullException(nameof(mastery));

        if (quests == null)
            throw new ArgumentNullException(nameof(quests));

        OperationId = operationId;
        Balance = balance;

        Purchases = new List<MetaUpgradeRecord>(purchases).AsReadOnly();
        Loadouts = new List<MetaLoadoutRecord>(loadouts).AsReadOnly();
        Mastery = new List<MetaMasteryRecord>(mastery).AsReadOnly();
        Quests = new List<QuestRecord>(quests).AsReadOnly();
    }
}