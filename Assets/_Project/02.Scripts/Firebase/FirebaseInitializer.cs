using System;
using System.Threading.Tasks;
using Firebase;

/// <summary>
/// Firebase SDK 의존성 준비와 기본 앱 초기화
/// </summary>
public class FirebaseInitializer
{
    Task<FirebaseApp> _initializationTask;

    async Task<FirebaseApp> InitializeCoreAsync()
    {
        DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

        if (dependencyStatus != DependencyStatus.Available)
            throw new InvalidOperationException($"Firebase 의존성 준비 실패: {dependencyStatus}");

        return FirebaseApp.DefaultInstance;
    }

    /// <summary>
    /// 최초 초기화 시작 또는 기존 초기화 작업 반환
    /// </summary>
    /// <returns>준비된 기본 Firebase 앱을 반환하는 초기화 작업</returns>
    public Task<FirebaseApp> InitializeAsync()
    {
        if (_initializationTask == null)
        {
            _initializationTask = InitializeCoreAsync();
        }

        return _initializationTask;
    }
}