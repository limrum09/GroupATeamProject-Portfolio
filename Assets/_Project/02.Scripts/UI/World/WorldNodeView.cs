using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 노드 버튼 표시와 ID 기반 클릭 전달
/// </summary>
public class WorldNodeView : MonoBehaviour
{
    [SerializeField] Button _button;
    [SerializeField] Image _background;
    [SerializeField] TMP_Text _label;
    [SerializeField] Sprite _defaultSprite;
    [SerializeField] Sprite _unknownSprite;
    [SerializeField] Image _clearedCheck;
    [SerializeField] Image _currentRing;
    [SerializeField] Color _clearedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
    [SerializeField] Vector2 _layoutSize = new Vector2(104f, 128f);

    public event Action<int> OnClicked;
    public int NodeId { get; private set; }
    public RectTransform Rect => (RectTransform)transform;
    public Vector2 LayoutSize => _layoutSize;

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
        if (_button == null || _background == null || _label == null ||
            _defaultSprite == null || _unknownSprite == null ||
            _clearedCheck == null || _currentRing == null ||
            _clearedCheck.sprite == null || _currentRing.sprite == null)
        {
            throw new MissingReferenceException($"{name}: 노드 도형·체크·현재 원과 UI 참조 연결 필요");
        }
        if (_layoutSize.x <= 0f || _layoutSize.y <= 0f)
            throw new InvalidOperationException($"{name}: 양수 노드 표시 영역 필요");

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

    /// <summary>
    /// 그래프 노드 표시와 현재 진행 상태 반영
    /// </summary>
    /// <param name="node">표시할 노드</param>
    /// <param name="current">현재 위치 여부</param>
    /// <param name="known">방문 또는 현재 위치 인접에 따른 확인 여부</param>
    /// <param name="cleared">클리어 여부</param>
    /// <param name="canEnter">현재 이동 요청 가능 여부</param>
    public void Present(Node node, bool current, bool known, bool cleared, bool canEnter)
    {
        NodeId = node.Id;
        string title = node.Type == MapBindings.MapType.Start ? "시작" :
            node.Type == MapBindings.MapType.Boss ? "보스" :
            node.Type == MapBindings.MapType.Shop ? "상점" : $"노드 {node.Id}";
        _label.text = title;
        _label.gameObject.SetActive(known);
        _background.sprite = known ? _defaultSprite : _unknownSprite;
        Color nodeColor = node.Type == MapBindings.MapType.Boss ? Color.red : Color.white;
        _background.color = cleared ? nodeColor * _clearedColor : nodeColor;
        _clearedCheck.enabled = cleared;
        _currentRing.enabled = current;
        _button.interactable = canEnter;
    }
}
