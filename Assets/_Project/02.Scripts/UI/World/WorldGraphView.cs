using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World 그래프의 노드와 연결 표시 및 이동 요청 전달
/// </summary>
public class WorldGraphView : MonoBehaviour
{
    [Header("----- 표시 구성 -----")]
    [SerializeField] WorldNodeView _nodePrefab;
    [SerializeField] RectTransform _nodeRoot;
    [SerializeField] RectTransform _lineRoot;
    [SerializeField] TMP_Text _statusText;
    [SerializeField] Image _tooltipPanel;
    [SerializeField] TMP_Text _tooltipText;
    [SerializeField] Canvas _canvas;
    [SerializeField] Canvas _backgroundCanvas;
    [SerializeField] WorldMapShardMotion _shardMotion;
    [SerializeField] GraphicRaycaster _raycaster;
    [SerializeField, Tooltip("가로·세로 기본 간격. 폭이 좁은 축의 과도한 확대 제한에 사용")]
    Vector2 _nodeSpacing = new Vector2(125f, 110f);
    [SerializeField, Tooltip("Canvas 크기에 대한 노드 배치 영역의 가로·세로 비율 (0 초과, 1 이하)")]
    Vector2 _nodeLayoutRatio = new Vector2(0.75f, 0.5f);
    [SerializeField] Sprite _inactiveLineSprite;
    [SerializeField] Sprite _activeLineSprite;
    [SerializeField, Min(0f)] float _nodeSwayAmplitude = 3f;

    SessionProgressManager _sessionProgress;
    SkillLoadout _travelActor;
    float _statusHideTime;

    readonly List<WorldNodeView> _views = new List<WorldNodeView>();
    readonly List<Vector2> _nodeCenters = new List<Vector2>();
    readonly List<(int First, int Second, Image Image)> _lines =
        new List<(int First, int Second, Image Image)>();
    float _layoutScale;
    Node _firstNode;
    WorldNodeView _hoveredNode;
    bool _subscribed;
    bool _rebuilding;
    bool _updatingLayout;
    bool _layoutDirty = true;
    Rect _layoutRect;

    public bool IsOpen => _canvas.enabled && isActiveAndEnabled;

    void Awake()
    {
        if (_nodePrefab == null || _nodeRoot == null ||
            _lineRoot == null || _statusText == null || _canvas == null || _backgroundCanvas == null ||
            _raycaster == null || _nodeRoot == _lineRoot || _shardMotion == null ||
            _inactiveLineSprite == null || _activeLineSprite == null ||
            _tooltipPanel == null || _tooltipPanel.sprite == null || _tooltipText == null ||
            _tooltipText.font == null || _tooltipText.fontSharedMaterial == null)
            throw new MissingReferenceException($"{name}: 노드 프리팹·연결선 스프라이트·표시 참조 연결 필요");
    }

    void OnDisable()
    {
        Close();
    }

    void OnDestroy()
    {
        Unbind();
    }

    void ClearElements()
    {
        HideTooltip();
        foreach (WorldNodeView view in _views)
        {
            view.OnClicked -= HandleClicked;
            view.OnHoverChanged -= HandleNodeHover;
            view.gameObject.SetActive(false);
            Destroy(view.gameObject);
        }
        _views.Clear();
        _nodeCenters.Clear();
        foreach (var line in _lines)
        {
            line.Image.gameObject.SetActive(false);
            Destroy(line.Image.gameObject);
        }
        _lines.Clear();
        _firstNode = null;
    }

    void HandleClicked(int nodeId)
    {
        if (!IsOpen) return;

        switch (_sessionProgress.TryEnterNodeFromMap(nodeId, _travelActor))
        {
            case SessionProgressManager.MapTravelResult.OutsidePortalRange:
                ShowStatus("포탈 근처에서만 이동할 수 있습니다");
                break;
            case SessionProgressManager.MapTravelResult.PortalClosed:
                ShowStatus("아직 포탈이 열리지 않았습니다");
                break;
            case SessionProgressManager.MapTravelResult.Accepted:
                Close();
                break;
        }
    }

    void ShowStatus(string message)
    {
        _statusText.text = message;
        _statusText.enabled = true;
        _statusHideTime = Time.unscaledTime + 2f;
    }

    void HandleNodeHover(WorldNodeView view, bool hovered)
    {
        if (!hovered)
        {
            if (_hoveredNode == view) HideTooltip();
            return;
        }
        if (!IsOpen) return;

        _hoveredNode = view;
        RefreshTooltip();
    }

