using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>아이콘 영역의 포인터 Hover 상태 전달</summary>
[DisallowMultipleComponent]
public sealed class SkillIconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action<SkillIconHover, bool> OnHoverChanged;

    public bool IsHovered { get; private set; }

    void OnDisable()
    {
        SetHovered(false);
    }

    void SetHovered(bool hovered)
    {
        if (IsHovered == hovered)
            return;

        IsHovered = hovered;
        OnHoverChanged?.Invoke(this, hovered);
    }

    /// <summary>포인터 진입 처리</summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        SetHovered(true);
    }

    /// <summary>포인터 이탈 처리</summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        SetHovered(false);
    }
}