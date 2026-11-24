using UnityEngine;

/// <summary>
/// 체력 포션의 회복 비율과 적 처치 드롭 확률 정의
/// </summary>
[CreateAssetMenu(menuName = "Project/Items/Health Potion Definition", fileName = "_HealthPotionDefinition")]
public class HealthPotionDefinition : ScriptableObject
{
    [Header("----- 회복 -----")]
    [SerializeField, Range(0.01f, 1f), Tooltip("최대 체력 대비 회복 비율")] float _healRatio = 0.15f;

    [Header("----- 드롭 -----")]
    [SerializeField, Range(0f, 1f), Tooltip("적 처치 1회당 드롭 확률")] float _dropChance = 0.2f;

    public float HealRatio => _healRatio;
    public float DropChance => _dropChance;
}
