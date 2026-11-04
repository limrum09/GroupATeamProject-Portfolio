using System;
using UnityEngine;

/// <summary>
/// 스킬의 조준 대상 선택 방식
/// </summary>
public enum SkillTargetMode
{
    None = 0,
    PreferAttackTargetThenNearest = 1
}

/// <summary>
/// 스킬의 공격 방향 결정 방식
/// </summary>
public enum SkillAimMode
{
    RequestedDirection = 0,
    CurrentForward = 1,
    SelectedTarget = 2
}

/// <summary>
/// 스킬 사용 시 캐릭터 회전 방식
/// </summary>
public enum SkillRotationMode
{
    FaceDirection,
    KeepFacing
}

/// <summary>
/// 플레이어 슬롯 스킬의 직업 제한
/// </summary>
public enum SkillCategory
{
    Public = 0,
    Knight = 1,
    Wizard = 2
}

/// <summary>
/// 한 단계 레벨업의 능력치 증가량
/// </summary>
[Serializable]
public struct SkillLevelGrowth
{
    [SerializeField, Min(0f)] float _upgradeCost;
    [SerializeField, Min(0f)] float _attackMultiplierIncrease;
    [SerializeField, Min(0f)] float _spellMultiplierIncrease;
    [SerializeField, Min(0f)] float _cooldownReduction;

    public float UpgradeCost => _upgradeCost;
    public float AttackMultiplierIncrease => _attackMultiplierIncrease;
    public float SpellMultiplierIncrease => _spellMultiplierIncrease;
    public float CooldownReduction => _cooldownReduction;

    internal SkillLevelGrowth(float upgradeCost, float attackMultiplierIncrease, float spellMultiplierIncrease, float cooldownReduction)
    {
        _upgradeCost = upgradeCost;
        _attackMultiplierIncrease = attackMultiplierIncrease;
        _spellMultiplierIncrease = spellMultiplierIncrease;
        _cooldownReduction = cooldownReduction;
    }

    internal void Validate()
    {
        if (!IsValid(_upgradeCost) || !IsValid(_attackMultiplierIncrease) || !IsValid(_spellMultiplierIncrease) || !IsValid(_cooldownReduction))
            throw new InvalidOperationException("스킬 성장값은 유한한 0 이상의 값 필요");
    }

    static bool IsValid(float value)
    {
        return value >= 0f && !float.IsInfinity(value);
    }
}

public abstract class SkillDefinition : ScriptableObject
{
    [Header("----- 정보 -----")]
    [SerializeField] string _skillUID;
    [SerializeField] Sprite _icon;
    [SerializeField] string _skillName;
    [SerializeField] string _skillDescription;
    [SerializeField] SkillCategory _category = SkillCategory.Public;

    [Header("----- 레벨별 성장 -----")]
    [SerializeField]
    SkillLevelGrowth[] _levelGrowth = Array.Empty<SkillLevelGrowth>();

    [Header("----- 사용 시간 -----")]
    [SerializeField, Min(0f)] float _castTime = 0.5f;
    [SerializeField, Min(0f)] float _cooldown = 4f;

    [Header("----- 모션 -----")]
    [SerializeField] AnimationClip motionClip;

    [Header("----- 대상 선택 -----")]
    [SerializeField] SkillTargetMode _targetMode = SkillTargetMode.None;
    [SerializeField, Min(0.1f)] float _range = 5f;
    [SerializeField] LayerMask _targetMask = ~0;

    [Header("----- 방향 -----")]
    [SerializeField] SkillAimMode _aimMode = SkillAimMode.CurrentForward;
    [SerializeField] SkillRotationMode _rotationMode = SkillRotationMode.FaceDirection;

    [Header("----- 선택 이펙트 -----")]
    [SerializeField] ParticleSystem _useEffectPrefab;
    [SerializeField] ParticleSystem _hitEffectPrefab;

    [Header("----- 이펙트 회전 보정 -----")]
    [SerializeField] Vector3 _useEffectRotation;
    [SerializeField] Vector3 _hitEffectRotation;

    [Header("----- 이펙트 기준점 -----")]
    [SerializeField] Vector3 _effectOffset;

    public string SkillUID => _skillUID;
    public float CastTime => _castTime;
    public float Cooldown => _cooldown;
    public AnimationClip MotionClip => motionClip;
    public SkillTargetMode TargetMode => _targetMode;
    public float Range => _range;
    public LayerMask TargetMask => _targetMask;
    public SkillAimMode AimMode => _aimMode;
    public SkillRotationMode RotationMode => _rotationMode;
    public ParticleSystem UseEffectPrefab => _useEffectPrefab;
    public ParticleSystem HitEffectPrefab => _hitEffectPrefab;
    public Vector3 UseEffectRotation => _useEffectRotation;
    public Vector3 HitEffectRotation => _hitEffectRotation;
    public Vector3 EffectOffset => _effectOffset;
    public int MaxLevel => _levelGrowth.Length + 1;
    public Sprite SkillIcon => _icon;
    public string SkillName => _skillName;
    public string SkillDescription => _skillDescription;

    protected virtual void OnValidate()
    {
        _castTime = Mathf.Max(0f, _castTime);
        _cooldown = Mathf.Max(0f, _cooldown);
        _range = Mathf.Max(0.1f, _range);
    }

    /// <summary>
    /// 지정 레벨까지 누적된 성장값 조회
    /// </summary>
    /// <param name="level">조회할 스킬 레벨</param>
    /// <returns>1레벨 기준 누적 증가량</returns>
    public SkillGrowthBonus GetGrowth(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level));

        float attackIncrease = 0f;
        float spellIncrease = 0f;
        float cooldownReduction = 0f;

        for (int i = 0; i < level - 1; i++)
        {
            SkillLevelGrowth growth = _levelGrowth[i];
            growth.Validate();

            attackIncrease += growth.AttackMultiplierIncrease;
            spellIncrease += growth.SpellMultiplierIncrease;
            cooldownReduction += growth.CooldownReduction;
        }

        return new SkillGrowthBonus(attackIncrease, spellIncrease, cooldownReduction);
    }

    /// <summary>
    /// 현재 레벨에서 다음 레벨로 강화하는 비용 조회
    /// </summary>
    /// <param name="currentLevel">강화 전 현재 스킬 레벨</param>
    /// <returns>다음 한 단계의 강화 비용</returns>
    public float GetUpgradeCost(int currentLevel)
    {
        if (currentLevel < 1 || currentLevel >= MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(currentLevel), "다음 강화 단계가 존재하는 현재 레벨 필요");

        SkillLevelGrowth nextGrowth = _levelGrowth[currentLevel - 1];
        nextGrowth.Validate();

        return nextGrowth.UpgradeCost;
    }

    /// <summary>
    /// 캐릭터의 스킬 장착 자격 확인
    /// </summary>
    /// <param name="character">장착할 캐릭터 정의</param>
    /// <returns>직업 제한에 따른 장착 가능 여부</returns>
    public bool IsAvailableTo(CharacterDefinition character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (_category == SkillCategory.Public)
            return true;

        if (!(character is PlayerDefinition player))
            return false;

        return (_category == SkillCategory.Knight && player.Class == PlayerClass.Knight) || (_category == SkillCategory.Wizard && player.Class == PlayerClass.Wizard);
    }

    /// <summary>
    /// 스킬 식별에 필요한 UID 설정 검사
    /// </summary>
    public void ValidateSkillUID()
    {
        if (string.IsNullOrWhiteSpace(_skillUID))
            throw new InvalidOperationException($"{name}: Skill UID 설정 필요");

    }
}