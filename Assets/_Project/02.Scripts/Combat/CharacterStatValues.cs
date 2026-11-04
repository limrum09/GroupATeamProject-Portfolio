using System;
using UnityEngine;

/// <summary>
/// 캐릭터 스탯 8종의 식별자
/// </summary>
public enum CharacterStatType
{
    MaxHealth = 0,
    AttackPower = 1,
    Defense = 2,
    SpellPower = 3,
    MoveSpeed = 4,
    AttackSpeed = 5,
    CriticalChance = 6,
    CriticalDamageMultiplier = 7
}

/// <summary>
/// 캐릭터 스탯과 성장 기여분에 사용하는 8종 값 묶음
/// </summary>
[Serializable]
public struct CharacterStatValues
{
    [Header("----- 스탯 값 -----")]
    [SerializeField, Min(0f)] float _maxHealth;
    [SerializeField, Min(0f)] float _attackPower;
    [SerializeField, Min(0f)] float _defense;
    [SerializeField, Min(0f)] float _spellPower;
    [SerializeField, Min(0f)] float _moveSpeed;
    [SerializeField, Min(0f)] float _attackSpeed;
    [SerializeField, Min(0f)] float _criticalChance;
    [SerializeField, Min(0f)] float _criticalDamageMultiplier;

    public float MaxHealth => _maxHealth;
    public float AttackPower => _attackPower;
    public float Defense => _defense;
    public float SpellPower => _spellPower;
    public float MoveSpeed => _moveSpeed;
    public float AttackSpeed => _attackSpeed;
    public float CriticalChance => _criticalChance;
    public float CriticalDamageMultiplier => _criticalDamageMultiplier;


    static void ValidateValue(float value, string statName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new InvalidOperationException($"{statName}: 유한한 0 이상의 스탯 값 필요");
    }

    /// <summary>
    /// 유한한 0 이상의 값으로 스탯 묶음 구성
    /// </summary>
    /// <param name="maxHealth">최대 체력 값</param>
    /// <param name="attackPower">공격력 값</param>
    /// <param name="defense">방어력 값</param>
    /// <param name="spellPower">주문력 값</param>
    /// <param name="moveSpeed">이동 속도 값</param>
    /// <param name="attackSpeed">초당 공격 횟수 기준 공격 속도 값</param>
    /// <param name="criticalChance">확률 단위의 치명타 확률 값</param>
    /// <param name="criticalDamageMultiplier">치명타 피해 배율 값</param>
    public CharacterStatValues(float maxHealth, float attackPower, float defense, float spellPower, float moveSpeed, float attackSpeed, float criticalChance, float criticalDamageMultiplier)
    {
        _maxHealth = maxHealth;
        _attackPower = attackPower;
        _defense = defense;
        _spellPower = spellPower;
        _moveSpeed = moveSpeed;
        _attackSpeed = attackSpeed;
        _criticalChance = criticalChance;
        _criticalDamageMultiplier = criticalDamageMultiplier;

        Validate();
    }

    /// <summary>모든 스탯 값의 유한성과 음수 여부 검사</summary>
    public void Validate()
    {
        ValidateValue(_maxHealth, nameof(MaxHealth));
        ValidateValue(_attackPower, nameof(AttackPower));
        ValidateValue(_defense, nameof(Defense));
        ValidateValue(_spellPower, nameof(SpellPower));
        ValidateValue(_moveSpeed, nameof(MoveSpeed));
        ValidateValue(_attackSpeed, nameof(AttackSpeed));
        ValidateValue(_criticalChance, nameof(CriticalChance));
        ValidateValue(_criticalDamageMultiplier, nameof(CriticalDamageMultiplier));
    }

    /// <summary>
    /// 식별자에 해당하는 스탯 값 조회
    /// </summary>
    /// <param name="stat">조회할 스탯 식별자</param>
    /// <returns>해당 스탯의 값</returns>
    public float GetValue(CharacterStatType stat)
    {
        switch (stat)
        {
            case CharacterStatType.MaxHealth:
                return _maxHealth;
            case CharacterStatType.AttackPower:
                return _attackPower;
            case CharacterStatType.Defense:
                return _defense;
            case CharacterStatType.SpellPower:
                return _spellPower;
            case CharacterStatType.MoveSpeed:
                return _moveSpeed;
            case CharacterStatType.AttackSpeed:
                return _attackSpeed;
            case CharacterStatType.CriticalChance:
                return _criticalChance;
            case CharacterStatType.CriticalDamageMultiplier:
                return _criticalDamageMultiplier;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(stat), stat, "지원하는 스탯 식별자 필요");
        }
    }

    /// <summary>
    /// 원본을 유지한 두 스탯 묶음의 항목별 합산
    /// </summary>
    /// <param name="other">더할 스탯 묶음</param>
    /// <returns>항목별 합산 결과</returns>
    public CharacterStatValues Add(CharacterStatValues other)
    {
        Validate();
        other.Validate();

        return new CharacterStatValues(_maxHealth + other.MaxHealth, _attackPower + other.AttackPower, _defense + other.Defense, _spellPower + other.SpellPower, _moveSpeed + other.MoveSpeed, _attackSpeed + other.AttackSpeed, _criticalChance + other.CriticalChance, _criticalDamageMultiplier + other.CriticalDamageMultiplier);
    }

    /// <summary>
    /// 원본을 유지한 지정 스탯의 증가량 합산
    /// </summary>
    /// <param name="stat">값을 더할 스탯 식별자</param>
    /// <param name="amount">유한한 0 이상의 증가량</param>
    /// <returns>지정 스탯에 증가량을 더한 스탯 묶음</returns>
    public CharacterStatValues Add(CharacterStatType stat, float amount)
    {
        Validate();
        ValidateValue(amount, nameof(amount));

        CharacterStatValues result = this;

        switch (stat)
        {
            case CharacterStatType.MaxHealth:
                result._maxHealth += amount;
                break;
            case CharacterStatType.AttackPower:
                result._attackPower += amount;
                break;
            case CharacterStatType.Defense:
                result._defense += amount;
                break;
            case CharacterStatType.SpellPower:
                result._spellPower += amount;
                break;
            case CharacterStatType.MoveSpeed:
                result._moveSpeed += amount;
                break;
            case CharacterStatType.AttackSpeed:
                result._attackSpeed += amount;
                break;
            case CharacterStatType.CriticalChance:
                result._criticalChance += amount;
                break;
            case CharacterStatType.CriticalDamageMultiplier:
                result._criticalDamageMultiplier += amount;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stat), stat, "지원하는 스탯 식별자 필요");
        }

        // 합산으로 발생한 무한대 등 유효하지 않은 결과 검사
        result.Validate();

        return result;
    }

    /// <summary>
    /// 원본을 유지한 모든 스탯 항목의 배수 계산
    /// </summary>
    /// <param name="multiplier">유한한 0 이상의 배수</param>
    /// <returns>항목별 배수 적용 결과</returns>
    public CharacterStatValues Multiply(float multiplier)
    {
        Validate();

        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "유한한 0 이상의 배수 필요");

        return new CharacterStatValues(_maxHealth * multiplier, _attackPower * multiplier, _defense * multiplier, _spellPower * multiplier, _moveSpeed * multiplier, _attackSpeed * multiplier, _criticalChance * multiplier, _criticalDamageMultiplier * multiplier);
    }
}