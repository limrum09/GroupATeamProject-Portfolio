using System;

/// <summary>
/// 지급 대상 런의 골드와 숙련도 경험치 전달
/// </summary>
public class RunSettlementRelay : IDisposable
{
    readonly GameFlowController _gameFlow;

    public event Action<long> OnMetaGoldReady;
    public event Action<string, long> OnMetaExperienceReady;

    void HandleRunEnded(RunEndReason reason, RunLevelProgression completedRun)
    {
        RunSettlementResult result = _gameFlow.LastScoreResult;
        if (!result.IsRewardEligible)
            return;

        string characterId = _gameFlow.StartRequest.CharacterId;
        try
        {
            OnMetaGoldReady?.Invoke(result.MetaGold);
        }
        finally
        {
            // 골드 구독자 오류와 경험치 전달의 분리
            OnMetaExperienceReady?.Invoke(characterId, result.MetaExperience);
        }
    }

    /// <summary>
    /// 지정한 런 흐름의 종료 이벤트 구독
    /// </summary>
    /// <param name="gameFlow">결산 결과를 확정하는 런 흐름 담당</param>
    public RunSettlementRelay(GameFlowController gameFlow)
    {
        if (gameFlow == null)
            throw new ArgumentNullException(nameof(gameFlow));

        _gameFlow = gameFlow;
        _gameFlow.OnRunEnded += HandleRunEnded;
    }

    /// <summary>
    /// 런 종료와 보상 전달 이벤트 구독 해제
    /// </summary>
    public void Dispose()
    {
        _gameFlow.OnRunEnded -= HandleRunEnded;
        OnMetaGoldReady = null;
        OnMetaExperienceReady = null;
    }
}
