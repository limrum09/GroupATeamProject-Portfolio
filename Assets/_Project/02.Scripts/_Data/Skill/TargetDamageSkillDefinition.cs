using UnityEngine;
/// <summary>
/// 탄환의 비행 방식
/// </summary>
public enum ProjectileFlightMode
{
    Homing = 0,
    Straight = 1
}

[CreateAssetMenu(menuName = "Project/Skills/Target Damage", fileName = "_TargetDamageSkill")]
public sealed class TargetDamageSkillDefinition : SkillDefinition
{
    [Header("----- 선택적 적중 범위 효과 -----")]
    [SerializeField] AreaEffectDefinition _onHitAreaEffect;

    [Header("----- 장애물 검사 -----")]
    [SerializeField] LayerMask _blockingMask;

    [Header("----- 피해 계수 -----")]
    [SerializeField, Min(0f)] float _damageMultiplier = 2f;
    [SerializeField, Min(0f)] float _spellPowerMultiplier = 0f;

    [Header("----- 탄환 -----")]
    [SerializeField] bool _useProjectile;
    [SerializeField] ProjectileFlightMode _flightMode = ProjectileFlightMode.Homing;
    [SerializeField] TargetProjectile _projectilePrefab;
    [SerializeField, Min(0.01f)] float _projectileSpeed = 12f;
    [SerializeField, Min(0.001f)] float _projectileRadius = 0.15f;
    [SerializeField, Min(0.01f)] float _projectileLifetime = 3f;
    [SerializeField] Vector3 _projectileSpawnOffset;

    // 적중 시 추가할 범위 효과, 미지정 시 직접 피해만 처리
    public AreaEffectDefinition OnHitAreaEffect => _onHitAreaEffect;
    public LayerMask BlockingMask => _blockingMask;
    public float DamageMultiplier => _damageMultiplier;
    public float SpellPowerMultiplier => _spellPowerMultiplier;
    public bool UseProjectile => _useProjectile;
    public ProjectileFlightMode FlightMode => _flightMode;
    public TargetProjectile ProjectilePrefab => _projectilePrefab;
    public float ProjectileSpeed => _projectileSpeed;
    public float ProjectileRadius => _projectileRadius;
    public float ProjectileLifetime => _projectileLifetime;
    public Vector3 ProjectileSpawnOffset => _projectileSpawnOffset;

    protected override void OnValidate()
    {
        base.OnValidate();

        _damageMultiplier = Mathf.Max(0f, _damageMultiplier);
        _spellPowerMultiplier = Mathf.Max(0f, _spellPowerMultiplier);
    }
}