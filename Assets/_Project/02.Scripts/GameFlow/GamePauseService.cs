using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 요청 소유자별 시간 정지와 이전 시간 배율 복원
/// </summary>
public class GamePauseService
{
    readonly HashSet<object> _owners = new HashSet<object>();
    float _previousTimeScale;

    public bool IsPaused => _owners.Count > 0;

    /// <summary>
    /// 첫 정지 요청에서 시간 배율 보관과 시간 정지
    /// </summary>
    /// <param name="owner">정지 요청 소유자</param>
    public void Request(object owner)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (!_owners.Add(owner) || _owners.Count > 1) return;

        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
    }

    /// <summary>
    /// 마지막 정지 요청 해제 시 이전 시간 배율 복원
    /// </summary>
    /// <param name="owner">정지 요청 소유자</param>
    public void Release(object owner)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (!_owners.Remove(owner) || _owners.Count > 0) return;

        Time.timeScale = _previousTimeScale;
    }
}
