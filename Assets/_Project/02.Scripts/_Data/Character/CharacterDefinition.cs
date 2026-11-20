using UnityEngine;

[CreateAssetMenu(
    menuName = "Project/Character Definition",
    fileName = "_CharacterDefinition")]
public class CharacterDefinition : ScriptableObject
{
    [Header("----- 기본 스탯 -----")]
    [SerializeField, Min(1f)] protected float _maxHealth = 100f;
    [SerializeField, Min(0f)] protected float _attackPower = 10f;
    [SerializeField, Min(0f)] protected float _defense = 0f;
    [SerializeField, Min(0f)] protected float _spellPower = 0f;
    [SerializeField, Min(0.1f)] protected float _moveSpeed = 5f;
    [SerializeField, Min(0.01f)] protected float _baseAttackSpeed = 1f / 0.6f;
    [SerializeField, Range(0f, 1f)] protected float _criticalChance = 0f;
    [SerializeField, Min(1f)] protected float _criticalDamageMultiplier = 2f;

    [Header("----- 기본 공격 설정 -----")]
    [SerializeField, Min(0.1f)] float _attackRange = 2f;
    [Tooltip("공격 주기 중 타격이 발생하는 시점. 0.3이면 30% 지점")]
    [SerializeField, Range(0f, 1f)] float _attackPointRatio = 1f / 3f;

    [Header("----- 고정 기본 공격 콤보 -----")]
    [SerializeField] BasicAttackComboDefinition _basicAttackCombo;

    public float MaxHealth => _maxHealth;
    public float AttackPower => _attackPower;
    public float Defense => _defense;
    public float SpellPower => _spellPower;
    public float MoveSpeed => _moveSpeed;
    public float BaseAttackSpeed => _baseAttackSpeed;
    public float CriticalChance => _criticalChance;
    public float CriticalDamageMultiplier => _criticalDamageMultiplier;
    public float AttackRange => _attackRange;
    public float AttackPointRatio => _attackPointRatio;
    public BasicAttackComboDefinition BasicAttackCombo => _basicAttackCombo;

    /// <summary>
    /// 지정 스킬의 고정 기본 공격 콤보 포함 여부
    /// </summary>
    /// <param name="skill">확인할 스킬 정의</param>
    /// <returns>콤보의 어느 단계에 포함되면 true</returns>
    public bool IsBasicAttackSkill(SkillDefinition skill)
    {
        return _basicAttackCombo != null && _basicAttackCombo.Contains(skill);
    }

    void OnValidate()
    {
        _maxHealth = Mathf.Max(1f, _maxHealth);
        _attackPower = Mathf.Max(0f, _attackPower);
        _defense = Mathf.Max(0f, _defense);
        _spellPower = Mathf.Max(0f, _spellPower);
        _moveSpeed = Mathf.Max(0.1f, _moveSpeed);
        _baseAttackSpeed = Mathf.Max(0.01f, _baseAttackSpeed);
        _criticalChance = Mathf.Clamp01(_criticalChance);
        _criticalDamageMultiplier = Mathf.Max(1f, _criticalDamageMultiplier);

        _attackRange = Mathf.Max(0.1f, _attackRange);
        _attackPointRatio = Mathf.Clamp01(_attackPointRatio);
    }
}
