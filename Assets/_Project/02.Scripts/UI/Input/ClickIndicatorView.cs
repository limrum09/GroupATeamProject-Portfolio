using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스프라이트 시트 클릭 표시의 재생과 풀 재사용 상태 초기화
/// </summary>
[DisallowMultipleComponent]
public class ClickIndicatorView : MonoBehaviour
{
    static readonly int PlaybackState = Animator.StringToHash("Base Layer.Play");

    [Header("----- 화면 연결 -----")]
    [SerializeField] RectTransform _rect;
    [SerializeField] CanvasGroup _group;
    [SerializeField] Image _image;

    [Header("----- 애니메이션 연결 -----")]
    [SerializeField] Animator _animator;
    [SerializeField] AnimationClip _clip;
    [SerializeField] Sprite _firstFrame;

    bool _playing;

    void OnDisable()
    {
        _playing = false;
        _animator.enabled = false;
        _group.alpha = 0f;
        _image.sprite = _firstFrame;
    }

    /// <summary>
    /// UI 참조와 단일 비반복 애니메이션 구성 검사
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_rect == null || _group == null || _image == null ||
            _animator == null || _clip == null || _firstFrame == null)
        {
            throw new MissingReferenceException(
                $"{name}: RectTransform, CanvasGroup, Image, Animator, Clip과 첫 Sprite 연결 필요");
        }

        if (_rect != transform || _group.gameObject != gameObject ||
            _image.transform != transform || _animator.transform != transform ||
            _rect.anchorMin != Vector2.one * 0.5f ||
            _rect.anchorMax != Vector2.one * 0.5f ||
            _group.interactable || _group.blocksRaycasts || _image.raycastTarget ||
            _image.type != Image.Type.Simple || _image.preserveAspect)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 오브젝트의 표시 참조, 중앙 Anchor와 비상호작용 Simple Image 설정 필요");
        }

        // 렌더링된 원 중심을 클릭 좌표에 맞추는 공통 피벗
        Vector2 spritePivot = new Vector2(
            _firstFrame.pivot.x / _firstFrame.rect.width,
            _firstFrame.pivot.y / _firstFrame.rect.height);
        if ((_rect.pivot - spritePivot).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException($"{name}: RectTransform과 Sprite의 원 중심 Pivot 일치 필요");

        if (_animator.runtimeAnimatorController == null ||
            _animator.updateMode != AnimatorUpdateMode.Normal ||
            _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate ||
            _animator.applyRootMotion || _clip.isLooping || _clip.length <= 0f)
        {
            throw new InvalidOperationException(
                $"{name}: Normal 시간, AlwaysAnimate, RootMotion 해제와 비반복 Clip 및 Controller 필요");
        }

        AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
        if (clips.Length != 1 || clips[0] != _clip)
            throw new InvalidOperationException($"{name}: 지정 Clip 하나를 재생하는 Controller 필요");
    }

    /// <summary>
    /// 활성화된 풀 표시의 첫 Sprite와 색상 및 재생 시간을 초기화하고 재생
    /// </summary>
    /// <param name="color">명령 종류에 따른 표시 색상</param>
    public void Begin(Color color)
    {
        if (!isActiveAndEnabled)
            throw new InvalidOperationException($"{name}: 표시 활성화 이후 Begin 호출 필요");

        ValidateConfiguration();

        _playing = false;
        _image.sprite = _firstFrame;
        _image.color = color;
        _group.alpha = 1f;
        _animator.speed = 1f;
        _animator.enabled = true;

        // 연속 클릭으로 중단된 상태도 첫 프레임부터 즉시 재평가
        _animator.Play(PlaybackState, 0, 0f);
        _animator.Update(0f);
        _playing = true;
    }

    /// <summary>
    /// Animator의 실제 재생 완료 여부 확인
    /// </summary>
    /// <returns>풀 반환 가능 여부</returns>
    public bool Tick()
    {
        return !_playing || _animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
    }

    /// <summary>
    /// 투영 위치와 화면 내 가시성 반영
    /// </summary>
    /// <param name="position">표시 루트 기준 좌표</param>
    /// <param name="visible">화면 내 표시 여부</param>
    public void SetPresentation(Vector2 position, bool visible)
    {
        _rect.anchoredPosition = position;
        _group.alpha = visible ? 1f : 0f;
    }
}