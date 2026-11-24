using System;
using UnityEngine;

/// <summary>
/// 월드에 놓인 체력 포션의 접근 감지와 회복 적용
/// </summary>
public class HealthPotionPickup : MonoBehaviour
{
    [Header("----- 접근 설정 -----")]
    [SerializeField, Min(0.1f)] float _pickupRange = 1.2f;

    HealthPotionDefinition _definition;
    CombatTarget _owner;

    void Start()
    {
        if (_definition == null || _owner == null)
        {
            enabled = false;
            throw new InvalidOperationException($"{name}: 활성화 전 Init 호출 필요");
        }
    }

    void Update()
    {
        if (!_owner.IsAlive)
            return;

        float distanceSquared = (_owner.Position - transform.position).sqrMagnitude;

        if (distanceSquared > _pickupRange * _pickupRange)
            return;

        CharacterStatus status = _owner.Status;

        // 최대 체력으로 회복량이 0이어도 획득 처리
        status.Heal(status.MaxHealth * _definition.HealRatio);

        // 제거 예약 이후 같은 프레임의 추가 처리 차단
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    /// <summary>
    /// 회복 정의와 획득 대상 연결
    /// </summary>
    /// <param name="definition">회복 비율 정의</param>
    /// <param name="owner">포션을 획득할 플레이어 전투 대상</param>
    public void Init(HealthPotionDefinition definition, CombatTarget owner)
    {
        if (definition == null || owner == null)
            throw new MissingReferenceException("포션 정의와 획득 대상 필요");

        _definition = definition;
        _owner = owner;
    }
}
