using UnityEngine;

/// <summary>확정된 스킬 적중 정보</summary>
public readonly struct SkillImpactInfo
{
    public long ActionId { get; }
    // 적중에 사용한 스킬, 스킬 없는 기본 공격이면 null
    public SkillDefinition Skill { get; }
    public CombatTarget Target { get; }
    public DamageRequest Damage { get; }
    public CombatSource Source => Damage.Source;
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    // 직접 지정한 대상의 적중 여부, Area와 주변 피해는 false
    public bool IsPrimaryTarget { get; }

    internal SkillImpactInfo(long actionId, SkillDefinition skill, CombatTarget target, DamageRequest damage, Vector3 position, Quaternion rotation, bool isPrimaryTarget)
    {
        ActionId = actionId;
        Skill = skill;
        Target = target;
        Damage = damage;
        Position = position;
        Rotation = rotation;
        IsPrimaryTarget = isPrimaryTarget;
    }
}