using System;

/// <summary>
/// 저장과 복원에 사용하는 캐릭터별 누적 숙련도 경험치
/// </summary>
public class MetaMasteryRecord
{
    public string CharacterId { get; }
    public long TotalExperience { get; }

    /// <summary>
    /// 캐릭터별 누적 숙련도 경험치 기록 구성
    /// </summary>
    /// <param name="characterId">숙련도 소유 캐릭터 식별자</param>
    /// <param name="totalExperience">0 이상의 누적 숙련도 경험치</param>
    public MetaMasteryRecord(string characterId, long totalExperience)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 식별자 필요", nameof(characterId));

        if (totalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));

        CharacterId = characterId;
        TotalExperience = totalExperience;
    }
}