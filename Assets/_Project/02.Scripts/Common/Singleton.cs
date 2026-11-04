using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// 전용 루트 오브젝트를 사용하는 애플리케이션 수명의 싱글톤
/// 기존 인스턴스 우선 사용, 미배치 시 Resources/Singletons/타입명 프리팹 생성
/// 파생 클래스의 Awake와 OnDestroy 대신 전용 초기화 및 정리 훅 사용
/// </summary>
/// <typeparam name="T">자기 자신을 형식 인수로 지정한 싱글톤 타입</typeparam>
[DisallowMultipleComponent]
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    static T _instance;
    static int _session;
    static bool _resolving;
    static bool _initializing;
    static bool _destroying;
    static Exception _failure;

    int _registeredSession;
    int _rejectedSession;

    /// <summary>
    /// 사용 가능한 등록 인스턴스 존재 여부, 검색과 생성 없이 확인
    /// </summary>
    public static bool HasInstance => TryGetInstance(out _);

    /// <summary>
    /// 메인 스레드의 Play 모드에서 조회하거나 최초 생성하는 인스턴스
    /// 종료, 순환 초기화 또는 초기화 실패 상태에서는 예외 발생
    /// </summary>
    public static T Instance
    {
        get
        {
            SingletonRuntime.RequirePlayMode();
            SynchronizeSession();

            if (SingletonRuntime.IsQuitting || _destroying)
                throw new InvalidOperationException($"{typeof(T).Name}: 종료 중 Instance 접근 불가, TryGetInstance 사용 필요");

            ThrowIfFailed();

            if (_initializing)
                throw new InvalidOperationException($"{typeof(T).Name}: 싱글톤 초기화의 순환 호출");

            if (_instance != null)
                return _instance;

            if (_resolving)
                throw new InvalidOperationException($"{typeof(T).Name}: 싱글톤 생성의 순환 호출");

            return ResolveInstance();
        }
    }

    void Awake()
    {
        RegisterInstance();
    }

    void OnDestroy()
    {
        // 중복 오브젝트의 파괴가 실제 인스턴스에 영향을 주지 않도록 소유권 확인
        if (!ReferenceEquals(_instance, this))
            return;

        _destroying = true;

        try
        {
            OnSingletonDestroy();
        }
        catch (Exception exception)
        {
            // 불완전한 정리 이후 새 인스턴스 생성 차단
            _failure = exception;
            throw;
        }
        finally
        {
            _instance = null;
            _registeredSession = 0;
            _destroying = false;
        }
    }

    static void SynchronizeSession()
    {
        if (_session == SingletonRuntime.Session)
            return;

        // Domain Reload 비활성 상태의 이전 Play 정적 상태 제거
        _session = SingletonRuntime.Session;
        _instance = null;
        _resolving = false;
        _initializing = false;
        _destroying = false;
        _failure = null;
    }

    static void ThrowIfFailed()
    {
        if (_failure != null)
            throw new InvalidOperationException($"{typeof(T).Name}: 싱글톤 초기화 또는 정리 실패, 원인 수정 후 Play 재시작 필요", _failure);
    }

    static void ValidatePlacement(T candidate)
    {
        if (candidate.transform.parent != null)
            throw new InvalidOperationException($"{typeof(T).Name}: 독립된 루트 GameObject에 배치 필요");

        if (!candidate.gameObject.activeSelf || !candidate.enabled)
            throw new InvalidOperationException($"{typeof(T).Name}: GameObject와 싱글톤 컴포넌트의 활성 설정 필요");
    }

    static T CreateInstance()
    {
        string path = $"Singletons/{typeof(T).Name}";
        GameObject prefab = Resources.Load<GameObject>(path);

        if (prefab == null)
            throw new MissingReferenceException($"{typeof(T).Name}: Resources/{path}.prefab 설정 필요");

        T component = prefab.GetComponent<T>();

        if (component == null)
            throw new MissingReferenceException($"{typeof(T).Name}: 생성 프리팹 루트에 해당 싱글톤 컴포넌트 연결 필요");

        ValidatePlacement(component);

        GameObject created = Instantiate(prefab);
        created.name = typeof(T).Name;

        return created.GetComponent<T>();
    }

    static T ResolveInstance()
    {
        _resolving = true;

        try
        {
            // Awake 실행 전 배치된 오브젝트와 비활성 구성 오류까지 확인
            T[] candidates = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            T candidate = null;

            foreach (T existing in candidates)
            {
                if (existing._rejectedSession != _session)
                {
                    candidate = existing;
                    break;
                }
            }

            if (candidate == null)
                candidate = CreateInstance();

            // Instantiate의 Awake 실행 여부와 관계없이 동일한 등록 경로 사용
            candidate.RegisterInstance();
            ThrowIfFailed();

            if (!ReferenceEquals(_instance, candidate))
                throw new InvalidOperationException($"{typeof(T).Name}: 싱글톤 인스턴스 등록 실패");

            foreach (T existing in candidates)
            {
                if (!ReferenceEquals(existing, candidate))
                    existing.RejectDuplicate();
            }

            return _instance;
        }
        finally
        {
            _resolving = false;
        }
    }

    void RegisterInstance()
    {
        SingletonRuntime.RequirePlayMode();
        SynchronizeSession();

        if (_rejectedSession == _session)
            return;

        if (SingletonRuntime.IsQuitting || (_instance != null && !ReferenceEquals(_instance, this)))
        {
            RejectDuplicate();
            return;
        }

        ThrowIfFailed();

        if (_registeredSession == _session && ReferenceEquals(_instance, this))
            return;

        T candidate = (T)this;
        ValidatePlacement(candidate);

        _instance = candidate;
        _registeredSession = _session;
        _initializing = true;

        try
        {
            DontDestroyOnLoad(gameObject);
            OnSingletonAwake();
        }
        catch (Exception exception)
        {
            // 부분 초기화된 인스턴스 정리와 같은 Play 내 자동 재시도 차단
            _failure = exception;
            enabled = false;
            Destroy(gameObject);
            throw;
        }
        finally
        {
            _initializing = false;
        }
    }

    void RejectDuplicate()
    {
        if (_rejectedSession == _session)
            return;

        _rejectedSession = _session;
        enabled = false;
        Destroy(gameObject);
    }

    /// <summary>
    /// 인스턴스 소유권 획득 후 한 번 실행하는 동기 초기화
    /// 씬 배치 인스턴스는 다른 컴포넌트의 Awake 이전에도 호출 가능
    /// 같은 타입의 Instance 재접근과 상호 순환 의존 금지
    /// 게임 데이터 복원과 비동기 준비 완료는 파생 클래스의 별도 계약으로 관리
    /// </summary>
    protected virtual void OnSingletonAwake()
    {
    }

    /// <summary>
    /// 실제 소유 인스턴스 파괴 시 실행하는 자원 및 구독 정리
    /// 초기화 도중 실패한 경우에도 호출되므로 부분 초기화 상태 고려 필요
    /// </summary>
    protected virtual void OnSingletonDestroy()
    {
    }

    /// <summary>
    /// 검색이나 생성 없이 사용 가능한 등록 인스턴스 조회
    /// 종료 중에도 기존 인스턴스 조회 허용, 초기화 및 정리 중에는 실패 반환
    /// Play 시작 이후에는 메인 스레드에서 호출 필요
    /// </summary>
    /// <param name="instance">조회에 성공한 인스턴스, 실패 시 null</param>
    /// <returns>사용 가능한 등록 인스턴스 존재 여부</returns>
    public static bool TryGetInstance(out T instance)
    {
        instance = null;

        if (!SingletonRuntime.CanAccess())
            return false;

        SynchronizeSession();

        if (_instance == null || _initializing || _destroying || _failure != null)
            return false;

        instance = _instance;
        return true;
    }
}

