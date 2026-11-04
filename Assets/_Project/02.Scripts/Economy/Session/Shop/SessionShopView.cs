using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 상품 선택과 보유 스킬 및 상세 정보 표시
/// </summary>
public sealed class SessionShopView : MonoBehaviour
{
    [Serializable]
    sealed class ProductRow
    {
        [SerializeField] GameObject _root;
        [SerializeField] Image _icon;
        [SerializeField] TMP_Text _nameText;
        [SerializeField] TMP_Text _priceText;
        [SerializeField] Button _selectButton;
        [SerializeField] CanvasGroup _contentGroup;
        [SerializeField] GameObject _soldOverlay;
        [SerializeField] GameObject _selectedDecoration;
        [SerializeField] Image _infoFrame;
        [SerializeField] Image _attackFrame;
        [SerializeField] TMP_Text _infoText;

        public GameObject Root => _root;
        public Image Icon => _icon;
        public TMP_Text NameText => _nameText;
        public TMP_Text PriceText => _priceText;
        public Button SelectButton => _selectButton;
        public CanvasGroup ContentGroup => _contentGroup;
        public GameObject SoldOverlay => _soldOverlay;
        public GameObject SelectedDecoration => _selectedDecoration;
        public Image InfoFrame => _infoFrame;
        public Image AttackFrame => _attackFrame;
        public TMP_Text InfoText => _infoText;
    }

    [Serializable]
    sealed class OwnedRow
    {
        [SerializeField] Button _selectButton;
        [SerializeField] Image _icon;
        [SerializeField] Image _frame;

        public Button SelectButton => _selectButton;
        public Image Icon => _icon;
        public Image Frame => _frame;
    }

    [Header("----- 상품과 보유 스킬 -----")]
    [SerializeField] GameObject _panelRoot;
    [SerializeField] TMP_Text _goldText;
    [SerializeField] Button _closeButton;
    [SerializeField] ProductRow[] _rows = Array.Empty<ProductRow>();
    [SerializeField] OwnedRow[] _ownedRows = Array.Empty<OwnedRow>();
    [SerializeField] Button _sellButton;
    [SerializeField] TMP_Text _sellLabel;

    [Header("----- 선택 표시 -----")]
    [SerializeField] Sprite _infoFrameActive;
    [SerializeField] Sprite _infoFrameInactive;
    [SerializeField] Sprite _attackFrameActive;
    [SerializeField] Sprite _attackFrameInactive;

    [Header("----- 선택 스킬 상세 -----")]
    [SerializeField] GameObject _detailRoot;
    [SerializeField] Image _detailIcon;
    [SerializeField] TMP_Text _detailName;
    [SerializeField] TMP_Text _detailDescription;
    [SerializeField] TMP_Text _detailPrice;
    [SerializeField] Button _purchaseButton;
    [SerializeField] TMP_Text _purchaseLabel;

    [Header("----- 구매 불가 안내 -----")]
    [SerializeField] GameObject _noticeRoot;
    [SerializeField] TMP_Text _noticeText;
    [SerializeField] Button _noticeCloseButton;

    SessionShop _shop;
    SessionShopPurchaseService _purchases;
    SkillLoadout _actor;
    UnityAction[] _productHandlers;
    UnityAction[] _ownedHandlers;
    int _selectedProduct = -1;
    int _selectedSlot = -1;
    SkillInstance _selectedOwnedSkill;
    bool _initialized;
    bool _isSubmitting;

    public bool IsOpen => _shop != null;

    void Awake()
    {
        ValidateReferences();
        _productHandlers = new UnityAction[_rows.Length];
        _ownedHandlers = new UnityAction[_ownedRows.Length];
        for (int i = 0; i < _rows.Length; i++)
        {
            int index = i;
            _productHandlers[i] = () => SelectProduct(index);
            _rows[i].SelectButton.onClick.AddListener(_productHandlers[i]);
        }
        for (int i = 0; i < _ownedRows.Length; i++)
        {
            int slot = i;
            _ownedHandlers[i] = () => SelectOwnedSkill(slot);
            _ownedRows[i].SelectButton.onClick.AddListener(_ownedHandlers[i]);
        }
        _closeButton.onClick.AddListener(HandleClose);
        _purchaseButton.onClick.AddListener(HandlePurchase);
        _sellButton.onClick.AddListener(HandleSale);
        _noticeCloseButton.onClick.AddListener(CloseNotice);
        _sellButton.interactable = false;
        _detailRoot.SetActive(false);
        _noticeRoot.SetActive(false);
        _panelRoot.SetActive(false);
        _initialized = true;
    }

