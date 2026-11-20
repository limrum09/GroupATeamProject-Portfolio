using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 노드 버튼 표시와 ID 기반 클릭 전달
/// </summary>
public class WorldNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    const float CurrentMarkerPeakScale = 1.15f;
    const float CurrentMarkerHalfCycle = 0.45f;

    [SerializeField] Button _button;
    [SerializeField] Image _background;
    [SerializeField] Image _overlay;
    [SerializeField] Sprite _normalBackgroundSprite;
    [SerializeField] Sprite _inactiveBackgroundSprite;
    [SerializeField] Sprite _bossBackgroundSprite;
    [SerializeField] Sprite _bossIconSprite;
    [SerializeField] Sprite _shopSprite;
    [SerializeField] Sprite _combatSprite;
    [SerializeField] Sprite _startMarkerSprite;
    [SerializeField] Sprite _currentMarkerSprite;
    [SerializeField] Color _visitedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    Vector3 _markerBaseScale;
    Tween _currentMarkerTween;

    public event Action<int> OnClicked;
    public event Action<WorldNodeView, bool> OnHoverChanged;
    public int NodeId { get; private set; }
    public string Description { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    void Awake()
    {
        ValidateReferences();
        _markerBaseScale = _overlay.rectTransform.localScale;

        _button.onClick.AddListener(HandleClicked);
    }

    void OnValidate()
    {
        ValidateReferences();
    }

    void ValidateReferences()
    {
        if (_button == null || _background == null || _normalBackgroundSprite == null ||
            _overlay == null || _inactiveBackgroundSprite == null || _bossBackgroundSprite == null ||
            _bossIconSprite == null || _shopSprite == null || _combatSprite == null || _currentMarkerSprite == null)
        {
            throw new MissingReferenceException($"{name}: 노드 바탕·아이콘·현재 위치 표시 참조 연결 필요");
        }
        if (_button.transition != UnityEngine.UI.Selectable.Transition.None)
            throw new InvalidOperationException($"{name}: 노드 상태 색을 유지하는 Button Transition None 설정 필요");
    }

    void OnDisable()
    {
        StopCurrentMarkerPulse();
    }

    void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleClicked);
    }

    void HandleClicked()
    {
        OnClicked?.Invoke(NodeId);
    }

    string GetDescription(MapBindings.MapType type, bool current, bool known, bool visited)
    {
        if (current) return "당신이 위치한 장소입니다";
        if (type == MapBindings.MapType.Boss)
            return "더 깊은 곳으로 이동할 수 있는 장소입니다. 강력한 적이 존재합니다.";
        if (!known) return "이 장소는 너무 멀리있어 아직 밝혀지지 않았습니다";
        if (type == MapBindings.MapType.Shop) return "이 장소에 상점이 있습니다";
        return visited ? "이미 방문한 장소입니다" : "이 장소는 아직 방문하지 않았습니다.";
    }

    /// <summary>
    /// 노드 위로 포인터 진입 전달
    /// </summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        OnHoverChanged?.Invoke(this, true);
    }

    /// <summary>
    /// 노드에서 포인터 이탈 전달
    /// </summary>
    /// <param name="eventData">포인터 이벤트 정보</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        OnHoverChanged?.Invoke(this, false);
    }

    /// <summary>
    /// 현재 위치 마커 트윈 종료와 기준 크기 복원
    /// </summary>
    public void StopCurrentMarkerPulse()
    {
        if (_currentMarkerTween == null) return;

        _currentMarkerTween.Kill();
        _currentMarkerTween = null;
        _overlay.rectTransform.localScale = _markerBaseScale;
    }

    /// <summary>
    /// 그래프 노드 표시와 현재 진행 상태 반영
    /// </summary>
    /// <param name="node">표시할 노드</param>
    /// <param name="current">현재 위치 여부</param>
    /// <param name="known">방문 또는 현재 위치 인접에 따른 확인 여부</param>
    /// <param name="visited">방문 여부</param>
    /// <param name="canEnter">현재 이동 요청 가능 여부</param>
    public void Present(Node node, bool current, bool known, bool visited, bool canEnter)
    {
        NodeId = node.Id;
        Description = GetDescription(node.Type, current, known, visited);
        bool isBoss = node.Type == MapBindings.MapType.Boss;

        Sprite overlay = current ? _currentMarkerSprite : (node.Type switch
        {
            MapBindings.MapType.Start => _startMarkerSprite,
            MapBindings.MapType.Boss => _bossIconSprite,
            MapBindings.MapType.Shop when known => _shopSprite,
            MapBindings.MapType.Combat when known => _combatSprite,
            _ => null
        });

        Color tint = visited && !current ? _visitedColor : Color.white;
        _background.sprite = isBoss ? _bossBackgroundSprite :
            known ? _normalBackgroundSprite : _inactiveBackgroundSprite;
        _background.enabled = !current;
        _background.color = tint;
        _overlay.sprite = overlay;
        _overlay.color = tint;
        _overlay.enabled = overlay != null;
        _overlay.raycastTarget = current;
        if (current)
        {
            _overlay.SetNativeSize();
        }
        else if (_overlay.enabled)
        {
            float scale = _background.rectTransform.rect.width / _background.sprite.rect.width;
            _overlay.rectTransform.sizeDelta = overlay.rect.size * scale;
        }

        if (!current)
        {
            StopCurrentMarkerPulse();
        }
        else if (_currentMarkerTween == null && isActiveAndEnabled)
        {
            _currentMarkerTween = _overlay.rectTransform
                .DOScale(_markerBaseScale * CurrentMarkerPeakScale, CurrentMarkerHalfCycle)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(UpdateType.Normal, true);
        }

        _button.interactable = canEnter;
    }
}
