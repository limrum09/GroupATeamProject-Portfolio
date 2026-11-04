using System;
using System.Threading.Tasks;

/// <summary>
/// 런 정산 저장 상태
/// </summary>
public enum RunSettlementState
{
    Ready,
    Saving,
    SavePending,
    Completed,
    Discarded
}

/// <summary>
/// 확정된 단일 런 정산의 저장 요청과 실패 재시도, 메인 스레드 호출 전용
/// </summary>
public class RunSettlementService
{
    readonly Func<RunSettlementResult, Task<bool>> _commitAsync;
    Task<bool> _activeAttempt;

    public RunSettlementResult Result { get; }
    public RunSettlementState State { get; private set; }
    public Exception LastError { get; private set; }
    public bool IsProcessing => State == RunSettlementState.Saving;
    public bool HasPendingSave => State == RunSettlementState.SavePending;

    Task<bool> BeginAttempt()
    {
        LastError = null;
        State = RunSettlementState.Saving;
        TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 동기 콜백의 재진입에도 동일한 처리 작업 반환
        _activeAttempt = completion.Task;
        _ = CompleteAttemptAsync(completion);
        return completion.Task;
    }

    async Task CompleteAttemptAsync(TaskCompletionSource<bool> completion)
    {
        bool saved = false;
        try
        {
            saved = await _commitAsync(Result);
        }
        catch (Exception exception)
        {
            // 확정 결과를 유지한 채 명시적 재시도 허용
            LastError = exception;
        }

        State = saved ? RunSettlementState.Completed : RunSettlementState.SavePending;
        _activeAttempt = null;
        completion.SetResult(saved);
    }

    /// <summary>
    /// 런별 정산 저장 요청 준비
    /// </summary>
    /// <param name="result">최초 종료 시 확정된 결과</param>
    /// <param name="commitAsync">RunId 기준 중복 지급 방지와 지급·저장을 담당하며 영속 저장 성공 시에만 true를 반환하는 콜백</param>
    public RunSettlementService(RunSettlementResult result, Func<RunSettlementResult, Task<bool>> commitAsync)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        _commitAsync = commitAsync ?? throw new ArgumentNullException(nameof(commitAsync));
        State = result.IsRewardEligible ? RunSettlementState.Ready : RunSettlementState.Discarded;
    }

    /// <summary>
    /// 최초 저장 요청, 진행 중 중복 호출은 동일 작업 반환
    /// </summary>
    /// <returns>저장 성공 여부, 직접 종료와 재시도 대기 상태는 false</returns>
    public Task<bool> SaveAsync()
    {
        if (IsProcessing) return _activeAttempt;
        if (State == RunSettlementState.Completed) return Task.FromResult(true);
        if (State != RunSettlementState.Ready) return Task.FromResult(false);
        return BeginAttempt();
    }

    /// <summary>
    /// 실패한 저장을 동일한 런 식별자와 결과로 재시도
    /// </summary>
    /// <returns>저장 성공 여부, 재시도할 결과가 없으면 false</returns>
    public Task<bool> RetrySaveAsync()
    {
        if (IsProcessing) return _activeAttempt;
        if (State == RunSettlementState.Completed) return Task.FromResult(true);
        if (!HasPendingSave) return Task.FromResult(false);
        return BeginAttempt();
    }
}
