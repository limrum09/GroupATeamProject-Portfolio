using System;
using UnityEngine;

/// <summary>
/// 모닥불 조명과 불꽃의 공통 일렁임 및 간헐적인 타오름 연출
/// </summary>
[DisallowMultipleComponent]
public class FireLightFlicker : MonoBehaviour
{
    [Header("----- 연출 연결 -----")]
    [SerializeField] Light _spotLight;
    [SerializeField] Light _pointLight;
    [SerializeField] ParticleSystem _fireSmoke;

    [Header("----- 평상시 일렁임 -----")]
    [SerializeField, Range(0f, 0.5f)] float _spotVariation = 0.25f;
    [SerializeField, Range(0f, 0.5f)] float _pointVariation = 0.12f;
    [SerializeField, Range(0f, 0.2f)] float _fireVariation = 0.03f;

    [Header("----- 일렁임 속도 -----")]
    [SerializeField, Min(0.01f)] float _slowSpeed = 1.5f;
    [SerializeField, Min(0.01f)] float _fastSpeed = 7f;
    [SerializeField, Range(0f, 1f)] float _fastWeight = 0.2f;

    [Header("----- 간헐적인 타오름 -----")]
    [SerializeField, Min(0.1f)] float _flareMinWait = 4f;
    [SerializeField, Min(0f)] float _flareRandomWait = 3f;
    [SerializeField, Min(0.01f)] float _flareRiseTime = 0.12f;
    [SerializeField, Min(0.01f)] float _flareFallTime = 1.4f;

    [Header("----- 타오름 추가 밝기 -----")]
    [SerializeField, Range(0f, 1f)] float _spotFlareBoost = 0.4f;
    [SerializeField, Range(0f, 1f)] float _pointFlareBoost = 0.15f;

    [Header("----- 타오름 추가 크기 -----")]
    [SerializeField, Range(0f, 1f)] float _fireWidthBoost = 0.15f;
    [SerializeField, Range(0f, 1f)] float _fireHeightBoost = 0.45f;

    // 게임플레이 난수 상태와 분리된 연출용 난수
    readonly System.Random _flareRandom = new System.Random();

    float _spotBaseIntensity;
    float _pointBaseIntensity;
    Vector3 _fireBaseScale;

    float _elapsedTime;

    // 음수는 대기 시간, 0 이상은 타오름 진행 시간
    float _flareTime;

    bool _hasCapturedState;

    void Awake()
    {
        if (_spotLight == null || _pointLight == null || _fireSmoke == null)
            throw new MissingReferenceException($"{name}: Spot, Point, FireSmoke 연결 필요");

        if (_spotLight.type != LightType.Spot || _pointLight.type != LightType.Point)
            throw new InvalidOperationException($"{name}: Spot과 Point의 Light Type 확인 필요");

        ParticleSystem.MainModule main = _fireSmoke.main;

        if (main.simulationSpace != ParticleSystemSimulationSpace.Local || main.scalingMode == ParticleSystemScalingMode.Shape)
            throw new InvalidOperationException($"{name}: FireSmoke는 Local Simulation Space와 " + "Local 또는 Hierarchy Scaling Mode 필요");
    }

    void OnEnable()
    {
        // 현재 Inspector 설정을 복원 가능한 기준값으로 보관
        _spotBaseIntensity = _spotLight.intensity;
        _pointBaseIntensity = _pointLight.intensity;
        _fireBaseScale = _fireSmoke.transform.localScale;

        _elapsedTime = 0f;
        _flareTime = -GetFlareWait();
        _hasCapturedState = true;
    }

    void OnDisable()
    {
        if (!_hasCapturedState) return;

        // 씬 해제 중 이미 제거된 대상을 제외하고 기준값 복원
        if (_spotLight != null)
            _spotLight.intensity = _spotBaseIntensity;

        if (_pointLight != null)
            _pointLight.intensity = _pointBaseIntensity;

        if (_fireSmoke != null)
            _fireSmoke.transform.localScale = _fireBaseScale;

        _hasCapturedState = false;
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;

        _elapsedTime += deltaTime;
        _flareTime += deltaTime;

        float slowNoise = Mathf.PerlinNoise(7.31f, _elapsedTime * _slowSpeed);

        float fastNoise = Mathf.PerlinNoise(83.17f, _elapsedTime * _fastSpeed);

        float noise = Mathf.Clamp01(Mathf.Lerp(slowNoise, fastNoise, _fastWeight));

        float flicker = noise * 2f - 1f;

        // 모든 대상이 공유할 타오름 값을 프레임당 한 번 계산
        float flare = CalculateFlare();

        _spotLight.intensity = _spotBaseIntensity * (1f + flicker * _spotVariation + flare * _spotFlareBoost);

        _pointLight.intensity = _pointBaseIntensity * (1f + flicker * _pointVariation + flare * _pointFlareBoost);

        float idleScale = 1f + flicker * _fireVariation;
        float widthScale = idleScale + flare * _fireWidthBoost;
        float heightScale = idleScale + flare * _fireHeightBoost;

        // 현재 크기에 누적하지 않고 기준 크기에서 매번 계산
        _fireSmoke.transform.localScale = Vector3.Scale(_fireBaseScale, new Vector3(widthScale, heightScale, widthScale));
    }

    float CalculateFlare()
    {
        if (_flareTime < 0f)
            return 0f;

        if (_flareTime < _flareRiseTime)
        {
            float progress = _flareTime / _flareRiseTime;
            return Mathf.SmoothStep(0f, 1f, progress);
        }

        float fadeProgress = (_flareTime - _flareRiseTime) / _flareFallTime;

        if (fadeProgress < 1f)
            return Mathf.SmoothStep(1f, 0f, fadeProgress);

        // 이전 타오름 종료 후 다음 대기 시간 결정
        _flareTime = -GetFlareWait();
        return 0f;
    }

    float GetFlareWait()
    {
        return _flareMinWait + (float)_flareRandom.NextDouble() * _flareRandomWait;
    }
}