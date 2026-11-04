using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>전달받은 스킬 슬롯 정보의 화면 표시</summary>
[DisallowMultipleComponent]
public sealed class SkillSlotView : MonoBehaviour
{
    [Header("----- 장착 정보 표시 -----")]
    [SerializeField] Image _icon;
    [SerializeField] TextMeshProUGUI _hotkeyText;

    [Header("----- 쿨타임 표시 -----")]
    [SerializeField] TextMeshProUGUI _cooldownText;
    [SerializeField] Image _cooldownFill;

    [Header("----- 선택 기능: 스킬 강화 표시 -----")]
    [SerializeField] Button _upgradeButton;
    [SerializeField] Image _upgradeCurrencyIcon;
    [SerializeField] TextMeshProUGUI _upgradeCostText;
    [SerializeField] Color _upgradeReadyColor = Color.black;
    [SerializeField] Color _upgradeBlockedColor = new Color(0.7f, 0.15f, 0.15f, 1f);

    SkillIconHover _hoverTarget;
    SkillSlotDragInput _dragInput;
    bool _bindingsInitialized;
    bool _upgradeBindingsInitialized;
    bool _upgradeMode;
    bool _dragEnabledBeforeUpgrade;

    public event Action<SkillSlotView> OnUpgradeRequested;

    public SkillIconHover HoverTarget => _hoverTarget;
    public SkillSlotDragInput DragInput => _dragInput;

    void Awake()
    {
        InitializeBindings();
    }
    void OnDisable()
    {
        if (_upgradeBindingsInitialized)
            SetUpgradeMode(false);
    }

    void OnDestroy()
    {
        if (_upgradeBindingsInitialized && _upgradeButton != null)
            _upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
    }

    void HandleUpgradeClicked()
    {
        if (_upgradeMode)
            OnUpgradeRequested?.Invoke(this);
    }

