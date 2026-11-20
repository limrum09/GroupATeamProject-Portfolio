using UnityEngine;

/// <summary>
/// 환경 오브젝트의 활성 수명에 따른 반복음 재생
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class AmbientSoundPlayer : MonoBehaviour
{
    [Header("----- 반복음 연결 -----")]
    [SerializeField] AudioSource _source;
    [SerializeField] SoundCue _cue;

    readonly System.Random _random = new System.Random();

    void OnEnable()
    {
        if (_source == null || _source.gameObject != gameObject || _cue == null)
            throw new MissingReferenceException($"{name}: 같은 오브젝트의 AudioSource와 반복음 설정 연결 필요");

        _cue.ValidateConfiguration();
        if (!_cue.Loop)
            throw new System.InvalidOperationException($"{name}: Loop가 켜진 환경음 설정 필요");

        SoundPlayer.ConfigureSource(_source, _cue, _random);
        _source.Play();
    }

    void OnDisable()
    {
        // 파괴 순서에 의해 먼저 해제된 소스만 제외
        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }
    }
}
