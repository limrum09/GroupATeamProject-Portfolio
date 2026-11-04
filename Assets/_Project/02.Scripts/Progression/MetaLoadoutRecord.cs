using System;
using System.Collections.Generic;

/// <summary>
/// 강화 장착 요청의 처리 결과
/// </summary>
public enum MetaEquipStatus
{
    Prepared,
    Unchanged,
    NotPurchased,
    AlreadyEquipped,
    Unavailable,
    StaleSelection,
    Busy,
    NotAllowed,
    Applied,
    Failed
}

/// <summary>
/// 캐릭터별 강화 장착 슬롯의 읽기 전용 기록
/// </summary>
public class MetaLoadoutRecord
{
    public string CharacterId { get; }
    public IReadOnlyList<string> Slots { get; }

    /// <summary>
    /// 캐릭터와 슬롯 순서를 보존한 장착 기록 구성
    /// </summary>
    /// <param name="characterId">장착 설정의 캐릭터 식별자</param>
    /// <param name="slots">슬롯별 강화 식별자, 빈 슬롯은 null</param>
    public MetaLoadoutRecord(string characterId, IReadOnlyList<string> slots)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 식별자 필요", nameof(characterId));

        if (slots == null)
            throw new ArgumentNullException(nameof(slots));

        CharacterId = characterId;
        Slots = new List<string>(slots).AsReadOnly();
    }
}