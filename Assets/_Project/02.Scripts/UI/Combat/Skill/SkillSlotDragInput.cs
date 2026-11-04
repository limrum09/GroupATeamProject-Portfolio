using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>슬롯의 왼쪽 버튼 드래그 입력 전달</summary>
[DisallowMultipleComponent]
public sealed class SkillSlotDragInput :MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action<SkillSlotDragInput, PointerEventData> OnDragStarted;
    public event Action<SkillSlotDragInput, PointerEventData> OnDragged;
    public event Action<SkillSlotDragInput, PointerEventData> OnDragReleased;
    public event Action<SkillSlotDragInput> OnDragCanceled;

    bool _dragging;
    int _pointerId;

    bool IsCurrentPointer(PointerEventData eventData)
    {
        return _dragging && eventData.pointerId == _pointerId && eventData.button == PointerEventData.InputButton.Left;
    }

    void OnDisable()
    {
        Cancel();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            Cancel();
    }

    /// <summary>드래그 시작 입력 전달</summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_dragging || eventData.button != PointerEventData.InputButton.Left)
            return;

        _dragging = true;
        _pointerId = eventData.pointerId;
        OnDragStarted?.Invoke(this, eventData);
    }

    /// <summary>드래그 이동 입력 전달</summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnDrag(PointerEventData eventData)
    {
        if (!IsCurrentPointer(eventData))
            return;

        OnDragged?.Invoke(this, eventData);
    }

    /// <summary>드래그 버튼 해제 입력 전달</summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!IsCurrentPointer(eventData))
            return;

        _dragging = false;
        OnDragReleased?.Invoke(this, eventData);
    }

    /// <summary>진행 중인 드래그 입력 취소</summary>
    public void Cancel()
    {
        if (!_dragging)
            return;

        _dragging = false;
        OnDragCanceled?.Invoke(this);
    }
}