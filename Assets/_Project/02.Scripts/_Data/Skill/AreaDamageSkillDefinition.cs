using UnityEngine;

/// <summary>
/// 범위 스킬의 생성 위치 선택 방식
/// </summary>
public enum AreaPlacementMode
{
    CasterPosition = 0,
    RequestedWorldPosition = 1
}

/// <summary>
/// 범위 효과를 생성 위치까지 전달하는 방식
/// </summary>
public enum AreaDeliveryMode
{
    Direct = 0,
    Projectile = 1
}

/// <summary>
/// 생성된 범위 효과의 위치 추적 방식
/// </summary>
public enum AreaTrackingMode
{
    Fixed = 0,
    FollowCaster = 1
}


[CreateAssetMenu(menuName = "Project/Skills/Area Damage", fileName = "_AreaDamageSkill")]
public class AreaDamageSkillDefinition : SkillDefinition
{
    [Header("----- 범위 효과 -----")]
    [SerializeField] AreaEffectDefinition _areaEffect;

    [Header("----- 위치 추적 -----")]
    [SerializeField] AreaTrackingMode _trackingMode = AreaTrackingMode.Fixed;

    [Header("----- 배치 방식 -----")]
    [SerializeField]
    AreaPlacementMode _placementMode = AreaPlacementMode.CasterPosition;
    [SerializeField]
    AreaDeliveryMode _deliveryMode = AreaDeliveryMode.Direct;

    [Header("----- 지정 위치 검사 -----")]
    [SerializeField, Min(0.1f)] float _maxPlacementDistance = 15f;
    [SerializeField] LayerMask _groundMask;
    [SerializeField, Min(0.01f)] float _groundProbeHeight = 3f;
    [SerializeField, Min(0.01f)] float _groundProbeDepth = 6f;

    [Header("----- 목적지 도착형 탄환 -----")]
    [SerializeField] TargetProjectile _projectilePrefab;
    [SerializeField, Min(0.01f)] float _projectileSpeed = 12f;
    [SerializeField, Min(0.01f)] float _projectileLifetime = 3f;
    [SerializeField] Vector3 _projectileSpawnOffset;

    [Header("----- 선택 발사 이펙트 -----")]
    [SerializeField] ParticleSystem _projectileLaunchEffectPrefab;

    public AreaEffectDefinition AreaEffect => _areaEffect;
    public AreaTrackingMode TrackingMode => _trackingMode;
    public AreaPlacementMode PlacementMode => _placementMode;
    public AreaDeliveryMode DeliveryMode => _deliveryMode;

    public float MaxPlacementDistance => _maxPlacementDistance;
    public LayerMask GroundMask => _groundMask;
    public float GroundProbeHeight => _groundProbeHeight;
    public float GroundProbeDepth => _groundProbeDepth;

    public TargetProjectile ProjectilePrefab => _projectilePrefab;
    public float ProjectileSpeed => _projectileSpeed;
    public float ProjectileLifetime => _projectileLifetime;
    public Vector3 ProjectileSpawnOffset => _projectileSpawnOffset;

    public ParticleSystem ProjectileLaunchEffectPrefab => _projectileLaunchEffectPrefab;
}
