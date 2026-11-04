using System;

/// <summary>
/// 로비 상태를 조회하여 캐릭터 정보와 메타 재화 화면에 전달
/// </summary>
public class LobbyStatusPresenter
{
    readonly LobbyManager _lobby;
    readonly LobbyPlayerInfoView _playerInfo;
    readonly LobbyMetaCurrencyView _currency;

    bool _active;

    void Refresh()
    {
        if (!_active)
            return;

        _playerInfo.ShowCharacterName(_lobby.SelectedCharacter.DisplayName);

        if (!_lobby.IsMetaReady || _lobby.IsCharacterTestMode)
        {
            _playerInfo.ShowProgressUnavailable();
            _currency.ShowUnavailable();
            return;
        }

        _lobby.GetIntegratedMasteryProgress(out int level, out long currentExperience, out long requiredExperience);

        _playerInfo.ShowProgress(level, currentExperience, requiredExperience);
        _currency.ShowBalance(_lobby.MetaBalance);
    }

    /// <summary>
    /// 로비 조회 대상과 두 표시 화면 연결
    /// </summary>
    /// <param name="lobby">캐릭터 선택과 메타 상태 조회 대상</param>
    /// <param name="playerInfo">캐릭터 이름과 통합 성장 표시 화면</param>
    /// <param name="currency">메타 재화 표시 화면</param>
    public LobbyStatusPresenter(LobbyManager lobby, LobbyPlayerInfoView playerInfo, LobbyMetaCurrencyView currency)
    {
        if (lobby == null)
            throw new ArgumentNullException(nameof(lobby));

        if (playerInfo == null)
            throw new ArgumentNullException(nameof(playerInfo));

        if (currency == null)
            throw new ArgumentNullException(nameof(currency));

        playerInfo.ValidateBindings();
        currency.ValidateBindings();

        _lobby = lobby;
        _playerInfo = playerInfo;
        _currency = currency;
    }

    /// <summary>
    /// 로비 변경 구독과 최초 표시 갱신
    /// </summary>
    public void Activate()
    {
        if (_active)
            return;

        _active = true;
        _lobby.OnChanged += Refresh;

        try
        {
            Refresh();
        }
        catch
        {
            Deactivate();
            throw;
        }
    }

    /// <summary>
    /// 로비 변경 구독 해제
    /// </summary>
    public void Deactivate()
    {
        if (!_active)
            return;

        _active = false;

        if (_lobby != null)
            _lobby.OnChanged -= Refresh;
    }
}