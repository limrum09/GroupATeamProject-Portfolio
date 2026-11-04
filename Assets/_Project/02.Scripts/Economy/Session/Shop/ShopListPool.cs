using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>상점 상품 후보 보관과 조건별 추첨</summary>
[CreateAssetMenu(
    menuName = "Project/Economy/Shop List Pool",
    fileName = "ShopListPool")]
public sealed class ShopListPool : ScriptableObject
{
    [Header("----- 상품 후보 -----")]
    [SerializeField] SessionShopProduct[] _products = Array.Empty<SessionShopProduct>();

    /// <summary>
    /// 월드 상품 추첨과 독립적인 카탈로그 기준 구매 가격 조회
    /// </summary>
    /// <param name="skill">조회할 스킬 정의</param>
    /// <param name="price">등록된 구매 가격</param>
    /// <returns>카탈로그 등록 여부</returns>
    public bool TryGetPrice(SkillDefinition skill, out float price)
    {
        foreach (SessionShopProduct product in _products)
        {
            if (product.Skill != skill) continue;
            price = product.Price;
            return true;
        }
        price = 0f;
        return false;
    }

    /// <summary>조건을 만족하는 상품의 중복 없는 무작위 추첨</summary>
    /// <param name="count">요청 상품 수</param>
    /// <param name="isEligible">추가 조건, null이면 추가 제한 없음</param>
    /// <returns>후보 수 이내의 읽기 전용 상품 목록</returns>
    public IReadOnlyList<SessionShopProduct> GetRandomProducts(
        int count,
        Predicate<SkillDefinition> isEligible = null)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        List<SessionShopProduct> candidates = new List<SessionShopProduct>();
        HashSet<SkillDefinition> registered = new HashSet<SkillDefinition>();

        foreach (SessionShopProduct product in _products)
        {
            if (product == null || product.Skill == null)
                throw new InvalidOperationException("상점 상품의 스킬 설정 필요");

            if (float.IsNaN(product.Price) ||
                float.IsInfinity(product.Price) ||
                product.Price < 0f)
            {
                throw new InvalidOperationException("유효한 상품 가격 필요");
            }

            if (!registered.Add(product.Skill))
                throw new InvalidOperationException("상품 풀의 중복 스킬 등록");

            if (isEligible != null && !isEligible(product.Skill))
                continue;

            candidates.Add(product);
        }

        int resultCount = Mathf.Min(count, candidates.Count);

        for (int i = 0; i < resultCount; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, candidates.Count);

            SessionShopProduct temporary = candidates[i];
            candidates[i] = candidates[randomIndex];
            candidates[randomIndex] = temporary;
        }

        return candidates.GetRange(0, resultCount).AsReadOnly();
    }
}
