using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CombatTarget : MonoBehaviour, IDamageReceiver
{
    [Header("----- 대상 정보 -----")]
    [SerializeField] CombatTeam _team;
    [SerializeField] CharacterStatus _status;
    [SerializeField] Transform _aimPoint;
    [SerializeField] Transform _actorTransform;

    Guid _combatantId;

    /// <summary>
    /// 받은 공격 정보와 실제 감소한 체력을 전달한다.
    /// 피해 숫자나 피격 연출에서 구독할 수 있다.
    /// </summary>
    public event Action<DamageRequest, float> OnDamaged;

    public CombatTeam Team => _team;
    public CharacterStatus Status => _status;
    public Transform ActorTransform => _actorTransform;
    public Vector3 Position => _actorTransform.position;
    public Vector3 AimPosition => _aimPoint.position;
    public bool IsAlive => isActiveAndEnabled && _status.IsAlive;
    /// <summary>현재 활성화의 식별값과 현재 진영으로 구성한 공격자 정보</summary>
    public CombatSource Source => new CombatSource(_combatantId, _team);

    void Awake()
    {
        if (_actorTransform == null || _status == null || _aimPoint == null)
            throw new MissingReferenceException($"{name}: 캐릭터 Transform, CharacterStatus, AimPoint가 필요합니다.");

        if (!transform.IsChildOf(_actorTransform) || !_status.transform.IsChildOf(_actorTransform) || !_aimPoint.IsChildOf(_actorTransform))
            throw new InvalidOperationException($"{name}: 전투 대상, 상태, AimPoint는 지정한 캐릭터 계층에 있어야 합니다.");
    }

    void OnEnable()
    {
        // 풀에서 재사용된 개체를 이전 활성화와 구분
        _combatantId = Guid.NewGuid();
    }

    /// <summary>살아 있는 시전자의 새 공격에 대한 피격 가능 여부</summary>
    /// <param name="source">공격 요청자</param>
    /// <returns>피격 가능 여부</returns>
    public bool CanReceiveFrom(CombatTarget source)
    {
        return source != null && source.IsAlive && CanReceiveFrom(source.Source);
    }

    /// <summary>이미 발동한 공격의 저장 정보에 따른 피격 가능 여부</summary>
    /// <param name="source">발동 당시 공격자 정보</param>
    /// <returns>피격 가능 여부</returns>
    public bool CanReceiveFrom(CombatSource source)
    {
        return IsAlive && source.IsValid && source.Id != _combatantId && source.Team != _team;
    }

    /// <summary>살아 있는 공격자의 대상·거리·차단 조건 검사</summary>
    /// <param name="source">공격 요청자</param>
    /// <param name="range">공격 거리</param>
    /// <param name="blockingMask">차단 레이어</param>
    /// <returns>대상 검사 결과</returns>
    public AttackTargetState CheckHitFrom(CombatTarget source, float range, LayerMask blockingMask)
    {
        if (source == null || !source.IsAlive)
            return AttackTargetState.Invalid;

        return CheckHitFrom(source.Source, source.Position, source.AimPosition, range, blockingMask);
    }

    /// <summary>발동 시 저장한 공격자 정보와 위치를 사용한 대상 검사</summary>
    /// <param name="source">발동 당시 공격자 정보</param>
    /// <param name="sourcePosition">공격자의 발동 당시 위치</param>
    /// <param name="sourceAimPosition">발동 당시 시야 검사 기준점</param>
    /// <param name="range">공격 거리</param>
    /// <param name="blockingMask">차단 레이어</param>
    /// <returns>대상 검사 결과</returns>
    public AttackTargetState CheckHitFrom(CombatSource source, Vector3 sourcePosition, Vector3 sourceAimPosition, float range, LayerMask blockingMask)
    {
        if (!CanReceiveFrom(source))
            return AttackTargetState.Invalid;

        float distanceSquared = (Position - sourcePosition).sqrMagnitude;

        if (distanceSquared > range * range)
            return AttackTargetState.OutOfRange;

        if (Physics.Linecast(sourceAimPosition, AimPosition, blockingMask, QueryTriggerInteraction.Ignore))
            return AttackTargetState.Blocked;

        return AttackTargetState.Valid;
    }

    public bool ReceiveDamage(DamageRequest request)
    {
        if (!CanReceiveFrom(request.Source))
            return false;

        if (float.IsNaN(request.Power) || float.IsInfinity(request.Power) || request.Power < 0f || float.IsNaN(request.CriticalMultiplier) || float.IsInfinity(request.CriticalMultiplier) || request.CriticalMultiplier < 1f)
            return false;

        float damage = Mathf.Max(0f, request.Power - _status.Defense);
        damage *= request.CriticalMultiplier;

        if (float.IsInfinity(damage))
            return false;

        float applied = _status.ApplyDamage(damage);
        OnDamaged?.Invoke(request, applied);

        return true;
    }
}