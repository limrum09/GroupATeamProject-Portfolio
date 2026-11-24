using System;
using System.Globalization;

/// <summary>
/// 한국 표준시 날짜 기준의 일일 퀘스트 주기 키 계산
/// </summary>
public static class DailyQuestPeriod
{
    // 한국 표준시의 고정 UTC 차이, 일광 절약 시간 없음
    const int KoreaUtcOffsetHours = 9;

    /// <summary>
    /// UTC 시각이 속한 한국 날짜의 주기 키 생성, 한국 시간 자정에 다음 주기로 전환
    /// </summary>
    /// <param name="utcNow">서버에서 확인한 UTC 시각</param>
    /// <returns>D:yyyy-MM-dd 형식의 일일 주기 키</returns>
    public static string CreateKey(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC 시각 필요", nameof(utcNow));

        return "D:" + utcNow.AddHours(KoreaUtcOffsetHours).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
