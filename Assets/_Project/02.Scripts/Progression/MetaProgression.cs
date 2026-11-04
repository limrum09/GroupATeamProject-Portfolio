using System;
using System.Collections.Generic;

/// <summary>
/// JSON 기반 강화 정의와 캐릭터별 구매 기록을 사용하는 메타 성장 관리
/// </summary>
public class MetaProgression
{
    readonly Dictionary<string, CharacterGrowthDefinition> _characters = new Dictionary<string, CharacterGrowthDefinition>(StringComparer.Ordinal);
    readonly Dictionary<string, MetaUpgradeData> _upgrades = new Dictionary<string, MetaUpgradeData>(StringComparer.Ordinal);
    Dictionary<string, MetaLoadoutRecord> _loadouts = new Dictionary<string, MetaLoadoutRecord>(StringComparer.Ordinal);
    Dictionary<string, long> _masteryExperience = new Dictionary<string, long>(StringComparer.Ordinal);
    Dictionary<(string CharacterId, string UpgradeId), int> _purchasedRanks = new Dictionary<(string CharacterId, string UpgradeId), int>();

    readonly MasteryLevelDefinition _integratedMasteryDefinition;
    readonly MasteryLevelDefinition _characterMasteryDefinition;

    public event Action OnChanged;

    public const int EquipSlotCount = 6;

    // 전체 캐릭터의 확정 숙련도 경험치를 합산한 통합 경험치
    public long IntegratedMasteryExperience => CalculateMasteryExperience(_masteryExperience);

    // 통합 경험치와 통합 레벨표로 계산한 계정 공통 레벨
    public int IntegratedMasteryLevel => _integratedMasteryDefinition.GetLevel(IntegratedMasteryExperience);

    static bool AppliesTo(MetaUpgradeData upgrade, string characterId)
    {
        return upgrade.TargetCharacterId == "ALL" || upgrade.TargetCharacterId == characterId;
    }

    static CharacterStatType ResolveStatType(string statId, string source)
    {
        switch (statId)
        {
            case "MaxHealth":
                return CharacterStatType.MaxHealth;
            case "Attack":
                return CharacterStatType.AttackPower;
            case "Defense":
                return CharacterStatType.Defense;
            case "SpellPower":
                return CharacterStatType.SpellPower;
            case "MoveSpeed":
                return CharacterStatType.MoveSpeed;
            case "AttackSpeed":
                return CharacterStatType.AttackSpeed;
            case "CritChance":
                return CharacterStatType.CriticalChance;
            case "CritDamageMultiplier":
                return CharacterStatType.CriticalDamageMultiplier;
            default:
                throw new InvalidOperationException($"{source}: 지원하지 않는 StatId: {statId}");
        }
    }

    static void GetRankBonuses(MetaUpgradeData upgrade, int rank, out CharacterStatValues permanentBonus, out CharacterStatValues perLevelBonus)
    {
        permanentBonus = default;
        perLevelBonus = default;

        MetaUpgradeRankData rankData = upgrade.GetRank(rank);
        string source = $"{upgrade.UpgradeId}/{rank}";

        // 현재 구매 단계의 전체 효과만 사용
        foreach (MetaUpgradeEffectData effect in rankData.Effects)
        {
            if (effect.ModifierType != "FlatAdd")
                throw new InvalidOperationException($"{source}: 지원하지 않는 ModifierType: " + effect.ModifierType);

            if (effect.Value < 0f)
                throw new InvalidOperationException($"{source}: 현재 성장 구조에서 활성 음수 효과 지원 불가");

            CharacterStatType stat = ResolveStatType(effect.StatId, source);

            switch (effect.ApplyTiming)
            {
                case "RunStart":
                    permanentBonus = permanentBonus.Add(stat, effect.Value);
                    break;
                case "LevelUp":
                    perLevelBonus = perLevelBonus.Add(stat, effect.Value);
                    break;
                default:
                    throw new InvalidOperationException($"{source}: 지원하지 않는 ApplyTiming: " + effect.ApplyTiming);
            }
        }
    }

