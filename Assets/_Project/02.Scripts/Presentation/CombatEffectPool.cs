using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

/// <summary>프리팹별 전투 이펙트 인스턴스의 대여와 보관</summary>
internal sealed class CombatEffectPool : IDisposable
{
    internal sealed class Entry
    {
        internal readonly ParticleSystem Prefab;
        internal readonly ParticleSystem Root;
        internal readonly ParticleSystem[] Particles;
        internal readonly TrailRenderer[] Trails;
        internal readonly BakedSpellPlayer BakedPlayer;
        internal readonly Vector3 OriginalScale;

        internal Entry(ParticleSystem prefab, ParticleSystem root)
        {
            Prefab = prefab;
            Root = root;
            Particles = root.GetComponentsInChildren<ParticleSystem>(true);
            Trails = root.GetComponentsInChildren<TrailRenderer>(true);

            // 루트에 선택적으로 부착된 기존 스펠 재생기
            BakedPlayer = root.GetComponent<BakedSpellPlayer>();
            OriginalScale = prefab.transform.localScale;
        }
    }

    readonly Dictionary<ParticleSystem, ObjectPool<Entry>> _pools = new Dictionary<ParticleSystem, ObjectPool<Entry>>();

    readonly Transform _storageRoot;
    readonly int _maxInactivePerPrefab;

    bool _disposed;

    internal CombatEffectPool(Transform owner, int maxInactivePerPrefab)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        if (maxInactivePerPrefab < 1)
            throw new ArgumentOutOfRangeException(nameof(maxInactivePerPrefab));

        _maxInactivePerPrefab = maxInactivePerPrefab;

        GameObject storage = new GameObject("InactiveEffects");
        storage.SetActive(false);

        if (storage.scene != owner.gameObject.scene)
            SceneManager.MoveGameObjectToScene(storage, owner.gameObject.scene);

        _storageRoot = storage.transform;
        _storageRoot.SetParent(owner, false);
    }

    internal Entry Rent(ParticleSystem prefab)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CombatEffectPool));

        if (_storageRoot == null)
            throw new MissingReferenceException("이펙트 풀의 보관 루트가 제거됨");

        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));

        if (prefab.transform.parent != null)
            throw new InvalidOperationException($"{prefab.name}: 프리팹 루트의 ParticleSystem 연결 필요");

        if (!_pools.TryGetValue(prefab, out ObjectPool<Entry> pool))
        {
            pool = new ObjectPool<Entry>(
                createFunc: () => Create(prefab),
                actionOnGet: null,
                actionOnRelease: ResetForStorage,
                actionOnDestroy: DestroyEntry,
                collectionCheck: true,
                defaultCapacity: 1,
                maxSize: _maxInactivePerPrefab);

            _pools.Add(prefab, pool);
        }

        Entry entry = pool.Get();

        if (entry.Root == null)
        {
            // 대여 개수를 복원한 뒤 외부 삭제 오류 보고
            pool.Release(entry);

            throw new MissingReferenceException($"{prefab.name}: 풀 소유 이펙트가 외부에서 삭제됨");
        }

        return entry;
    }

    Entry Create(ParticleSystem prefab)
    {
        // 비활성 부모 아래 생성하여 재생 전 초기화 보장
        ParticleSystem instance = UnityEngine.Object.Instantiate(prefab, _storageRoot, false);

        instance.gameObject.SetActive(false);

        try
        {
            Entry entry = new Entry(prefab, instance);

            if (entry.BakedPlayer != null && !entry.BakedPlayer.enabled)
                throw new InvalidOperationException($"{prefab.name}: BakedSpellPlayer 활성화 필요");

            foreach (ParticleSystem particle in entry.Particles)
            {
                ParticleSystem.MainModule main = particle.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;

                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            return entry;
        }
        catch
        {
            // 초기화 실패로 대여되지 않은 복제본 정리
            UnityEngine.Object.Destroy(instance.gameObject);
            throw;
        }
    }

    void ResetForStorage(Entry entry)
    {
        // 씬 종료 중 이미 제거된 객체
        if (entry.Root == null)
            return;

        foreach (ParticleSystem particle in entry.Particles)
        {
            if (particle != null)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        foreach (TrailRenderer trail in entry.Trails)
        {
            if (trail != null)
                trail.Clear();
        }

        entry.Root.gameObject.SetActive(false);

        // 씬 정리 순서상 보관 루트가 먼저 제거된 경우
        // 종료와 비활성화 중 부모 변경 생략
        if (_disposed || _storageRoot == null || _storageRoot.parent == null || !_storageRoot.parent.gameObject.activeInHierarchy)
            return;

        Transform effectTransform = entry.Root.transform;
        effectTransform.SetParent(_storageRoot, false);
        effectTransform.localPosition = Vector3.zero;
        effectTransform.localRotation = Quaternion.identity;
        effectTransform.localScale = entry.OriginalScale;
    }

    static void DestroyEntry(Entry entry)
    {
        if (entry.Root != null)
            UnityEngine.Object.Destroy(entry.Root.gameObject);
    }

    internal void Return(Entry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (_disposed)
        {
            // 폐기 이후 도착한 반환은 재보관 없이 정리
            ResetForStorage(entry);
            DestroyEntry(entry);
            return;
        }

        if (!_pools.TryGetValue(entry.Prefab, out ObjectPool<Entry> pool))
            throw new InvalidOperationException("이 풀에서 생성한 이펙트만 반환 가능");

        pool.Release(entry);
    }

    /// <summary>프리팹별 풀과 보관 루트의 폐기</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (ObjectPool<Entry> pool in _pools.Values)
            pool.Dispose();

        _pools.Clear();

        if (_storageRoot != null)
            UnityEngine.Object.Destroy(_storageRoot.gameObject);
    }
}