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

    public IReadOnlyList<SkillDefinition> Steps => _steps;
}
