using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 수명의 고정 수 단발음 소스와 재생 제한 관리
/// </summary>
[DisallowMultipleComponent]
public class SoundPlayer : MonoBehaviour
{
    [Header("----- 씬 재생 용량 -----")]
    [SerializeField, Min(1)] int _voiceCapacity = 24;

    // 게임플레이 난수 상태와 분리된 음원 선택
    readonly System.Random _random = new System.Random();
    readonly Dictionary<SoundCue, double> _lastPlayed = new Dictionary<SoundCue, double>();
    AudioSource[] _sources;
    SoundCue[] _playingCues;
    UnityEngine.Object[] _owners;

    void Awake()
    {
        if (_voiceCapacity < 1)
            throw new InvalidOperationException($"{name}: 단발음 재생 용량은 1 이상 필요");

        // 설정된 용량의 재생 전용 자식을 한 번 생성하고 씬 종료까지 재사용
        _sources = new AudioSource[_voiceCapacity];
        _playingCues = new SoundCue[_voiceCapacity];
        _owners = new UnityEngine.Object[_voiceCapacity];
        for (int index = 0; index < _voiceCapacity; index++)
        {
            GameObject voice = new GameObject($"AudioVoice_{index:00}");
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            _sources[index] = source;
        }
    }

    void ReleaseVoice(int index)
    {
        _sources[index].Stop();
        _sources[index].clip = null;
        _playingCues[index] = null;
        _owners[index] = null;
    }

    void OnDisable()
    {
        if (_sources == null) return;
        for (int index = 0; index < _sources.Length; index++)
            ReleaseVoice(index);
        _lastPlayed.Clear();
    }

    /// <summary>
    /// 단발음과 환경음이 공유하는 소스 출력 설정 적용
    /// </summary>
    /// <param name="source">설정을 적용할 재생 소스</param>
    /// <param name="cue">검증할 소리 설정</param>
    /// <param name="random">재생자가 소유한 연출 난수</param>
    public static void ConfigureSource(AudioSource source, SoundCue cue, System.Random random)
    {
        if (source == null || cue == null)
            throw new MissingReferenceException("AudioSource와 소리 설정 연결 필요");
        if (random == null) throw new ArgumentNullException(nameof(random));

        cue.ValidateConfiguration();
        source.playOnAwake = false;
        source.clip = cue.GetClip(random.Next(cue.ClipCount));
        source.outputAudioMixerGroup = cue.OutputGroup;
        source.volume = cue.Volume;
        source.pitch = Mathf.Lerp(cue.PitchRange.x, cue.PitchRange.y, (float)random.NextDouble());
        source.loop = cue.Loop;
        source.spatialBlend = cue.SpatialBlend;
        source.minDistance = cue.MinDistance;
        source.maxDistance = cue.MaxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = cue.IgnoreListenerPause;
        source.ignoreListenerVolume = false;
    }

    /// <summary>
    /// 소리별 간격과 동시 재생 제한을 적용한 단발음 요청
    /// </summary>
    /// <param name="cue">재생할 단발음 설정</param>
    /// <param name="worldPosition">월드 재생 위치, 2D 음원은 거리 영향 없음</param>
    /// <param name="owner">일괄 정지에 사용할 선택적 소유자, null이면 씬 수명</param>
    /// <returns>재생 시작 여부, 간격 또는 용량 제한이면 false</returns>
    public bool Play(SoundCue cue, Vector3 worldPosition, UnityEngine.Object owner = null)
    {
        if (cue == null) throw new MissingReferenceException($"{name}: 단발음 설정 연결 필요");
        if (!isActiveAndEnabled || _sources == null)
            throw new InvalidOperationException($"{name}: 활성화되고 초기화된 씬 재생기 필요");

        cue.ValidateConfiguration();
        if (cue.Loop)
            throw new InvalidOperationException($"{cue.name}: 반복음은 AmbientSoundPlayer에 연결 필요");

        double now = Time.unscaledTimeAsDouble;
        if (_lastPlayed.TryGetValue(cue, out double last) && now - last < cue.MinimumInterval)
            return false;

        int available = -1;
        int count = 0;
        for (int index = 0; index < _sources.Length; index++)
        {
            AudioSource source = _sources[index];
            // Listener 정지로 isPlaying이 false인 소스의 점유 보존
            bool occupied = source.isPlaying ||
                (AudioListener.pause && !source.ignoreListenerPause && _playingCues[index] != null);
            if (!occupied)
            {
                _playingCues[index] = null;
                _owners[index] = null;
                if (available < 0) available = index;
            }
            else if (_playingCues[index] == cue)
            {
                count++;
            }
        }

        if (available < 0 || count >= cue.MaximumVoices) return false;

        AudioSource selected = _sources[available];
        ConfigureSource(selected, cue, _random);
        selected.transform.position = worldPosition;
        selected.Play();
        _playingCues[available] = cue;
        _owners[available] = owner;
        _lastPlayed[cue] = now;
        return true;
    }

    /// <summary>
    /// 종료된 캐릭터의 소유 음원만 정지
    /// </summary>
    /// <param name="owner">재생을 요청했던 소유자</param>
    public void StopOwner(UnityEngine.Object owner)
    {
        if (ReferenceEquals(owner, null)) throw new ArgumentNullException(nameof(owner));
        if (_sources == null) return;

        for (int index = 0; index < _sources.Length; index++)
        {
            if (ReferenceEquals(_owners[index], owner))
                ReleaseVoice(index);
        }
    }
}
