using UnityEngine;

/// <summary>
/// 시전자 또는 대상 주변의 무작위 지면에 고정되는 범위 스킬 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Skills/Random Area Damage", fileName = "_RandomAreaDamageSkill")]
public class RandomAreaDamageSkillDefinition : AreaDamageSkillDefinition
{
    [Header("----- 무작위 배치 범위 -----")]
    [SerializeField, Min(0.1f)] float _placementRadius = 4f;
    [SerializeField] bool _centerOnTarget;
    [SerializeField] bool _ignoreTargetDistance;

    public float PlacementRadius => _placementRadius;
    public bool CenterOnTarget => _centerOnTarget;
    public bool IgnoreTargetDistance => _ignoreTargetDistance;
}