    /// <summary>
    /// 슬롯 루트와 아이콘의 필수 입력 참조 연결
    /// </summary>
    public void InitializeBindings()
    {
        if (_bindingsInitialized)
            return;

        if (_icon == null || _hotkeyText == null || _cooldownText == null || _cooldownFill == null)
            throw new MissingReferenceException($"{name}: 슬롯 UI 참조 연결 필요");

        if (!_icon.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 현재 슬롯 내부의 아이콘 연결 필요");

        if (_cooldownFill.type != UnityEngine.UI.Image.Type.Filled || _cooldownFill.sprite == null)
            throw new InvalidOperationException($"{name}: CooldownImage의 Source Image와 Filled 설정 필요");

        SkillIconHover hoverTarget = _icon.GetComponent<SkillIconHover>();

        SkillSlotDragInput dragInput = GetComponent<SkillSlotDragInput>();

        if (hoverTarget == null)
            throw new MissingReferenceException($"{name}: 아이콘과 같은 GameObject에 SkillIconHover 필요");

        if (dragInput == null)
            throw new MissingReferenceException($"{name}: 슬롯 루트에 SkillSlotDragInput 필요");

        _hoverTarget = hoverTarget;
        _dragInput = dragInput;
        _bindingsInitialized = true;
    }

    /// <summary>
    /// 강화 기능을 사용하는 슬롯의 추가 UI 참조 준비
    /// </summary>
    public void InitializeUpgradeBindings()
    {
        if (_upgradeBindingsInitialized)
            return;

        InitializeBindings();

        if (_upgradeButton == null || _upgradeCurrencyIcon == null || _upgradeCostText == null || _upgradeCurrencyIcon.sprite == null)
            throw new MissingReferenceException($"{name}: 강화 버튼, 재화 아이콘과 Sprite, 비용 텍스트 연결 필요");

        if (_upgradeButton.gameObject == gameObject || !_upgradeButton.transform.IsChildOf(transform) || !_upgradeCurrencyIcon.transform.IsChildOf(_upgradeButton.transform) || !_upgradeCostText.transform.IsChildOf(_upgradeButton.transform))
            throw new InvalidOperationException($"{name}: 슬롯 하위 강화 버튼과 그 안의 아이콘·텍스트 배치 필요");

        _upgradeButton.onClick.AddListener(HandleUpgradeClicked);
        _upgradeButton.gameObject.SetActive(false);
        _upgradeBindingsInitialized = true;
    }

    /// <summary>
    /// 강화 모드에 따른 슬롯 드래그와 오버레이 제어
    /// </summary>
    /// <param name="enabled">강화 모드 여부</param>
    public void SetUpgradeMode(bool enabled)
    {
        if (!_upgradeBindingsInitialized)
            throw new InvalidOperationException($"{name}: 강화 UI 초기화 이후 모드 변경 필요");

        if (_upgradeMode == enabled)
            return;

        _upgradeMode = enabled;

        if (enabled)
        {
            _dragEnabledBeforeUpgrade = _dragInput.enabled;
            _dragInput.Cancel();
            _dragInput.enabled = false;
        }
        else
        {
            // 씬 종료 중 먼저 제거된 입력 컴포넌트 제외
            if (_dragInput != null)
                _dragInput.enabled = _dragEnabledBeforeUpgrade;

            if (_upgradeButton != null)
                _upgradeButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 조회된 강화 정보로 레벨과 비용 및 입력 상태 표시
    /// </summary>
    /// <param name="quote">현재 슬롯의 강화 정보</param>
    public void ShowUpgrade(SkillUpgradeQuote quote)
    {
        if (!_upgradeMode)
            return;

        bool occupied = quote.Skill != null;
        _upgradeButton.gameObject.SetActive(occupied);

        if (!occupied)
            return;

        bool ready = quote.Status == SkillUpgradeStatus.Ready;
        bool maximum = quote.Status == SkillUpgradeStatus.MaxLevel;

        _upgradeButton.interactable = ready;
        _upgradeCurrencyIcon.gameObject.SetActive(!maximum);

        _upgradeCostText.text = maximum ? $"Lv.{quote.Level}\nMAX" : $"{quote.Cost}";
        _upgradeCostText.color = ready ? _upgradeReadyColor : _upgradeBlockedColor;
    }

    /// <summary>
    /// 슬롯 단축키 표시
    /// </summary>
    /// <param name="label">표시할 단축키 문자열</param>
    public void SetHotkey(string label)
    {
        _hotkeyText.text = label;
    }

    /// <summary>
    /// 장착 스킬 아이콘 표시
    /// </summary>
    /// <param name="icon">표시할 아이콘, 미지정 시 숨김</param>
    public void ShowIcon(Sprite icon)
    {
        _icon.sprite = icon;
        _icon.enabled = icon != null;
    }

    /// <summary>
    /// 남은 쿨타임의 숫자와 Fill 표시
    /// </summary>
    /// <param name="remainingSeconds">남은 시간, 초 단위</param>
    /// <param name="remainingRatio">남은 비율, 0부터 1까지</param>
    public void ShowCooldown(float remainingSeconds, float remainingRatio)
    {
        bool coolingDown = remainingSeconds > 0f;

        _cooldownFill.fillAmount = coolingDown ? Mathf.Clamp01(remainingRatio) : 0f;

        _cooldownFill.enabled = coolingDown;
        _cooldownText.enabled = coolingDown;

        if (!coolingDown)
        {
            _cooldownText.text = string.Empty;
            return;
        }

        // 준비 완료 전에 0.0으로 표시되지 않도록 소수 첫째 자리 올림
        float displaySeconds = Mathf.Ceil(remainingSeconds * 10f) / 10f;
        _cooldownText.SetText("{0:0.0}", displaySeconds);
    }

    /// <summary>
    /// 단축키를 유지한 채 빈 슬롯 표시
    /// </summary>
    public void ShowEmpty()
    {
        ShowIcon(null);
        ShowCooldown(0f, 0f);
    }
}