    static long CalculateMasteryExperience(IReadOnlyDictionary<string, long> masteryExperience)
    {
        long totalExperience = 0;

        foreach (long experience in masteryExperience.Values)
            totalExperience = checked(totalExperience + experience);

        return totalExperience;
    }

    MetaEquipStatus ValidateLoadout(string characterId, IReadOnlyList<string> slots)
    {
        GetCharacter(characterId);

        if (slots.Count != EquipSlotCount)
            throw new InvalidOperationException($"슬롯은 {EquipSlotCount}개 필요");

        HashSet<string> equipped = new HashSet<string>(StringComparer.Ordinal);

        foreach (string upgradeId in slots)
        {
            if (upgradeId == null)
                continue;

            MetaUpgradeData upgrade = GetUpgrade(characterId, upgradeId);

            if (!upgrade.Enabled || !AppliesTo(upgrade, characterId))
                return MetaEquipStatus.Unavailable;

            if (GetPurchasedRank(characterId, upgradeId) == 0)
                return MetaEquipStatus.NotPurchased;

            if (!equipped.Add(upgradeId))
                return MetaEquipStatus.AlreadyEquipped;
        }

        return MetaEquipStatus.Prepared;
    }

    CharacterGrowthDefinition GetCharacter(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("캐릭터 식별자 필요", nameof(characterId));

        if (!_characters.TryGetValue(characterId, out CharacterGrowthDefinition character))
            throw new InvalidOperationException($"등록되지 않은 캐릭터 ID: {characterId}");

        return character;
    }

    MetaUpgradeData GetUpgrade(string characterId, string upgradeId)
    {
        GetCharacter(characterId);

        if (string.IsNullOrWhiteSpace(upgradeId))
            throw new ArgumentException("강화 식별자 필요", nameof(upgradeId));

        if (!_upgrades.TryGetValue(upgradeId, out MetaUpgradeData upgrade))
            throw new InvalidOperationException($"등록되지 않은 강화 ID: {upgradeId}");

        return upgrade;
    }

    /// <summary>
    /// 캐릭터 성장과 강화 정의 및 두 종류의 숙련도 레벨표 등록
    /// </summary>
    /// <param name="characters">게임에 등록할 전체 캐릭터 성장 설정</param>
    /// <param name="upgrades">MetaUpgradeJsonLoader에서 읽은 강화 정의</param>
    /// <param name="integratedMasteryDefinition">통합 숙련도 레벨표</param>
    /// <param name="characterMasteryDefinition">캐릭터별 숙련도에 적용할 레벨표</param>
    public MetaProgression(IReadOnlyList<CharacterGrowthDefinition> characters, IReadOnlyDictionary<string, MetaUpgradeData> upgrades, MasteryLevelDefinition integratedMasteryDefinition, MasteryLevelDefinition characterMasteryDefinition)
    {
        if (characters == null)
            throw new ArgumentNullException(nameof(characters));

        if (upgrades == null)
            throw new ArgumentNullException(nameof(upgrades));

        if (integratedMasteryDefinition == null)
            throw new ArgumentNullException(nameof(integratedMasteryDefinition));

        if (characters.Count == 0)
            throw new ArgumentException("하나 이상의 캐릭터 성장 설정 필요", nameof(characters));

        if (characterMasteryDefinition == null)
            throw new ArgumentNullException(nameof(characterMasteryDefinition));

        integratedMasteryDefinition.Validate();
        characterMasteryDefinition.Validate();

        _integratedMasteryDefinition = integratedMasteryDefinition;
        _characterMasteryDefinition = characterMasteryDefinition;

        foreach (CharacterGrowthDefinition character in characters)
        {
            if (character == null)
                throw new InvalidOperationException("캐릭터 성장 설정의 빈 항목 제거 필요");

            character.Validate();

            if (character.CharacterId == "ALL")
                throw new InvalidOperationException("ALL은 강화 적용 대상에 사용하는 예약 ID");

            if (_characters.ContainsKey(character.CharacterId))
                throw new InvalidOperationException($"중복 캐릭터 ID: {character.CharacterId}");

            _characters.Add(character.CharacterId, character);

            _loadouts.Add(character.CharacterId, new MetaLoadoutRecord( character.CharacterId, new string[EquipSlotCount]));
        }

        foreach (KeyValuePair<string, MetaUpgradeData> entry in upgrades)
        {
            MetaUpgradeData upgrade = entry.Value;

            if (upgrade == null)
                throw new InvalidOperationException("강화 정의의 빈 항목 제거 필요");

            if (!string.Equals(entry.Key, upgrade.UpgradeId, StringComparison.Ordinal))
                throw new InvalidOperationException($"강화 조회 키와 UpgradeId 불일치: {entry.Key}");

            if (upgrade.TargetCharacterId != "ALL" && !_characters.ContainsKey(upgrade.TargetCharacterId))
                throw new InvalidOperationException($"{upgrade.UpgradeId}: 등록되지 않은 대상 캐릭터: " + upgrade.TargetCharacterId);

            // 활성 강화의 모든 단계를 실제 보너스 계산 경로로 검사
            if (upgrade.Enabled)
            {
                foreach (MetaUpgradeRankData rank in upgrade.Ranks)
                {
                    GetRankBonuses(upgrade, rank.Rank, out _, out _);
                }
            }

            _upgrades.Add(upgrade.UpgradeId, upgrade);
        }
    }

