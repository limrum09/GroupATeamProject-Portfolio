using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 강화 한 종류의 해금, 장착, 잠금 아이콘 참조
/// </summary>
[Serializable]
public class MetaUpgradeIconEntry
{
    [SerializeField] string _upgradeId;
    [SerializeField] Sprite _available;
    [SerializeField] Sprite _equipped;
    [SerializeField] Sprite _unavailable;

    public string UpgradeId => _upgradeId;
    public Sprite Available => _available;
    public Sprite Equipped => _equipped;
    public Sprite Unavailable => _unavailable;

    /// <summary>
    /// CSV에서 선택한 강화별 아이콘 참조 구성
    /// </summary>
    /// <param name="upgradeId">강화 식별자</param>
    /// <param name="available">해금 상태 아이콘</param>
    /// <param name="equipped">장착 상태 아이콘</param>
    /// <param name="unavailable">잠금 상태 아이콘</param>
    public MetaUpgradeIconEntry(
        string upgradeId, Sprite available, Sprite equipped, Sprite unavailable)
    {
        _upgradeId = upgradeId;
        _available = available;
        _equipped = equipped;
        _unavailable = unavailable;
    }
}

/// <summary>
/// 내보낸 메타 강화 아이콘의 ID별 조회
/// </summary>
public class MetaUpgradeIconCatalog : ScriptableObject
{
    [Header("----- 강화별 아이콘 -----")]
    [SerializeField] MetaUpgradeIconEntry[] _entries = Array.Empty<MetaUpgradeIconEntry>();

    Dictionary<string, MetaUpgradeIconEntry> _byId;

    void OnEnable()
    {
        _byId = null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        _byId = null;
    }
#endif

    /// <summary>
    /// 강화 ID에 해당하는 아이콘 세트 조회
    /// </summary>
    /// <param name="upgradeId">강화 식별자</param>
    /// <returns>해당 강화의 아이콘 세트</returns>
    public MetaUpgradeIconEntry Get(string upgradeId)
    {
        if (_byId == null)
        {
            Dictionary<string, MetaUpgradeIconEntry> byId = new Dictionary<string, MetaUpgradeIconEntry>(_entries.Length, StringComparer.Ordinal);

            foreach (MetaUpgradeIconEntry entry in _entries)
                byId.Add(entry.UpgradeId, entry);

            _byId = byId;
        }

        if (!_byId.TryGetValue(upgradeId, out MetaUpgradeIconEntry icons))
            throw new InvalidOperationException($"{name}: 강화 아이콘 누락: {upgradeId}");

        return icons;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 내보내기 시 아이콘 목록 교체와 이전 목록 반환
    /// </summary>
    /// <param name="entries">검증한 새 아이콘 목록</param>
    /// <returns>저장 실패 시 복원할 이전 목록</returns>
    public MetaUpgradeIconEntry[] ReplaceEntries(MetaUpgradeIconEntry[] entries)
    {
        MetaUpgradeIconEntry[] previous = _entries;
        _entries = entries;
        _byId = null;
        return previous;
    }
#endif
}