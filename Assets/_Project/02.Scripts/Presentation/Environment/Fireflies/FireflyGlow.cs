using System;
using UnityEngine;

/// <summary>
/// 반딧불 한 개체의 발광 주기와 공유 머티리얼 표시 상태
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FireflyGlow : MonoBehaviour
{
    [Header("----- 빛점 연결 -----")]
    [SerializeField] MeshRenderer _renderer;
    [SerializeField] MeshFilter _meshFilter;

    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int SizeId = Shader.PropertyToID("_Size");
    readonly double[] _durations = new double[4];
    readonly Vector2[] _ranges = new Vector2[4];
    MaterialPropertyBlock _properties;
    System.Random _random;
    Color _color;
    Vector2 _peakRange;
    float _size;
    float _peak;
    double _cycleTime;
    double _cycleLength;

    float Sample(Vector2 range)
    {
        return Mathf.Lerp(range.x, range.y, (float)_random.NextDouble());
    }

    void BeginCycle()
    {
        _cycleLength = 0d;
        for (int i = 0; i < _durations.Length; i++)
        {
            _durations[i] = Sample(_ranges[i]);
            _cycleLength += _durations[i];
        }
        _peak = Sample(_peakRange);
    }

    void ApplyGlow()
    {
        double time = _cycleTime;
        float envelope;
        if (time < _durations[0]) envelope = 0f;
        else if ((time -= _durations[0]) < _durations[1])
            envelope = Mathf.SmoothStep(0f, 1f, (float)(time / _durations[1]));
        else if ((time -= _durations[1]) < _durations[2]) envelope = 1f;
        else
            envelope = Mathf.SmoothStep(1f, 0f, (float)((time - _durations[2]) / _durations[3]));

        Color emission = _color * (_peak * envelope);
        emission.a = 1f;
        _properties.SetColor(GlowColorId, emission);
        _renderer.SetPropertyBlock(_properties);
    }

    /// <summary>
    /// 프리팹의 명시적 렌더링 연결과 전용 셰이더 계약 검사
    /// </summary>
    /// <param name="error">잘못된 연결의 설명</param>
    /// <returns>연결의 유효 여부</returns>
    public bool ValidateVisual(out string error)
    {
        if (_renderer == null || _meshFilter == null || _renderer.gameObject != gameObject ||
            _meshFilter.gameObject != gameObject || _meshFilter.sharedMesh == null)
        {
            error = "반딧불에 같은 오브젝트의 MeshRenderer와 Quad MeshFilter 연결이 필요합니다.";
            return false;
        }
        Material material = _renderer.sharedMaterial;
        if (material == null || material.shader == null || material.shader.name != "Project/Presentation/FireflyGlow" ||
            _renderer.sharedMaterials.Length != 1)
        {
            error = "반딧불에는 Project/Presentation/FireflyGlow 머티리얼 하나가 필요합니다.";
            return false;
        }
        if (!gameObject.activeSelf || !enabled || !_renderer.enabled)
        {
            error = "반딧불 프리팹과 FireflyGlow, MeshRenderer를 활성화하세요.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// 프로필 값과 개체별 난수로 최초 발광 상태 설정
    /// </summary>
    /// <param name="profile">검증된 공통 표현 설정</param>
    /// <param name="seed">개체 전용 난수 시드</param>
    public void Initialize(FireflyProfile profile, int seed)
    {
        _random = new System.Random(seed);
        _properties = new MaterialPropertyBlock();
        _color = profile.Color;
        _peakRange = profile.PeakIntensity;
        _size = Sample(profile.Size);
        _ranges[0] = profile.DarkDuration;
        _ranges[1] = profile.FadeInDuration;
        _ranges[2] = profile.BrightDuration;
        _ranges[3] = profile.FadeOutDuration;
        _properties.SetFloat(SizeId, _size);
        BeginCycle();
        // 최초 생성부터 서로 다른 주기 위치에 분산
        _cycleTime = _random.NextDouble() * _cycleLength;
        ApplyGlow();
    }

    /// <summary>
    /// 이동과 무관한 발광 시간 진행
    /// </summary>
    /// <param name="deltaTime">활성 상태에서의 경과 시간</param>
    public void Tick(float deltaTime)
    {
        _cycleTime += deltaTime;
        while (_cycleTime >= _cycleLength)
        {
            _cycleTime -= _cycleLength;
            BeginCycle();
        }
        ApplyGlow();
    }

    /// <summary>
    /// 빌보드 위치와 카메라 방향에 독립적인 렌더링 경계 갱신
    /// </summary>
    /// <param name="position">새 월드 위치</param>
    public void SetPosition(Vector3 position)
    {
        transform.position = position;
        _renderer.bounds = new Bounds(position, Vector3.one * (_size * 2f));
    }
}
