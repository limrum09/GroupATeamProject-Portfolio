using System;
using UnityEngine;

/// <summary>
/// 상점 접근 검사와 거래 문맥 수명 관리
/// </summary>
public sealed class SessionShop : MonoBehaviour, IInteractable
{
    [Header("----- 연결 -----")]
    [SerializeField] SessionShopPurchaseService _purchases;

    [Header("----- 접근 설정 -----")]
    [SerializeField, Min(0.1f)] float _interactionRange = 3f;

    SkillLoadout _actor;
    bool _isOpen;

    public event Action OnOpened;
    public event Action OnClosed;

    void Update()
    {
        if (_isOpen && !CanInteract(_actor))
            Close();
    }

    void OnDisable()
    {
        if (_isOpen)
            Close();
    }

    /// <summary>
    /// 월드 공용 구매 서비스 연결, 교체 시 기존 상점 화면 종료
    /// </summary>
    /// <param name="purchases">세션에서 공유할 구매 서비스</param>
    public void BindPurchases(SessionShopPurchaseService purchases)
    {
        if (purchases == null)
            throw new System.ArgumentNullException(nameof(purchases));
        if (_purchases == purchases) return;

        Close();
        _purchases = purchases;
    }

    /// <summary>
    /// 구매 서비스 연결과 생존 상태 및 상점 접근 거리 확인
    /// </summary>
    /// <param name="actor">요청 플레이어의 스킬 구성</param>
    /// <returns>현재 상점 접근 가능 여부</returns>
    public bool CanInteract(SkillLoadout actor)
    {
        if (!isActiveAndEnabled || _purchases == null || !_purchases.isActiveAndEnabled ||
            actor == null || !actor.isActiveAndEnabled)
            return false;

        CombatController combat = actor.Combat;

        return combat.isActiveAndEnabled &&
            combat.Owner.Team == CombatTeam.Player &&
            combat.Owner.IsAlive &&
            (combat.Owner.Position - transform.position).sqrMagnitude <=
                _interactionRange * _interactionRange;
    }

    /// <summary>상점 접근 후 화면 열기 또는 닫기</summary>
    /// <param name="actor">요청 플레이어의 스킬 구성</param>
    /// <returns>상호작용 성공 여부</returns>
    public bool TryInteract(SkillLoadout actor)
    {
        if (!CanInteract(actor))
        {
            return false;
        }

        if (_isOpen)
        {
            Close();
            return true;
        }

        _actor = actor;
        _purchases.BeginInteraction(this, actor);
        _isOpen = true;

        OnOpened?.Invoke();
        return true;
    }

    /// <summary>상점 접근 문맥과 확인 대기 상태 해제</summary>
    public void Close()
    {
        if (!_isOpen) return;

        _isOpen = false;
        _purchases.EndInteraction();
        _actor = null;

        OnClosed?.Invoke();
    }
}
