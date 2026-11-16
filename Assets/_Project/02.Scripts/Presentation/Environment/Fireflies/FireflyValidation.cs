using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 반딧불 초기화와 Inspector가 공유하는 설정 제약
/// </summary>
public static class FireflyValidation
{
    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool IsRange(Vector2 range, float minimum)
    {
        return IsFinite(range.x) && IsFinite(range.y) && range.x >= minimum && range.y >= range.x;
    }

    static bool ValidateProfile(FireflyProfile profile, out string error)
    {
        if (profile == null)
        {
            error = "FireflyProfile 연결이 필요합니다.";
            return false;
        }
        Color color = profile.Color;
        if (!IsFinite(color.r) || !IsFinite(color.g) || !IsFinite(color.b) ||
            color.r < 0f || color.g < 0f || color.b < 0f ||
            !IsRange(profile.Size, 0.001f) || !IsRange(profile.PeakIntensity, 0f) ||
            !IsRange(profile.DarkDuration, 0.01f) || !IsRange(profile.FadeInDuration, 0.01f) ||
            !IsRange(profile.BrightDuration, 0.01f) || !IsRange(profile.FadeOutDuration, 0.01f))
        {
            error = "프로필 범위는 최소 ≤ 최대인 유한한 값이 필요합니다. 크기 ≥ 0.001, 시간 ≥ 0.01초, 색상·밝기 ≥ 0.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// 경로의 연결, 수치 및 실제 월드 길이에 대한 배치 가능성 검사
    /// </summary>
    /// <param name="settings">검사할 경로 설정</param>
    /// <param name="length">검증된 월드 경로 길이</param>
    /// <param name="error">실패 이유</param>
    /// <returns>경로 설정의 유효 여부</returns>
    public static bool ValidatePath(FireflyPathSettings settings, out float length, out string error)
    {
        length = 0f;
        error = null;
        if (settings == null || settings.Count < 0)
        {
            error = "경로 설정과 0 이상의 개체 수가 필요합니다.";
            return false;
        }
        // 0마리 경로는 명시적으로 제외된 선택 항목
        if (settings.Count == 0) return true;
        SplineContainer container = settings.Container;
        if (container == null || settings.SplineIndex < 0 || settings.SplineIndex >= container.Splines.Count ||
            container.Splines[settings.SplineIndex].Count < 2)
        {
            error = "두 개 이상의 Knot이 있는 SplineContainer와 올바른 스플라인 인덱스가 필요합니다.";
            return false;
        }
        if (!IsFinite(settings.Speed) || settings.Speed <= 0f ||
            !IsRange(new Vector2(settings.MinSpacing, settings.MaxSpacing), 0f) ||
            !IsFinite(settings.HorizontalSway) || settings.HorizontalSway < 0f ||
            !IsFinite(settings.VerticalSway) || settings.VerticalSway < 0f ||
            !IsFinite(settings.SwayPeriod) || settings.SwayPeriod < 0.01f ||
            !IsFinite(settings.TurnDuration) || settings.TurnDuration < 0.01f)
        {
            error = "유한한 값으로 속도 > 0, 0 ≤ 최소 간격 ≤ 최대 간격, 흔들림 ≥ 0, 주기·감속 시간 ≥ 0.01초를 지정하세요.";
            return false;
        }
        Spline spline = container.Splines[settings.SplineIndex];
        Matrix4x4 matrix = container.transform.localToWorldMatrix;
        for (int i = 0; i < 16; i++)
        {
            if (IsFinite(matrix[i])) continue;
            error = "SplineContainer의 월드 Transform에는 유한한 값이 필요합니다.";
            return false;
        }
        foreach (BezierKnot knot in spline)
        {
            if (!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(knot.Position)) ||
                !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(knot.TangentIn)) ||
                !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(knot.TangentOut)) ||
                !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(knot.Rotation.value)) ||
                Unity.Mathematics.math.abs(Unity.Mathematics.math.lengthsq(knot.Rotation.value) - 1f) > 0.01f)
            {
                error = "Knot 위치·Tangent에는 유한한 값, 회전에는 정규화된 Quaternion이 필요합니다.";
                return false;
            }
        }
        using (FireflySplinePath path = new FireflySplinePath(settings, Allocator.Temp))
        {
            length = path.Length;
            if (!IsFinite(length) || length < 0.001f)
            {
                error = "월드 경로 길이는 0.001 이상의 유한한 값이 필요합니다. Knot과 Transform을 확인하세요.";
                return false;
            }
            if (settings.Count == 1) return true;
            int gapCount = path.Closed ? settings.Count : settings.Count - 1;
            double minSpan = gapCount * (double)settings.MinSpacing;
            double maxSpan = gapCount * (double)settings.MaxSpacing;
            if (path.Closed && (length < minSpan || length > maxSpan))
            {
                error = $"닫힌 경로 길이 {length:F3}: 개체 수 × 최소 간격({minSpan:F3}) ≤ 길이 ≤ 개체 수 × 최대 간격({maxSpan:F3}) 조건이 필요합니다.";
                return false;
            }
            if (!path.Closed && length - minSpan < 0.001d)
            {
                error = $"열린 경로 길이 {length:F3}: (개체 수 - 1) × 최소 간격({minSpan:F3}) 외에 왕복 공간 0.001 이상이 필요합니다.";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 전체 생성 전에 공통 연결과 모든 활성 경로 검사
    /// </summary>
    /// <param name="swarm">검사할 생성 소유자</param>
    /// <param name="error">첫 번째 설정 오류</param>
    /// <returns>전체 설정의 유효 여부</returns>
    public static bool Validate(FireflySwarm swarm, out string error)
    {
        if (!ValidateProfile(swarm.Profile, out error)) return false;
        if (swarm.Prefab == null)
        {
            error = "Firefly 프리팹 연결이 필요합니다.";
            return false;
        }
        if (swarm.Prefab.gameObject.scene.IsValid())
        {
            error = "Scene 인스턴스 대신 Project의 Firefly 프리팹 에셋을 연결하세요.";
            return false;
        }
        if (!swarm.Prefab.ValidateVisual(out error)) return false;
        IReadOnlyList<FireflyPathSettings> paths = swarm.Paths;
        if (paths == null)
        {
            error = "경로 목록은 빈 목록 또는 명시적인 항목으로 지정하세요.";
            return false;
        }
        long total = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            if (!ValidatePath(paths[i], out _, out error))
            {
                error = $"경로 {i + 1}: {error}";
                return false;
            }
            FireflyPathSettings path = paths[i];
            total += path.Count;
            if (path.Count == 0) continue;
            for (int j = 0; j < i; j++)
            {
                if (paths[j].Count > 0 && paths[j].Container == path.Container && paths[j].SplineIndex == path.SplineIndex)
                {
                    error = $"경로 {i + 1}: 경로 {j + 1}과 동일한 스플라인입니다. 간격 관리 소유자는 하나만 지정하세요.";
                    return false;
                }
            }
        }
        if (total > int.MaxValue)
        {
            error = "전체 개체 수가 지원하는 정수 범위를 초과합니다.";
            return false;
        }
        error = null;
        return true;
    }
}
