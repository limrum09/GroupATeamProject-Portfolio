using System;
using UnityEngine;

/// <summary>
/// 현재 상점 노드의 맵 배치점에 공용 상점 표시
/// </summary>
public class SessionShopPlacement : MonoBehaviour
{
    [Header("----- 월드와 상점 -----")]
    [SerializeField] SessionProgressManager _progress;
    [SerializeField] SessionShop _shop;

    void OnEnable()
    {
        if (_progress == null || _shop == null || _shop.transform.IsChildOf(transform))
            throw new MissingReferenceException($"{name}: 별도 계층의 공용 상점과 월드 진행 연결 필요");
        _progress.OnStateChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (_progress != null) _progress.OnStateChanged -= Refresh;
        if (_shop != null)
        {
            _shop.Close();
            _shop.gameObject.SetActive(false);
        }
    }

    void Refresh()
    {
        bool available = _progress.IsReady && !_progress.IsTransitioning && _progress.Progress.HasCurrentNode;
        Node node = available ? _progress.Graph.GetNode(_progress.Progress.CurrentNodeId) : null;
        if (node == null || node.Type != MapBindings.MapType.Shop)
        {
            _shop.Close();
            _shop.gameObject.SetActive(false);
            return;
        }

        Transform point = node.Map.ShopPoint;
        if (point == null || !point.IsChildOf(node.Map.transform))
            throw new InvalidOperationException($"{node.Map.name}: 맵 하위 상점 배치점 연결 필요");
        _shop.transform.SetPositionAndRotation(point.position, point.rotation);
        _shop.gameObject.SetActive(true);
    }
}
