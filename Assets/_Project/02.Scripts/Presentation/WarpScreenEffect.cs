using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 배경 블러와 워프 파티클 합성 및 검정 화면 전환
/// </summary>
[DisallowMultipleComponent]
public class WarpScreenEffect : MonoBehaviour
{
    const float BlurEnd = 0.62f;
    const float BlackFadeStart = 0.68f;
    const float BlackFadeDuration = 1f;
    const float MaxBlur = 7f;
    const float MaxBackgroundZoom = 1.25f;

    static readonly int ParticlesId = Shader.PropertyToID("_ParticlesTex");
    static readonly int BlurId = Shader.PropertyToID("_BlurStrength");
    static readonly int BackgroundZoomId = Shader.PropertyToID("_BackgroundZoom");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int BlackoutId = Shader.PropertyToID("_Blackout");
    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [Header("----- 화면 구성 -----")]
    [SerializeField] RawImage _image;
    [SerializeField] Shader _compositeShader;
    [SerializeField, Min(0.1f)] float _duration = 1.6f;

    [Header("----- 워프 파티클 -----")]
    [SerializeField] GameObject _particleSources;
    [SerializeField, Min(0.01f)] float _particleSpeed = 2f;

    Material _material;
    RenderTexture _particleTexture;
    GameObject _stage;
    Camera _particleCamera;
    ParticleSystem[] _particleSystems;
    Func<bool> _canStartDissolve;
    float _elapsed;
    bool _playing;

    // 워프 화면의 등장 투명도와 완전 차폐 및 디졸브 완료 여부를 담은 재생 종료 알림
    public event Action<float> OnCoverChanged;
    public event Action OnFullyCovered;
    public event Action<bool> OnEnded;

    void Awake()
    {
        if (_image == null || _image.texture == null || _compositeShader == null ||
            _particleSources == null)
            throw new MissingReferenceException($"{name}: 배경 이미지와 합성 Shader 및 파티클 원본 연결 필요");

        if (_image.transform != transform || float.IsNaN(_duration) ||
            float.IsInfinity(_duration) || _duration <= 0f)
            throw new InvalidOperationException($"{name}: 같은 오브젝트의 RawImage와 유한한 양수 재생 시간 필요");

        if (_particleSources.transform.parent != transform || _particleSources.activeSelf)
            throw new InvalidOperationException($"{name}: 비활성 파티클 원본 자식 필요");

        if (float.IsNaN(_particleSpeed) || float.IsInfinity(_particleSpeed) || _particleSpeed <= 0f)
            throw new InvalidOperationException($"{name}: 유한한 양수 파티클 재생 배율 필요");

        _image.enabled = false;
    }

    void Update()
    {
        if (!_playing) return;

        float blackStart = _duration * BlackFadeStart;
        float dissolveStart = blackStart + BlackFadeDuration;
        float end = _duration + BlackFadeDuration;
        bool wasCovered = _elapsed >= dissolveStart;

        // 완전 차폐 상태에서 디졸브 시작 조건 대기
        if (_elapsed == dissolveStart && _canStartDissolve?.Invoke() == false)
            return;

        float nextElapsed = _elapsed + Time.unscaledDeltaTime;

        // 프레임 지연 시에도 디졸브 전에 완전히 검은 화면 한 프레임 보장
        _elapsed = _elapsed < dissolveStart ? Mathf.Min(nextElapsed, dissolveStart) : nextElapsed;

        float blackout = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(blackStart, dissolveStart, _elapsed));
        float dissolve = Mathf.InverseLerp(dissolveStart, end, _elapsed);
        float backgroundZoom = Mathf.SmoothStep(1f, MaxBackgroundZoom, Mathf.Clamp01(_elapsed / dissolveStart));
        ApplyProgress(_elapsed / _duration, backgroundZoom, blackout, dissolve);

        if (_playing && !wasCovered && _elapsed >= dissolveStart)
            OnFullyCovered?.Invoke();

