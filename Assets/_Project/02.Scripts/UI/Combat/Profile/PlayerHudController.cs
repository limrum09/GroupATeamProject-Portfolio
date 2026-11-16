using System;
using UnityEngine;

/// <summary>
/// 플레이어 머리 위 체력 HUD 연결과 위치 갱신
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class PlayerHudController : MonoBehaviour
{
    [Header("----- 씬 연결 -----")]
    [SerializeField] PlayerActor _player;
    [SerializeField] Camera _worldCamera;
    [SerializeField] Canvas _canvas;

    [Header("----- 플레이어 전용 HUD -----")]
    [SerializeField] RectTransform _hudRoot;
    [SerializeField] EnemyHudView _viewPrefab;
    [SerializeField] Vector3 _worldOffset = new Vector3(0f, 2.2f, 0f);

    EnemyHudView _view;
    CharacterStatus _status;
    Guid _runId;

    void OnEnable()
    {
        ValidateConfiguration();

        if (_view == null)
            _view = Instantiate(_viewPrefab, _hudRoot, false);

        _view.SetPresentation(Vector2.zero, false);
        _view.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        ClearBinding();
    }

    void OnDestroy()
    {
        ClearBinding();

        // 외부 Canvas 아래에 생성한 소유 화면 정리
        if (_view != null)
            Destroy(_view.gameObject);
    }

    void LateUpdate()
    {
        if (!_player.isActiveAndEnabled ||
            !_player.IsPrepared ||
            !_player.Status.IsAlive)
        {
            ClearBinding();
            return;
        }

        if (_status != _player.Status || _runId != _player.RunId)
            BindStatus();

        UpdatePresentation();
        _view.Tick(Time.deltaTime);
    }

    void ValidateConfiguration()
    {
        if (_player == null || _worldCamera == null || _canvas == null ||
            _hudRoot == null || _viewPrefab == null)
        {
            throw new MissingReferenceException(
                $"{name}: PlayerActor, Camera, Canvas, HUD Root와 플레이어 HUD 프리팹 연결 필요");
        }

        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay ||
            !_canvas.isRootCanvas ||
            !_hudRoot.IsChildOf(_canvas.transform) ||
            _hudRoot.pivot != Vector2.one * 0.5f ||
            _player.gameObject.scene != gameObject.scene ||
            _worldCamera.gameObject.scene != gameObject.scene ||
            _canvas.gameObject.scene != gameObject.scene ||
            _worldCamera.targetDisplay != _canvas.targetDisplay ||
            _worldCamera.targetTexture != null)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 씬과 Display의 Overlay Canvas, Camera 및 중앙 Pivot HUD Root 필요");
        }

        _viewPrefab.ValidateConfiguration();
    }

    void BindStatus()
    {
        ClearBinding();

        _status = _player.Status;
        _runId = _player.RunId;

        // 새 런에서 이전 체력과 피해 잔상 초기화
        _view.ResetHealth(_status.CurrentHealth, _status.MaxHealth);
        _status.OnHealthChanged += HandleHealthChanged;
    }

    void HandleHealthChanged(float current, float maximum)
    {
        _view.SetHealth(current, maximum);
    }

    void UpdatePresentation()
    {
        Vector3 worldPosition = _player.transform.position + _worldOffset;
        Vector3 viewport = _worldCamera.WorldToViewportPoint(worldPosition);

        bool visible =
            _canvas.isActiveAndEnabled &&
            _hudRoot.gameObject.activeInHierarchy &&
            _worldCamera.isActiveAndEnabled &&
            viewport.z >= _worldCamera.nearClipPlane &&
            viewport.z <= _worldCamera.farClipPlane &&
            viewport.x >= 0f && viewport.x <= 1f &&
            viewport.y >= 0f && viewport.y <= 1f;

        Vector2 localPosition = Vector2.zero;

        if (visible)
        {
            Vector3 screen = _worldCamera.WorldToScreenPoint(worldPosition);

            visible = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _hudRoot,
                new Vector2(screen.x, screen.y),
                null,
                out localPosition);
        }

        _view.SetPresentation(localPosition, visible);
    }

    void ClearBinding()
    {
        if (_status != null)
            _status.OnHealthChanged -= HandleHealthChanged;

        _status = null;
        _runId = Guid.Empty;

        if (_view != null)
            _view.SetPresentation(Vector2.zero, false);
    }
}
