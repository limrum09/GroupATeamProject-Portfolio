using System;
using UnityEngine;

/// <summary>
/// 한 경로의 간격 제약, 무리 진행 및 개체별 부유 움직임
/// </summary>
public class FireflyPathMotion
{
    readonly FireflySplinePath _path;
    readonly double[] _offsets;
    readonly double[] _amplitudes;
    readonly double[] _phases;
    readonly double _speed;
    readonly double _start;
    readonly double _travelRange;
    readonly double _rampDistance;
    readonly double _rampTime;
    readonly double _legTime;
    readonly double _spacingFrequency;
    readonly double _swayFrequency;
    readonly float _horizontalSway;
    readonly float _verticalSway;
    readonly bool _reverse;
    double _elapsed;
    double _origin;

    static double[] CreateGaps(int count, double total, double minimum, double maximum, System.Random random)
    {
        double[] gaps = new double[count];
        double remaining = total;
        for (int i = 0; i < count; i++)
        {
            int rest = count - i - 1;
            double low = Math.Max(minimum, remaining - rest * maximum);
            double high = Math.Min(maximum, remaining - rest * minimum);
            gaps[i] = rest == 0 ? remaining : low + (high - low) * random.NextDouble();
            remaining -= gaps[i];
        }
        for (int i = count - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            double saved = gaps[i];
            gaps[i] = gaps[other];
            gaps[other] = saved;
        }
        return gaps;
    }

    double EvaluateLeg(double time)
    {
        if (time < _rampTime)
            return _rampDistance * (1d - Math.Cos(Math.PI * 0.5d * time / _rampTime));
        if (time > _legTime - _rampTime)
            return _travelRange - _rampDistance * (1d - Math.Cos(Math.PI * 0.5d * (_legTime - time) / _rampTime));
        return _rampDistance + (time - _rampTime) * _speed;
    }

    void UpdateOrigin()
    {
        if (_path.Closed)
        {
            _origin = _start + (_reverse ? -1d : 1d) * _speed * _elapsed;
            return;
        }

        double cycle = (_start + _elapsed) % (2d * _legTime);
        _origin = cycle < _legTime ? EvaluateLeg(cycle) : _travelRange - EvaluateLeg(cycle - _legTime);
    }

    /// <summary>
    /// 검증된 설정으로 무리의 간격과 독립적인 흔들림 위상 초기화
    /// </summary>
    /// <param name="settings">경로별 정적 설정</param>
    /// <param name="path">무리보다 오래 유지되는 경로 스냅샷</param>
    /// <param name="random">게임플레이와 분리된 초기 배치 난수</param>
    public FireflyPathMotion(FireflyPathSettings settings, FireflySplinePath path, System.Random random)
    {
        _path = path;
        _speed = settings.Speed;
        _reverse = settings.Reverse;
        _horizontalSway = settings.HorizontalSway;
        _verticalSway = settings.VerticalSway;
        _swayFrequency = Math.PI * 2d / settings.SwayPeriod;
        int count = settings.Count;
        _offsets = new double[count];
        _amplitudes = new double[count];
        _phases = new double[count];

        int gapCount = count > 1 ? (path.Closed ? count : count - 1) : 0;
        double minimumSpan = gapCount * (double)settings.MinSpacing;
        double span = path.Closed ? path.Length : minimumSpan +
            0.65d * (Math.Min(gapCount * (double)settings.MaxSpacing, path.Length) - minimumSpan);
        double[] gaps = gapCount == 0 ? Array.Empty<double>() :
            CreateGaps(gapCount, span, settings.MinSpacing, settings.MaxSpacing, random);
        double largestAmplitude = 0d;

        for (int i = 0; i < count; i++)
        {
            if (i > 0) _offsets[i] = _offsets[i - 1] + gaps[i - 1];
            _phases[i] = random.NextDouble() * Math.PI * 2d;
            if (gapCount == 0 || (!path.Closed && (i == 0 || i == count - 1))) continue;

            double before = gaps[(i + gapCount - 1) % gapCount];
            double after = gaps[i % gapCount];
            double slack = Math.Min(Math.Min(before - settings.MinSpacing, settings.MaxSpacing - before),
                Math.Min(after - settings.MinSpacing, settings.MaxSpacing - after));
            // 이웃 두 개체의 변위 합을 해당 간격 여유의 90% 이내로 제한
            _amplitudes[i] = Math.Max(0d, slack) * 0.45d;
            largestAmplitude = Math.Max(largestAmplitude, _amplitudes[i]);
        }

        // 경로 진행에 대한 개체 변위 미분을 0.25 이하로 제한하여 추월과 역주행 방지
        _spacingFrequency = Math.Min(Math.PI * 2d / Math.Max(path.Length, 1d),
            largestAmplitude > 0d ? 0.25d / largestAmplitude : double.MaxValue);
        _travelRange = path.Closed ? 0d : path.Length - (count > 1 ? _offsets[count - 1] : 0d);
        _rampDistance = Math.Min(_travelRange * 0.25d, _speed * settings.TurnDuration * 2d / Math.PI);
        _rampTime = _rampDistance * Math.PI / (2d * _speed);
        _legTime = 2d * _rampTime + (_travelRange - 2d * _rampDistance) / _speed;
        _start = path.Closed ? random.NextDouble() * path.Length :
            random.NextDouble() * _legTime + (_reverse ? _legTime : 0d);
        UpdateOrigin();
    }

    /// <summary>
    /// 활성 상태의 경과 시간 진행
    /// </summary>
    /// <param name="deltaTime">이번 프레임의 경과 시간</param>
    public void Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        UpdateOrigin();
    }

    /// <summary>
    /// 간격 제약을 유지하는 개체의 경로상 거리 계산
    /// </summary>
    /// <param name="index">무리 내부 개체 인덱스</param>
    /// <returns>순환 정규화 이전의 경로상 거리</returns>
    public double GetDistance(int index)
    {
        return _origin + _offsets[index] + _amplitudes[index] * Math.Sin(_origin * _spacingFrequency + _phases[index]);
    }

    /// <summary>
    /// 월드 수평 반경과 수직 높이 안의 부유 변위를 포함한 위치 계산
    /// </summary>
    /// <param name="index">무리 내부 개체 인덱스</param>
    /// <returns>표시할 월드 위치</returns>
    public Vector3 GetPosition(int index)
    {
        double phase = _phases[index];
        double time = _elapsed * _swayFrequency;
        double angle = time * 0.73d + phase;
        double radius = Math.Sin(time * 0.41d + phase * 2d) * _horizontalSway;
        Vector3 sway = new Vector3((float)(Math.Cos(angle) * radius),
            (float)(Math.Sin(time + phase) * _verticalSway), (float)(Math.Sin(angle) * radius));
        return _path.Evaluate(GetDistance(index)) + sway;
    }
}
