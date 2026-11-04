using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 포탈의 다음 월드 확인창과 Yes·No 요청 처리
/// </summary>
[DisallowMultipleComponent]
public class NextWorldUIController : MonoBehaviour
{
    [Header("----- 확인창 구성 -----")]
    [SerializeField] Canvas _canvas;
    [SerializeField] GraphicRaycaster _raycaster;
    [SerializeField] Button _yesButton;
    [SerializeField] Button _noButton;

    SessionProgressManager _world;
    WorldPortal _portal;
    SkillLoadout _actor;
    bool _submitting;

    void Awake()
    {
        ValidateReferences();
        Close();
    }

    void Update()
    {
        if (_canvas.enabled && !_submitting && !CanConfirm())
            Close();
    }

    void OnDisable()
    {
        Close();
    }

    void OnDestroy()
    {
        Unbind();
    }

    void ValidateReferences()
    {
        if (_canvas == null || _raycaster == null || _yesButton == null || _noButton == null)
            throw new MissingReferenceException($"{name}: Canvas와 Raycaster 및 Yes·No 버튼 연결 필요");
        if (_canvas.transform != transform || _raycaster.transform != transform ||
            !_yesButton.transform.IsChildOf(transform) || !_noButton.transform.IsChildOf(transform) ||
            _yesButton == _noButton)
            throw new InvalidOperationException($"{name}: 확인창 Canvas와 하위의 서로 다른 버튼 연결 필요");
    }

    bool CanConfirm()
    {
        return _world != null && _portal != null &&
            _portal.CanInteract(_actor) && _world.CanEnterNextWorld();
    }

    void HandleYes()
    {
        if (_submitting) return;
        if (!CanConfirm())
        {
            Close();
            return;
        }

        _submitting = true;
        _yesButton.interactable = false;
        try
        {
            if (_world.TryEnterNextWorld())
                Close();
        }
        finally
        {
            _submitting = false;
            _yesButton.interactable = true;
        }
    }

    void HandleNo()
    {
        if (!_submitting)
            Close();
    }

    /// <summary>
    /// 같은 씬의 월드 진행과 확인 버튼 및 월드 해제 알림 연결
    /// </summary>
    /// <param name="world">진입 요청을 처리할 월드 진행 담당자</param>
    public void Bind(SessionProgressManager world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        ValidateReferences();
        if (_world != null || !isActiveAndEnabled || world.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬의 활성 미연결 확인창 필요");

        _world = world;
        _world.OnWorldReleased += Close;
        _yesButton.onClick.AddListener(HandleYes);
        _noButton.onClick.AddListener(HandleNo);
        Close();
    }

    /// <summary>
    /// 접근 가능한 보스 포탈의 다음 월드 확인창 표시
    /// </summary>
    /// <param name="portal">상호작용을 시작한 보스 포탈</param>
    /// <param name="actor">상호작용을 요청한 플레이어</param>
    public void Open(WorldPortal portal, SkillLoadout actor)
    {
        if (_world == null) throw new InvalidOperationException("확인창의 월드 연결 필요");
        if (portal == null) throw new ArgumentNullException(nameof(portal));
        if (actor == null) throw new ArgumentNullException(nameof(actor));
        if (_submitting || !isActiveAndEnabled) return;
        if (portal.gameObject.scene != gameObject.scene ||
            !portal.CanInteract(actor) || !_world.CanEnterNextWorld())
            return;

        _portal = portal;
        _actor = actor;
        _canvas.enabled = true;
        _raycaster.enabled = true;
    }

    /// <summary>
    /// 다음 월드 확인 취소 또는 요청 처리 중 입력 소비
    /// </summary>
    /// <returns>확인창이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        if (!_canvas.enabled) return false;

        HandleNo();
        return true;
    }

    /// <summary>
    /// 확인창과 이전 포탈·플레이어 선택 해제
    /// </summary>
    public void Close()
    {
        _canvas.enabled = false;
        _raycaster.enabled = false;
        _portal = null;
        _actor = null;
    }

    /// <summary>
    /// 월드 구독과 버튼 요청 연결 해제
    /// </summary>
    public void Unbind()
    {
        Close();
        if (_world == null) return;
        _world.OnWorldReleased -= Close;
        _yesButton.onClick.RemoveListener(HandleYes);
        _noButton.onClick.RemoveListener(HandleNo);
        _world = null;
    }
}
