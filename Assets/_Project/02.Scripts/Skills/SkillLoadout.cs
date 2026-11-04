using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SkillLoadout : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] CombatController _combat;
    
    Guid _startingSkillsRunId;

    const int FixedSlotCount = 4;
    
    readonly SkillInstance[] _slots = new SkillInstance[FixedSlotCount];

    public event Action OnChanged;

    public CombatController Combat => _combat;
    public int SlotCount => FixedSlotCount;
    bool CanChangeSkills => isActiveAndEnabled && _combat.isActiveAndEnabled && _combat.Owner.IsAlive;
    bool CanEdit => CanChangeSkills && !_combat.IsCasting;

    void Awake()
    {
        if (_combat == null || _combat.Owner == null || _combat.Owner.Status == null || _combat.Owner.Status.Definition == null || _combat.Owner.ActorTransform == null)
            throw new MissingReferenceException($"{name}: CombatController와 소유 캐릭터 설정이 필요합니다.");

        if (_combat.Skills != this || !transform.IsChildOf(_combat.Owner.ActorTransform))
            throw new InvalidOperationException($"{name}: 같은 캐릭터의 CombatController와 SkillLoadout을 서로 연결하세요.");
    }

    bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < _slots.Length;
    }

    bool IsSlotSkillDefinition(SkillDefinition definition)
    {
        CharacterDefinition owner = _combat.Owner.Status.Definition;

        return definition != null && !(definition is MovementSkillDefinition) && definition != owner.BasicAttackSkill && definition.IsAvailableTo(owner);
    }

    bool CanEquipDefinition(SkillDefinition definition)
    {
        return CanEdit && IsSlotSkillDefinition(definition);
    }

    public SkillInstance GetSkill(int slot)
    {
        if (!IsValidSlot(slot))
            return null;

        return _slots[slot];
    }

    /// <summary>
    /// 선택 당시 스킬과 레벨을 확인한 뒤 한 단계 강화
    /// </summary>
    /// <param name="slot">강화할 슬롯 번호</param>
    /// <param name="expected">선택 당시 스킬 인스턴스</param>
    /// <param name="expectedLevel">선택 당시 스킬 레벨</param>
    /// <returns>강화 적용 성공 여부</returns>
    internal bool TryUpgradeSkill(int slot, SkillInstance expected, int expectedLevel)
    {
        if (!CanUpgradeSkill(slot) || expected == null || _slots[slot] != expected || expected.Level != expectedLevel)
            return false;

        // 재화 차감과 강화 적용 완료 후 거래 조정자에서 알림
        return expected.TryLevelUp();
    }

    /// <summary>
    /// 거래 완료 후 장착 스킬 상태 변경 알림
    /// </summary>
    internal void PublishChanged()
    {
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 새 런의 QWER 시작 구성을 슬롯 위치 그대로 적용
    /// </summary>
    /// <param name="definition">선택한 플레이어 정의</param>
    /// <param name="runId">초기화할 런 식별자</param>
    public void InitializeStartingSkills(PlayerDefinition definition, Guid runId)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (runId == Guid.Empty)
            throw new ArgumentException("유효한 런 식별자 필요", nameof(runId));

        CharacterStatus status = _combat.Owner.Status;

        if (!status.IsInitialized || status.Definition != definition || status.RunId != runId)
            throw new InvalidOperationException($"{name}: 같은 플레이어 정의와 런으로 전투 상태 초기화 필요");

        // 같은 런의 재연결에서는 장착 상태와 강화·쿨타임 보존
        if (_startingSkillsRunId == runId)
            return;

        if (_combat.IsBusy)
            throw new InvalidOperationException($"{name}: 진행 중인 전투 행동 종료 후 시작 슬롯 초기화 필요");

        SkillInstance[] startingSlots = new SkillInstance[FixedSlotCount];

        for (int slot = 0; slot < startingSlots.Length; slot++)
        {
            SkillDefinition skillDefinition = definition.GetStartingSkill(slot);

            // 미지정 슬롯은 해당 위치의 null 유지
            if (skillDefinition == null)
                continue;

            if (!IsSlotSkillDefinition(skillDefinition))
                throw new InvalidOperationException($"{definition.name}: {slot}번 시작 슬롯에 {skillDefinition.name} 장착 불가");

            SkillExecutor.ValidateDefinition(skillDefinition);

            startingSlots[slot] = new SkillInstance(skillDefinition);
        }

        // 모든 슬롯의 검증과 생성 완료 후 기존 구성 교체
        Array.Copy(startingSlots, _slots, _slots.Length);

        _startingSkillsRunId = runId;

        OnChanged?.Invoke();
    }

    /// <summary>
    /// 스킬 인스턴스를 유지한 슬롯 이동 또는 교환
    /// </summary>
    /// <param name="first">출발 슬롯</param>
    /// <param name="second">도착 슬롯</param>
    /// <param name="expectedSource">확인할 출발 스킬, 미지정 시 현재 상태 사용</param>
    /// <returns>이동 또는 교환 성공 여부</returns>
    public bool TrySwapSlots(int first, int second, SkillInstance expectedSource = null)
    {
        if (!CanEdit || !IsValidSlot(first) || !IsValidSlot(second))
            return false;

        if (expectedSource != null && _slots[first] != expectedSource)
            return false;

        if (first == second)
            return true;

        SkillInstance temporary = _slots[first];
        _slots[first] = _slots[second];
        _slots[second] = temporary;

        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 현재 장착 조건을 만족하는 첫 빈 슬롯 조회, 슬롯 예약 없음
    /// </summary>
    /// <param name="definition">지급할 스킬 정의</param>
    /// <param name="slot">QWER 순서의 첫 빈 슬롯, 실패 시 -1</param>
    /// <returns>현재 지급 가능 여부</returns>
    public bool TryGetAcquisitionSlot(SkillDefinition definition, out int slot)
    {
        slot = -1;

        if (!CanEquipDefinition(definition))
            return false;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null)
                continue;

            slot = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 실행 시점의 첫 빈 슬롯에 새 스킬 지급과 변경 알림
    /// </summary>
    /// <param name="definition">지급할 스킬 정의</param>
    /// <param name="slot">장착된 슬롯, 실패 시 -1</param>
    /// <returns>지급 성공 여부</returns>
    internal bool TryAcquireSkill(SkillDefinition definition, out int slot)
    {
        // 사전 조회 이후 달라진 장착 조건과 빈 슬롯 재확인
        if (!TryGetAcquisitionSlot(definition, out slot))
            return false;

        _slots[slot] = new SkillInstance(definition);

        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 선택한 스킬이 유지된 슬롯의 제거와 변경 알림
    /// </summary>
    /// <param name="slot">제거할 슬롯 번호</param>
    /// <param name="expected">선택 당시의 스킬 인스턴스</param>
    /// <returns>제거 성공 여부</returns>
    internal bool TryRemoveSkill(int slot, SkillInstance expected)
    {
        // 드래그 이후 교환되거나 교체된 스킬의 잘못된 제거 방지
        if (!CanRemoveSkill(slot, expected))
            return false;

        _slots[slot] = null;

        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 현재 QWER 슬롯의 동일 UID 스킬 보유 여부 조회
    /// </summary>
    /// <param name="definition">보유 여부를 확인할 스킬 정의</param>
    /// <returns>같은 UID의 스킬을 보유하면 true</returns>
    public bool HasSkill(SkillDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        definition.ValidateSkillUID();

        foreach (SkillInstance skill in _slots)
        {
            if (skill == null)
                continue;

            SkillDefinition ownedDefinition = skill.Definition;
            ownedDefinition.ValidateSkillUID();

            if (StringComparer.Ordinal.Equals(
                ownedDefinition.SkillUID,
                definition.SkillUID))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 지정 슬롯과 선택한 스킬 인스턴스의 현재 제거 가능 여부
    /// </summary>
    /// <param name="slot">제거할 슬롯 번호</param>
    /// <param name="expected">선택 당시의 스킬 인스턴스</param>
    /// <returns>현재 제거 가능 여부</returns>
    public bool CanRemoveSkill(int slot, SkillInstance expected)
    {
        return CanEdit &&IsValidSlot(slot) && expected != null && _slots[slot] == expected;
    }

    public bool CanUpgradeSkill(int slot)
    {
        return CanChangeSkills && IsValidSlot(slot) && _slots[slot] != null && _slots[slot].CanLevelUp;
    }
}