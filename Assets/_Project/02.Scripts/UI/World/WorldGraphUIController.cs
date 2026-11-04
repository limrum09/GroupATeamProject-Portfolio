using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 세션 월드 진행과 지도 화면 연결 및 개폐
/// </summary>
[DisallowMultipleComponent]
public class WorldGraphUIController : MonoBehaviour
{
    [Header("----- 월드맵 화면 -----")]
    [SerializeField] WorldGraphView _view;

    bool _cancelInputRouted;
    SessionProgressManager _world;

    void LateUpdate()
    {
        if (_world == null || !_view.IsOpen || Time.timeScale <= 0f)
            return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool closePressed = !_cancelInputRouted && keyboard != null &&
            keyboard.escapeKey.wasPressedThisFrame;
        bool movePressed = mouse != null &&
            mouse.rightButton.wasPressedThisFrame;

        if (closePressed || movePressed)
            TryHandleCancel();
    }

    /// <summary>
    /// 월드 진행과 지도 화면 연결
    /// </summary>
    /// <param name="world">표시할 월드 진행 담당자</param>
    public void Bind(SessionProgressManager world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (_view == null) throw new MissingReferenceException("WorldGraphView 연결 필요");
        if (_world != null || !isActiveAndEnabled || !_view.isActiveAndEnabled ||
            world.gameObject.scene != gameObject.scene || _view.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException("같은 씬의 미연결 Controller와 활성 지도 화면 필요");
        _world = world;
        _view.Bind(world);
    }

    /// <summary>
    /// 준비된 월드의 지도 화면 표시
    /// </summary>
    public void Open()
    {
        if (_world == null) throw new InvalidOperationException("월드 UI 연결 필요");
        if (!_world.IsReady || _world.IsTransitioning) return;
        _view.Open();
    }

    /// <summary>
    /// 씬 라우터의 취소 입력 처리 여부 지정
    /// </summary>
    /// <param name="routed">라우터 연결 여부</param>
    public void SetCancelInputRouted(bool routed)
    {
        _cancelInputRouted = routed;
    }

    /// <summary>
    /// 열린 월드맵의 취소 입력 처리
    /// </summary>
    /// <returns>월드맵이 취소 입력을 소비했는지 여부</returns>
    public bool TryHandleCancel()
    {
        if (!_view.IsOpen) return false;

        Close();
        return true;
    }

    /// <summary>
    /// 지도 화면 숨김
    /// </summary>
    public void Close()
    {
        _view.Close();
    }

    /// <summary>
    /// 월드 진행과 지도 화면 연결 해제
    /// </summary>
    public void Unbind()
    {
        _world = null;
        if (_view != null) _view.Unbind();
    }
}
