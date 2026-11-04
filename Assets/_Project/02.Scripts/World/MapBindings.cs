using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 맵의 진입 위치와 구역별 웨이브 및 소환 범위 참조 제공
/// </summary>
[DisallowMultipleComponent]
public class MapBindings : MonoBehaviour
{
    /// <summary>
    /// 맵 용도 구분
    /// </summary>
    public enum MapType
    {
        Start,
        Combat,
        Boss,
        Shop
    }

    /// <summary>
    /// 동일 구역의 정적 설정과 씬 범위 참조
    /// </summary>
    [Serializable]
    public class CombatAreaBinding
    {
        [SerializeField] Transform _root;
        [SerializeField] SphereCollider _entryArea;
        [SerializeField] CombatAreaSpawnData _spawnData = new CombatAreaSpawnData();
        [SerializeField] List<SphereCollider> _spawnAreas = new List<SphereCollider>();

        public Transform Root => _root;
        public SphereCollider EntryArea => _entryArea;
        public CombatAreaSpawnData SpawnData => _spawnData;
        public IReadOnlyList<SphereCollider> SpawnAreas => _spawnAreas;

#if UNITY_EDITOR
        /// <summary>
        /// 편집 도구에서 생성한 구역의 설정과 참조 구성
        /// </summary>
        /// <param name="root">생성한 구역의 기준 Transform</param>
        /// <param name="entryArea">물리 충돌 없이 진입 범위를 정의할 Collider</param>
        /// <returns>진입 영역과 빈 소환 설정 및 범위 목록을 가진 구역</returns>
        public static CombatAreaBinding CreateForEditor(Transform root, SphereCollider entryArea)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            if (entryArea == null || entryArea.transform != root || entryArea.enabled)
                throw new InvalidOperationException("구역 Root의 비활성 진입 영역 Collider 연결 필요");

            return new CombatAreaBinding
            {
                _root = root,
                _entryArea = entryArea
            };
        }

        /// <summary>
        /// 편집 도구에서 생성한 범위와 같은 순번의 빈 웨이브 등록
        /// </summary>
        /// <param name="spawnArea">생성된 범위</param>
        public void RegisterCreatedArea(SphereCollider spawnArea)
        {
            if (spawnArea == null || _spawnAreas == null)
                throw new InvalidOperationException("범위와 참조 목록 구성 필요");

            if (_spawnData == null || _spawnData.WaveSpawnDatas == null ||
                _spawnData.WaveSpawnDatas.Length != _spawnAreas.Count)
            {
                throw new InvalidOperationException("소환 범위와 웨이브 목록의 1:1 구성 필요");
            }

            _spawnData.AppendWaveForEditor();
            _spawnAreas.Add(spawnArea);
        }
#endif
    }

    [Header("----- 맵 구성 -----")]
    [SerializeField] MapType _mapType;
    [SerializeField] EnemyWorldType _worldType;
    [SerializeField] Transform _playerEntry;
    [SerializeField] Transform _shopPoint;

    [Header("----- 전투 구역 -----")]
    [SerializeField] Transform _combatAreasRoot;
    [SerializeField] CombatAreaBinding[] _combatAreas = new CombatAreaBinding[0];

    [Header("----- 범위 생성 도구 -----")]
    [SerializeField] SphereCollider _spawnAreaPrefab;
    [SerializeField, Min(0)] int _editAreaIndex;

    [Header("----- 포탈 배치 도구 -----")]
    [SerializeField] WorldPortal _portalPrefab;
    [SerializeField] SessionProgressManager _portalProgress;
    [SerializeField] SessionUIController _portalUi;
    [SerializeField] Transform _portalSpawnPoint;
    [SerializeField] WorldPortal _portal;

    public MapType Type => _mapType;
    public EnemyWorldType WorldType => _worldType;
    public Transform PlayerEntry => _playerEntry;
    public Transform ShopPoint => _shopPoint;
    public IReadOnlyList<CombatAreaBinding> CombatAreas => _combatAreas;

