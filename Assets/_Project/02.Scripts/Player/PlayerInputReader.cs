using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlayerInputReader : MonoBehaviour
{
    [Header("----- 연결 -----")]
    [SerializeField] PlayerController _controller;

    [Header("----- 입력 설정 -----")]
    [SerializeField] LayerMask _worldMask;
    [SerializeField] LayerMask _skillAimMask;
    [SerializeField, Min(1f)] float _rayDistance = 500f;

    [Header("----- 월드 상호작용 입력 -----")]
    [SerializeField] LayerMask _interactionMask = ~0;
    [SerializeField, Min(0.1f)] float _interactionRange = 3f;

    readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

    Collider[] _interactionHits = new Collider[32];
    Camera _camera;
    EventSystem _eventSystem;
    PointerEventData _pointerData;

    InputAction _pointAction;
    InputAction _moveAction;
    InputAction _attackAction;
    InputAction _attackModeAction;
    InputAction _movementSkillAction;
    InputAction _interactAction;

    readonly InputAction[] _skillActions = new InputAction[4];

    public PlayerController Controller => _controller;
    // 씬 입력 참조와 포인터 데이터의 준비 완료 여부
    public bool HasSceneBindings => _camera != null && _eventSystem != null && _pointerData != null;
    public bool AttackModeHeld { get; private set; }


    void OnEnable()
    {
        try
        {
            if (_controller == null)
                throw new MissingReferenceException($"{name}: PlayerController 연결 필요");

            if (!HasSceneBindings)
                throw new System.InvalidOperationException($"{name}: 활성화 전에 PlayerActor의 BindScene 호출 필요");
        }
        catch
        {
            enabled = false;
            throw;
        }
    }

    void OnDisable()
    {
        AttackModeHeld = false;
    }

    void Update()
    {
        AttackModeHeld = false;

        if (!_controller.CanAcceptCommands)
            return;

        bool hasPointer = _pointAction.enabled && _pointAction.controls.Count > 0;

        Vector2 screenPosition = hasPointer? _pointAction.ReadValue<Vector2>() : Vector2.zero;

        AttackModeHeld = _attackModeAction.IsPressed();

        if (_movementSkillAction.WasPressedThisFrame())
        {
            if (!hasPointer || IsPointerOverUI(screenPosition, PointerEventData.InputButton.Left))
                return;

            ReadSkillAim(screenPosition, hasPointer, out Vector3 movementDirection, out _, out bool hasMovementAim);

            if (hasMovementAim)
                _controller.RequestMovementSkill(movementDirection);

            return;
        }

        int skillSlot = ReadSkillSlot();

        if (skillSlot >= 0)
        {
            ReadSkillAim(screenPosition, hasPointer, out Vector3 direction, out Vector3 worldPosition, out bool hasWorldPosition);

            _controller.RequestUseSkill(skillSlot, direction, worldPosition, hasWorldPosition);

            return;
        }

        if (_interactAction.WasPressedThisFrame())
        {
            IInteractable interactionTarget = FindNearestInteractionTarget();
            _controller.RequestInteract(interactionTarget);
            return;
        }

        if (!hasPointer)
            return;

        bool attackClicked = _attackAction.WasPressedThisFrame();
        bool moveClicked = _moveAction.WasPressedThisFrame();

        if (!attackClicked && !moveClicked)
            return;

        // 양쪽 버튼을 동시에 누르면 일반 이동 우선
        PointerEventData.InputButton button = moveClicked ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left;

        if (IsPointerOverUI(screenPosition, button))
            return;

        if (!TryReadPointer(screenPosition, out Vector3 position, out CombatTarget target))
            return;

        if (moveClicked)
        {
            _controller.RequestMove(position);
        }
        else
        {
            _controller.RequestAttack(position, target);
        }
    }

    void BindInputActions()
    {
        InputActionAsset actions = InputSystem.actions;

        if (actions == null)
            throw new MissingReferenceException($"{name}: 프로젝트 공용 Input Actions 설정 필요");

        InputActionMap player = actions.FindActionMap("Player", true);

        _pointAction = player.FindAction("Point", true);
        _moveAction = player.FindAction("MoveClick", true);
        _attackAction = player.FindAction("Attack", true);
        _attackModeAction = player.FindAction("AttackMode", true);
        _movementSkillAction = player.FindAction("MovementSkill", true);
        _interactAction = player.FindAction("Interact", true);

        for (int slot = 0; slot < _skillActions.Length; slot++)
        {
            _skillActions[slot] = player.FindAction($"Skill{slot + 1}", true);
        }
    }

    int ReadSkillSlot()
    {
        for (int slot = 0; slot < _skillActions.Length; slot++)
        {
            if (_skillActions[slot].WasPressedThisFrame())
                return slot;
        }

        return -1;
    }

    void ReadSkillAim(Vector2 screenPosition, bool hasPointer, out Vector3 direction, out Vector3 worldPosition, out bool hasWorldPosition)
    {
        direction = _controller.Forward;
        worldPosition = default;
        hasWorldPosition = false;

        if (!hasPointer)
            return;

        Vector3 origin = _controller.Position;
        Ray ray = _camera.ScreenPointToRay(screenPosition);

        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _skillAimMask, QueryTriggerInteraction.Ignore))
        {
            worldPosition = hit.point;
        }
        else
        {
            // 기존 방향 조준의 수평면 보정 유지
            Plane aimPlane = new Plane(Vector3.up, origin);

            if (!aimPlane.Raycast(ray, out float distance))
                return;

            worldPosition = ray.GetPoint(distance);
        }

        direction = worldPosition - origin;
        hasWorldPosition = true;
    }

    bool TryReadPointer(Vector2 screenPosition, out Vector3 position, out CombatTarget target)
    {
        position = default;
        target = null;

        Ray ray = _camera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _worldMask, QueryTriggerInteraction.Collide))
            return false;

        target = hit.collider.GetComponentInParent<CombatTarget>();

        // 적의 몸을 클릭해도 이동 요청에는 바닥 기준 위치를 사용한다.
        position = target != null ? target.Position : hit.point;

        return true;
    }

    bool IsPointerOverUI(Vector2 screenPosition, PointerEventData.InputButton button)
    {
        _pointerData.Reset();
        _pointerData.position = screenPosition;
        _pointerData.button = button;

        _uiHits.Clear();
        _eventSystem.RaycastAll(_pointerData, _uiHits);

        foreach (RaycastResult hit in _uiHits)
        {
            if (hit.module is GraphicRaycaster)
                return true;
        }

        return false;
    }

    IInteractable FindNearestInteractionTarget()
    {
        Vector3 origin = _controller.Position;

        int hitCount = Physics.OverlapSphereNonAlloc(origin, _interactionRange, _interactionHits, _interactionMask, QueryTriggerInteraction.Collide);

        // 탐색 결과가 잘리지 않도록 필요한 경우 버퍼 확장
        while (hitCount == _interactionHits.Length)
        {
            System.Array.Resize(ref _interactionHits, _interactionHits.Length * 2);

            hitCount = Physics.OverlapSphereNonAlloc(origin, _interactionRange, _interactionHits, _interactionMask, QueryTriggerInteraction.Collide);
        }

        IInteractable nearest = null;
        float nearestDistanceSquared = _interactionRange * _interactionRange;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _interactionHits[i];

            IInteractable candidate = hit.GetComponentInParent<IInteractable>();

            if (!(candidate is Component component) || component == null)
                continue;

            float distanceSquared = (component.transform.position - origin).sqrMagnitude;

            if (distanceSquared > nearestDistanceSquared)
                continue;

            if (!_controller.CanInteract(candidate))
                continue;

            nearest = candidate;
            nearestDistanceSquared = distanceSquared;
        }

        return nearest;
    }

    /// <summary>
    /// 입력이 중단된 상태에서 씬 카메라와 UI 입력 판정 대상 연결
    /// </summary>
    /// <param name="inputCamera">월드 입력 좌표를 계산할 카메라</param>
    /// <param name="eventSystem">UI 입력 소비 여부를 판정할 EventSystem</param>
    public void BindScene(Camera inputCamera, EventSystem eventSystem)
    {
        if (isActiveAndEnabled)
            throw new System.InvalidOperationException($"{name}: 입력 컴포넌트 또는 플레이어 루트를 비활성화한 뒤 연결 필요");

        if (_controller == null)
            throw new MissingReferenceException($"{name}: PlayerController 연결 필요");

        if (inputCamera == null)
            throw new System.ArgumentNullException(nameof(inputCamera));

        if (eventSystem == null)
            throw new System.ArgumentNullException(nameof(eventSystem));

        // 활성화 전 준비한 같은 연결은 그대로 유지
        if (HasSceneBindings && _camera == inputCamera && _eventSystem == eventSystem)
            return;
        
        BindInputActions();

        PointerEventData pointerData = new PointerEventData(eventSystem);

        _camera = inputCamera;
        _eventSystem = eventSystem;
        _pointerData = pointerData;

        _uiHits.Clear();
        AttackModeHeld = false;
    }

    /// <summary>
    /// 입력이 중단된 상태에서 주입받은 씬 참조와 포인터 데이터 해제
    /// </summary>
    public void UnbindScene()
    {
        if (isActiveAndEnabled)
            throw new System.InvalidOperationException($"{name}: 입력 비활성화 후 씬 연결 해제 필요");

        _camera = null;
        _eventSystem = null;
        _pointerData = null;

        _uiHits.Clear();
        AttackModeHeld = false;
    }
}