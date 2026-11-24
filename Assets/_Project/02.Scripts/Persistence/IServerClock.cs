using System;
using System.Threading.Tasks;

/// <summary>
/// 프로필 저장소와 같은 기준의 현재 시각 조회 계약
/// </summary>
public interface IServerClock
{
    /// <summary>
    /// 날짜 기반 규칙에 사용할 현재 시각 조회
    /// </summary>
    /// <returns>UTC 기준 현재 시각</returns>
    Task<DateTime> GetUtcNowAsync();
}
