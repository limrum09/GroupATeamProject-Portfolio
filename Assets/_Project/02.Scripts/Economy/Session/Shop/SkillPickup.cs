using UnityEngine;

/// <summary>구매 완료 스킬의 월드 획득 상호작용</summary>
public sealed class SkillPickup : MonoBehaviour, IInteractable
{
    [Header("----- 접근 설정 -----")]
    [SerializeField, Min(0.1f)] float _interactionRange = 2f;

    SkillDefinition _skill;
    SkillLoadout _owner;

    bool _claimed;

    /// <summary>구매한 스킬과 획득 소유자 연결</summary>
    /// <param name="skill">구매한 스킬 정의</param>
    /// <param name="owner">획득 가능한 플레이어</param>
    public void Init(SkillDefinition skill, SkillLoadout owner)
    {
        if (skill == null || owner == null)
            throw new MissingReferenceException("드롭 스킬과 소유자 필요");

        _skill = skill;
        _owner = owner;
        _claimed = false;
    }

    /// <summary>소유자와 접근 거리 및 빈 슬롯 확인</summary>
    /// <param name="actor">상호작용 요청자</param>
    /// <returns>현재 획득 가능 여부</returns>
    public bool CanInteract(SkillLoadout actor)
    {
        if (!isActiveAndEnabled || _claimed || actor == null || actor != _owner)
            return false;

        float distanceSquared =
            (actor.Combat.Owner.Position - transform.position).sqrMagnitude;

        return distanceSquared <= _interactionRange * _interactionRange &&
            actor.TryGetAcquisitionSlot(_skill, out int slot);
    }

    /// <summary>첫 빈 슬롯에 장착 후 월드 드롭 제거</summary>
    /// <param name="actor">상호작용 요청자</param>
    /// <returns>스킬 획득 성공 여부</returns>
    public bool TryInteract(SkillLoadout actor)
    {
        if (!CanInteract(actor))
            return false;

        // 장착 변경 알림 중 발생하는 중복 획득 차단
        _claimed = true;

        if (!actor.TryAcquireSkill(_skill, out int slot))
        {
            _claimed = false;
            return false;
        }

        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
}
