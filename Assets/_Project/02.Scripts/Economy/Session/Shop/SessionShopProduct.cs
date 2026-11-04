using System;
using UnityEngine;

/// <summary>스킬과 구매 가격으로 구성된 정적 상품</summary>
[Serializable]
public sealed class SessionShopProduct
{
    [SerializeField] SkillDefinition _skill;
    [SerializeField, Min(0f)] float _price;

    public SkillDefinition Skill => _skill;
    public float Price => _price;
}
