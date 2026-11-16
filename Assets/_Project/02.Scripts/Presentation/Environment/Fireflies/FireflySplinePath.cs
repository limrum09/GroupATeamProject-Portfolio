using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 시작 시점의 월드 변환을 적용한 스플라인과 거리 기반 위치 평가
/// </summary>
public class FireflySplinePath : IDisposable
{
    NativeSpline _spline;
    bool _disposed;

    public float Length { get; }
    public bool Closed { get; }

    /// <summary>
    /// 검증된 경로의 월드 공간 스냅샷 생성
    /// </summary>
    /// <param name="settings">사용할 스플라인 연결</param>
    /// <param name="allocator">경로 보관 기간에 맞는 메모리 할당 방식</param>
    public FireflySplinePath(FireflyPathSettings settings, Allocator allocator = Allocator.Persistent)
    {
        _spline = new NativeSpline(settings.Container.Splines[settings.SplineIndex],
            (float4x4)settings.Container.transform.localToWorldMatrix, allocator);
        Length = _spline.GetLength();
        Closed = _spline.Closed;
    }

    /// <summary>
    /// 경로상 월드 거리에 대응하는 위치 평가
    /// </summary>
    /// <param name="distance">시작점에서의 경로상 거리</param>
    /// <returns>월드 위치</returns>
    public Vector3 Evaluate(double distance)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FireflySplinePath));
        double bounded = Closed ? distance - Math.Floor(distance / Length) * Length : Math.Max(0d, Math.Min(Length, distance));
        float t = SplineUtility.GetNormalizedInterpolation(_spline, (float)bounded, PathIndexUnit.Distance);
        return (Vector3)SplineUtility.EvaluatePosition(_spline, t);
    }

    /// <summary>
    /// 보유한 스플라인 네이티브 메모리 해제
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _spline.Dispose();
        _disposed = true;
    }
}
