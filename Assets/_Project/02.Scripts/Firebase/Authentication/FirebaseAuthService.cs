using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;

/// <summary>
/// Google 인증 정보를 이용한 Firebase 로그인과 인증 상태 접근
/// </summary>
public class FirebaseAuthService
{
    readonly FirebaseAuth _auth;

    bool _isSigningIn;

    public FirebaseUser CurrentUser => _auth.CurrentUser;
    public bool IsSigningIn => _isSigningIn;
    // SDK에 캐시된 로그인 사용자의 존재 여부
    public bool IsSignedIn => _auth.CurrentUser != null;

    /// <summary>
    /// 초기화가 완료된 Firebase 앱의 인증 서비스 구성
    /// </summary>
    /// <param name="app">FirebaseInitializer에서 준비한 Firebase 앱</param>
    public FirebaseAuthService(FirebaseApp app)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        _auth = FirebaseAuth.GetAuth(app);
    }

    /// <summary>
    /// Unity 메인 스레드에서 Google ID 토큰을 이용한 Firebase 로그인
    /// </summary>
    /// <param name="googleIdToken">GoogleDesktopSignIn에서 받은 Google ID 토큰</param>
    /// <returns>Firebase 로그인을 완료한 사용자</returns>
    public async Task<FirebaseUser> SignInWithGoogleAsync(string googleIdToken)
    {
        if (string.IsNullOrWhiteSpace(googleIdToken))
            throw new ArgumentException("Google ID 토큰 필요", nameof(googleIdToken));

        if (_isSigningIn)
            throw new InvalidOperationException("이미 Firebase 로그인 진행 중");

        _isSigningIn = true;

        try
        {
            using Credential credential = GoogleAuthProvider.GetCredential(googleIdToken, null);

            FirebaseUser user = await _auth.SignInWithCredentialAsync(credential);

            if (user == null)
                throw new InvalidOperationException("Firebase 로그인 사용자 누락");

            return user;
        }
        finally
        {
            // 로그인 실패 시에도 다음 요청을 받을 수 있도록 진행 상태 해제
            _isSigningIn = false;
        }
    }

    /// <summary>
    /// Unity 메인 스레드에서 현재 Firebase 사용자 로그아웃
    /// </summary>
    public void SignOut()
    {
        if (_isSigningIn)
        {
            throw new InvalidOperationException("Firebase 로그인 요청 완료 후 로그아웃 필요");
        }

        _auth.SignOut();
    }
}