    void RefreshTooltip()
    {
        if (_hoveredNode == null) return;

        RectTransform canvasRect = (RectTransform)transform;
        float maxTextWidth = Mathf.Max(1f, Mathf.Min(320f, canvasRect.rect.width - 48f));
        _tooltipPanel.gameObject.SetActive(true);
        _tooltipText.text = _hoveredNode.Description;
        Vector2 textSize = _tooltipText.GetPreferredValues(_tooltipText.text, maxTextWidth, 0f);
        Vector2 padding = new Vector2(24f, 16f);
        _tooltipPanel.rectTransform.sizeDelta = new Vector2(
            Mathf.Ceil(Mathf.Min(textSize.x, maxTextWidth)),
            Mathf.Ceil(textSize.y)) + padding;
        PositionTooltip();
    }

    void PositionTooltip()
    {
        if (_hoveredNode == null) return;

        RectTransform canvasRect = (RectTransform)transform;
        RectTransform panelRect = _tooltipPanel.rectTransform;
        Rect bounds = canvasRect.rect;
        float bottom = canvasRect.InverseTransformPoint(
            _nodeRoot.TransformPoint(new Vector2(0f, _nodeRoot.rect.yMin))).y;
        RectTransform nodeRect = _hoveredNode.Rect;
        Vector2 nodeCenter = canvasRect.InverseTransformPoint(nodeRect.position);
        float nodeTop = canvasRect.InverseTransformPoint(
            nodeRect.TransformPoint(new Vector2(0f, nodeRect.rect.yMax))).y;
        Vector2 halfSize = panelRect.rect.size * 0.5f;
        const float gap = 8f;
        float x = Mathf.Clamp(nodeCenter.x, bounds.xMin + halfSize.x + gap, bounds.xMax - halfSize.x - gap);
        float y = nodeTop + halfSize.y + gap;
        if (y + halfSize.y > bounds.yMax - gap)
            y = 2f * nodeCenter.y - nodeTop - halfSize.y - gap;
        y = Mathf.Clamp(y, bottom + halfSize.y + gap, bounds.yMax - halfSize.y - gap);
        panelRect.localPosition = new Vector3(x, y, 0f);
    }

    void HideTooltip()
    {
        _hoveredNode = null;
        _tooltipPanel.gameObject.SetActive(false);
    }

    void OnRectTransformDimensionsChange()
    {
        // 크기 갱신 도중 재진입하지 않고 다음 레이아웃 확정 시점에 반영
        _layoutDirty = true;
    }

    void OnValidate()
    {
        _layoutDirty = true;
    }

    void LateUpdate()
    {
        if (_statusText.enabled && Time.unscaledTime >= _statusHideTime)
            _statusText.enabled = false;

        if (_sessionProgress == null || !IsOpen || _updatingLayout || _rebuilding) return;

        if (_layoutDirty)
        {
            RefreshLayout();
            return;
        }

        UpdateAnimatedPositions();
    }

    void RefreshLayout()
    {
        _updatingLayout = true;
        try
        {
            Canvas.ForceUpdateCanvases();
            ((RectTransform)transform).ForceUpdateRectTransforms();
            _nodeRoot.ForceUpdateRectTransforms();
            _lineRoot.ForceUpdateRectTransforms();
        }
        finally
        {
            _updatingLayout = false;
        }

        try
        {
            _layoutDirty = false;
            _layoutRect = default;
            Refresh();
        }
        catch
        {
            // 실패한 표시의 입력 차단과 부분 생성 요소 정리
            Close();
            ClearElements();
            throw;
        }
    }

    Rect GetLayoutRect()
    {
        if (!(_nodeLayoutRatio.x > 0f && _nodeLayoutRatio.x <= 1f &&
              _nodeLayoutRatio.y > 0f && _nodeLayoutRatio.y <= 1f))
            throw new InvalidOperationException($"{name}: 0 초과 1 이하의 노드 배치 영역 비율 필요");

        Vector3[] corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        Vector2 minimum = _nodeRoot.InverseTransformPoint(corners[0]);
        Vector2 maximum = _nodeRoot.InverseTransformPoint(corners[2]);
        Rect rootRect = _nodeRoot.rect;
        const float margin = 20f;
        Rect available = Rect.MinMaxRect(Mathf.Max(rootRect.xMin, minimum.x) + margin,
            Mathf.Max(rootRect.yMin, minimum.y) + margin,
            Mathf.Min(rootRect.xMax, maximum.x) - margin,
            Mathf.Min(rootRect.yMax, maximum.y) - margin);
        Vector2 targetSize = Vector2.Scale(maximum - minimum, _nodeLayoutRatio);
        Vector2 size = Vector2.Min(available.size, targetSize);
        Vector2 origin = (minimum + maximum - size) * 0.5f;
        origin.x = Mathf.Clamp(origin.x, available.xMin, available.xMax - size.x);
        origin.y = Mathf.Clamp(origin.y, available.yMin, available.yMax - size.y);
        return new Rect(origin, size);
    }

