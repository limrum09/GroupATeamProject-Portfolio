using System;
using System.Threading;
using Firebase;
using Firebase.Auth;
using UnityEngine;

/// <summary>
/// Firebase 준비와 로그인 및 최초 로비 진입 순서 조율
/// </summary>
[DisallowMultipleComponent]
public class GameBootstrap : MonoBehaviour
{
    [Header("----- 흐름 연결 -----")]
    [SerializeField] SceneFlowController _sceneFlow;

    [Header("----- Google 데스크톱 OAuth -----")]
    [SerializeField] string _googleClientId;
    [SerializeField] string _googleClientSecret;

    readonly FirebaseInitializer _firebaseInitializer = new FirebaseInitializer();

    FirebaseAuthService _authService;
    GoogleDesktopSignIn _googleSignIn;

    CancellationTokenSource _lifetimeCancellation;
    CancellationToken _lifetimeToken;

    bool _ready;
    bool _busy = true;
    bool _lobbyRequested;

    bool _backgroundOverridden;
    bool _previousRunInBackground;

    public event Action OnStateChanged;

    public string StatusMessage { get; private set; } = "Firebase 준비 중...";

    /// <summary>
    /// 준비 완료 후 중복 요청 없이 로그인을 시작할 수 있는 상태
    /// </summary>
    public bool CanRequestGoogleLogin => isActiveAndEnabled && _ready && !_busy && !_lobbyRequested;

    void Awake()
    {
        if (transform.parent != null)
            throw new InvalidOperationException($"{name}: 독립된 루트 오브젝트 필요");

        if (_sceneFlow == null)
            throw new MissingReferenceException($"{name}: SceneFlowController 연결 필요");

        if (_sceneFlow.IsCharacterTestMode)
            throw new InvalidOperationException($"{name}: 로그인 실행에서는 캐릭터 테스트 모드 해제 필요");

        if (string.IsNullOrWhiteSpace(_googleClientId))
            throw new InvalidOperationException($"{name}: Google 데스크톱 클라이언트 ID 필요");

        _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken, Application.exitCancellationToken);
        _lifetimeToken = _lifetimeCancellation.Token;

        // 씬 전환 이후에도 Firebase 앱과 인증 서비스의 수명 유지
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        try
        {
            _sceneFlow.Initialize();

            FirebaseApp app = await _firebaseInitializer.InitializeAsync();

            _lifetimeToken.ThrowIfCancellationRequested();

            _authService = new FirebaseAuthService(app);
            _googleSignIn = new GoogleDesktopSignIn(_googleClientId, _googleClientSecret);

            _ready = true;
            StatusMessage = "Google 로그인 버튼을 눌러주세요.";
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusMessage = $"시작 준비 실패: {exception.Message}";
            Debug.LogException(exception, this);
        }
        finally
        {
            _busy = false;
        }

        NotifyStateChanged();
    }

    void OnDestroy()
    {
        if (_lifetimeCancellation != null)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        }

        RestoreBackgroundSetting();
    }

    void NotifyStateChanged()
    {
        if (!_lifetimeToken.IsCancellationRequested)
            OnStateChanged?.Invoke();
    }

    void RestoreBackgroundSetting()
    {
        if (!_backgroundOverridden)
            return;

        Application.runInBackground = _previousRunInBackground;
        _backgroundOverridden = false;
    }

    /// <summary>
    /// 버튼 요청에 따른 Google 로그인과 최초 로비 진입
    /// </summary>
    public async void RequestGoogleLogin()
    {
        if (!CanRequestGoogleLogin)
            return;

        _busy = true;
        StatusMessage = "로그인 및 게임 데이터 준비 중...";

        _previousRunInBackground = Application.runInBackground;
        _backgroundOverridden = true;

        // 브라우저에 포커스가 있어도 로그인 작업 진행
        Application.runInBackground = true;

        try
        {
            NotifyStateChanged();

            string googleIdToken = await _googleSignIn.SignInAsync(_lifetimeToken);

            _lifetimeToken.ThrowIfCancellationRequested();

            FirebaseUser user = await _authService.SignInWithGoogleAsync(googleIdToken);

            _lifetimeToken.ThrowIfCancellationRequested();

            bool initialized = await _sceneFlow.EnsureInitializedAsync(retryFailed: true, userId: user.UserId);

            _lifetimeToken.ThrowIfCancellationRequested();

            if (!initialized)
                throw new InvalidOperationException("사용자 프로필 준비 실패. Console의 초기화 오류 확인 필요");

            if (!_sceneFlow.TryReturnToMetaScene())
                throw new InvalidOperationException("현재 로비 전환 요청을 받을 수 없는 상태");

            _lobbyRequested = true;
            StatusMessage = "로비로 이동 중...";
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "로그인이 취소되었습니다. 다시 시도하세요.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"로그인 또는 진입 준비 실패: {exception.Message}";

            Debug.LogException(exception, this);
        }
        finally
        {
            _busy = false;
            RestoreBackgroundSetting();
        }

        NotifyStateChanged();
    }
}