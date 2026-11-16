using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 최초 재생 완료 이후 범위 효과 설정에 따른 파티클 반복 재생
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public class AreaEffectParticleRepeater : MonoBehaviour
{
    ParticleSystem _root;
    Coroutine _repeatRoutine;

    void Awake()
    {
        _root = GetComponent<ParticleSystem>();
    }

    void OnDisable()
    {
        StopRepeating();
    }

    void StopRepeating()
    {
        if (_repeatRoutine == null)
            return;

        StopCoroutine(_repeatRoutine);
        _repeatRoutine = null;
    }

    bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    void ValidateRepeatSettings(AreaEffectDefinition definition)
    {
        if (!IsPositiveFinite(definition.PulseInterval))
            throw new InvalidOperationException($"{definition.name}: 유한한 양수의 반복 간격 필요");

        switch (definition.EndMode)
        {
            case AreaEffectEndMode.Duration:
                if (!IsPositiveFinite(definition.Duration))
                    throw new InvalidOperationException($"{definition.name}: 유한한 양수의 지속시간 필요");
                break;
            case AreaEffectEndMode.PulseCount:
                if (definition.PulseCount < 1)
                    throw new InvalidOperationException($"{definition.name}: 1 이상의 반복 횟수 필요");
                break;
            default:
                throw new InvalidOperationException($"{definition.name}: 지원하지 않는 종료 기준");
        }

        ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem particle in particles)
        {
            if (particle.gameObject.activeInHierarchy && particle.main.loop)
                throw new InvalidOperationException($"{name}: 반복 재생 대상 {particle.name}의 Looping 해제 필요");
        }
    }

    bool CanRepeat(AreaEffectDefinition definition, int playbackCount, double endsAt)
    {
        if (definition.EndMode == AreaEffectEndMode.Duration)
            return Time.timeAsDouble < endsAt;

        return playbackCount < definition.PulseCount;
    }

    IEnumerator RepeatAfterCompletion(AreaEffectDefinition definition)
    {
        // CombatEffect에서 이미 재생한 첫 회차
        int playbackCount = 1;
        double startedAt = Time.timeAsDouble;
        double endsAt = startedAt + definition.Duration;
        double nextPlayAt = startedAt + definition.PulseInterval;

        while (CanRepeat(definition, playbackCount, endsAt))
        {
            double now = Time.timeAsDouble;

            if (Time.timeScale > 0f && now >= nextPlayAt)
            {
                _root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _root.Play(true);

                playbackCount++;
                nextPlayAt = now + definition.PulseInterval;
            }

            yield return null;
        }

        _repeatRoutine = null;
    }

    /// <summary>
    /// 기존 최초 재생 이후 추가 반복 처리 시작
    /// 설정 없음 또는 Once에서는 파티클 재생에 개입하지 않음
    /// </summary>
    /// <param name="definition">이번 재생의 선택적 범위 효과 설정</param>
    public void BeginAfterInitialPlay(AreaEffectDefinition definition)
    {
        StopRepeating();

        if (!isActiveAndEnabled || definition == null)
            return;

        if (definition.DamageMode == AreaEffectDamageMode.Once)
            return;

        if (definition.DamageMode != AreaEffectDamageMode.Repeat)
            throw new InvalidOperationException($"{definition.name}: 지원하지 않는 피해 실행 방식");

        ValidateRepeatSettings(definition);

        _repeatRoutine = StartCoroutine(RepeatAfterCompletion(definition));
    }
}