/// <summary>특정 이펙트 대여 건의 재생 제어 핸들</summary>
public readonly struct CombatEffectHandle
{
    readonly CombatEffect _player;
    readonly long _playbackId;

    internal CombatEffectHandle(CombatEffect player, long playbackId)
    {
        _player = player;
        _playbackId = playbackId;
    }

    public bool IsPlaying => _player != null && _player.IsPlaying(_playbackId);

    /// <summary>해당 대여 건의 재생 종료와 반환 요청</summary>
    public void Stop()
    {
        if (_player != null)
            _player.Stop(_playbackId);
    }
}