using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 재생자가 공유하는 음원 후보와 출력 및 공간 설정
/// </summary>
[CreateAssetMenu(menuName = "Audio/Sound Cue", fileName = "SoundCue")]
public class SoundCue : ScriptableObject
{
    [Header("----- 음원과 출력 -----")]
    [SerializeField] AudioClip[] _clips;
    [SerializeField] AudioMixerGroup _outputGroup;
    [SerializeField, Range(0f, 1f)] float _volume = 0.5f;
    [SerializeField] Vector2 _pitchRange = Vector2.one;
    [SerializeField] bool _loop;
    [SerializeField] bool _ignoreListenerPause;

    [Header("----- 공간 설정 -----")]
    [SerializeField, Range(0f, 1f)] float _spatialBlend;
    [SerializeField, Min(0.01f)] float _minDistance = 3f;
    [SerializeField, Min(0.01f)] float _maxDistance = 30f;

    [Header("----- 단발음 재생 제한 -----")]
    [SerializeField, Min(0f)] float _minimumInterval = 0.04f;
    [SerializeField, Min(1)] int _maximumVoices = 4;

    public int ClipCount => _clips.Length;
    public AudioMixerGroup OutputGroup => _outputGroup;
    public float Volume => _volume;
    public Vector2 PitchRange => _pitchRange;
    public bool Loop => _loop;
    public bool IgnoreListenerPause => _ignoreListenerPause;
    public float SpatialBlend => _spatialBlend;
    public float MinDistance => _minDistance;
    public float MaxDistance => _maxDistance;
    public float MinimumInterval => _minimumInterval;
    public int MaximumVoices => _maximumVoices;

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 지정한 인덱스의 후보 음원 조회
    /// </summary>
    /// <param name="index">후보 인덱스</param>
    /// <returns>후보 음원</returns>
    public AudioClip GetClip(int index)
    {
        return _clips[index];
    }

    /// <summary>
    /// 필수 음원과 출력 설정 및 수치 범위 검증
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_clips == null || _clips.Length == 0 || _outputGroup == null)
            throw new MissingReferenceException($"{name}: 음원 후보와 AudioMixer 그룹 연결 필요");

        foreach (AudioClip clip in _clips)
        {
            if (clip == null)
                throw new MissingReferenceException($"{name}: 비어 있는 음원 후보 확인 필요");
        }

        if (!IsFinite(_volume) || _volume < 0f || _volume > 1f ||
            !IsFinite(_pitchRange.x) || !IsFinite(_pitchRange.y) ||
            _pitchRange.x <= 0f || _pitchRange.y < _pitchRange.x || _pitchRange.y > 3f ||
            !IsFinite(_spatialBlend) || _spatialBlend < 0f || _spatialBlend > 1f ||
            !IsFinite(_minDistance) || !IsFinite(_maxDistance) ||
            _minDistance <= 0f || _maxDistance <= _minDistance ||
            !IsFinite(_minimumInterval) || _minimumInterval < 0f || _maximumVoices < 1)
            throw new InvalidOperationException($"{name}: 음량, 피치, 거리와 재생 제한 범위 확인 필요");
    }
}