        if (_playing && _elapsed >= end)
            EndPlayback(dissolveCompleted: true);
    }

    void OnDisable()
    {
        if (_playing)
            EndPlayback(dissolveCompleted: false);
    }

    void OnDestroy()
    {
        if (_stage != null)
            Destroy(_stage);
        if (_particleTexture != null)
            Destroy(_particleTexture);
        if (_material != null)
            Destroy(_material);
    }

    void CreateParticleStage()
    {
        // 게임 카메라와 분리된 위치에서 파티클만 RenderTexture에 촬영
        _stage = new GameObject("Warp Particle Stage");
        _stage.SetActive(false);
        SceneManager.MoveGameObjectToScene(_stage, gameObject.scene);
        _stage.transform.position = new Vector3(0f, -10000f, 0f);

        GameObject cameraObject = new GameObject("Warp Particle Camera");
        cameraObject.transform.SetParent(_stage.transform, false);
        // EffectTest Main Camera와 같은 파티클 시점과 원근 투영
        cameraObject.transform.localPosition = new Vector3(0f, 1f, -10f);
        _particleCamera = cameraObject.AddComponent<Camera>();
        _particleCamera.clearFlags = CameraClearFlags.SolidColor;
        _particleCamera.backgroundColor = Color.clear;
        _particleCamera.fieldOfView = 60f;
        _particleCamera.nearClipPlane = 0.3f;
        _particleCamera.farClipPlane = 1000f;
        _particleCamera.useOcclusionCulling = false;
        _particleCamera.stereoTargetEye = StereoTargetEyeMask.None;

        GameObject sources = Instantiate(_particleSources, _stage.transform, false);

        _particleSystems = sources.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particles in _particleSystems)
        {
            ParticleSystem.MainModule settings = particles.main;
            settings.playOnAwake = false;
            settings.useUnscaledTime = true;
            settings.simulationSpeed *= _particleSpeed;
        }
        sources.SetActive(true);

        _material = new Material(_compositeShader);
        _image.material = _material;
    }

    void ConfigureParticleFrame()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (_particleTexture != null && _particleTexture.width == width && _particleTexture.height == height)
            return;

        if (_particleTexture != null)
            Destroy(_particleTexture);

        _particleTexture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "Warp Particles",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _particleCamera.targetTexture = _particleTexture;
        _material.SetTexture(ParticlesId, _particleTexture);

        _particleCamera.aspect = (float)width / height;
    }

    void ApplyProgress(float progress, float backgroundZoom, float blackout, float dissolve)
    {
        float opacity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.18f));
        float blur = Mathf.SmoothStep(0f, MaxBlur, Mathf.Clamp01(progress / BlurEnd));
        _material.SetFloat(OpacityId, opacity);
        _material.SetFloat(BlurId, blur);
        _material.SetFloat(BackgroundZoomId, backgroundZoom);
        _material.SetFloat(BlackoutId, blackout);
        _material.SetFloat(DissolveId, dissolve);
        OnCoverChanged?.Invoke(opacity);
    }

    void EndPlayback(bool dissolveCompleted)
    {
        StopPlayback();
        OnEnded?.Invoke(dissolveCompleted);
    }

    void StopPlayback()
    {
        _playing = false;
        _canStartDissolve = null;
        if (_image != null)
            _image.enabled = false;
        if (_stage == null) return;

        foreach (ParticleSystem particles in _particleSystems)
        {
            if (particles.gameObject.activeInHierarchy)
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        _stage.SetActive(false);
    }

    /// <summary>
    /// 첫 프레임부터 워프 화면 재생
    /// </summary>
    public void Play()
    {
        Play(null);
    }

    /// <summary>
    /// 완전 차폐 이후 디졸브 시작 조건을 확인하는 워프 화면 재생
    /// </summary>
    /// <param name="canStartDissolve">디졸브 시작 조건, 추가 대기 조건이 없으면 null</param>
    public void Play(Func<bool> canStartDissolve)
    {
        if (_playing || !isActiveAndEnabled) return;

        if (_stage == null)
            CreateParticleStage();
        ConfigureParticleFrame();

        _canStartDissolve = canStartDissolve;
        _elapsed = 0f;
        ApplyProgress(0f, 1f, 0f, 0f);
        _stage.SetActive(true);
        foreach (ParticleSystem particles in _particleSystems)
        {
            if (particles.gameObject.activeInHierarchy)
                particles.Play(false);
        }
        _image.enabled = true;
        _playing = true;
    }

    /// <summary>
    /// 종료 알림 없이 현재 워프 화면과 파티클 정리
    /// </summary>
    public void Cancel()
    {
        StopPlayback();
    }
}
