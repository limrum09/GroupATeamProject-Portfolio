using System;
using System.Threading.Tasks;

/// <summary>
/// 서버가 없는 로컬 테스트 프로필의 기기 시각 제공
/// </summary>
public class LocalDeviceClock : IServerClock
{
    /// <summary>
    /// 기기의 현재 시각 조회
    /// </summary>
    /// <returns>기기 기준 UTC 시각</returns>
    public Task<DateTime> GetUtcNowAsync()
    {
        return Task.FromResult(DateTime.UtcNow);
    }
}
