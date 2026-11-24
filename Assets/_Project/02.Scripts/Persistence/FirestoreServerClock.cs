using System;
using System.Threading.Tasks;
using Firebase.Firestore;

/// <summary>
/// 프로필 문서의 서버 타임스탬프 기록을 통한 Firebase 서버 시각 조회
/// </summary>
public class FirestoreServerClock : IServerClock
{
    const string ServerTimeField = "serverTime";

    readonly DocumentReference _document;

    /// <summary>
    /// 서버 시각을 기록할 사용자 프로필 문서 연결
    /// </summary>
    /// <param name="document">로그인한 사용자의 프로필 문서</param>
    public FirestoreServerClock(DocumentReference document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// 서버가 기록한 현재 시각 조회
    /// </summary>
    /// <returns>Firebase 서버 기준 UTC 시각</returns>
    public async Task<DateTime> GetUtcNowAsync()
    {
        // 기기 시각 배제를 위한 서버 타임스탬프 기록 후 서버 재조회
        await _document.UpdateAsync(ServerTimeField, FieldValue.ServerTimestamp);

        DocumentSnapshot snapshot = await _document.GetSnapshotAsync(Source.Server);

        return snapshot.GetValue<Timestamp>(ServerTimeField).ToDateTime();
    }
}
