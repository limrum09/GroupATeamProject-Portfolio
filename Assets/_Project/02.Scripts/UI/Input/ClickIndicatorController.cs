using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 접수된 지면 클릭의 UI 투영과 표시 풀 관리
/// </summary>
[DisallowMultipleComponent]
public class ClickIndicatorController : MonoBehaviour
{
    [Header("----- 씬 연결 -----")]
    [SerializeField] PlayerInputReader _input;
    [SerializeField] Camera _worldCamera;
    [SerializeField] Canvas _canvas;
    [SerializeField] RectTransform _root;

    [Header("----- 표시 설정 -----")]
    [SerializeField] ClickIndicatorView _viewPrefab;
    [SerializeField] Color _moveColor = new Color(0.5f, 1f, 0.2f);
    [SerializeField] Color _attackMoveColor = new Color(1f, 0.25f, 0.2f);

    readonly Stack<ClickIndicatorView> _available = new Stack<ClickIndicatorView>();

    ClickIndicatorView _active;
    Vector3 _worldPosition;
    bool _subscribed;

    public event Action OnGroundClickAccepted;

    void OnEnable()
    {
        ValidateConfiguration();
        _input.OnGroundClickAccepted += HandleGroundClick;
        _subscribed = true;
    }

    void OnDisable()
    {
        if (_subscribed)
        {
            _input.OnGroundClickAccepted -= HandleGroundClick;
            _subscribed = false;
        }

        ReturnActive();
    }

    void LateUpdate()
    {
        if (_active == null) return;

        if (_active.Tick())
        {
            ReturnActive();
            return;
        }

        UpdatePosition();
    }

    void ValidateConfiguration()
    {
        if (_input == null || _worldCamera == null ||
            _canvas == null || _root == null || _viewPrefab == null)
        {
            throw new MissingReferenceException(
                $"{name}: 입력, 월드 Camera, Canvas, Root와 표시 프리팹 연결 필요");
        }

        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay ||
            !_canvas.isRootCanvas || _root != transform ||
            !_root.IsChildOf(_canvas.transform) ||
            _root.pivot != Vector2.one * 0.5f ||
            _input.gameObject.scene != gameObject.scene ||
            _worldCamera.gameObject.scene != gameObject.scene ||
            _canvas.gameObject.scene != gameObject.scene ||
            _worldCamera.targetDisplay != _canvas.targetDisplay ||
            _worldCamera.targetTexture != null)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 씬과 Display의 Overlay Canvas, 월드 Camera와 중앙 Pivot Root 필요");
        }

        _viewPrefab.ValidateConfiguration();
    }

    void HandleGroundClick(Vector3 position, bool isAttackMove)
    {
        OnGroundClickAccepted?.Invoke();
        ReturnActive();

        _worldPosition = position;
        _active = _available.Count > 0
            ? _available.Pop()
            : Instantiate(_viewPrefab, _root, false);

        _active.gameObject.SetActive(true);
        _active.Begin(isAttackMove ? _attackMoveColor : _moveColor);
        UpdatePosition();
        _active.transform.SetAsLastSibling();
    }

    void UpdatePosition()
    {
        Vector3 viewport = _worldCamera.WorldToViewportPoint(_worldPosition);

        bool visible = _canvas.isActiveAndEnabled &&
            _worldCamera.isActiveAndEnabled &&
            viewport.z >= _worldCamera.nearClipPlane &&
            viewport.z <= _worldCamera.farClipPlane &&
            viewport.x >= 0f && viewport.x <= 1f &&
            viewport.y >= 0f && viewport.y <= 1f;

        Vector2 position = Vector2.zero;

        if (visible)
        {
            Vector3 screen = _worldCamera.WorldToScreenPoint(_worldPosition);

            visible = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _root, new Vector2(screen.x, screen.y), null, out position);
        }

        _active.SetPresentation(position, visible);
    }

    void ReturnActive()
    {
        if (_active == null) return;

        _active.gameObject.SetActive(false);
        _available.Push(_active);
        _active = null;
    }
}
