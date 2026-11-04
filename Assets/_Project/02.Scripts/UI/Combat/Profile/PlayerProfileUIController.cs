using System;
using UnityEngine;

/// <summary>
/// 플레이어 프로필의 런 연결과 변경 구독 및 화면 개폐
/// </summary>
[DisallowMultipleComponent]
public class PlayerProfileUIController : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] PlayerProfileView _view;

    PlayerDefinition _definition;
    RunLevelProgression _progression;

    bool _listening;

    public bool IsBound => _definition != null && _progression != null;

    void OnEnable()
    {
        if (!IsBound)
            return;

        Refresh();
        StartListening();
    }

    void OnDisable()
    {
        StopListening();
    }

    void OnDestroy()
    {
        StopListening();
    }

    void ValidateView()
    {
        if (_view == null)
            throw new MissingReferenceException($"{name}: PlayerProfileView 연결 필요");

        if (_view.gameObject != gameObject || !_view.enabled || !enabled)
            throw new InvalidOperationException($"{name}: 같은 PlayerProfile의 활성 View와 Controller 필요");

        _view.ValidateReferences();
    }

    void StartListening()
    {
        if (_listening)
            return;

        _progression.OnChanged += Refresh;
        _listening = true;
    }

    void StopListening()
    {
        if (!_listening)
            return;

        _progression.OnChanged -= Refresh;
        _listening = false;
    }

    void Refresh()
    {
        float experienceRatio = _progression.IsMaxLevel ? 1f : _progression.Experience / _progression.RequiredExperience;

        _view.Show(_definition.Icon, _progression.Level, experienceRatio);
    }

    /// <summary>
    /// 캐릭터 정의와 이번 런의 성장 상태 연결
    /// </summary>
    /// <param name="definition">표시할 캐릭터 정의</param>
    /// <param name="progression">이번 런의 성장 상태</param>
    public void Bind(PlayerDefinition definition, RunLevelProgression progression)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (progression == null)
            throw new ArgumentNullException(nameof(progression));

        if (definition.Icon == null)
            throw new MissingReferenceException($"{definition.name}: 프로필 Icon 설정 필요");

        ValidateView();
        Unbind();

        _definition = definition;
        _progression = progression;

        // 런 시작 전 준비 단계에서도 현재 상태 표시
        Refresh();

        if (isActiveAndEnabled)
            StartListening();
    }

    /// <summary>
    /// 성장 상태 구독과 프로필 연결 해제
    /// </summary>
    public void Unbind()
    {
        StopListening();

        _definition = null;
        _progression = null;

        if (_view != null)
            _view.Clear();
    }

    /// <summary>
    /// 연결된 프로필 화면 표시
    /// </summary>
    public void Open()
    {
        if (!IsBound)
            throw new InvalidOperationException($"{name}: 프로필 연결 완료 필요");

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 연결을 유지한 채 프로필 화면 숨김
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }
}