// 제네릭 타입별 인스턴스와 분리한 Play 세션 및 종료 상태
static class SingletonRuntime
{
    static int _mainThreadId;

    public static int Session { get; private set; }
    public static bool IsQuitting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        Session++;
        IsQuitting = false;

        // Domain Reload 비활성 상태의 종료 이벤트 중복 등록 방지
        Application.quitting -= HandleQuitting;
        Application.quitting += HandleQuitting;
    }

    static void HandleQuitting()
    {
        IsQuitting = true;
    }

    /// <summary>
    /// Unity API 호출 전 메인 스레드 확인과 Play 상태 조회
    /// </summary>
    /// <returns>Play 모드의 메인 스레드에서 접근 가능한지 여부</returns>
    public static bool CanAccess()
    {
        if (_mainThreadId == 0)
            return false;

        if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            throw new InvalidOperationException("Unity 싱글톤은 메인 스레드에서 접근 필요");

        return Application.isPlaying;
    }

    /// <summary>
    /// 싱글톤 생성과 등록에 필요한 Play 모드 및 메인 스레드 확인
    /// </summary>
    public static void RequirePlayMode()
    {
        if (!CanAccess())
            throw new InvalidOperationException("Unity 싱글톤은 Play 모드에서 접근 필요");
    }
}