    void OnDisable()
    {
        if (IsOpen) _shop.Close();
    }

    void OnDestroy()
    {
        if (!_initialized) return;
        Close();
        for (int i = 0; i < _rows.Length; i++)
            _rows[i].SelectButton.onClick.RemoveListener(_productHandlers[i]);
        for (int i = 0; i < _ownedRows.Length; i++)
            _ownedRows[i].SelectButton.onClick.RemoveListener(_ownedHandlers[i]);
        _closeButton.onClick.RemoveListener(HandleClose);
        _purchaseButton.onClick.RemoveListener(HandlePurchase);
        _sellButton.onClick.RemoveListener(HandleSale);
        _noticeCloseButton.onClick.RemoveListener(CloseNotice);
    }

    void ValidateReferences()
    {
        if (_panelRoot == null || _goldText == null || _closeButton == null ||
            _detailRoot == null || _detailIcon == null || _detailName == null ||
            _detailDescription == null || _detailPrice == null || _purchaseButton == null ||
            _sellButton == null || _sellLabel == null || _purchaseLabel == null ||
            _noticeRoot == null || _noticeText == null || _noticeCloseButton == null ||
            _infoFrameActive == null || _infoFrameInactive == null ||
            _attackFrameActive == null || _attackFrameInactive == null)
            throw new MissingReferenceException($"{name}: 상점 상품·보유 목록·상세창·안내창 연결 필요");
        if (_panelRoot == gameObject || !_panelRoot.transform.IsChildOf(transform) ||
            _detailRoot == _panelRoot || !_detailRoot.transform.IsChildOf(_panelRoot.transform) ||
            _noticeRoot == _panelRoot || !_noticeRoot.transform.IsChildOf(_panelRoot.transform))
            throw new InvalidOperationException($"{name}: View 하위 Panel과 별도 상세·안내 영역 구성 필요");
        if (_rows.Length != 3 || _ownedRows.Length == 0)
            throw new InvalidOperationException($"{name}: 상품 3개와 보유 슬롯 구성 필요");

        foreach (ProductRow row in _rows)
        {
            if (row == null || row.Root == null || row.Icon == null || row.NameText == null ||
                row.PriceText == null || row.SelectButton == null ||
                row.ContentGroup == null || row.SoldOverlay == null || row.SelectedDecoration == null ||
                row.InfoFrame == null || row.AttackFrame == null || row.InfoText == null)
                throw new MissingReferenceException($"{name}: 상품 카드 참조 연결 필요");
            if (!row.Root.transform.IsChildOf(_panelRoot.transform) ||
                row.SelectButton.gameObject != row.Root || row.ContentGroup.gameObject != row.Root ||
                row.SoldOverlay == row.Root || !row.SoldOverlay.transform.IsChildOf(row.Root.transform))
                throw new InvalidOperationException($"{name}: 카드 루트의 Button·CanvasGroup과 자식 디밍 구성 필요");
        }
        foreach (OwnedRow row in _ownedRows)
        {
            if (row == null || row.SelectButton == null || row.Icon == null || row.Frame == null)
                throw new MissingReferenceException($"{name}: 보유 슬롯 Button과 Icon 연결 필요");
        }
    }

    void SelectProduct(int index)
    {
        if (!IsOpen || _isSubmitting || _noticeRoot.activeSelf) return;
        _selectedProduct = index;
        _selectedSlot = -1;
        _selectedOwnedSkill = null;
        Refresh();
    }

    void SelectOwnedSkill(int slot)
    {
        if (!IsOpen || _isSubmitting || _noticeRoot.activeSelf) return;
        SkillInstance skill = _actor.GetSkill(slot);
        if (skill == null) return;
        _selectedProduct = -1;
        _selectedSlot = slot;
        _selectedOwnedSkill = skill;
        Refresh();
    }

