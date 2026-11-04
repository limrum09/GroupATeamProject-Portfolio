using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 구매 요청 결과
/// </summary>
public enum ShopPurchaseResult
{
    Purchased,
    Unavailable,
    InsufficientGold,
    SlotsFull,
    AlreadyOwned
}

/// <summary>
/// 보유 스킬 판매 요청 결과
/// </summary>
public enum ShopSaleResult
{
    Sold,
    Unavailable
}

/// <summary>
/// 월드 공용 상점 재고와 스킬 구매·판매 조정
/// </summary>
public sealed class SessionShopPurchaseService : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] SessionEconomyManager _economy;
    [SerializeField] ShopListPool _pool;

    [Header("----- 상품 설정 -----")]
    [SerializeField, Min(1)] int _productCount = 3;

    IReadOnlyList<SessionShopProduct> _products;
    readonly HashSet<int> _soldIndices = new HashSet<int>();
    SessionShop _shop;
    SkillLoadout _actor;
    SessionProgressManager _world;
    bool _isTrading;
    bool _endWorldPending;

    // 파괴된 Unity 참조도 명시적인 연결 해제 이전에는 독립 테스트로 전환 금지
    bool HasWorldBinding => !ReferenceEquals(_world, null);

    public event Action OnChanged;

    public event Action<float> OnGoldChanged
    {
        add => _economy.OnGoldChanged += value;
        remove => _economy.OnGoldChanged -= value;
    }

    public IReadOnlyList<SessionShopProduct> Products => _products;
    public SkillLoadout Actor => _actor;
    public float Gold => _economy.Gold;
    public bool HasWorld => _products != null;

    void Awake()
    {
        if (_economy == null || _pool == null)
            throw new MissingReferenceException($"{name}: Economy와 상품 풀 연결 필요");
    }

    void OnDisable()
    {
        UnbindWorld();
    }

    void ClearWorld()
    {
        bool hadWorld = HasWorld;
        _products = null;
        _soldIndices.Clear();
        _endWorldPending = false;
        if (hadWorld)
            NotifySafely(OnChanged);
    }

    bool CanAccessWorld()
    {
        return isActiveAndEnabled && !_endWorldPending &&
            (!HasWorldBinding || (_world != null && _world.IsReady && !_world.IsTransitioning &&
                _world.Progress.HasCurrentNode &&
                _world.Graph.GetNode(_world.Progress.CurrentNodeId).Type == MapBindings.MapType.Shop));
    }

    void NotifySafely(Action listeners)
    {
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            try
            {
                listener();
            }
            catch (Exception exception)
            {
                // 확정된 거래를 유지하고 실패한 표시 알림 보고
                Debug.LogException(exception, this);
            }
        }
    }

    internal void BeginInteraction(SessionShop shop, SkillLoadout actor)
    {
        if (!CanAccessWorld())
            throw new InvalidOperationException("활성 서비스와 준비된 월드에서 상점 접근 필요");

        if (!HasWorld)
        {
            if (HasWorldBinding)
                throw new InvalidOperationException("월드 준비 완료 후 상점 접근 필요");

            // 월드 진행을 연결하지 않은 기존 독립 상점 테스트의 최초 진입
            BeginWorld();
        }
        if (_shop != null)
            _shop.Close();

        _shop = shop;
        _actor = actor;
    }

    internal void EndInteraction()
    {
        _shop = null;
        _actor = null;
    }

    /// <summary>
    /// 월드 준비·해제 이벤트 연결과 이미 준비된 월드의 재고 구성
    /// </summary>
    /// <param name="world">공용 상점 재고의 수명을 제공할 월드 진행 담당자</param>
    public void BindWorld(SessionProgressManager world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (!isActiveAndEnabled || _isTrading || _endWorldPending)
            throw new InvalidOperationException("활성 상태에서 거래 종료 후 월드 연결 필요");
        if (ReferenceEquals(_world, world)) return;
        if (HasWorldBinding)
            throw new InvalidOperationException("기존 월드 연결 해제 후 다른 월드 연결 필요");

        EndWorld();
        _world = world;
        _world.OnWorldPrepared += BeginWorld;
        _world.OnWorldReleased += EndWorld;
        try
        {
            if (_world.IsReady)
                BeginWorld();
        }
        catch
        {
            UnbindWorld();
            throw;
        }
    }

    /// <summary>
    /// 월드 구독과 열린 상점 및 공용 재고 해제
    /// </summary>
    public void UnbindWorld()
    {
        if (_world != null)
        {
            _world.OnWorldPrepared -= BeginWorld;
            _world.OnWorldReleased -= EndWorld;
        }
        _world = null;
        EndWorld();
    }

    /// <summary>
    /// 월드 공용 상품 추첨과 빈 판매 기록 구성, 같은 월드의 중복 초기화 생략
    /// </summary>
    public void BeginWorld()
    {
        if (!isActiveAndEnabled || _isTrading || _endWorldPending ||
            (HasWorldBinding && (_world == null || !_world.IsReady)))
            throw new InvalidOperationException("준비된 월드와 거래 종료 상태에서 재고 초기화 필요");
        if (HasWorld) return;
        if (_productCount < 1)
            throw new InvalidOperationException("양수 상품 수 설정 필요");

        IReadOnlyList<SessionShopProduct> products = _pool.GetRandomProducts(_productCount);
        _soldIndices.Clear();
        _products = products;
        NotifySafely(OnChanged);
    }

    /// <summary>
    /// 열린 상점 종료와 공용 재고 제거, 진행 중 거래는 확정·복구 이후 제거
    /// </summary>
    public void EndWorld()
    {
        _endWorldPending = true;
        SessionShop shop = _shop;
        EndInteraction();
        try
        {
            if (shop != null)
                shop.Close();
        }
        finally
        {
            if (!_isTrading)
                ClearWorld();
        }
    }

    /// <summary>
    /// 상품의 판매 완료 여부 조회
    /// </summary>
    /// <param name="index">상품 목록 인덱스</param>
    /// <returns>판매 완료 여부</returns>
    public bool IsSold(int index)
    {
        return _soldIndices.Contains(index);
    }

    /// <summary>
    /// 슬롯 여유와 잔액을 제외한 현재 상품 요청 조건 조회
    /// </summary>
    /// <param name="index">상품 목록 인덱스</param>
    /// <returns>현재 상품의 구매 요청 가능 여부</returns>
    public bool CanRequestPurchase(int index)
    {
        if (!CanAccessWorld())
            return false;
        if (_shop == null || _actor == null || !_shop.CanInteract(_actor))
            return false;
        if (_products == null || index < 0 || index >= _products.Count || IsSold(index))
            return false;

        return true;
    }

    /// <summary>
    /// 선택한 보유 스킬의 현재 판매 가능 여부와 기준가의 75% 조회
    /// </summary>
    /// <param name="slot">선택한 슬롯</param>
    /// <param name="expected">선택 당시 스킬 인스턴스</param>
    /// <param name="price">판매 시 지급할 세션 골드</param>
    /// <returns>현재 판매 가능 여부</returns>
    public bool TryGetSalePrice(int slot, SkillInstance expected, out float price)
    {
        price = 0f;
        if (!CanAccessWorld() || _shop == null || _actor == null || !_shop.CanInteract(_actor) ||
            !_actor.CanRemoveSkill(slot, expected) || !_pool.TryGetPrice(expected.Definition, out float purchasePrice))
            return false;
        price = purchasePrice * 0.75f;
        return true;
    }

    /// <summary>
    /// 현재 선택 인스턴스를 제거하고 판매 골드 단일 지급
    /// </summary>
    /// <param name="slot">판매할 슬롯</param>
    /// <param name="expected">선택 당시 스킬 인스턴스</param>
    /// <returns>판매 성공 또는 요청 거절</returns>
    public ShopSaleResult RequestSale(int slot, SkillInstance expected)
    {
        if (_isTrading || !TryGetSalePrice(slot, expected, out float price))
            return ShopSaleResult.Unavailable;
        if (float.IsInfinity(_economy.Gold + price))
            throw new OverflowException("판매 후 세션 골드 범위 초과");

        SkillLoadout actor = _actor;
        _isTrading = true;
        try
        {
            try
            {
                if (!actor.TryRemoveSkill(slot, expected)) return ShopSaleResult.Unavailable;
            }
            catch (Exception exception)
            {
                // 제거 후 알림 예외에서도 실제 제거 결과에 대해 한 번만 지급
                if (actor.GetSkill(slot) == expected) throw;
                Debug.LogException(exception, this);
            }
            NotifySafely(() => _economy.AddGold(price, GoldGainSource.SkillSale));
            NotifySafely(OnChanged);
            return ShopSaleResult.Sold;
        }
        finally
        {
            _isTrading = false;
            if (_endWorldPending) ClearWorld();
        }
    }

    /// <summary>
    /// 빈 슬롯에 자동 장착하고 결제와 재고 확정
    /// </summary>
    /// <param name="index">구매할 상품 인덱스</param>
    /// <returns>구매 성공 또는 거절 사유</returns>
    public ShopPurchaseResult RequestPurchase(int index)
    {
        if (_isTrading || !CanRequestPurchase(index))
            return ShopPurchaseResult.Unavailable;

        SessionShopProduct product = _products[index];
        SkillLoadout actor = _actor;

        if (actor.HasSkill(product.Skill))
            return ShopPurchaseResult.AlreadyOwned;

        if (!actor.TryGetAcquisitionSlot(product.Skill, out int slot))
        {
            // 기존 조회 실패를 슬롯 부족으로 단정하지 않고 실제 빈 슬롯 구분
            for (int i = 0; i < actor.SlotCount; i++)
            {
                if (actor.GetSkill(i) == null)
                    return ShopPurchaseResult.Unavailable;
            }
            return ShopPurchaseResult.SlotsFull;
        }

        bool debited = false;
        bool committed = false;
        _isTrading = true;
        try
        {
            if (!_economy.TryDebit(product.Price))
                return ShopPurchaseResult.InsufficientGold;

            debited = true;
            _soldIndices.Add(index);
            try
            {
                if (!actor.TryAcquireSkill(product.Skill, out slot))
                    return ShopPurchaseResult.Unavailable;
            }
            catch (Exception exception)
            {
                // 기존 API의 지급 후 알림 예외에서는 실제 슬롯의 지급 결과 유지
                SkillInstance acquired = actor.GetSkill(slot);
                if (acquired == null || acquired.Definition != product.Skill)
                    throw;

                committed = true;
                Debug.LogException(exception, this);
            }

            committed = true;
            NotifySafely(_economy.PublishGoldChanged);
            NotifySafely(OnChanged);
            return ShopPurchaseResult.Purchased;
        }
        finally
        {
            if (!committed)
            {
                _soldIndices.Remove(index);
                if (debited)
                    _economy.RefundDebit(product.Price);
            }
            _isTrading = false;
            if (_endWorldPending)
                ClearWorld();
        }
    }
}
