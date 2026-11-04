/// <summary>플레이어의 월드 상호작용 계약</summary>
public interface IInteractable
{
    /// <summary>현재 상호작용 가능 여부 확인</summary>
    /// <param name="actor">요청 플레이어의 스킬 구성</param>
    /// <returns>상호작용 가능 여부</returns>
    bool CanInteract(SkillLoadout actor);

    /// <summary>접근 조건을 재검사한 상호작용 실행</summary>
    /// <param name="actor">요청 플레이어의 스킬 구성</param>
    /// <returns>상호작용 성공 여부</returns>
    bool TryInteract(SkillLoadout actor);
}