    void HandleGoldChanged(float gold)
    {
        Refresh();
    }

    void Refresh()
    {
        if (!IsOpen) return;
        _goldText.text = $"{_purchases.Gold:0.##}";
        for (int i = 0; i < _rows.Length; i++)
        {
            ProductRow row = _rows[i];
            bool hasProduct = i < _purchases.Products.Count;
            row.Root.SetActive(hasProduct);
            if (!hasProduct) continue;

            SessionShopProduct product = _purchases.Products[i];
            bool sold = _purchases.IsSold(i);
            row.Icon.sprite = product.Skill.SkillIcon;
            row.Icon.enabled = product.Skill.SkillIcon != null;
            row.NameText.text = product.Skill.SkillName;
            row.PriceText.text = $"{product.Price:0.##}";
            bool selected = _selectedProduct == i;
            row.SelectedDecoration.SetActive(selected);
            row.InfoFrame.sprite = selected ? _infoFrameActive : _infoFrameInactive;
            row.InfoFrame.rectTransform.sizeDelta = selected ? new Vector2(140f, 30f) : new Vector2(130f, 26f);
            row.AttackFrame.sprite = selected ? _attackFrameActive : _attackFrameInactive;
            row.InfoText.text = $"쿨타임 {product.Skill.Cooldown:0.#}초";
            row.SoldOverlay.SetActive(sold);
            // 품절 카드도 상세 조회 허용, 구매는 서비스에서 거절
            row.ContentGroup.interactable = true;
            row.ContentGroup.blocksRaycasts = true;
            row.SelectButton.interactable = !_isSubmitting && !_noticeRoot.activeSelf;
        }

        for (int slot = 0; slot < _ownedRows.Length; slot++)
        {
            SkillInstance skill = _actor.GetSkill(slot);
            _ownedRows[slot].Icon.sprite = skill != null ? skill.Definition.SkillIcon : null;
            _ownedRows[slot].Icon.enabled = skill != null && skill.Definition.SkillIcon != null;
            _ownedRows[slot].Frame.color = skill != null && skill == _selectedOwnedSkill && _selectedSlot == slot ?
                Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
            _ownedRows[slot].SelectButton.interactable =
                skill != null && !_isSubmitting && !_noticeRoot.activeSelf;
        }
        RefreshDetails();
    }

    void RefreshDetails()
    {
        SkillDefinition definition = null;
        bool productSelected = _selectedProduct >= 0 && _selectedProduct < _purchases.Products.Count;
        if (productSelected)
        {
            SessionShopProduct product = _purchases.Products[_selectedProduct];
            definition = product.Skill;
            _detailPrice.text = $"{product.Price:0.##}";
        }
        else if (_selectedSlot >= 0 && _actor.GetSkill(_selectedSlot) == _selectedOwnedSkill &&
                 _selectedOwnedSkill != null)
        {
            definition = _selectedOwnedSkill.Definition;
            _detailPrice.text = _purchases.TryGetSalePrice(_selectedSlot, _selectedOwnedSkill, out float price) ?
                $"판매 가격 {price:0.##}" : "판매 불가";
        }
        else
        {
            _selectedSlot = -1;
            _selectedOwnedSkill = null;
        }

        _sellButton.interactable = !productSelected && !_isSubmitting && !_noticeRoot.activeSelf &&
            _purchases.TryGetSalePrice(_selectedSlot, _selectedOwnedSkill, out _);
        _detailRoot.SetActive(definition != null);
        _purchaseButton.gameObject.SetActive(true);
        _purchaseButton.interactable = productSelected && !_isSubmitting &&
            !_noticeRoot.activeSelf && _purchases.CanRequestPurchase(_selectedProduct) &&
            _purchases.Gold >= _purchases.Products[_selectedProduct].Price &&
            _actor.TryGetAcquisitionSlot(definition, out _);
        _purchaseLabel.color = _purchaseButton.interactable ? Color.white : Color.gray;
        _sellLabel.color = _sellButton.interactable ? Color.white : Color.gray;
        if (definition == null) return;
        _detailIcon.sprite = definition.SkillIcon;
        _detailIcon.enabled = definition.SkillIcon != null;
        _detailName.text = definition.SkillName;
        _detailDescription.text = definition.SkillDescription;
    }

