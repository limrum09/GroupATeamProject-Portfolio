using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬 전환과 준비 구간의 진행 단계 표시와 화면 입력 차단
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup))]
public class LoadingScreenView : MonoBehaviour
{
    [Header("----- 화면 구성 -----")]
    [Tooltip("전체 화면 배경과 진행 표시를 포함한 표시 대상, 배경 Image의 Raycast Target 활성 필요")]
    [SerializeField] GameObject _content;
    [Tooltip("loading_bar_fill, Image Type Filled / Horizontal / Left 설정")]
    [SerializeField] Image _progressFill;
    [SerializeField] TMP_Text _stageText;

    [Header("----- 진행 표시 -----")]
    [Tooltip("초당 채움 비율")]
    [SerializeField] float _fillSpeed = 1.5f;

    [Header("----- 사라짐 연출 -----")]
    [Tooltip("초 단위 FadeOut 시간")]
    [SerializeField] float _fadeOutDuration = 0.4f;

    // 요청 담당이 지정한 목표 진행률과 화면에 보간 중인 진행률
    float _targetProgress;
    float _displayedProgress;

    CanvasGroup _canvasGroup;
    Tween _fadeTween;

    // 표시 중이며 FadeOut이 진행되지 않는 상태
    public bool IsVisible => _content.activeSelf && _fadeTween == null;

    // 목표 진행률까지 표시가 따라온 상태
    public bool IsFillSettled => _displayedProgress >= _targetProgress;

    void Awake()
    {
        if (_content == null || _progressFill == null || _stageText == null)
            throw new MissingReferenceException($"{name}: 표시 대상, 진행 바, 단계 문구 연결 필요");

        if (_progressFill.type != Image.Type.Filled)
            throw new InvalidOperationException($"{name}: 진행 바 Image Type Filled 설정 필요");

        if (_fillSpeed <= 0f)
            throw new InvalidOperationException($"{name}: 0보다 큰 채움 속도 필요");

        if (_fadeOutDuration <= 0f)
            throw new InvalidOperationException($"{name}: 0보다 큰 FadeOut 시간 필요");

        _canvasGroup = GetComponent<CanvasGroup>();
        _content.SetActive(false);
    }

    void Update()
    {
        if (!IsVisible || IsFillSettled) return;

        // 메뉴 일시정지의 timeScale 영향 제외
        _displayedProgress = Mathf.MoveTowards(_displayedProgress, _targetProgress, _fillSpeed * Time.unscaledDeltaTime);
        _progressFill.fillAmount = _displayedProgress;
    }

    void OnDestroy()
    {
        KillFade();
    }

    void SetDisplayed(float progress)
    {
        _targetProgress = progress;
        _displayedProgress = progress;
        _progressFill.fillAmount = progress;
    }

    void KillFade()
    {
        if (_fadeTween == null) return;

        _fadeTween.Kill();
        _fadeTween = null;
    }

    /// <summary>
    /// 지정 진행률에서 보간 없이 로딩 화면 표시 시작
    /// </summary>
    /// <param name="stage">현재 준비 단계 문구</param>
    /// <param name="progress">0~1 시작 진행률</param>
    public void Show(string stage, float progress)
    {
        // 진행 중인 FadeOut 취소 후 즉시 불투명 표시
        KillFade();
        _canvasGroup.alpha = 1f;

        SetDisplayed(Mathf.Clamp01(progress));
        _stageText.text = stage;
        _content.SetActive(true);
    }

    /// <summary>
    /// 단계 문구와 목표 진행률 갱신, 진행률 감소 무시
    /// </summary>
    /// <param name="stage">현재 준비 단계 문구</param>
    /// <param name="progress">0~1 목표 진행률</param>
    public void SetStage(string stage, float progress)
    {
        _targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp01(progress));
        _stageText.text = stage;
    }

    /// <summary>
    /// 보간 생략 즉시 완료 표시
    /// </summary>
    public void CompleteFill()
    {
        SetDisplayed(1f);
    }

    /// <summary>
    /// FadeOut 후 로딩 화면 표시 해제, 진행 중인 FadeOut 유지
    /// </summary>
    public void Hide()
    {
        if (!_content.activeSelf || _fadeTween != null) return;

        // FadeOut 완료까지 입력 차단 유지, 메뉴 일시정지의 timeScale 영향 제외
        _fadeTween = _canvasGroup.DOFade(0f, _fadeOutDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(UpdateType.Normal, true)
            .OnComplete(() => _content.SetActive(false))
            .OnKill(() => _fadeTween = null);
    }
}
