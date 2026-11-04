using System;

/// <summary>
/// 세션 메뉴 요청과 기존 런 기능 연결
/// </summary>
public class GameMenuActionController
{
    readonly GameMenuController _menu;
    readonly GameFlowController _runFlow;

    void HandleReturnToLobbyRequested()
    {
        _runFlow.RequestEndRun(RunEndReason.Manual);
    }

    /// <summary>
    /// 현재 세션의 메뉴 요청 연결
    /// </summary>
    /// <param name="menu">요청을 전달할 공통 메뉴</param>
    /// <param name="runFlow">런 종료 담당자</param>
    public GameMenuActionController(GameMenuController menu, GameFlowController runFlow)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        if (runFlow == null) throw new ArgumentNullException(nameof(runFlow));
        if (!menu.IsBound || !runFlow.isActiveAndEnabled)
            throw new InvalidOperationException("연결된 메뉴와 활성 런 종료 담당자 필요");

        _menu = menu;
        _runFlow = runFlow;

        _menu.OnReturnToLobbyRequested += HandleReturnToLobbyRequested;
    }

    /// <summary>
    /// 메뉴 요청 구독 해제
    /// </summary>
    public void Unbind()
    {
        if (_menu != null)
            _menu.OnReturnToLobbyRequested -= HandleReturnToLobbyRequested;
    }
}
