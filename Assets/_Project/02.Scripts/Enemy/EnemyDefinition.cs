using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyWorldType
{
    World1,
    World2,
    Count
}

public enum EnemyRankType
{
    Normal,
    Epic,
    MidBoss,
    Boss
}

[CreateAssetMenu(
    menuName = "Project/Enemy/Enemy Definition",
    fileName = "_EnemyDefinition")]
public class EnemyDefinition : CharacterDefinition
{
    [Header("----- 기본 정보 -----")]
    [SerializeField] string _name;
    [SerializeField] GameObject _prefab;

    [Header("----- 사용 스킬 -----")]
    [SerializeField] SkillDefinition[] _skills = Array.Empty<SkillDefinition>();

    [Header("----- 연속 공격 -----")]
    [SerializeField] EnemyAttackSequence[] _attackSequences = Array.Empty<EnemyAttackSequence>();

    [Header("----- 월드 타입 -----")]
    [SerializeField] EnemyWorldType _worldType;

    [Header("----- 등급 -----")]
    [SerializeField] EnemyRankType _rankType;

    [SerializeField] string _enemyId;

    public string EnemyId => _enemyId;
    public GameObject Prefab => _prefab;
    public string DisplayName => _name;
    public IReadOnlyList<SkillDefinition> Skills => _skills;
    public IReadOnlyList<EnemyAttackSequence> AttackSequences => _attackSequences;
    public EnemyRankType RankType => _rankType;
}
