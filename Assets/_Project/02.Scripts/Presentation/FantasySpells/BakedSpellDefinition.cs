using System;
using UnityEngine;

/// <summary>미리 조립한 스펠의 생성 시점과 시각 동작 설정</summary>
public sealed class BakedSpellDefinition : ScriptableObject
{
    public const float StepSeconds = 1f / 60f;

    public string SourcePath;
    public float LastSpawnSeconds;
    public float EndSeconds;
    public bool IsPersistent;
    public SpellPose[] Poses = Array.Empty<SpellPose>();
    public SpellMotion[] Motions = Array.Empty<SpellMotion>();
    public SpellSurface[] Surfaces = Array.Empty<SpellSurface>();
    public SpellParticleTiming[] Particles = Array.Empty<SpellParticleTiming>();
    public SpellWind[] Winds = Array.Empty<SpellWind>();
}

/// <summary>생성 시점에 확정된 로컬 배치</summary>
[Serializable]
public struct SpellPose
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
}

/// <summary>원본 스펠의 시각 동작 종류</summary>
public enum SpellMotionKind
{
    MoveToObject,
    Translate,
    Rotate,
    Scale
}

/// <summary>생성 시점 기준의 이동과 크기 변화 설정</summary>
[Serializable]
public struct SpellMotion
{
    public SpellMotionKind Kind;
    public int Binding;
    public int Target;
    public float Birth;
    public float Death;
    public float Delay;
    public float Duration;
    public float Power;
    public float Look;
    public Vector3 Vector;
    public Vector3 Axes;
}

/// <summary>메시 표시 구간과 재질 소멸 설정</summary>
[Serializable]
public struct SpellSurface
{
    public float Birth;
    public float Death;
    public bool InitiallyEnabled;
    public bool Dissolve;
    public float FadeDelay;
    public float FadeFactor;
}

/// <summary>파티클의 계산된 시작과 최대 종료 시점</summary>
[Serializable]
public struct SpellParticleTiming
{
    public string Name;
    public float Birth;
    public float StartMin;
    public float StartMax;
    public float End;
    public float Death;
    public bool Looping;
    public bool Prewarm;
}

/// <summary>바람 파츠의 생성 구간과 원본 세기</summary>
[Serializable]
public struct SpellWind
{
    public float Birth;
    public float Death;
    public float Main;
    public float Turbulence;
    public float PulseMagnitude;
}
