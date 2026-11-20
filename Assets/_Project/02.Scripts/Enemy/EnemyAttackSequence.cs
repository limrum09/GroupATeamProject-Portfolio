using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 순서대로 요청할 적 스킬 설정
/// </summary>
[CreateAssetMenu(
    menuName = "Project/Enemy/Enemy Attack Sequence",
    fileName = "_EnemyAttackSequence")]
public class EnemyAttackSequence : ScriptableObject
{
    [Header("----- 연속 공격 순서 -----")]
    [SerializeField] SkillDefinition[] _steps = Array.Empty<SkillDefinition>();
    [SerializeField] bool _chainOnActionCompleted;
    [SerializeField, Min(0f)] float _chainDelay;

    public IReadOnlyList<SkillDefinition> Steps => _steps;
    public bool ChainOnActionCompleted => _chainOnActionCompleted;
    public float ChainDelay => _chainDelay;
}
