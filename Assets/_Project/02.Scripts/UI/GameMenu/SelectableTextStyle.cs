using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 선택 가능한 UI의 상태별 텍스트 색상 표시
/// </summary>
[DisallowMultipleComponent]
public class SelectableTextStyle : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("----- UI 참조 -----")]
    [SerializeField] Selectable _selectable;
    [SerializeField] TMP_Text _label;

    [Header("----- 텍스트 색상 -----")]
    [SerializeField] Color _normalColor = new Color32(145, 145, 145, 255);
    [SerializeField] Color _activeColor = new Color32(195, 195, 195, 255);

    bool _pointerInside;
    bool _selected;

    void Awake()
    {
        if (_selectable == null || _label == null)
            throw new MissingReferenceException($"{name}: 선택 UI와 텍스트 참조 연결 필요");
    }

    void OnEnable()
    {
        _pointerInside = false;
        _selected = false;
        RefreshColor();
    }

    void LateUpdate()
    {
        // 드롭다운 펼침과 목록 선택, 상위 CanvasGroup 상태 반영
        RefreshColor();
    }

    void RefreshColor()
    {
        bool active = _pointerInside || _selected;

        if (_selectable is TMP_Dropdown dropdown)
            active |= dropdown.IsExpanded;

        if (_selectable is Toggle toggle)
            active |= toggle.isOn;

        Color color = _selectable.IsInteractable() && active
            ? _activeColor
            : _normalColor;

        if (_label.color != color)
            _label.color = color;
    }

    /// <summary>
    /// 포인터 진입 상태 반영
    /// </summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
        RefreshColor();
    }

    /// <summary>
    /// 포인터 이탈 상태 반영
    /// </summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
        RefreshColor();
    }

    /// <summary>
    /// 내비게이션 선택 상태 반영
    /// </summary>
    /// <param name="eventData">선택 이벤트 정보</param>
    public void OnSelect(BaseEventData eventData)
    {
        _selected = true;
        RefreshColor();
    }

    /// <summary>
    /// 내비게이션 선택 해제 상태 반영
    /// </summary>
    /// <param name="eventData">선택 이벤트 정보</param>
    public void OnDeselect(BaseEventData eventData)
    {
        _selected = false;
        RefreshColor();
    }
}
