using System;
using UnityEngine;

/// <summary>사전 조립된 스펠의 시각 동작과 편집기 재생</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public sealed class BakedSpellPlayer : MonoBehaviour
{
    [Header("----- 계산된 재생 데이터 -----")]
    [SerializeField] BakedSpellDefinition _definition;
    [SerializeField] Transform[] _bindings = Array.Empty<Transform>();
    [SerializeField] MeshRenderer[] _surfaces = Array.Empty<MeshRenderer>();
    [SerializeField] ParticleSystem[] _particles = Array.Empty<ParticleSystem>();
    [SerializeField] WindZone[] _winds = Array.Empty<WindZone>();
    [SerializeField] ParticleSystem _clock;

    SpellMotionEvaluator.State[] _motionStates;
    bool[] _deadParticles;
    bool[] _warmedParticles;
    MaterialPropertyBlock _properties;
    float _requestedTime;
    int _frame;
    bool _manual;

    public BakedSpellDefinition Definition => _definition;
    public float PlaybackTime => _frame * BakedSpellDefinition.StepSeconds;

    /// <summary>변환 도구의 명시적 참조 설정</summary>
    /// <param name="definition">계산된 설정</param>
    /// <param name="bindings">동작 대상</param>
    /// <param name="surfaces">메시 렌더러</param>
    /// <param name="particles">파티클 대상</param>
    /// <param name="winds">바람 파츠</param>
    /// <param name="clock">루트 수명 관리 파티클</param>
    public void Configure(BakedSpellDefinition definition, Transform[] bindings, MeshRenderer[] surfaces, ParticleSystem[] particles, WindZone[] winds, ParticleSystem clock)
    {
        _definition = definition;
        _bindings = bindings;
        _surfaces = surfaces;
        _particles = particles;
        _winds = winds;
        _clock = clock;
    }

    void OnEnable()
    {
        if (Application.isPlaying) Restart(false);
    }

    void Update()
    {
        if (!Application.isPlaying || _manual) return;
        _requestedTime += Time.deltaTime;
        AdvanceTo(_requestedTime, false);
    }

    /// <summary>동일한 배치와 난수로 재생 초기화</summary>
    /// <param name="manual">편집기 수동 시뮬레이션 여부</param>
    public void Restart(bool manual)
    {
        if (_definition == null || _clock == null || _bindings.Length != _definition.Poses.Length ||
            _particles.Length != _definition.Particles.Length || _surfaces.Length != _definition.Surfaces.Length || _winds.Length != _definition.Winds.Length)
            throw new InvalidOperationException($"{name}: 스펠 재생 데이터와 바인딩 불일치");

        _manual = manual;
        _frame = 0;
        _requestedTime = 0f;
        _properties ??= new MaterialPropertyBlock();
        _motionStates = new SpellMotionEvaluator.State[_definition.Motions.Length];
        _deadParticles = new bool[_particles.Length];
        _warmedParticles = new bool[_particles.Length];
        _clock.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        for (int i = 0; i < _particles.Length; i++)
        {
            if (!_definition.Particles[i].Prewarm) continue;
            ParticleSystem.MainModule main = _particles[i].main;
            main.startDelay = _definition.Particles[i].StartMin * main.simulationSpeed;
        }
        for (int i = 0; i < _bindings.Length; i++)
        {
            SpellPose pose = _definition.Poses[i];
            _bindings[i].localPosition = pose.Position;
            _bindings[i].localRotation = pose.Rotation;
            _bindings[i].localScale = pose.Scale;
        }
        ApplySurfaces(0f);
        ApplyWind(0f);
        if (manual) _clock.Simulate(0f, true, true, false);
        else _clock.Play(true);
        ApplyPrewarm(0f, manual);
    }

    /// <summary>원하는 시점까지 고정 간격으로 미리보기</summary>
    /// <param name="time">재생 위치</param>
    public void PreviewAt(float time)
    {
        time = Mathf.Max(0f, time);
        if (_motionStates == null || !_manual || time < PlaybackTime) Restart(true);
        AdvanceTo(time, true);
    }

    void AdvanceTo(float time, bool simulate)
    {
        int targetFrame = Mathf.FloorToInt(time / BakedSpellDefinition.StepSeconds + 0.0001f);
        while (_frame < targetFrame)
        {
            _frame++;
            float current = PlaybackTime;
            for (int i = 0; i < _definition.Motions.Length; i++)
            {
                SpellMotion motion = _definition.Motions[i];
                Transform target = motion.Kind == SpellMotionKind.MoveToObject ? _bindings[motion.Target] : null;
                SpellMotionEvaluator.Step(motion, _bindings[motion.Binding], target, ref _motionStates[i], current);
            }
            ApplySurfaces(current);
            ApplyWind(current);
            ApplyPrewarm(current, simulate);
            if (simulate) _clock.Simulate(BakedSpellDefinition.StepSeconds, true, false, false);
            for (int i = 0; i < _particles.Length; i++)
            {
                if (!_deadParticles[i] && current >= _definition.Particles[i].Death)
                {
                    _particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _deadParticles[i] = true;
                }
            }
        }
    }

    void ApplySurfaces(float time)
    {
        for (int i = 0; i < _surfaces.Length; i++)
        {
            SpellSurface surface = _definition.Surfaces[i];
            _surfaces[i].enabled = surface.InitiallyEnabled && time >= surface.Birth && time < surface.Death;
            if (!surface.Dissolve) continue;
            _surfaces[i].GetPropertyBlock(_properties);
            _properties.SetFloat("_CutOut", SpellMotionEvaluator.Dissolve(time - surface.Birth, surface.FadeDelay, surface.FadeFactor));
            _surfaces[i].SetPropertyBlock(_properties);
            _properties.Clear();
        }
    }

    void ApplyWind(float time)
    {
        for (int i = 0; i < _winds.Length; i++)
        {
            SpellWind wind = _definition.Winds[i];
            bool active = time >= wind.Birth && time < wind.Death;
            _winds[i].windMain = active ? wind.Main : 0f;
            _winds[i].windTurbulence = active ? wind.Turbulence : 0f;
            _winds[i].windPulseMagnitude = active ? wind.PulseMagnitude : 0f;
        }
    }

    void ApplyPrewarm(float time, bool manual)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            SpellParticleTiming timing = _definition.Particles[i];
            if (!timing.Prewarm || _warmedParticles[i] || time + 0.00001f < timing.StartMin || time >= timing.Death) continue;
            ParticleSystem particle = _particles[i];
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particle.main;
            main.startDelay = 0f;
            // 예열 입자가 실제 생성 시점 전에 노출되는 Unity 기본 동작 보정
            particle.Simulate(0f, false, true, false);
            float remaining = main.duration / main.simulationSpeed;
            while (remaining > 0.000001f)
            {
                float step = Mathf.Min(BakedSpellDefinition.StepSeconds, remaining);
                particle.Simulate(step, false, false, false);
                remaining -= step;
            }
            if (!manual) particle.Play(false);
            _warmedParticles[i] = true;
        }
    }
}
