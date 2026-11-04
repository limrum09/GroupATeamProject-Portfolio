using UnityEngine;

/// <summary>
/// Inspector에서 선택한 메타 프로필 저장소 생성
/// </summary>
public abstract class MetaProfileStoreProvider : MonoBehaviour
{
    /// <summary>
    /// 지정한 사용자에 연결된 저장소 생성
    /// </summary>
    /// <param name="userId">사용자 UID, 로컬 테스트에서는 null 허용</param>
    /// <returns>사용자에게 연결된 저장소</returns>
    public abstract IMetaProfileStore CreateStore(string userId);
}