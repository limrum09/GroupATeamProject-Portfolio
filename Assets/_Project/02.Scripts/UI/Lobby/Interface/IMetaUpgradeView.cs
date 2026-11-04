using System;

/// <summary>
/// 메타 강화 화면의 입력과 표시 계약
/// </summary>
public interface IMetaUpgradeView
{
    int CatalogCount { get; }

    event Action<bool, int, bool> Clicked;
    event Action<bool, int> DragStarted;
    event Action<int> Dropped;
    event Action DragCanceled;
    event Action PurchaseRequested;
    event Action CloseRequested;

    /// <summary>
    /// 캐릭터 이름과 메타 재화 표시
    /// </summary>
    /// <param name="characterName">캐릭터 이름</param>
    /// <param name="balance">보유 재화</param>
    void ShowHeader(string characterName, long balance);

    /// <summary>
    /// 오른쪽 강화 항목의 구매 단계와 선택 상태 표시
    /// </summary>
    /// <param name="index">목록 인덱스</param>
    /// <param name="rank">구매 단계 문자열</param>
    /// <param name="equipped">현재 캐릭터의 장착 여부</param>
    /// <param name="selected">상세 정보 선택 여부</param>
    void ShowCatalog(int index, string rank, bool equipped, bool selected);

    /// <summary>
    /// 왼쪽 장착 슬롯 표시
    /// </summary>
    /// <param name="slot">슬롯 번호</param>
    /// <param name="catalogIndex">목록 인덱스, 빈 슬롯은 -1</param>
    /// <param name="selected">선택 표시 여부</param>
    void ShowSlot(int slot, int catalogIndex, bool selected);

    /// <summary>
    /// 선택한 강화의 상세 정보 표시
    /// </summary>
    /// <param name="catalogIndex">아이콘을 가져올 목록 인덱스, 없으면 -1</param>
    /// <param name="upgradeName">강화 이름</param>
    /// <param name="rank">구매 단계 문자열</param>
    /// <param name="description">효과 설명</param>
    /// <param name="price">구매 비용 문자열</param>
    void ShowDetails(int catalogIndex, string upgradeName, string rank, string description, string price);

    /// <summary>
    /// 입력 허용 상태와 구매 버튼 문구 표시
    /// </summary>
    /// <param name="inspect">항목 선택과 새 드래그 허용 여부</param>
    /// <param name="purchase">구매 버튼 허용 여부</param>
    /// <param name="close">닫기 버튼 허용 여부</param>
    /// <param name="purchaseLabel">구매 버튼 문구</param>
    void SetInteraction(bool inspect, bool purchase, bool close, string purchaseLabel);

    /// <summary>
    /// 계정 공통 통합 숙련도 레벨 표시
    /// </summary>
    /// <param name="level">1부터 시작하는 통합 레벨</param>
    void ShowIntegratedLevel(long level);

    /// <summary>
    /// 강화 이용 제한 조건 표시
    /// </summary>
    /// <param name="restriction">제한 문구, 제한이 없으면 빈 문자열</param>
    void ShowRestriction(string restriction);

    /// <summary>
    /// 통합 레벨과 선택 캐릭터의 숙련도 진행 표시
    /// </summary>
    /// <param name="integratedLevel">계정 공통 통합 레벨</param>
    /// <param name="characterLevel">선택 캐릭터의 숙련도 레벨</param>
    /// <param name="currentExperience">현재 캐릭터 레벨 구간의 획득 숙련도</param>
    /// <param name="requiredExperience">다음 레벨까지 필요한 숙련도, 최대 레벨은 0</param>
    void ShowMastery(int integratedLevel, int characterLevel, long currentExperience, long requiredExperience);

    /// <summary>
    /// 현재 드래그 포인터에 강화 아이콘 표시
    /// </summary>
    /// <param name="catalogIndex">아이콘을 가져올 목록 인덱스</param>
    void ShowDragIcon(int catalogIndex);

    /// <summary>
    /// 취소 이벤트를 다시 발생시키지 않고 드래그 정리
    /// </summary>
    void CancelDrag();

    /// <summary>
    /// 준비되지 않은 화면의 표시 초기화
    /// </summary>
    void Clear();
}