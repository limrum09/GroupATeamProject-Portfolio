using System;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 로그인한 사용자의 Firestore 프로필 저장소 생성
/// </summary>
[DisallowMultipleComponent]
public class FirestoreMetaProfileStoreProvider : MetaProfileStoreProvider
{
    /// <summary>
    /// 로그인 UID에 연결된 서버 저장소 생성
    /// </summary>
    /// <param name="userId">프로필을 불러올 Firebase UID</param>
    /// <returns>해당 사용자의 Firestore 저장소</returns>
    public override IMetaProfileStore CreateStore(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Contains("/"))
            throw new ArgumentException("유효한 Firebase UID 필요", nameof(userId));

        FirebaseAuth auth = FirebaseAuth.DefaultInstance;
        FirebaseUser user = auth.CurrentUser;

        if (user == null || !string.Equals(user.UserId, userId, StringComparison.Ordinal))
            throw new InvalidOperationException("로그인한 사용자와 프로필 UID가 일치해야 합니다.");

        FirebaseFirestore firestore = FirebaseFirestore.GetInstance(auth.App);
        DocumentReference document = firestore.Collection("profiles").Document(userId);

        return new FirestoreMetaProfileStore(document);
    }
}