using System;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 로그인한 사용자의 Firestore 테스트 문서 서버 읽기 확인
/// </summary>
[DisallowMultipleComponent]
public class FirestoreConnectionCheck : MonoBehaviour
{
#if UNITY_EDITOR
    bool _isReading;

    [ContextMenu("테스트/Firestore 서버 읽기")]
    async void ReadFromServer()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Play 모드에서 로그인 후 실행하세요.", this);
            return;
        }

        if (_isReading)
            return;

        _isReading = true;

        try
        {
            // GameBootstrap의 Firebase 초기화와 로그인 완료 후 사용
            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser user = auth.CurrentUser;

            if (user == null)
                throw new InvalidOperationException("먼저 Google 로그인 완료 필요");

            FirebaseFirestore firestore = FirebaseFirestore.GetInstance(auth.App);

            DocumentReference document = firestore.Collection("connectionChecks").Document(user.UserId);

            Debug.Log($"[Firestore] 서버 읽기 요청: {document.Path}", this);

            DocumentSnapshot snapshot = await document.GetSnapshotAsync(Source.Server);

            // 대기 중 Play 종료 또는 확인용 컴포넌트 제거 시 후속 출력 중단
            if (this == null || !Application.isPlaying)
                return;

            if (!snapshot.Exists)
            {
                Debug.LogWarning($"[Firestore] 서버 응답 수신, 문서 없음: {document.Path}", this);
                return;
            }

            if (!snapshot.TryGetValue<string>("message", out string message) || message == null)
                throw new InvalidOperationException("테스트 문서에 message 문자열 필드 필요");

            Debug.Log($"[Firestore] 서버 읽기 성공\n" + $"문서: {document.Path}\n" + $"message: {message}", this);
        }
        catch (Exception exception)
        {
            if (this != null && Application.isPlaying)
                Debug.LogException(exception, this);
        }
        finally
        {
            _isReading = false;
        }
    }
#endif
}