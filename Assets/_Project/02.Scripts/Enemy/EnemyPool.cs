using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 월드와 등급별 적 데이터 후보 조회
/// </summary>
public class EnemyPool : MonoBehaviour
{
    [Header("----- 월드1 적 데이터 -----")]
    [SerializeField] EnemyDefinition[] _world1Definitions;	// World1에서 소환 가능한 적 데이터 후보

    [Header("----- 월드2 적 데이터 -----")]
    [SerializeField] EnemyDefinition[] _world2Definitions;	// World2에서 소환 가능한 적 데이터 후보

    /// <summary>
    /// 지원하는 소환 월드 값 검사
    /// </summary>
    /// <param name="worldType">검사할 월드 종류</param>
    public void ValidateWorldType(EnemyWorldType worldType)
    {
        if (worldType != EnemyWorldType.World1 && worldType != EnemyWorldType.World2)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(worldType), worldType,
                $"{name}: 소환 월드는 World1 또는 World2여야 함");
        }
    }

    /// <summary>
    /// 월드와 등급에 해당하는 적 데이터 후보 조회
    /// </summary>
    /// <param name="worldType">월드 종류</param>
    /// <param name="rankType">적 등급</param>
    /// <returns>조건에 맞는 적 데이터 목록</returns>
    public List<EnemyDefinition> GetEnemyDefinitions(
        EnemyWorldType worldType,
        EnemyRankType rankType)
    {
        ValidateWorldType(worldType);
        if (!System.Enum.IsDefined(typeof(EnemyRankType), rankType))
            throw new System.ArgumentOutOfRangeException(nameof(rankType), rankType, "지원하지 않는 적 등급");

        EnemyDefinition[] definitions = worldType switch
        {
            EnemyWorldType.World1 => _world1Definitions,
            EnemyWorldType.World2 => _world2Definitions,
            _ => throw new System.ArgumentOutOfRangeException(nameof(worldType))
        };

        if (definitions == null)
            throw new MissingReferenceException($"{name}: {worldType}의 EnemyDefinition 배열 연결 필요");

        // 소환 수량과 무관한 해당 등급의 데이터 후보
        List<EnemyDefinition> candidates = new List<EnemyDefinition>();

        for (int i = 0; i < definitions.Length; i++)
        {
            EnemyDefinition definition = definitions[i];
            if (definition == null)
                throw new MissingReferenceException($"{name}: {worldType}의 EnemyDefinition[{i}] 누락");

            if (definition.RankType == rankType)
                candidates.Add(definition);
        }

        return candidates;
    }
}
