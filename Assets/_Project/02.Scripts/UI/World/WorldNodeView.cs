using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 노드 버튼 표시와 ID 기반 클릭 전달
/// </summary>
public class WorldNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] Button _button;
    [SerializeField] Image _background;
    [SerializeField] Image _overlay;
    [SerializeField] Sprite _bossSprite;
    [SerializeField] Sprite _shopSprite;
    [SerializeField] Sprite _combatSprite;
    [SerializeField] Sprite _unexploredSprite;
    [SerializeField] Image _currentMarker;
    [SerializeField] Color _visitedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    public event Action<int> OnClicked;
    public event Action<WorldNodeView, bool> OnHoverChanged;
    public int NodeId { get; private set; }
    public string Description { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    void Awake()
    {
        ValidateReferences();

        _button.onClick.AddListener(HandleClicked);
    }

    void OnValidate()
    {
        ValidateReferences();
    }

    void ValidateReferences()
    {
        if (_button == null || _background == null || _background.sprite == null ||
            _overlay == null || _bossSprite == null ||
            _shopSprite == null || _combatSprite == null || _unexploredSprite == null ||
            _currentMarker == null || _currentMarker.sprite == null)
        {
            throw new MissingReferenceException($"{name}: 노드 바탕·아이콘·현재 위치 표시 참조 연결 필요");
        }
        if (_button.transition != UnityEngine.UI.Selectable.Transition.None)
            throw new InvalidOperationException($"{name}: 노드 상태 색을 유지하는 Button Transition None 설정 필요");
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
        bool isStart = node.Type == MapBindings.MapType.Start;
        bool isBoss = node.Type == MapBindings.MapType.Boss;
        bool showBackground = !isStart && !isBoss && known;

        Sprite overlay = isBoss ? _bossSprite : known ? (node.Type switch
        {
            MapBindings.MapType.Shop => _shopSprite,
            MapBindings.MapType.Combat => _combatSprite,
            _ => null
        }) : _unexploredSprite;

        Color tint = visited && !current ? _visitedColor : Color.white;
        _background.enabled = showBackground;
        _background.color = tint;
        _overlay.sprite = overlay;
        _overlay.color = tint;
        _overlay.enabled = !isStart && overlay != null;
        _overlay.raycastTarget = !showBackground && _overlay.enabled;
        if (_overlay.enabled)
        {
            float scale = _background.rectTransform.rect.width / _background.sprite.rect.width;
            _overlay.rectTransform.sizeDelta = overlay.rect.size * scale;
        }

        _currentMarker.enabled = isStart;
        _button.interactable = canEnter;
    }
}