    void ArrangeNodes(WorldGraphManager graph)
    {
        _layoutRect = GetLayoutRect();
        Vector2 minimum = graph.Nodes[0].LayoutPosition;
        Vector2 maximum = minimum;
        Vector2 visualSize = _nodePrefab.Rect.rect.size + Vector2.one * (2f * _nodeSwayAmplitude);
        if (!(_nodeSpacing.x > 0f && _nodeSpacing.x < float.PositiveInfinity &&
              _nodeSpacing.y > 0f && _nodeSpacing.y < float.PositiveInfinity))
            throw new InvalidOperationException($"{name}: 유한한 양수 노드 간격 필요");

        foreach (Node node in graph.Nodes)
        {
            minimum = Vector2.Min(minimum, node.LayoutPosition);
            maximum = Vector2.Max(maximum, node.LayoutPosition);
        }

        Vector2 extent = maximum - minimum;
        Vector2 centerSpace = Vector2.Max(Vector2.zero, _layoutRect.size - visualSize);
        // 같은 그래프의 좁은 축에 동일한 간격 기준 적용
        System.Random random = new System.Random(graph.Nodes[0].GetInstanceID());
        float spacingRatio = Mathf.Lerp(2f / 3f, 1f, (float)random.NextDouble());
        Vector2 baseSpacing = _nodeSpacing * spacingRatio;
        Vector2 baseExtent = Vector2.Scale(extent, baseSpacing);

        // 한 격자보다 좁은 좌표 범위의 과도한 확대 방지
        const float minimumReferenceSpan = 125f;
        Vector2 spacing = new Vector2(
            centerSpace.x / Mathf.Max(baseExtent.x, minimumReferenceSpan) * baseSpacing.x,
            centerSpace.y / Mathf.Max(baseExtent.y, minimumReferenceSpan) * baseSpacing.y);

        float nodeScale = Mathf.Min(1f, _layoutRect.width / visualSize.x, _layoutRect.height / visualSize.y);
        foreach (Node node in graph.Nodes)
        {
            for (int other = node.Id + 1; other < graph.Nodes.Count; other++)
            {
                Vector2 delta = Vector2.Scale(graph.Nodes[other].LayoutPosition - node.LayoutPosition, spacing);
                float pairScale = Mathf.Max(Mathf.Abs(delta.x) / visualSize.x, Mathf.Abs(delta.y) / visualSize.y);
                nodeScale = Mathf.Min(nodeScale, pairScale);
            }
        }

        Vector2 buttonSize = _nodePrefab.Rect.rect.size * nodeScale;
        if (buttonSize.x < 32f || buttonSize.y < 32f)
        {
            throw new InvalidOperationException(
                $"{name}: 노드 수에 맞는 UI 표시 영역과 버튼 최소 크기 확보 필요 " +
                $"(노드={graph.Nodes.Count}, 노드 Root={_nodeRoot.rect.size}, " +
                $"표시 영역={_layoutRect.size}, 배율={nodeScale:F3}, 버튼={buttonSize}, 최소=32)");
        }

        _layoutScale = nodeScale;
        _nodeCenters.Clear();
        Vector2 center = (minimum + maximum) * 0.5f;
        foreach (Node node in graph.Nodes)
        {
            WorldNodeView view = _views[node.Id];
            view.Rect.localScale = Vector3.one * _layoutScale;
            Vector2 offset = Vector2.Scale(node.LayoutPosition - center, spacing);
            _nodeCenters.Add(_layoutRect.center + offset);
        }
    }

    void UpdateAnimatedPositions()
    {
        float time = Time.unscaledTime;
        float amplitude = _nodeSwayAmplitude * _layoutScale;
        for (int id = 0; id < _views.Count; id++)
        {
            float phase = id * 2.399963f;
            Vector2 offset = new Vector2(
                Mathf.Sin(time * 0.85f + phase) * 0.65f,
                Mathf.Sin(time * 1.1f + phase));
            _views[id].Rect.anchoredPosition = _nodeCenters[id] + offset * amplitude;
        }

        foreach (var line in _lines)
        {
            Vector2 start = _lineRoot.InverseTransformPoint(_views[line.First].Rect.position);
            Vector2 end = _lineRoot.InverseTransformPoint(_views[line.Second].Rect.position);
            Vector2 delta = end - start;
            RectTransform rect = line.Image.rectTransform;
            rect.localPosition = (start + end) * 0.5f;
            // 세로 원본 스프라이트의 위쪽 축을 연결 방향으로 회전
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            rect.sizeDelta = new Vector2(line.Image.sprite.rect.width * _layoutScale, delta.magnitude);
        }
        PositionTooltip();
    }

