using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 등록된 스킬의 목록 표시와 QWER 드래그 장착 테스트
/// </summary>
[DisallowMultipleComponent]
public class RuntimeSkillTestPanel : MonoBehaviour
{
    [Header("----- 플레이어 연결 -----")]
    [SerializeField] SkillLoadout _loadout;
    [SerializeField] SkillBarView _skillBar;

    [Header("----- 드롭 대상: Q W E R 순서 -----")]
    [SerializeField] SkillSlotView[] _dropSlots = new SkillSlotView[4];

    [Header("----- 목록 UI -----")]
    [SerializeField] Canvas _canvas;
    [SerializeField] RectTransform _listRoot;
    [SerializeField] SkillSlotView _slotPrefab;

    [Header("----- 테스트할 스킬 -----")]
    [SerializeField] List<SkillDefinition> _testSkills = new List<SkillDefinition>();

    [Header("----- 선택: 결과 표시 -----")]
    [SerializeField] TMP_Text _messageText;

    readonly List<SkillSlotView> _createdSlots = new List<SkillSlotView>();

    readonly Dictionary<SkillSlotDragInput, SkillDefinition> _catalog = new Dictionary<SkillSlotDragInput, SkillDefinition>();

    SkillSlotDragInput _dragSource;
    SkillDefinition _draggedSkill;
    SkillSlotView _dragView;

    void OnEnable()
    {
        ValidateBindings();

        try
        {
            CreateSlots();
        }
        catch
        {
            ClearSlots();
            throw;
        }
    }

    void OnDisable()
    {
        ClearSlots();
    }

    void OnDestroy()
    {
        ClearSlots();
    }

    void ValidateBindings()
    {
        
    }

    void CreateSlots()
    {
        for (int i = 0; i < _testSkills.Count; i++)
        {
            SkillDefinition definition = _testSkills[i];
            SkillSlotView slot = Instantiate(_slotPrefab, _listRoot);

            _createdSlots.Add(slot);

            slot.name = $"TestSkill_{definition.name}";
            slot.InitializeBindings();
            slot.SetHotkey((i + 1).ToString());
            slot.ShowIcon(definition.SkillIcon);
            slot.ShowCooldown(0f, 0f);
            slot.gameObject.SetActive(true);

            SkillSlotDragInput input = slot.DragInput;
            input.enabled = true;

            _catalog.Add(input, definition);

            input.OnDragStarted += HandleDragStarted;
            input.OnDragged += HandleDragged;
            input.OnDragReleased += HandleDragReleased;
            input.OnDragCanceled += HandleDragCanceled;
        }
    }

    void HandleDragStarted(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (_dragSource != null)
        {
            source.Cancel();
            return;
        }

        _dragSource = source;
        _draggedSkill = _catalog[source];

        // 원본 목록을 유지하는 별도 드래그 표시
        _dragView = Instantiate(_slotPrefab, _canvas.transform);
        _dragView.name = "TestSkillDragIcon";
        _dragView.InitializeBindings();
        _dragView.SetHotkey(string.Empty);
        _dragView.ShowIcon(_draggedSkill.SkillIcon);
        _dragView.ShowCooldown(0f, 0f);
        _dragView.DragInput.enabled = false;

        CanvasGroup group = _dragView.GetComponent<CanvasGroup>();

        if (group == null)
            group = _dragView.gameObject.AddComponent<CanvasGroup>();

        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0.8f;

        _dragView.gameObject.SetActive(true);
        _dragView.transform.SetAsLastSibling();

        MoveDragView(eventData.position);
        ShowMessage($"{_draggedSkill.name}: QWER 슬롯에 놓으세요");
    }

    void HandleDragged(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (source != _dragSource)
            return;

        MoveDragView(eventData.position);
    }

    void HandleDragReleased(SkillSlotDragInput source, PointerEventData eventData)
    {
        if (source != _dragSource)
            return;

        SkillDefinition definition = _draggedSkill;
        GameObject hitObject = eventData.pointerCurrentRaycast.gameObject;

        int destination = -1;

        if (hitObject != null)
        {
            Transform hitTransform = hitObject.transform;

            for (int i = 0; i < _dropSlots.Length; i++)
            {
                Transform slotTransform = _dropSlots[i].transform;

                if (hitTransform == slotTransform || hitTransform.IsChildOf(slotTransform))
                {
                    destination = i;
                    break;
                }
            }
        }

        // 장착 변경 알림 이전의 드래그 상태 정리
        ClearDrag();

        if (destination < 0 || destination >= _loadout.SlotCount)
        {
            ShowMessage("QWER 슬롯 아이콘 위에 놓으세요");
            return;
        }

        EquipSkill(definition, destination);
    }

