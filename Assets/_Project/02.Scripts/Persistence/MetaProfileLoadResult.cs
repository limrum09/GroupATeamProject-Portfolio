using System;

/// <summary>
/// 로컬 메타 프로필 불러오기 상태
/// </summary>
public enum MetaProfileLoadStatus
{
    Loaded,
    Missing,
    InvalidData,
    ReadFailed,
    RecoveryRequired
}

/// <summary>
/// 로컬 프로필 불러오기 결과와 복원할 데이터
/// </summary>
public class MetaProfileLoadResult
{
    public MetaProfileLoadStatus Status { get; }
    public MetaProfileData Data { get; }
    public string Message { get; }
    public bool RequiresMigration { get; }

    /// <summary>
    /// 공통 프로필 로딩 결과 구성
    /// </summary>
    /// <param name="status">불러오기 결과 상태</param>
    /// <param name="data">정상적으로 읽은 공통 프로필</param>
    /// <param name="message">실패 또는 복구 필요 사유</param>
    /// <param name="requiresMigration">구형 파일을 새 형식으로 기록해야 하는지 여부</param>
    public MetaProfileLoadResult(MetaProfileLoadStatus status, MetaProfileData data = null, string message = null, bool requiresMigration = false)
    {
        if (status == MetaProfileLoadStatus.Loaded && data == null)
            throw new ArgumentNullException(nameof(data));

        if (status != MetaProfileLoadStatus.Loaded && data != null)
            throw new ArgumentException("정상 로딩 결과에만 데이터 지정", nameof(data));

        Status = status;
        Data = data;
        Message = message;
        RequiresMigration = requiresMigration;
    }
}