    void HandlePurchase()
    {
        if (!IsOpen || _isSubmitting || _selectedProduct < 0 || _noticeRoot.activeSelf) return;
        _isSubmitting = true;
        try
        {
            ShopPurchaseResult result = _purchases.RequestPurchase(_selectedProduct);
            if (result == ShopPurchaseResult.AlreadyOwned)
                ShowNotice("이미 보유한 스킬입니다.");
            else if (result == ShopPurchaseResult.SlotsFull)
                ShowNotice("스킬 슬롯이 가득 차 구매할 수 없습니다.");
            else if (result == ShopPurchaseResult.InsufficientGold)
                ShowNotice("골드가 부족합니다.");
        }
        finally
        {
            _isSubmitting = false;
            Refresh();
        }
    }

    void HandleSale()
    {
        if (!IsOpen || _isSubmitting || _selectedOwnedSkill == null || _noticeRoot.activeSelf) return;
        _isSubmitting = true;
        try
        {
            if (_purchases.RequestSale(_selectedSlot, _selectedOwnedSkill) != ShopSaleResult.Sold)
                ShowNotice("현재 선택한 스킬을 판매할 수 없습니다.");
        }
        finally
        {
            _isSubmitting = false;
            Refresh();
        }
    }

    void ShowNotice(string message)
    {
        _noticeText.text = message;
        _noticeRoot.SetActive(true);
    }

    void CloseNotice()
    {
        _noticeRoot.SetActive(false);
        Refresh();
    }

    void HandleClose()
    {
        if (IsOpen) _shop.Close();
    }

    /// <summary>
    /// 거래 처리 중 입력 소비 또는 구매 불가 안내창 닫기
    /// 상품과 보유 스킬의 상세 표시 및 선택 상태 유지
    /// </summary>
    /// <returns>내부 화면이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        if (!IsOpen) return false;
        if (_isSubmitting) return true;
        if (!_noticeRoot.activeSelf) return false;

        CloseNotice();
        return true;
    }

    /// <summary>
    /// 상점 상품과 현재 보유 스킬을 연결한 화면 열기
    /// </summary>
    /// <param name="shop">접근과 종료 담당 상점</param>
    /// <param name="purchases">상품과 거래 담당 서비스</param>
    public void Open(SessionShop shop, SessionShopPurchaseService purchases)
    {
        if (shop == null) throw new ArgumentNullException(nameof(shop));
        if (purchases == null) throw new ArgumentNullException(nameof(purchases));
        if (purchases.Products == null || purchases.Actor == null || purchases.Products.Count > _rows.Length)
            throw new InvalidOperationException("상품과 플레이어 준비 및 표시 행 수 확인 필요");
        if (purchases.Actor.SlotCount != _ownedRows.Length)
            throw new InvalidOperationException("보유 목록과 실제 스킬 슬롯 수 일치 필요");
        if (IsOpen) _shop.Close();
        _shop = shop;
        _purchases = purchases;
        _actor = purchases.Actor;
        _selectedProduct = -1;
        _selectedSlot = -1;
        _selectedOwnedSkill = null;
        _isSubmitting = false;
        _purchases.OnChanged += Refresh;
        _purchases.OnGoldChanged += HandleGoldChanged;
        _actor.OnChanged += Refresh;
        _noticeRoot.SetActive(false);
        _panelRoot.SetActive(true);
        Refresh();
    }

    /// <summary>
    /// 상점 표시와 선택 상태 및 이벤트 구독 해제
    /// </summary>
    public void Close()
    {
        if (_purchases != null)
        {
            _purchases.OnChanged -= Refresh;
            _purchases.OnGoldChanged -= HandleGoldChanged;
        }
        if (_actor != null) _actor.OnChanged -= Refresh;
        _shop = null;
        _purchases = null;
        _actor = null;
        _selectedProduct = -1;
        _selectedSlot = -1;
        _selectedOwnedSkill = null;
        _isSubmitting = false;
        _noticeRoot.SetActive(false);
        _detailRoot.SetActive(false);
        _panelRoot.SetActive(false);
    }
}
