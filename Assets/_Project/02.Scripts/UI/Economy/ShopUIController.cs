using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 상점 접근 이벤트와 독립 상점 화면 연결
/// </summary>
[DisallowMultipleComponent]
public class ShopUIController : MonoBehaviour
{
    [Header("----- 상점 화면 -----")]
    [SerializeField] SessionShopView _view;

    bool _cancelInputRouted;
    SessionShop _shop;
    SessionShopPurchaseService _purchases;

    public event Action OnOpening;
    public event Action OnClosed;

    void Update()
    {
        if (_cancelInputRouted || !_view.IsOpen) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            TryHandleCancel();
    }

    void HandleOpened()
    {
        try
        {
            OnOpening?.Invoke();
            _view.Open(_shop, _purchases);
        }
        catch
        {
            _shop.Close();
            throw;
        }
    }

    void HandleClosed()
    {
        try { _view.Close(); }
        finally { OnClosed?.Invoke(); }
    }

    /// <summary>
    /// 상점과 구매 서비스의 화면 연결
    /// </summary>
    /// <param name="shop">접근 문맥을 소유한 상점</param>
    /// <param name="purchases">연결할 구매 서비스</param>
    public void Bind(SessionShop shop, SessionShopPurchaseService purchases)
    {
        if (shop == null) throw new ArgumentNullException(nameof(shop));
        if (purchases == null) throw new ArgumentNullException(nameof(purchases));
        if (_view == null) throw new MissingReferenceException("SessionShopView 연결 필요");
        if (_shop != null || !isActiveAndEnabled || !_view.isActiveAndEnabled ||
            shop.gameObject.scene != gameObject.scene || purchases.gameObject.scene != gameObject.scene ||
            _view.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬의 미연결 Controller와 활성 상점 화면 필요");

        _shop = shop;
        _purchases = purchases;
        _shop.OnOpened += HandleOpened;
        _shop.OnClosed += HandleClosed;
        _view.Close();
    }

    /// <summary>
    /// 씬 라우터의 취소 입력 처리 여부 지정
    /// </summary>
    /// <param name="routed">라우터 연결 여부</param>
    public void SetCancelInputRouted(bool routed)
    {
        _cancelInputRouted = routed;
    }

    /// <summary>
    /// 상점 내부 취소 우선 처리 후 상점 접근 종료
    /// </summary>
    /// <returns>상점이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        if (!_view.IsOpen) return false;
        if (_view.TryHandleCancel()) return true;

        Close();
        return true;
    }

    /// <summary>
    /// 상점 문맥과 화면 종료
    /// </summary>
    public void Close()
    {
        if (_shop != null) _shop.Close();
    }

    /// <summary>
    /// 상점 이벤트와 화면 참조 해제
    /// </summary>
    public void Unbind()
    {
        SessionShop shop = _shop;
        _shop = null;
        _purchases = null;
        if (shop != null)
        {
            shop.OnOpened -= HandleOpened;
            shop.OnClosed -= HandleClosed;
        }
        try
        {
            if (shop != null) shop.Close();
        }
        finally
        {
            if (_view != null) _view.Close();
        }
    }
}
