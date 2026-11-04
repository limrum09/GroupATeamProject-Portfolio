using UnityEngine;

/// <summary>구매 스킬의 월드 드롭 생성</summary>
public sealed class SkillDropFactory : MonoBehaviour
{
    [Header("----- 드롭 설정 -----")]
    [SerializeField] SkillPickup _prefab;
    [SerializeField] Transform _dropRoot;

    void Awake()
    {
        if (_prefab == null || _dropRoot == null)
            throw new MissingReferenceException(
                $"{name}: 드롭 프리팹과 세션 드롭 루트 필요");

        if (_prefab.gameObject.activeSelf)
            throw new System.InvalidOperationException(
                "드롭 프리팹은 비활성 상태로 저장 필요");
    }

    /// <summary>스킬과 소유자를 연결한 비활성 드롭 생성</summary>
    /// <param name="skill">구매한 스킬 정의</param>
    /// <param name="owner">획득 가능한 플레이어</param>
    /// <param name="position">드롭 월드 위치</param>
    /// <returns>활성화 전 드롭 객체</returns>
    public SkillPickup CreateInactive(
        SkillDefinition skill,
        SkillLoadout owner,
        Vector3 position)
    {
        SkillPickup pickup = Instantiate(
            _prefab,
            position,
            Quaternion.identity,
            _dropRoot);

        pickup.Init(skill, owner);
        return pickup;
    }
}
