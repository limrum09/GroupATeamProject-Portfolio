using System.Threading.Tasks;

/// <summary>
/// 로컬과 서버가 공유하는 메타 프로필 저장소 계약
/// </summary>
public interface IMetaProfileStore
{
    /// <summary>
    /// 저장된 프로필을 공통 데이터 형식으로 반환
    /// </summary>
    /// <returns>프로필 데이터와 불러오기 결과</returns>
    Task<MetaProfileLoadResult> LoadAsync();

    /// <summary>
    /// 전달된 항목만 함께 저장하고 나머지 항목 유지
    /// 동일 데이터 재시도 시 게임 효과의 추가 적용 없이 같은 값 기록
    /// </summary>
    /// <param name="changes">저장할 항목과 고정된 데이터</param>
    /// <returns>요청한 모든 항목의 저장 완료 확인 여부</returns>
    Task<bool> SaveAsync(MetaSaveChanges changes);
}