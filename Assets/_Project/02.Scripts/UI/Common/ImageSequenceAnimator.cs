using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Image의 Sprite를 고정 간격으로 순차 교체하는 반복 재생
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class ImageSequenceAnimator : MonoBehaviour
{
    [Header("----- 재생 설정 -----")]
    [Tooltip("재생 순서대로 나열한 Sprite")]
    [SerializeField] Sprite[] _frames;
    [Tooltip("초당 표시 프레임 수")]
    [SerializeField] float _framesPerSecond = 12f;

    Image _image;

    float _elapsed;
    int _frameIndex;

    void Awake()
    {
        if (_frames == null || _frames.Length == 0)
            throw new MissingReferenceException($"{name}: 재생 Sprite 연결 필요");

        foreach (Sprite frame in _frames)
        {
            if (frame == null)
                throw new MissingReferenceException($"{name}: 비어 있는 재생 Sprite 칸 연결 필요");
        }

        if (_framesPerSecond <= 0f)
            throw new InvalidOperationException($"{name}: 0보다 큰 초당 프레임 수 필요");

        _image = GetComponent<Image>();
    }

    void OnEnable()
    {
        // 재표시마다 첫 프레임부터 재생
        _elapsed = 0f;
        ShowFrame(0);
    }

    void Update()
    {
        // 메뉴 일시정지의 timeScale 영향 제외
        _elapsed += Time.unscaledDeltaTime;

        float frameDuration = 1f / _framesPerSecond;
        if (_elapsed < frameDuration) return;

        // 로딩 중 긴 프레임 정지 이후에도 경과 시간만큼 건너뛰어 재생 속도 유지
        int advanced = Mathf.FloorToInt(_elapsed / frameDuration);
        _elapsed -= advanced * frameDuration;
        ShowFrame((_frameIndex + advanced) % _frames.Length);
    }

    void ShowFrame(int index)
    {
        _frameIndex = index;
        _image.sprite = _frames[index];
    }
}
