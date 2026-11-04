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
    [SerializeField] Canvas _canvas;
    [SerializeField] GraphicRaycaster _raycaster;
    [SerializeField] Vector2 _nodeSpacing = new Vector2(125f, 110f);

    SessionProgressManager _sessionProgress;

    readonly List<WorldNodeView> _views = new List<WorldNodeView>();
    readonly List<GameObject> _lines = new List<GameObject>();
    Node _firstNode;
    bool _subscribed;
    bool _rebuilding;
    bool _updatingLayout;
    bool _layoutDirty = true;
    Rect _layoutRect;

    public bool IsOpen => _canvas.enabled && isActiveAndEnabled;

    void Awake()
    {
        if (_nodePrefab == null || _nodeRoot == null ||
            _lineRoot == null || _statusText == null || _canvas == null || _raycaster == null || _nodeRoot == _lineRoot)
            throw new MissingReferenceException($"{name}: 노드 프리팹과 표시 참조 연결 필요");
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
        foreach (WorldNodeView view in _views)
        {
            view.OnClicked -= HandleClicked;
            view.gameObject.SetActive(false);
            Destroy(view.gameObject);
        }
        _views.Clear();
        foreach (GameObject line in _lines)
        {
            line.SetActive(false);
            Destroy(line);
        }
        _lines.Clear();
        _firstNode = null;
    }

    void HandleClicked(int nodeId)
    {
        if (_sessionProgress.TryEnterNode(nodeId)) Close();
    }

    void OnRectTransformDimensionsChange()
    {
        // 크기 갱신 도중 재진입하지 않고 다음 레이아웃 확정 시점에 반영
        _layoutDirty = true;
    }

    void LateUpdate()
    {
        if (_sessionProgress != null && IsOpen && _layoutDirty && !_updatingLayout && !_rebuilding)
            RefreshLayout();
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
        Vector3[] corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        Vector2 minimum = _nodeRoot.InverseTransformPoint(corners[0]);
        Vector2 maximum = _nodeRoot.InverseTransformPoint(corners[2]);
        Rect rootRect = _nodeRoot.rect;
        const float margin = 20f;
        return Rect.MinMaxRect(Mathf.Max(rootRect.xMin, minimum.x) + margin,
            Mathf.Max(rootRect.yMin, minimum.y) + margin,
            Mathf.Min(rootRect.xMax, maximum.x) - margin,
            Mathf.Min(rootRect.yMax, maximum.y) - margin);
    }

    void ArrangeNodes(WorldGraphManager graph)
    {
        _layoutRect = GetLayoutRect();
        Vector2 minimum = graph.Nodes[0].LayoutPosition;
        Vector2 maximum = minimum;
        Vector2 visualSize = _nodePrefab.LayoutSize;
        float spacing = Mathf.Max(_nodeSpacing.x, _nodeSpacing.y);
        foreach (Node node in graph.Nodes)
        {
            minimum = Vector2.Min(minimum, node.LayoutPosition);
            maximum = Vector2.Max(maximum, node.LayoutPosition);
            for (int other = node.Id + 1; other < graph.Nodes.Count; other++)
            {
                Vector2 delta = graph.Nodes[other].LayoutPosition - node.LayoutPosition;
                float horizontal = Mathf.Abs(delta.x) < 0.0001f ? float.PositiveInfinity :
                    visualSize.x / Mathf.Abs(delta.x);
                float vertical = Mathf.Abs(delta.y) < 0.0001f ? float.PositiveInfinity :
                    visualSize.y / Mathf.Abs(delta.y);
                spacing = Mathf.Max(spacing, Mathf.Min(horizontal, vertical));
            }
        }

        Vector2 requiredSize = (maximum - minimum) * spacing + visualSize;
        float fit = Mathf.Min(1f, _layoutRect.width / requiredSize.x, _layoutRect.height / requiredSize.y);
        Vector2 buttonSize = _nodePrefab.Rect.rect.size * fit;
        if (fit <= 0f || buttonSize.x < 32f || buttonSize.y < 32f)
        {
            throw new InvalidOperationException(
                $"{name}: 노드 수에 맞는 UI 표시 영역과 버튼 최소 크기 확보 필요 " +
                $"(노드={graph.Nodes.Count}, 노드 Root={_nodeRoot.rect.size}, " +
                $"표시 영역={_layoutRect.size}, 필요 영역={requiredSize}, " +
                $"배율={fit:F3}, 버튼={buttonSize}, 최소=32)");
        }

        Vector2 center = (minimum + maximum) * 0.5f;
        foreach (Node node in graph.Nodes)
        {
            WorldNodeView view = _views[node.Id];
            view.Rect.localScale = Vector3.one * fit;
            view.Rect.anchoredPosition = _layoutRect.center + (node.LayoutPosition - center) * spacing * fit;
        }
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
            _views.Add(view);
        }
        ArrangeNodes(graph);

        foreach (Node node in graph.Nodes)
        {
            foreach (int next in graph.GetConnections(node.Id))
            {
                if (next <= node.Id)
                    continue;
                Vector3 firstWorld = _nodeRoot.TransformPoint(_views[node.Id].Rect.anchoredPosition);
                Vector3 secondWorld = _nodeRoot.TransformPoint(_views[next].Rect.anchoredPosition);
                Vector2 start = _lineRoot.InverseTransformPoint(firstWorld);
                Vector2 difference = (Vector2)_lineRoot.InverseTransformPoint(secondWorld) - start;
                GameObject line = new GameObject($"Link_{node.Id}_{next}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                RectTransform rect = (RectTransform)line.transform;
                rect.SetParent(_lineRoot, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = start + difference * 0.5f;
                rect.sizeDelta = new Vector2(difference.magnitude, 3f);
                rect.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg);
                Image image = line.GetComponent<Image>();
                image.color = new Color(0.45f, 0.55f, 0.65f);
                image.raycastTarget = false;
                _lines.Add(line);
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
            _statusText.text = "월드 준비 대기";
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
                progress.IsNodeKnown(node.Id), progress.IsCleared(node.Id),
                _sessionProgress.CanEnterNode(node.Id));

        _statusText.text = _sessionProgress.IsTransitioning ? "플레이어 배치 응답 대기" :
            _sessionProgress.HasFailure ? "진행 실패: 설정 확인 또는 소환 재시도 필요" :
            _sessionProgress.IsReady ? "연결된 노드를 선택하세요" : "시작 노드 배치 대기";
    }

    /// <summary>
    /// 표시할 월드 진행의 명시적 연결
    /// </summary>
    /// <param name="progress">현재 세션의 월드 진행 담당자</param>
    public void Bind(SessionProgressManager progress)
    {
        if (progress == null) throw new ArgumentNullException(nameof(progress));
        if (_sessionProgress != null)
            throw new InvalidOperationException("이전 월드맵 연결 해제 필요");
        _sessionProgress = progress;
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
        RefreshLayout();
        _raycaster.enabled = IsOpen;
    }

    /// <summary>
    /// 진행 구독을 유지한 월드 맵 표시와 UI 입력 중단
    /// </summary>
    public void Close()
    {
        _canvas.enabled = false;
        _raycaster.enabled = false;
    }
}
