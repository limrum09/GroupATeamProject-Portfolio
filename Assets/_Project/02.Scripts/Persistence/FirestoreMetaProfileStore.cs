using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;

/// <summary>
/// 사용자 한 명의 Firestore 프로필 읽기와 변경 필드 저장
/// </summary>
public class FirestoreMetaProfileStore : IMetaProfileStore
{
    readonly DocumentReference _document;

    // null은 아직 서버에서 프로필 유무를 확인하지 못한 상태
    bool? _documentExists;

    async Task CreateProfileAsync(Dictionary<string, object> fields)
    {
        await _document.Firestore.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot current = await transaction.GetSnapshotAsync(_document);

            if (!current.Exists)
            {
                transaction.Set(_document, fields, SetOptions.Overwrite);
                return;
            }

            // 최초 쓰기의 응답만 놓친 경우 동일한 초기값을 성공으로 처리
            if (FirestoreMetaProfileData.HasSameFields(current.ToDictionary(), fields))
                return;

            throw new InvalidOperationException("다른 실행에서 프로필을 먼저 생성했습니다. 게임을 다시 시작해 주세요.");
        });
    }

    /// <summary>
    /// 사용자 프로필 문서 연결
    /// </summary>
    /// <param name="document">로그인한 사용자의 프로필 문서</param>
    public FirestoreMetaProfileStore(DocumentReference document)
    {
        _document = document
            ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// 서버에 기록된 프로필과 읽기 결과 반환
    /// </summary>
    /// <returns>정상, 문서 없음, 읽기 실패 또는 잘못된 데이터 결과</returns>
    public async Task<MetaProfileLoadResult> LoadAsync()
    {
        _documentExists = null;

        DocumentSnapshot snapshot;

        try
        {
            snapshot = await _document.GetSnapshotAsync(Source.Server);
        }
        catch (Exception exception)
        {
            return new MetaProfileLoadResult(MetaProfileLoadStatus.ReadFailed, message: exception.Message);
        }

        if (snapshot.Metadata.HasPendingWrites)
        {
            return new MetaProfileLoadResult(MetaProfileLoadStatus.ReadFailed, message: "서버에 확정되지 않은 프로필 쓰기가 있습니다.");
        }

        if (!snapshot.Exists)
        {
            _documentExists = false;
            return new MetaProfileLoadResult(MetaProfileLoadStatus.Missing);
        }

        try
        {
            MetaProfileData profile = FirestoreMetaProfileData.ReadProfile(snapshot.ToDictionary());

            _documentExists = true;
            return new MetaProfileLoadResult(MetaProfileLoadStatus.Loaded, profile);
        }
        catch (Exception exception)
        {
            return new MetaProfileLoadResult(MetaProfileLoadStatus.InvalidData, message: exception.Message);
        }
    }

    /// <summary>
    /// 최초 프로필 생성 또는 전달받은 변경 필드의 동시 기록
    /// </summary>
    /// <param name="changes">저장 조율자가 고정한 변경 데이터</param>
    /// <returns>요청한 쓰기의 서버 완료 여부</returns>
    public async Task<bool> SaveAsync(MetaSaveChanges changes)
    {
        if (changes == null)
            throw new ArgumentNullException(nameof(changes));

        if (!_documentExists.HasValue)
            throw new InvalidOperationException("서버 프로필 읽기 완료 필요");

        if (changes.Parts == MetaSavePart.None)
            return true;

        Dictionary<string, object> fields = FirestoreMetaProfileData.ToFields(changes);

        if (!_documentExists.Value)
        {
            if (changes.Parts != MetaSavePart.All)
                throw new InvalidOperationException("최초 프로필은 전체 항목 필요");

            await CreateProfileAsync(fields);
            _documentExists = true;
        }
        else
        {
            // 수령 상태와 잔액처럼 함께 전달된 필드를 한 번에 기록
            await _document.UpdateAsync(fields);
        }

        return true;
    }
}