    /// <summary>
    /// 구매 자격과 기존 단계 확인 후 한 단계 증가
    /// </summary>
    /// <param name="characterId">강화 대상 캐릭터 식별자</param>
    /// <param name="upgradeId">구매할 강화 식별자</param>
    /// <param name="expectedRank">거래 검증 시 확인한 기존 구매 단계</param>
    /// <returns>구매 단계 변경 성공 여부</returns>
    public bool TryIncreaseRank(string characterId, string upgradeId, int expectedRank)
    {
        if (!TryGetNextRank(characterId, upgradeId, out MetaUpgradeRankData nextRank))
            return false;

        if (nextRank.Rank - 1 != expectedRank)
            return false;

        _purchasedRanks[(characterId, upgradeId)] = nextRank.Rank;

        // 재화 차감과 강화 적용 완료 후 거래 조정자에서 변경 알림
        return true;
    }

    /// <summary>
    /// 기존 슬롯 확인과 장착 검증을 거친 변경 후보 준비
    /// </summary>
    /// <param name="characterId">장착 대상 캐릭터 식별자</param>
    /// <param name="slotIndex">변경할 슬롯 번호, 0부터 5</param>
    /// <param name="upgradeId">장착할 강화 식별자, 해제는 null</param>
    /// <param name="expectedUpgradeId">화면에서 확인한 기존 슬롯 식별자</param>
    /// <param name="candidate">저장에 사용할 검증된 후보</param>
    /// <returns>후보 준비 결과 또는 거절 사유</returns>
    public MetaEquipStatus TryPrepareLoadout(string characterId, int slotIndex, string upgradeId, string expectedUpgradeId, out MetaLoadoutRecord candidate)
    {
        candidate = null;

        GetCharacter(characterId);

        if (slotIndex < 0 || slotIndex >= EquipSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        IReadOnlyList<string> current = _loadouts[characterId].Slots;

        if (!string.Equals(current[slotIndex], expectedUpgradeId, StringComparison.Ordinal))
            return MetaEquipStatus.StaleSelection;

        if (string.Equals(current[slotIndex], upgradeId, StringComparison.Ordinal))
            return MetaEquipStatus.Unchanged;

        List<string> slots = new List<string>(current);
        slots[slotIndex] = upgradeId;

        MetaEquipStatus result = ValidateLoadout(characterId, slots);

        if (result != MetaEquipStatus.Prepared)
            return result;

        candidate = new MetaLoadoutRecord(characterId, slots);
        return MetaEquipStatus.Prepared;
    }

    /// <summary>
    /// 기존 슬롯의 이동 또는 교환 후보 준비
    /// </summary>
    /// <param name="characterId">대상 캐릭터 식별자</param>
    /// <param name="sourceSlot">출발 슬롯 번호</param>
    /// <param name="destinationSlot">도착 슬롯 번호</param>
    /// <param name="expectedSource">드래그 시작 시 확인한 출발 강화</param>
    /// <param name="expectedDestination">드래그 시작 시 확인한 도착 강화</param>
    /// <param name="candidate">저장할 장착 후보</param>
    /// <returns>후보 준비 결과 또는 슬롯 변경에 따른 거절</returns>
    public MetaEquipStatus TryPrepareSwap(string characterId, int sourceSlot, int destinationSlot, string expectedSource, string expectedDestination, out MetaLoadoutRecord candidate)
    {
        candidate = null;

        IReadOnlyList<string> current = _loadouts[characterId].Slots;

        if (!string.Equals(current[sourceSlot], expectedSource, StringComparison.Ordinal) || !string.Equals(current[destinationSlot], expectedDestination, StringComparison.Ordinal))
            return MetaEquipStatus.StaleSelection;

        if (sourceSlot == destinationSlot)
            return MetaEquipStatus.Unchanged;

        List<string> slots = new List<string>(current);

        slots[sourceSlot] = current[destinationSlot];
        slots[destinationSlot] = current[sourceSlot];

        // 검증된 기존 항목의 위치 교환이므로 구매와 중복 장착 재검사 불필요
        candidate = new MetaLoadoutRecord(characterId, slots);
        return MetaEquipStatus.Prepared;
    }

    /// <summary>
    /// 저장을 완료한 장착 후보 적용
    /// </summary>
    /// <param name="candidate">장착 또는 교환 준비를 완료한 후보</param>
    public void ApplyPreparedLoadout(MetaLoadoutRecord candidate)
    {
        _loadouts[candidate.CharacterId] = candidate;

        // 외부 알림은 저장 요청 담당에서 수행
    }

    /// <summary>
    /// 완료되지 않은 거래의 직전 구매 단계 증가 복구
    /// </summary>
    /// <param name="characterId">강화 대상 캐릭터 식별자</param>
    /// <param name="upgradeId">복구할 강화 식별자</param>
    /// <param name="previousRank">거래 이전의 구매 단계</param>
    public void RollbackIncrease(string characterId, string upgradeId, int previousRank)
    {
        MetaUpgradeData upgrade = GetUpgrade(characterId, upgradeId);

        if (previousRank < 0 || previousRank >= upgrade.MaxRank)
            throw new ArgumentOutOfRangeException(nameof(previousRank), "강화 증가 이전의 유효한 구매 단계 필요");

        int currentRank = GetPurchasedRank(characterId, upgradeId);

        if (currentRank != previousRank + 1)
            throw new InvalidOperationException("직전 거래에서 증가한 구매 단계만 복구 가능");

        (string CharacterId, string UpgradeId) key = (characterId, upgradeId);

        if (previousRank == 0)
            _purchasedRanks.Remove(key);
        else
            _purchasedRanks[key] = previousRank;
    }

    /// <summary>
    /// 거래 조정자가 완료한 구매 상태의 변경 알림
    /// </summary>
    public void PublishChanged()
    {
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 지정 캐릭터에 적용 가능한 활성 강화 목록 조회
    /// </summary>
    /// <param name="characterId">조회할 캐릭터 식별자</param>
    /// <returns>표시 순서로 정렬한 읽기 전용 강화 목록</returns>
    public IReadOnlyList<MetaUpgradeData> GetUpgrades(string characterId)
    {
        GetCharacter(characterId);

        List<MetaUpgradeData> result = new List<MetaUpgradeData>();

        foreach (MetaUpgradeData upgrade in _upgrades.Values)
        {
            if (upgrade.Enabled && AppliesTo(upgrade, characterId))
                result.Add(upgrade);
        }

        result.Sort((left, right) =>
        {
            int order = left.SortOrder.CompareTo(right.SortOrder);

            return order != 0 ? order : StringComparer.Ordinal.Compare(left.UpgradeId, right.UpgradeId);
        });

        return result.AsReadOnly();
    }

    /// <summary>
    /// 캐릭터별 현재 구매 단계 조회
    /// </summary>
    /// <param name="characterId">강화 대상 캐릭터 식별자</param>
    /// <param name="upgradeId">조회할 강화 식별자</param>
    /// <returns>구매 완료 단계, 미구매 상태는 0</returns>
    public int GetPurchasedRank(string characterId, string upgradeId)
    {
        MetaUpgradeData upgrade = GetUpgrade(characterId, upgradeId);

        if (!AppliesTo(upgrade, characterId))
            throw new InvalidOperationException($"해당 캐릭터에 적용할 수 없는 강화: {characterId}/{upgradeId}");

        if (_purchasedRanks.TryGetValue((characterId, upgradeId), out int rank))
            return rank;

        return 0;
    }/// <summary>
     /// 캐릭터별 장착 슬롯 조회
     /// </summary>
     /// <param name="characterId">조회할 캐릭터 식별자</param>
     /// <returns>슬롯별 강화 식별자, 빈 슬롯은 null</returns>
    public IReadOnlyList<string> GetEquippedUpgradeIds(string characterId)
    {
        GetCharacter(characterId);

        return _loadouts[characterId].Slots;
    }
    /// <summary>
    /// 캐릭터의 확정된 누적 숙련도 경험치 조회
    /// </summary>
    /// <param name="characterId">조회할 캐릭터 식별자</param>
    /// <returns>누적 숙련도 경험치, 기록이 없으면 0</returns>
    public long GetMasteryExperience(string characterId)
    {
        GetCharacter(characterId);

        return _masteryExperience.TryGetValue(characterId, out long experience) ? experience : 0;
    }

    /// <summary>
    /// 지정 캐릭터의 숙련도 레벨과 현재 구간 진행량 조회
    /// </summary>
    /// <param name="characterId">조회할 캐릭터 식별자</param>
    /// <param name="level">캐릭터 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 숙련도</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 숙련도, 최대 레벨은 0</param>
    public void GetCharacterMasteryProgress(string characterId, out int level, out long currentExperience, out long requiredExperience)
    {
        long totalExperience = GetMasteryExperience(characterId);

        _characterMasteryDefinition.GetProgress(totalExperience, out level, out currentExperience, out requiredExperience);
    }

    /// <summary>
    /// 전체 캐릭터의 저장용 숙련도 기록 캡처
    /// </summary>
    /// <returns>캐릭터별 누적 숙련도 기록</returns>
    public IReadOnlyList<MetaMasteryRecord> CaptureMastery()
    {
        List<MetaMasteryRecord> records = new List<MetaMasteryRecord>();

        foreach (string characterId in _characters.Keys)
        {
            records.Add(new MetaMasteryRecord(characterId, GetMasteryExperience(characterId)));
        }

        return records.AsReadOnly();
    }

    /// <summary>
    /// 캐릭터별 숙련도와 통합 계산 범위 검증 후 상태 복원
    /// </summary>
    /// <param name="records">복원할 캐릭터별 숙련도 기록</param>
    public void RestoreMastery(IReadOnlyList<MetaMasteryRecord> records)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        Dictionary<string, long> restored = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (MetaMasteryRecord record in records)
        {
            if (record == null)
                throw new InvalidOperationException("빈 숙련도 기록 제거 필요");

            GetCharacter(record.CharacterId);

            if (restored.ContainsKey(record.CharacterId))
                throw new InvalidOperationException($"중복 숙련도 기록: {record.CharacterId}");

            restored.Add(record.CharacterId, record.TotalExperience);
        }

        // 기존 상태 교체 전에 통합 경험치와 레벨의 표현 범위 검사
        long totalExperience = CalculateMasteryExperience(restored);
        _integratedMasteryDefinition.GetLevel(totalExperience);

        _masteryExperience = restored;

        OnChanged?.Invoke();
    }

    /// <summary>
    /// 통합 숙련도 레벨과 현재 경험치 구간의 진행량 조회
    /// </summary>
    /// <param name="level">통합 숙련도 레벨</param>
    /// <param name="currentExperience">현재 레벨 구간에서 획득한 경험치</param>
    /// <param name="requiredExperience">다음 레벨 필요 경험치, 최대 레벨은 0</param>
    public void GetIntegratedMasteryProgress(out int level, out long currentExperience, out long requiredExperience)
    {
        _integratedMasteryDefinition.GetProgress(IntegratedMasteryExperience, out level, out currentExperience, out requiredExperience);
    }

    /// <summary>
    /// 전체 캐릭터의 저장용 장착 기록 캡처
    /// </summary>
    /// <param name="replacement">이번 저장에 사용할 검증된 변경 후보, 없으면 현재 기록</param>
    /// <returns>전체 캐릭터의 장착 기록</returns>
    public IReadOnlyList<MetaLoadoutRecord> CaptureLoadouts(MetaLoadoutRecord replacement = null)
    {
        if (replacement != null)
            GetCharacter(replacement.CharacterId);

        List<MetaLoadoutRecord> records = new List<MetaLoadoutRecord>();

        foreach (KeyValuePair<string, MetaLoadoutRecord> entry in _loadouts)
        {
            records.Add(replacement != null && entry.Key == replacement.CharacterId ? replacement : entry.Value);
        }

        return records.AsReadOnly();
    }

    /// <summary>
    /// 구매 기록 복원 이후 장착 기록 전체 검증과 적용
    /// </summary>
    /// <param name="records">복원할 캐릭터별 장착 기록</param>
    public void RestoreLoadouts(IReadOnlyList<MetaLoadoutRecord> records)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        Dictionary<string, MetaLoadoutRecord> restored =
            new Dictionary<string, MetaLoadoutRecord>(StringComparer.Ordinal);

        foreach (string characterId in _characters.Keys)
        {
            restored.Add(characterId, new MetaLoadoutRecord( characterId, new string[EquipSlotCount]));
        }

        HashSet<string> restoredCharacters = new HashSet<string>(StringComparer.Ordinal);

        foreach (MetaLoadoutRecord record in records)
        {
            if (record == null)
                throw new InvalidOperationException("빈 장착 기록 제거 필요");

            if (!restoredCharacters.Add(record.CharacterId))
                throw new InvalidOperationException($"중복 장착 기록: {record.CharacterId}");

            MetaEquipStatus result = ValidateLoadout(record.CharacterId, record.Slots);

            if (result != MetaEquipStatus.Prepared)
                throw new InvalidOperationException($"유효하지 않은 장착 기록: {record.CharacterId}/{result}");

            restored[record.CharacterId] = record;
        }

        _loadouts = restored;
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 활성 상태와 대상 캐릭터 및 최대 단계를 확인하여 다음 단계 조회
    /// </summary>
    /// <param name="characterId">강화 대상 캐릭터 식별자</param>
    /// <param name="upgradeId">조회할 강화 식별자</param>
    /// <param name="nextRank">다음 단계의 재화와 비용 및 전체 효과</param>
    /// <returns>구매 가능한 다음 단계 존재 여부</returns>
    public bool TryGetNextRank(string characterId, string upgradeId, out MetaUpgradeRankData nextRank)
    {
        nextRank = null;

        MetaUpgradeData upgrade = GetUpgrade(characterId, upgradeId);

        if (!upgrade.Enabled || !AppliesTo(upgrade, characterId))
            return false;

        int currentRank = GetPurchasedRank(characterId, upgradeId);

        if (currentRank >= upgrade.MaxRank)
            return false;

        nextRank = upgrade.GetRank(currentRank + 1);
        return true;
    }

    /// <summary>
    /// 장착한 강화의 기존 구매 단계로 이번 런 보너스 확정
    /// </summary>
    /// <param name="characterId">런에 사용할 캐릭터 식별자</param>
    /// <returns>캐릭터 성장 설정과 메타 보너스의 스냅샷</returns>
    public RunGrowthSnapshot CreateRunSnapshot(string characterId)
    {
        CharacterGrowthDefinition character = GetCharacter(characterId);

        CharacterStatValues permanentBonus = default;
        CharacterStatValues perLevelBonus = default;

        foreach (string upgradeId in _loadouts[characterId].Slots)
        {
            if (upgradeId == null)
                continue;

            MetaUpgradeData upgrade = GetUpgrade(characterId, upgradeId);
            int rank = GetPurchasedRank(characterId, upgradeId);

            if (rank == 0)
                continue;

            GetRankBonuses(upgrade, rank, out CharacterStatValues startBonus, out CharacterStatValues growthBonus);

            permanentBonus = permanentBonus.Add(startBonus);
            perLevelBonus = perLevelBonus.Add(growthBonus);
        }

        return new RunGrowthSnapshot(character, permanentBonus, perLevelBonus);
    }

    /// <summary>
    /// 저장할 캐릭터별 구매 기록의 읽기 전용 복사본 생성
    /// </summary>
    /// <returns>구매 단계가 1 이상인 강화 기록</returns>
    public IReadOnlyList<MetaUpgradeRecord> CapturePurchases()
    {
        List<MetaUpgradeRecord> records = new List<MetaUpgradeRecord>();

        foreach (KeyValuePair<(string CharacterId, string UpgradeId), int> entry
                 in _purchasedRanks)
        {
            if (entry.Value == 0)
                continue;

            records.Add(new MetaUpgradeRecord(entry.Key.CharacterId, entry.Key.UpgradeId, entry.Value));
        }

        return records.AsReadOnly();
    }

    /// <summary>
    /// 런 진입 전 저장 기록 전체 검증 후 구매 상태 교체
    /// </summary>
    /// <param name="records">복원할 캐릭터별 구매 기록</param>
    public void RestorePurchases(IReadOnlyList<MetaUpgradeRecord> records)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        Dictionary<(string CharacterId, string UpgradeId), int> restoredRanks = new Dictionary<(string CharacterId, string UpgradeId), int>();

        foreach (MetaUpgradeRecord record in records)
        {
            MetaUpgradeData upgrade = GetUpgrade(record.CharacterId, record.UpgradeId);

            if (!AppliesTo(upgrade, record.CharacterId))
                throw new InvalidOperationException($"저장된 강화의 대상 캐릭터 불일치: " + $"{record.CharacterId}/{record.UpgradeId}");

            if (record.Rank < 0 || record.Rank > upgrade.MaxRank)
                throw new InvalidOperationException($"저장된 강화 단계 범위 오류: " + $"{record.CharacterId}/{record.UpgradeId}/{record.Rank}");

            (string CharacterId, string UpgradeId) key = (record.CharacterId, record.UpgradeId);

            if (restoredRanks.ContainsKey(key))
                throw new InvalidOperationException($"중복 저장 기록: {key.CharacterId}/{key.UpgradeId}");

            // 비활성 강화의 구매 기록도 보존하고 실제 효과 적용에서 제외
            restoredRanks.Add(key, record.Rank);
        }

        // 전체 검증 성공 후 구매 상태 교체
        _purchasedRanks = restoredRanks;

        OnChanged?.Invoke();
    }
}