    void Rebuild(WorldGraphManager graph)
    {
        _rebuilding = true;
        try
        {
            RebuildElements(graph);
            _layoutDirty = false;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    void RebuildElements(WorldGraphManager graph)
    {
        ClearElements();
        _firstNode = graph.Nodes[0];
        foreach (Node node in graph.Nodes)
        {
            WorldNodeView view = Instantiate(_nodePrefab, _nodeRoot, false);
            view.OnClicked += HandleClicked;
            view.OnHoverChanged += HandleNodeHover;
            _views.Add(view);
        }
        ArrangeNodes(graph);

        foreach (Node node in graph.Nodes)
        {
            foreach (int next in graph.GetConnections(node.Id))
            {
                if (next <= node.Id)
                    continue;
                GameObject line = new GameObject($"Link_{node.Id}_{next}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                RectTransform rect = (RectTransform)line.transform;
                rect.SetParent(_lineRoot, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                Image image = line.GetComponent<Image>();
                image.sprite = _inactiveLineSprite;
                image.raycastTarget = false;
                _lines.Add((node.Id, next, image));
            }
        }
    }

    void Refresh()
    {
        if (_updatingLayout || _rebuilding)
        {
            _layoutDirty = true;
            return;
        }

        WorldGraphManager graph = _sessionProgress.Graph;
        if (graph == null || !graph.IsGenerated)
        {
            Close();
            ClearElements();
            return;
        }

        // 닫힌 Canvas의 미확정 크기로 배치하지 않고 열 때 최신 상태 반영
        if (!IsOpen)
            return;

        if (_layoutDirty)
        {
            RefreshLayout();
            return;
        }

        if (_firstNode != graph.Nodes[0] || _layoutRect != GetLayoutRect())
            Rebuild(graph);
        WorldProgress progress = _sessionProgress.Progress;
        foreach (Node node in graph.Nodes)
            _views[node.Id].Present(node, progress.CurrentNodeId == node.Id,
                progress.IsNodeKnown(node.Id), progress.IsVisited(node.Id),
                _travelActor != null && !_sessionProgress.IsTransitioning && progress.CanEnterNode(node.Id));

        foreach (var line in _lines)
        {
            bool active = progress.IsNodeKnown(line.First) && progress.IsNodeKnown(line.Second);
            line.Image.sprite = active ? _activeLineSprite : _inactiveLineSprite;
        }
        UpdateAnimatedPositions();
        RefreshTooltip();
    }

    /// <summary>
    /// 표시할 월드 진행의 명시적 연결
    /// </summary>
    /// <param name="progress">현재 세션의 월드 진행 담당자</param>
    /// <param name="actor">이동 요청 플레이어, 플레이어 없는 화면에서는 생략 가능</param>
    public void Bind(SessionProgressManager progress, SkillLoadout actor)
    {
        if (progress == null) throw new ArgumentNullException(nameof(progress));
        if (_sessionProgress != null)
            throw new InvalidOperationException("이전 월드맵 연결 해제 필요");
        _sessionProgress = progress;
        _travelActor = actor;
        _sessionProgress.OnStateChanged += Refresh;
        _subscribed = true;
        Close();
        Refresh();
    }

    /// <summary>
    /// 월드 구독과 생성한 노드 표시 해제
    /// </summary>
    public void Unbind()
    {
        Close();
        if (_subscribed && _sessionProgress != null)
            _sessionProgress.OnStateChanged -= Refresh;
        _subscribed = false;
        _sessionProgress = null;
        _travelActor = null;
        ClearElements();
    }

    /// <summary>
    /// 진행 구독을 유지한 월드 맵 표시와 UI 입력 허용
    /// </summary>
    public void Open()
    {
        if (_sessionProgress == null)
            throw new InvalidOperationException("월드맵 연결 필요");
        if (!_sessionProgress.IsReady || _sessionProgress.IsTransitioning) return;

        _raycaster.enabled = false;
        _canvas.enabled = true;
        _backgroundCanvas.enabled = true;
        RefreshLayout();
        _raycaster.enabled = IsOpen;
        if (IsOpen) _shardMotion.Play();
    }

    /// <summary>
    /// 진행 구독을 유지한 월드 맵 표시와 UI 입력 중단
    /// </summary>
    public void Close()
    {
        _canvas.enabled = false;
        _backgroundCanvas.enabled = false;
        _shardMotion.Stop();
        _raycaster.enabled = false;
        HideTooltip();
        _statusText.enabled = false;
    }
}
