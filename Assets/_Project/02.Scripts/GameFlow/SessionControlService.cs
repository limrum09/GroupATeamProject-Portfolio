using System;
using UnityEngine;

/// <summary>
/// 세션 UI 상태에 따른 조작 제한과 메뉴 정지 정책
/// </summary>
public class SessionControlService
{
    readonly PlayerActor _player;
    readonly GamePauseService _pauseService;

    bool _shopOpen;
    bool _menuOpen;
    bool _stopped;
    int _inputConsumedFrame = -1;

    public bool IsStopped => _stopped;

    void ApplyInputBlock()
    {
        if (_stopped) return;

        _player.SetInputBlocked(_shopOpen || _menuOpen || _inputConsumedFrame == Time.frameCount);
    }

    /// <summary>
    /// 준비된 플레이어와 세션 시간 정지 담당자 연결
    /// </summary>
    /// <param name="player">이번 세션의 준비된 플레이어</param>
    /// <param name="pauseService">이번 세션의 시간 정지 담당자</param>
    public SessionControlService(PlayerActor player, GamePauseService pauseService)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (pauseService == null) throw new ArgumentNullException(nameof(pauseService));
        if (!player.IsPrepared) throw new InvalidOperationException("세션 제어 연결 전 플레이어 준비 필요");

        _player = player;
        _pauseService = pauseService;
    }

    /// <summary>
    /// 상점 이용 상태에 따른 조작 제한 갱신
    /// </summary>
    /// <param name="isOpen">상점 이용 중 여부</param>
    public void SetShopOpen(bool isOpen)
    {
        _shopOpen = isOpen;
        ApplyInputBlock();
    }

    /// <summary>
    /// 메뉴 표시 상태에 따른 조작 제한과 시간 정지 갱신
    /// </summary>
    /// <param name="isOpen">메뉴 표시 여부</param>
    public void SetMenuOpen(bool isOpen)
    {
        _menuOpen = isOpen;

        if (isOpen && !_stopped)
            _pauseService.Request(this);
        else
            _pauseService.Release(this);

        ApplyInputBlock();
    }

    /// <summary>
    /// 취소 입력을 처리한 프레임의 플레이어 명령 차단
    /// </summary>
    public void ConsumeCancelInput()
    {
        if (_stopped) return;

        _inputConsumedFrame = Time.frameCount;
        ApplyInputBlock();
    }

    /// <summary>
    /// 프레임 종료 시 취소 입력에 의한 임시 차단 해제
    /// </summary>
    public void EndFrame()
    {
        if (_stopped || _inputConsumedFrame != Time.frameCount) return;

        _inputConsumedFrame = -1;
        ApplyInputBlock();
    }

    /// <summary>
    /// 조작 제한 유지와 메뉴 정지 요청 해제
    /// </summary>
    public void Stop()
    {
        if (_stopped) return;

        _stopped = true;
        _inputConsumedFrame = -1;

        try
        {
            if (_player != null)
            {
                // 종료 중 활성 씬 의존성 재검사를 막도록 조작 허용부터 해제
                _player.DisableControl();
                _player.SetInputBlocked(true);
            }
        }
        finally
        {
            _pauseService.Release(this);
        }
    }
}
