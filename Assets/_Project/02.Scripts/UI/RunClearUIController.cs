using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전체 월드 클리어 표시와 로비 복귀를 위한 런 종료 요청
/// </summary>
[DisallowMultipleComponent]
public class RunClearUIController : MonoBehaviour
{
    [Header("----- 클리어 창 구성 -----")]
    [SerializeField] Canvas _canvas;
    [SerializeField] GraphicRaycaster _raycaster;
    [SerializeField] Button _returnButton;

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
        if (_canvas.enabled && !_submitting && !CanReturn()) Close();
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
        if (_canvas == null || _raycaster == null || _returnButton == null)
            throw new MissingReferenceException($"{name}: Canvas와 Raycaster 및 복귀 버튼 연결 필요");
        if (_canvas.transform != transform || _raycaster.transform != transform ||
            !_returnButton.transform.IsChildOf(transform))
            throw new InvalidOperationException("클리어 Canvas와 하위 복귀 버튼 연결 필요");
    }

    bool CanReturn()
    {
        return _world != null && _portal != null &&
            _portal.CanInteract(_actor) && _world.CanRequestRunClear();
    }

    void HandleReturn()
    {
        if (_submitting) return;
        if (!CanReturn())
        {
            Close();
            return;
        }

        _submitting = true;
        _returnButton.interactable = false;
        try
        {
            if (_world.TryRequestRunClear()) Close();
        }
        finally
        {
            _submitting = false;
            _returnButton.interactable = true;
        }
    }

    /// <summary>
    /// 같은 씬의 월드와 복귀 버튼 및 해제 알림 연결
    /// </summary>
    /// <param name="world">클리어 조건과 종료 요청 담당자</param>
    public void Bind(SessionProgressManager world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        ValidateReferences();
        if (_world != null || !isActiveAndEnabled || world.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬의 활성 미연결 클리어 창 필요");

        _world = world;
        _world.OnWorldReleased += Close;
        _returnButton.onClick.AddListener(HandleReturn);
        Close();
    }

    /// <summary>
    /// 접근 가능한 최종 보스 포탈의 클리어 창 표시
    /// </summary>
    /// <param name="portal">상호작용한 보스 포탈</param>
    /// <param name="actor">상호작용한 플레이어</param>
    public void Open(WorldPortal portal, SkillLoadout actor)
    {
        if (_world == null) throw new InvalidOperationException("클리어 창의 월드 연결 필요");
        if (portal == null) throw new ArgumentNullException(nameof(portal));
        if (actor == null) throw new ArgumentNullException(nameof(actor));
        if (_submitting || !isActiveAndEnabled) return;
        if (portal.gameObject.scene != gameObject.scene ||
            !portal.CanInteract(actor) || !_world.CanRequestRunClear()) return;

        _portal = portal;
        _actor = actor;
        _canvas.enabled = true;
        _raycaster.enabled = true;
    }

    /// <summary>
    /// 클리어 창 유지와 취소 입력 소비
    /// </summary>
    /// <returns>클리어 창이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        return _canvas.enabled;
    }

    /// <summary>
    /// 클리어 창과 포탈 접근 문맥 해제
    /// </summary>
    public void Close()
    {
        _canvas.enabled = false;
        _raycaster.enabled = false;
        _portal = null;
        _actor = null;
    }

    /// <summary>
    /// 월드 구독과 복귀 버튼 요청 해제
    /// </summary>
    public void Unbind()
    {
        Close();
        if (_world == null) return;
        _world.OnWorldReleased -= Close;
        _returnButton.onClick.RemoveListener(HandleReturn);
        _world = null;
    }
}
