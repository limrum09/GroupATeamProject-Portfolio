using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 영구 데이터 저장 시도 이후의 애플리케이션 종료 조율
/// </summary>
public class ApplicationExitService : IDisposable
{
    readonly Func<Task<bool>> _saveBeforeExit;
    readonly Action _quit;

    bool _allowQuit;
    bool _disposed;

    public event Action OnChanged;

    public bool IsProcessing { get; private set; }

    void PublishChanged()
    {
        if (_disposed)
            return;

        Delegate[] callbacks = OnChanged?.GetInvocationList();

        if (callbacks == null)
            return;

        foreach (Action callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    bool HandleWantsToQuit()
    {
        if (_disposed || _allowQuit)
            return true;

        if (!IsProcessing)
            RequestFromApplication();

        return false;
    }

    async void RequestFromApplication()
    {
        await RequestExitAsync();
    }

    async Task<bool> TrySaveAsync()
    {
        try
        {
            return await _saveBeforeExit();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
        }
    }

    /// <summary>
    /// 종료 전 저장 함수와 실제 종료 동작 연결
    /// </summary>
    /// <param name="saveBeforeExit">영구 데이터 저장 시도 함수</param>
    /// <param name="quit">실제 애플리케이션 종료 동작</param>
    public ApplicationExitService(Func<Task<bool>> saveBeforeExit, Action quit)
    {
        _saveBeforeExit = saveBeforeExit ?? throw new ArgumentNullException(nameof(saveBeforeExit));
        _quit = quit ?? throw new ArgumentNullException(nameof(quit));

        Application.wantsToQuit += HandleWantsToQuit;
    }

    /// <summary>
    /// 저장 실패 시에도 종료하는 명시적인 종료 요청
    /// </summary>
    /// <returns>종료 요청 실행 여부</returns>
    public async Task<bool> RequestExitAsync()
    {
        if (_disposed || IsProcessing)
            return false;

        IsProcessing = true;
        PublishChanged();

        try
        {
            // 저장 실패 시 미저장 변경을 버리고 마지막 정상 저장본 유지
            if (!await TrySaveAsync())
                Debug.LogWarning("저장하지 못한 변경을 버리고 종료");

            // 기존 종료 요청의 취소 처리가 끝난 뒤 다시 종료 요청
            await Task.Yield();

            if (_disposed)
                return false;

            _allowQuit = true;
            _quit();

            return true;
        }
        finally
        {
            IsProcessing = false;
            PublishChanged();
        }
    }

    /// <summary>
    /// 종료 이벤트 연결 해제와 후속 종료 실행 차단
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Application.wantsToQuit -= HandleWantsToQuit;
        OnChanged = null;
    }
}