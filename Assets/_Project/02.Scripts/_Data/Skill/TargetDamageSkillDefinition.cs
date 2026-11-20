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
    [SerializeField] bool _applyDirectDamage = true;
    [SerializeField] bool _includeDirectTargetInArea;
    [SerializeField] bool _playHitEffectOnAreaTargets;

    [Header("----- 사용 이펙트 위치 -----")]
    [Tooltip("켜면 사용 이펙트를 대상 위치가 아닌 시전자 위치에서 재생")]
    [SerializeField] bool _useEffectAtCaster;

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
    [SerializeField, Min(1)] int _projectileBurstCount = 1;
    [SerializeField, Min(0.01f)] float _projectileBurstInterval = 0.1f;

    // 적중 시 추가할 범위 효과, 미지정 시 직접 피해만 처리
    public AreaEffectDefinition OnHitAreaEffect => _onHitAreaEffect;
    public bool AppliesDirectDamage => _onHitAreaEffect == null || _applyDirectDamage;
    public bool IncludeDirectTargetInArea => _includeDirectTargetInArea;
    // 적중 범위 피해 대상에도 공통 Hit Effect 재생 여부
    public bool PlayHitEffectOnAreaTargets => _playHitEffectOnAreaTargets;
    // 사용 이펙트를 시전자 위치에서 재생할지 여부, 꺼지면 대상 위치
    public bool UseEffectAtCaster => _useEffectAtCaster;
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
    public int ProjectileBurstCount => Mathf.Max(1, _projectileBurstCount);
    public float ProjectileBurstInterval => _projectileBurstInterval;

    protected override void OnValidate()
    {
        base.OnValidate();

        _damageMultiplier = Mathf.Max(0f, _damageMultiplier);
        _spellPowerMultiplier = Mathf.Max(0f, _spellPowerMultiplier);
    }
}