    void HandleDragCanceled(SkillSlotDragInput source)
    {
        if (source == _dragSource)
            ClearDrag();
    }

    void MoveDragView(Vector2 screenPosition)
    {
        if (_dragView == null)
            return;

        RectTransform canvasRect = (RectTransform)_canvas.transform;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPosition))
            _dragView.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
    }

    bool ValidateSkill(SkillDefinition definition)
    {
        try
        {
            definition.ValidateSkillUID();
            SkillExecutor.ValidateDefinition(definition);

            if (_loadout.HasSkill(definition))
            {
                ShowMessage("이미 장착된 스킬입니다");
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is MissingReferenceException)
        {
            // 설정 오류에 대한 장착 거절과 사용자 피드백
            ShowMessage(exception.Message);
            return false;
        }
    }

    void EquipSkill(SkillDefinition definition, int destination)
    {
        if (!ValidateSkill(definition))
            return;

        bool canAcquire = _loadout.TryGetAcquisitionSlot(definition, out _);

        bool hasEmptySlot = false;

        for (int i = 0; i < _loadout.SlotCount; i++)
        {
            if (_loadout.GetSkill(i) == null)
            {
                hasEmptySlot = true;
                break;
            }
        }

        // 빈 슬롯이 있는데도 거절된 경우는 장착 조건 불충족
        if (!canAcquire && hasEmptySlot)
        {
            ShowMessage("현재 상태 또는 스킬의 장착 조건을 확인하세요");
            return;
        }

        SkillInstance previous = _loadout.GetSkill(destination);

        if (previous != null && !_loadout.TryRemoveSkill(destination, previous))
        {
            ShowMessage("현재 상태에서는 스킬을 교체할 수 없습니다");
            return;
        }

        // 기존 획득 API 내부의 장착 조건 재검사
        if (!_loadout.TryAcquireSkill(definition, out int acquiredSlot))
        {
            ShowMessage("스킬 장착이 거절되었습니다. " + RestorePrevious(destination, previous));
            return;
        }

        SkillInstance acquired = _loadout.GetSkill(acquiredSlot);

        // 첫 빈 슬롯에 들어간 스킬을 실제 드롭 위치로 이동
        if (acquiredSlot != destination && !_loadout.TrySwapSlots( acquiredSlot, destination, acquired))
        {
            bool removed = _loadout.TryRemoveSkill(acquiredSlot, acquired);

            if (!removed)
            {
                ShowMessage("슬롯 이동에 실패했습니다. 현재 장착 상태를 확인하세요");
                return;
            }

            ShowMessage("슬롯 이동에 실패했습니다. " + RestorePrevious(destination, previous));
            return;
        }

        ShowMessage($"{destination + 1}번 슬롯에 {definition.name} 장착");
    }

    string RestorePrevious(int destination, SkillInstance previous)
    {
        if (previous == null)
            return string.Empty;

        if (_loadout.GetSkill(destination) != null)
            return "이전 스킬 복구 불가: 대상 슬롯 확인 필요";

        if (!_loadout.TryAcquireSkill(previous.Definition, out int restoredSlot))
            return "이전 스킬 재장착 실패";

        SkillInstance restored = _loadout.GetSkill(restoredSlot);

        if (restoredSlot != destination && !_loadout.TrySwapSlots( restoredSlot, destination, restored))
            return "이전 스킬의 복구 위치 확인 필요";

        return "이전 스킬 재장착 완료: 레벨·쿨타임 초기화";
    }

    void ClearDrag()
    {
        _dragSource = null;
        _draggedSkill = null;

        if (_dragView != null)
        {
            _dragView.gameObject.SetActive(false);
            Destroy(_dragView.gameObject);
            _dragView = null;
        }
    }

    void ClearSlots()
    {
        ClearDrag();

        foreach (SkillSlotView slot in _createdSlots)
        {
            if (slot == null)
                continue;

            SkillSlotDragInput input = slot.DragInput;

            if (input != null)
            {
                input.OnDragStarted -= HandleDragStarted;
                input.OnDragged -= HandleDragged;
                input.OnDragReleased -= HandleDragReleased;
                input.OnDragCanceled -= HandleDragCanceled;
                input.Cancel();
            }

            slot.gameObject.SetActive(false);
            Destroy(slot.gameObject);
        }

        _createdSlots.Clear();
        _catalog.Clear();
    }

    void ShowMessage(string message)
    {
        if (_messageText != null)
            _messageText.text = message;
        else
            Debug.Log($"[{name}] {message}", this);
    }
}