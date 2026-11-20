using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain 나무 종류 목록의 중복 없는 추가와 되돌리기 처리
/// </summary>
public static class TerrainTreePaletteUtility
{
    const string UndoName = "Terrain 나무 목록 일괄 등록";

    class AppendPlan
    {
        public TerrainData Data;
        public TreePrototype[] Prototypes;
    }

    static bool TryReadSource(Terrain source, out TreePrototype[] prototypes, out string error)
    {
        prototypes = null;
        if (source == null)
        {
            error = "기준 Terrain을 지정하세요.";
            return false;
        }

        if (source.terrainData == null)
        {
            error = $"{source.name}: TerrainData가 없습니다.";
            return false;
        }

        prototypes = source.terrainData.treePrototypes;
        if (prototypes.Length == 0)
        {
            error = "기준 Terrain에 등록된 나무 종류가 없습니다.";
            return false;
        }

        return ValidatePrototypes(prototypes, source.name, out error);
    }

    static bool ValidatePrototypes(TreePrototype[] prototypes, string owner, out string error)
    {
        for (int i = 0; i < prototypes.Length; i++)
        {
            if (prototypes[i] == null || prototypes[i].prefab == null)
            {
                error = $"{owner}: {i + 1}번째 나무의 프리팹 연결이 없습니다. 먼저 해당 항목을 확인하세요.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    static bool TryReadTarget(Terrain source, Terrain target, out TreePrototype[] prototypes, out string error)
    {
        prototypes = null;
        if (!TerrainDataEditGuard.TryValidate(target, out error)) return false;

        if (target.terrainData == source.terrainData)
        {
            error = $"{target.name}: 기준 Terrain과 같은 TerrainData입니다.";
            return false;
        }

        prototypes = target.terrainData.treePrototypes;
        return ValidatePrototypes(prototypes, target.name, out error);
    }

    static TreePrototype[] BuildMergedPrototypes(TreePrototype[] source, TreePrototype[] target, out int addedCount)
    {
        List<TreePrototype> merged = new List<TreePrototype>(target);
        HashSet<(GameObject Prefab, float BendFactor, int NavMeshLod)> known =
            new HashSet<(GameObject Prefab, float BendFactor, int NavMeshLod)>();

        foreach (TreePrototype prototype in target)
        {
            known.Add((prototype.prefab, prototype.bendFactor, prototype.navMeshLod));
        }

        foreach (TreePrototype prototype in source)
        {
            if (!known.Add((prototype.prefab, prototype.bendFactor, prototype.navMeshLod))) continue;

            merged.Add(new TreePrototype
            {
                prefab = prototype.prefab,
                bendFactor = prototype.bendFactor,
                navMeshLod = prototype.navMeshLod
            });
        }

        // 기존 treeInstances의 prototypeIndex 보존을 위한 끝 추가
        addedCount = merged.Count - target.Length;
        return merged.ToArray();
    }

    static void ApplyPlans(List<AppendPlan> plans, IReadOnlyList<Terrain> terrains)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);

        try
        {
            foreach (AppendPlan plan in plans)
            {
                Undo.RegisterCompleteObjectUndo(plan.Data, UndoName);
                plan.Data.treePrototypes = plan.Prototypes;
                plan.Data.RefreshPrototypes();
                EditorUtility.SetDirty(plan.Data);
            }

            foreach (Terrain terrain in terrains)
            {
                terrain.Flush();
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }
        catch
        {
            // 일부 대상만 변경된 상태 방지를 위한 전체 실행 그룹 복원
            Undo.RevertAllDownToGroup(undoGroup);
            SceneView.RepaintAll();
            throw;
        }
    }

    /// <summary>
    /// 기준과 대상의 나무 종류 비교 및 추가 개수 계산
    /// </summary>
    /// <param name="source">기준 Terrain</param>
    /// <param name="target">등록 대상 Terrain</param>
    /// <param name="addedCount">새로 추가할 종류 수</param>
    /// <param name="error">등록 불가 사유</param>
    /// <returns>등록 조건 충족 여부</returns>
    public static bool TryGetAdditionCount(Terrain source, Terrain target, out int addedCount, out string error)
    {
        addedCount = 0;
        if (!TryReadSource(source, out TreePrototype[] sourcePrototypes, out error)) return false;
        if (!TryReadTarget(source, target, out TreePrototype[] targetPrototypes, out error)) return false;

        BuildMergedPrototypes(sourcePrototypes, targetPrototypes, out addedCount);
        return true;
    }

    /// <summary>
    /// 모든 대상의 사전 검증 후 누락된 나무 종류 일괄 추가
    /// </summary>
    /// <param name="source">기준 Terrain</param>
    /// <param name="targets">등록 대상 Terrain 목록</param>
    /// <param name="message">실행 결과 또는 등록 불가 사유</param>
    /// <returns>검증과 등록 처리 성공 여부</returns>
    public static bool TryAppendMissing(Terrain source, IReadOnlyList<Terrain> targets, out string message)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            message = "플레이 모드에서는 나무 목록을 등록할 수 없습니다.";
            return false;
        }

        if (!TryReadSource(source, out TreePrototype[] sourcePrototypes, out message)) return false;
        if (targets == null || targets.Count == 0)
        {
            message = "대상 Terrain을 하나 이상 지정하세요.";
            return false;
        }

        List<AppendPlan> plans = new List<AppendPlan>();
        HashSet<TerrainData> visited = new HashSet<TerrainData>();
        int totalAdded = 0;

        foreach (Terrain target in targets)
        {
            if (!TryReadTarget(source, target, out TreePrototype[] targetPrototypes, out message)) return false;
            if (!visited.Add(target.terrainData)) continue;

            TreePrototype[] merged = BuildMergedPrototypes(sourcePrototypes, targetPrototypes, out int addedCount);
            if (addedCount == 0) continue;

            plans.Add(new AppendPlan { Data = target.terrainData, Prototypes = merged });
            totalAdded += addedCount;
        }

        if (plans.Count == 0)
        {
            message = "모든 대상에 같은 나무 종류가 이미 등록되어 있습니다.";
            return true;
        }

        ApplyPlans(plans, targets);
        message = $"TerrainData {plans.Count}개에 총 {totalAdded}종을 추가했습니다. Ctrl+Z로 되돌리거나 Unity에서 변경 내용을 저장하세요.";
        return true;
    }
}
