using System;
using UnityEngine;

/// <summary>
/// 플레이어 체력 화면의 연결과 변경 구독 및 개폐
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealthUIController : MonoBehaviour
{
    [Header("----- 화면 연결 -----")]
    [SerializeField] PlayerHealthView _view;

    CharacterStatus _status;
    bool _listening;

    public bool IsBound => _status != null;

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
            throw new MissingReferenceException($"{name}: PlayerHealthView 연결 필요");

        if (_view.gameObject != gameObject || !_view.enabled || !enabled)
            throw new InvalidOperationException($"{name}: 같은 HP 화면의 활성 View와 Controller 필요");

        _view.ValidateReferences();
    }

    void StartListening()
    {
        if (_listening)
            return;

        _status.OnHealthChanged += HandleHealthChanged;
        _listening = true;
    }

    void StopListening()
    {
        if (!_listening)
            return;

        _status.OnHealthChanged -= HandleHealthChanged;
        _listening = false;
    }

    void Refresh()
    {
        HandleHealthChanged(_status.CurrentHealth, _status.MaxHealth);
    }

    void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        _view.Show(currentHealth, maxHealth);
    }

    /// <summary>
    /// 초기화된 플레이어 전투 상태 연결
    /// </summary>
    /// <param name="status">체력을 표시할 플레이어 전투 상태</param>
    public void Bind(CharacterStatus status)
    {
        if (status == null)
            throw new ArgumentNullException(nameof(status));

        if (!status.IsInitialized)
            throw new InvalidOperationException($"{name}: 체력 화면 연결 전 CharacterStatus 초기화 필요");

        ValidateView();
        Unbind();

        _status = status;

        Refresh();

        if (isActiveAndEnabled)
            StartListening();
    }

    /// <summary>
    /// 체력 변경 구독과 전투 상태 연결 해제
    /// </summary>
    public void Unbind()
    {
        StopListening();

        _status = null;

        if (_view != null)
            _view.Clear();
    }

    /// <summary>
    /// 연결된 체력 화면 표시
    /// </summary>
    public void Open()
    {
        if (!IsBound)
            throw new InvalidOperationException($"{name}: 플레이어 체력 연결 완료 필요");

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 연결을 유지한 채 체력 화면 숨김
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }
}