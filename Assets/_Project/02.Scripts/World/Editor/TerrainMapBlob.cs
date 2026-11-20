using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타원 덩어리를 겹쳐 만든 넓은 공간의 불규칙한 평면 모양
/// </summary>
public class TerrainMapBlob
{
    const float MaxElongation = 0.45f;      // 불규칙도 1일 때 가로세로 비율 변화폭
    const float MaxWobble = 0.22f;          // 불규칙도 1일 때 둘레 반지름 출렁임 비율
    const int MaxExtraLobes = 3;            // 불규칙도 1일 때 본체에 붙이는 최대 혹 수
    const float MinLobeScale = 0.4f;        // 혹 반지름 (공간 반지름 배율)
    const float MaxLobeScale = 0.7f;
    const float MinLobeDistance = 0.55f;    // 본체 중심에서 혹 중심까지 거리 (공간 반지름 배율)
    const float MaxLobeDistance = 0.85f;

    readonly List<Lobe> _lobes = new List<Lobe>();

    TerrainMapBlob()
    {
    }

    /// <summary>
    /// 공간 중심에서 모양 가장 바깥까지 최대 거리
    /// </summary>
    public float Extent { get; private set; }

    readonly struct Lobe
    {
        readonly Vector2 _center;
        readonly Vector2 _radii;
        readonly float _cos;
        readonly float _sin;
        readonly float _wobble;
        readonly float _phaseTwo;       // 둘레 2회 출렁임 위상
        readonly float _phaseThree;     // 둘레 3회 출렁임 위상

        public Lobe(Vector2 center, Vector2 radii, float angle, float wobble, float phaseTwo, float phaseThree)
        {
            _center = center;
            _radii = radii;
            _cos = Mathf.Cos(angle);
            _sin = Mathf.Sin(angle);
            _wobble = wobble;
            _phaseTwo = phaseTwo;
            _phaseThree = phaseThree;
        }

        public float Extent => _center.magnitude + Mathf.Max(_radii.x, _radii.y) * (1f + _wobble);

        public bool Contains(Vector2 offset)
        {
            // 회전을 되돌린 타원 기준 정규화 좌표
            Vector2 delta = offset - _center;
            float localX = (delta.x * _cos + delta.y * _sin) / _radii.x;
            float localZ = (-delta.x * _sin + delta.y * _cos) / _radii.y;
            float distance = Mathf.Sqrt(localX * localX + localZ * localZ);
            float angle = Mathf.Atan2(localZ, localX);
            float limit = 1f + _wobble * (0.6f * Mathf.Sin(2f * angle + _phaseTwo) + 0.4f * Mathf.Sin(3f * angle + _phaseThree));
            return distance <= limit;
        }
    }

    static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    static Lobe CreateLobe(System.Random random, Vector2 center, float radius, float elongation, float wobble)
    {
        // 넓이를 유지한 채 가로세로 비율만 변경
        float stretch = Mathf.Sqrt(1f + Range(random, -1f, 1f) * elongation);
        return new Lobe(center, new Vector2(radius * stretch, radius / stretch), Range(random, 0f, Mathf.PI * 2f),
            wobble, Range(random, 0f, Mathf.PI * 2f), Range(random, 0f, Mathf.PI * 2f));
    }

    void AddLobe(Lobe lobe)
    {
        _lobes.Add(lobe);
        Extent = Mathf.Max(Extent, lobe.Extent);
    }

    /// <summary>
    /// 불규칙도 기반 공간 모양 생성
    /// </summary>
    /// <param name="random">배치 난수</param>
    /// <param name="radius">본체 평균 반지름</param>
    /// <param name="irregularity">0 원형 ~ 1 최대 불규칙</param>
    /// <returns>공간 중심 기준 모양</returns>
    public static TerrainMapBlob Create(System.Random random, float radius, float irregularity)
    {
        TerrainMapBlob blob = new TerrainMapBlob();
        float wobble = MaxWobble * irregularity;
        blob.AddLobe(CreateLobe(random, Vector2.zero, radius, MaxElongation * irregularity, wobble));

        // 본체와 넓게 겹치는 혹만 붙여 막다른 곁가지 모양 방지
        int extraLobes = random.Next(Mathf.RoundToInt(MaxExtraLobes * irregularity) + 1);
        for (int i = 0; i < extraLobes; i++)
        {
            float direction = Range(random, 0f, Mathf.PI * 2f);
            Vector2 center = new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) *
                             (radius * Range(random, MinLobeDistance, MaxLobeDistance));
            blob.AddLobe(CreateLobe(random, center, radius * Range(random, MinLobeScale, MaxLobeScale),
                MaxElongation * irregularity * 0.5f, wobble));
        }

        return blob;
    }

    /// <summary>
    /// 공간 중심 기준 위치의 모양 포함 여부
    /// </summary>
    /// <param name="offset">공간 중심에서의 XZ 거리</param>
    /// <returns>포함 여부</returns>
    public bool Contains(Vector2 offset)
    {
        foreach (Lobe lobe in _lobes)
        {
            if (lobe.Contains(offset)) return true;
        }
        return false;
    }
}