#if UNITY_EDITOR
    [ContextMenu("포탈/포탈 배치")]
    void CreatePortal()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this))
            throw new InvalidOperationException("씬 맵의 편집 모드에서만 포탈 배치 가능");

        if (_portalPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(_portalPrefab))
            throw new MissingReferenceException("WorldPortal 컴포넌트를 가진 포탈 프리팹 연결 필요");
        if (_portalProgress == null || _portalUi == null ||
            _portalProgress.gameObject.scene != gameObject.scene ||
            _portalUi.gameObject.scene != gameObject.scene)
            throw new MissingReferenceException("같은 씬의 SessionProgressManager와 SessionUIController 연결 필요");
        if (_portal != null)
            throw new InvalidOperationException("이미 배치된 포탈의 생성 위치를 조정하거나 기존 포탈 제거 후 배치 필요");
        if (_portalSpawnPoint != null && (_portalSpawnPoint == transform ||
            !_portalSpawnPoint.IsChildOf(transform) ||
            _portalSpawnPoint.gameObject.scene != gameObject.scene))
            throw new InvalidOperationException("같은 맵 하위의 포탈 생성 위치 연결 필요");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("맵 포탈 배치");
        try
        {
            Undo.RecordObject(this, "포탈 배치 참조 등록");
            if (_portalSpawnPoint == null)
            {
                GameObject spawnPoint = new GameObject(
                    GameObjectUtility.GetUniqueNameForSibling(transform, "PortalSpawnPoint"));
                Undo.RegisterCreatedObjectUndo(spawnPoint, "포탈 생성 위치 추가");
                spawnPoint.transform.SetParent(transform, false);
                _portalSpawnPoint = spawnPoint.transform;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                _portalPrefab.gameObject, _portalSpawnPoint);
            Undo.RegisterCreatedObjectUndo(instance, "포탈 프리팹 배치");
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            _portal = instance.GetComponent<WorldPortal>();
            Undo.RecordObject(_portal, "포탈 진행과 화면 연결");
            _portal.ConfigureForEditor(this, _portalProgress, _portalUi);
            EditorUtility.SetDirty(_portal);
            PrefabUtility.RecordPrefabInstancePropertyModifications(_portal);
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            Selection.activeGameObject = _portalSpawnPoint.gameObject;
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            // 위치·인스턴스 생성과 참조 등록의 일괄 복구
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    [ContextMenu("전투 구역/구역 추가")]
    void CreateCombatArea()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this))
            throw new InvalidOperationException("씬 맵의 편집 모드에서만 구역 생성 가능");

        if (_mapType != MapType.Combat && _mapType != MapType.Boss)
            throw new InvalidOperationException("전투맵 또는 보스맵에서만 구역 생성 가능");

        if (_combatAreas == null)
            throw new InvalidOperationException("전투 구역 목록 구성 필요");

        if (_combatAreasRoot != null &&
            (_combatAreasRoot == transform ||
             !_combatAreasRoot.IsChildOf(transform) ||
             _combatAreasRoot.gameObject.scene != gameObject.scene))
        {
            throw new InvalidOperationException("같은 맵의 자식 CombatAreas 부모 연결 필요");
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("맵 전투 구역 추가");

        try
        {
            // 부모 참조가 없으면 생성 후 명시적으로 연결
            if (_combatAreasRoot == null)
            {
                GameObject container = new GameObject(
                    GameObjectUtility.GetUniqueNameForSibling(transform, "CombatAreas"));
                Undo.RegisterCreatedObjectUndo(container, "전투 구역 부모 생성");
                container.transform.SetParent(transform, false);
                Undo.RecordObject(this, "전투 구역 부모 등록");
                _combatAreasRoot = container.transform;
            }

            GameObject areaObject = new GameObject(
                GameObjectUtility.GetUniqueNameForSibling(_combatAreasRoot, "CombatArea"),
                typeof(SphereCollider));
            // 물리 장애물이 아닌 진입 판정용 범위 데이터
            SphereCollider entryArea = areaObject.GetComponent<SphereCollider>();
            entryArea.enabled = false;
            entryArea.center = Vector3.zero;
            entryArea.radius = 20f;
            Undo.RegisterCreatedObjectUndo(areaObject, "전투 구역 생성");
            areaObject.transform.SetParent(_combatAreasRoot, false);

            Undo.RecordObject(this, "맵 전투 구역 등록");
            int newIndex = _combatAreas.Length;
            Array.Resize(ref _combatAreas, newIndex + 1);
            _combatAreas[newIndex] = CombatAreaBinding.CreateForEditor(areaObject.transform, entryArea);

            // 이어서 범위 추가 메뉴를 실행할 대상
            _editAreaIndex = newIndex;

            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);

            Selection.activeGameObject = areaObject;
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            // 부모·구역 생성과 참조 등록을 한 번에 복구
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    [ContextMenu("소환 범위/지정 구역에 범위 추가")]
    void CreateSpawnArea()
    {
        if (Application.isPlaying || EditorUtility.IsPersistent(this))
            throw new InvalidOperationException("씬 맵의 편집 모드에서만 범위 생성 가능");

        if (_spawnAreaPrefab == null ||
            !PrefabUtility.IsPartOfPrefabAsset(_spawnAreaPrefab) || _spawnAreaPrefab.enabled)
        {
            throw new MissingReferenceException("비활성 SphereCollider를 가진 범위 프리팹 연결 필요");
        }

        CombatAreaBinding area = GetCombatArea(_editAreaIndex);
        if (area.SpawnAreas == null)
            throw new InvalidOperationException("구역의 범위 목록 구성 필요");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("맵 소환 범위 추가");

        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                _spawnAreaPrefab.gameObject, area.Root);
            Undo.RegisterCreatedObjectUndo(instance, "맵 소환 범위 추가");
            instance.name = GameObjectUtility.GetUniqueNameForSibling(area.Root, "SpawnArea");

            Undo.RecordObject(this, "맵 소환 범위 등록");
            area.RegisterCreatedArea(instance.GetComponent<SphereCollider>());
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            Selection.activeGameObject = instance;
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            // 생성과 참조 등록을 동일 편집 작업으로 복구
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }
#endif

    /// <summary>
    /// 구역 데이터와 기준 Transform의 동일 맵 소유 검증 후 조회
    /// </summary>
    /// <param name="index">구역 목록 인덱스</param>
    /// <returns>선택한 구역의 설정과 씬 참조</returns>
    public CombatAreaBinding GetCombatArea(int index)
    {
        if (_mapType != MapType.Combat && _mapType != MapType.Boss)
            throw new InvalidOperationException($"{name}: 전투맵 또는 보스맵에서만 구역 요청 가능");

        if (_combatAreas == null || index < 0 || index >= _combatAreas.Length)
            throw new ArgumentOutOfRangeException(nameof(index), "유효한 구역 인덱스 필요");

        CombatAreaBinding area = _combatAreas[index];
        if (area == null || area.Root == null || area.SpawnData == null ||
            area.Root == transform || !area.Root.IsChildOf(transform) ||
            area.Root.gameObject.scene != gameObject.scene)
        {
            throw new MissingReferenceException($"{name}: 같은 맵의 자식 구역과 웨이브 설정 연결 필요");
        }

        return area;
    }

    /// <summary>
    /// 기존 노드 진행 입력에 제공할 구역별 설정 조회
    /// </summary>
    /// <returns>맵의 구역별 설정 목록</returns>
    public CombatAreaSpawnData[] GetCombatAreaSpawnDatas()
    {
        if (_mapType == MapType.Start || _mapType == MapType.Shop)
            return new CombatAreaSpawnData[0];

        if (_combatAreas == null || _combatAreas.Length == 0)
            throw new MissingReferenceException($"{name}: 전투 구역 연결 필요");

        CombatAreaSpawnData[] result = new CombatAreaSpawnData[_combatAreas.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetCombatArea(i).SpawnData;

        return result;
    }
}
