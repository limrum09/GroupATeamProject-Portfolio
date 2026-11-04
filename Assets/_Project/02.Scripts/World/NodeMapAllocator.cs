using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 맵 후보에서 노드별 맵 할당과 활성 상태 관리
/// </summary>
public class NodeMapAllocator : MonoBehaviour
{
    [Header("----- 씬 맵 후보 -----")]
    [SerializeField] MapBindings[] _maps;

    readonly Dictionary<MapBindings, bool> _originalActive = new Dictionary<MapBindings, bool>();
    readonly List<Node> _allocated = new List<Node>();

    Dictionary<MapBindings.MapType, List<MapBindings>> GetCandidates(EnemyWorldType worldType)
    {
        if ((int)worldType < 0 || worldType >= EnemyWorldType.Count)
            throw new ArgumentOutOfRangeException(nameof(worldType));
        if (_maps == null || _maps.Length == 0)
            throw new MissingReferenceException($"{name}: 씬 맵 후보 연결 필요");

        Dictionary<MapBindings.MapType, List<MapBindings>> candidates =
            new Dictionary<MapBindings.MapType, List<MapBindings>>();
        HashSet<MapBindings> registered = new HashSet<MapBindings>();
        foreach (MapBindings map in _maps)
        {
            if (map == null || !map.enabled || map.PlayerEntry == null ||
                map.gameObject.scene != gameObject.scene || !map.PlayerEntry.IsChildOf(map.transform) ||
                map.transform == transform || transform.IsChildOf(map.transform))
                throw new MissingReferenceException($"{name}: 같은 씬의 독립 맵과 자식 PlayerEntry 연결 필요");

            if (!registered.Add(map))
                throw new InvalidOperationException("맵 후보 중복 등록 불가");
            if (map.WorldType != worldType)
                continue;

            if (!candidates.TryGetValue(map.Type, out List<MapBindings> list))
            {
                list = new List<MapBindings>();
                candidates.Add(map.Type, list);
            }
            list.Add(map);
        }
        return candidates;
    }

    /// <summary>
    /// 이전 월드 해제 전 다음 월드의 필수 맵과 전투 구역 구성 검사
    /// </summary>
    /// <param name="worldType">검사할 월드 구분</param>
    public void ValidateWorldCandidates(EnemyWorldType worldType)
    {
        GetValidatedWorldCandidates(worldType);
    }

    /// <summary>
    /// 월드의 필수 맵과 전투 구역을 검증하고 후보 반환
    /// </summary>
    /// <param name="worldType">검사할 월드</param>
    /// <returns>검증한 맵 후보 목록</returns>
    public IReadOnlyList<MapBindings> GetValidatedWorldCandidates(EnemyWorldType worldType)
    {
        List<MapBindings> result = new List<MapBindings>();
        Dictionary<MapBindings.MapType, List<MapBindings>> candidates = GetCandidates(worldType);
        foreach (MapBindings.MapType type in Enum.GetValues(typeof(MapBindings.MapType)))
        {
            if (!candidates.TryGetValue(type, out List<MapBindings> maps))
                throw new InvalidOperationException($"{name}: {worldType}의 {type} 맵 후보 부재");

            foreach (MapBindings map in maps)
            {
                if (type == MapBindings.MapType.Shop &&
                    (map.ShopPoint == null || !map.ShopPoint.IsChildOf(map.transform)))
                    throw new MissingReferenceException($"{map.name}: 자식 ShopPoint 연결 필요");

                // 전투 맵의 필수 구역 참조도 기존 월드를 해제하기 전에 확인
                map.GetCombatAreaSpawnDatas();
                result.Add(map);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 월드와 용도가 일치하는 후보 할당 후 모든 후보 맵 비활성화
    /// </summary>
    /// <param name="nodes">현재 월드의 노드 목록</param>
    /// <param name="worldType">할당할 월드 구분</param>
    public void AllocateMaps(IReadOnlyList<Node> nodes, EnemyWorldType worldType = EnemyWorldType.World1)
    {
        if (nodes == null) throw new ArgumentNullException(nameof(nodes));
        if (_allocated.Count > 0)
            throw new InvalidOperationException("이전 맵 할당 해제 필요");

        Dictionary<MapBindings.MapType, List<MapBindings>> candidates = GetCandidates(worldType);
        foreach (Node node in nodes)
        {
            if (node == null || !candidates.ContainsKey(node.Type))
                throw new InvalidOperationException($"{name}: {worldType}의 노드별 맵 후보 연결 필요");
        }

        Dictionary<MapBindings.MapType, int> indices = new Dictionary<MapBindings.MapType, int>();
        // 동일 맵 후보 재사용 시 진행 기록은 노드 ID에만 보관
        foreach (Node node in nodes)
        {
            List<MapBindings> list = candidates[node.Type];
            indices.TryGetValue(node.Type, out int index);
            node.AssignMap(list[index % list.Count]);
            indices[node.Type] = index + 1;
        }

        foreach (MapBindings map in _maps)
            _originalActive.Add(map, map.gameObject.activeSelf);
        foreach (Node node in nodes)
            _allocated.Add(node);
        foreach (MapBindings map in _maps)
            map.gameObject.SetActive(false);
    }

    /// <summary>
    /// 목적 맵만 활성화
    /// </summary>
    /// <param name="node">진입할 노드</param>
    public void Activate(Node node)
    {
        if (!_allocated.Contains(node) || node.Map == null)
            throw new InvalidOperationException("할당된 노드 맵 필요");

        foreach (MapBindings map in _maps)
            map.gameObject.SetActive(map == node.Map);
    }

    /// <summary>
    /// 첫 배치 실패 시 현재 할당된 맵 표시 중단
    /// </summary>
    public void DeactivateMaps()
    {
        foreach (MapBindings map in _originalActive.Keys)
            map.gameObject.SetActive(false);
    }

    /// <summary>
    /// 노드 연결 해제와 원래 씬 맵 활성 상태 복원
    /// </summary>
    public void ReleaseMaps()
    {
        foreach (Node node in _allocated)
        {
            if (node != null)
                node.AssignMap(null);
        }
        _allocated.Clear();

        foreach (KeyValuePair<MapBindings, bool> entry in _originalActive)
        {
            if (entry.Key != null)
                entry.Key.gameObject.SetActive(entry.Value);
        }
        _originalActive.Clear();
    }
}
