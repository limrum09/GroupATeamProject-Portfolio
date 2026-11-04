using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 영구 데이터 저장 완료 이후의 애플리케이션 종료 조율
/// </summary>
public class ApplicationExitService : IDisposable
{
    readonly Func<Task<bool>> _saveBeforeExit;
    readonly Action _quit;

    bool _allowQuit;
    bool _disposed;

    public event Action OnChanged;

    public bool IsProcessing { get; private set; }
    public string ErrorMessage { get; private set; }

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

    /// <summary>
    /// 종료 전 저장 함수와 실제 종료 동작 연결
    /// </summary>
    /// <param name="saveBeforeExit">영구 데이터 저장 완료 함수</param>
    /// <param name="quit">실제 애플리케이션 종료 동작</param>
    public ApplicationExitService(Func<Task<bool>> saveBeforeExit, Action quit)
    {
        _saveBeforeExit = saveBeforeExit ?? throw new ArgumentNullException(nameof(saveBeforeExit));
        _quit = quit ?? throw new ArgumentNullException(nameof(quit));

        Application.wantsToQuit += HandleWantsToQuit;
    }

    /// <summary>
    /// 저장 실패 시 종료를 보류하는 명시적인 종료 요청
    /// </summary>
    /// <returns>종료 요청 실행 여부</returns>
    public async Task<bool> RequestExitAsync()
    {
        if (_disposed || IsProcessing)
            return false;

        IsProcessing = true;
        ErrorMessage = null;
        PublishChanged();

        try
        {
            if (!await _saveBeforeExit())
            {
                ErrorMessage =
                    "저장을 완료하지 못해 종료를 보류했습니다. 기존 작업을 마치거나 저장을 재시도해 주세요.";

                Debug.LogWarning(ErrorMessage);
                return false;
            }

            // 기존 종료 요청의 취소 처리가 끝난 뒤 다시 종료 요청
            await Task.Yield();

            if (_disposed)
                return false;

            _allowQuit = true;
            _quit();

            return true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Debug.LogException(exception);

            return false;
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