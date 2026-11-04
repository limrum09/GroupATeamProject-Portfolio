using UnityEngine;

/// <summary>정식 세션 초기화 연결 전 테스트 골드와 시작 스킬 구성</summary>
public sealed class ShopTestSetup : MonoBehaviour
{
    [Header("----- 테스트 연결 -----")]
    [SerializeField] SessionEconomyManager _economy;
    [SerializeField] SkillLoadout _actor;

    [Header("----- 테스트 값 -----")]
    [SerializeField, Min(0f)] float _startingGold = 200f;

    void Start()
    {
        _economy.ResetSession();
        _economy.AddGold(_startingGold, GoldGainSource.TestGrant);
    }

    [ContextMenu("테스트/골드 200 추가")]
    void AddTestGold()
    {
        RequirePlayMode();
        _economy.AddGold(200f, GoldGainSource.TestGrant);
    }

    [ContextMenu("테스트/첫 시작 스킬로 빈 슬롯 채우기")]
    void FillSlots()
    {
        RequirePlayMode();
    }

    [ContextMenu("테스트/R 슬롯 비우기")]
    void ClearLastSlot()
    {
        RequirePlayMode();
        int slot = _actor.SlotCount - 1;
        SkillInstance skill = _actor.GetSkill(slot);
        if (skill != null)
            _actor.TryRemoveSkill(slot, skill);
    }

    static void RequirePlayMode()
    {
        if (!Application.isPlaying)
            throw new System.InvalidOperationException("Play 중 실행 필요");
    }
}
