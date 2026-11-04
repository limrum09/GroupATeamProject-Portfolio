using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 현재 씬의 취소 입력 감지와 요청 전달
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class UICancelInputRouter : MonoBehaviour
{
    [Header("----- 취소 입력 -----")]
    [SerializeField] Key _cancelKey = Key.Escape;

    Func<bool> _canHandleInput;
    Action _handleCancel;

    public bool IsBound => _canHandleInput != null;

    void Update()
    {
        if (!IsBound || !_canHandleInput()) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[_cancelKey].wasPressedThisFrame) return;

        _handleCancel();
    }

    void Awake()
    {
        if (_cancelKey == Key.None)
            throw new InvalidOperationException($"{name}: 취소 입력 키 지정 필요");
    }

    void OnDestroy()
    {
        Unbind();
    }

    /// <summary>
    /// 현재 씬의 취소 입력 처리 연결
    /// </summary>
    /// <param name="canHandleInput">입력 허용 조건</param>
    /// <param name="handleCancel">씬 취소 처리</param>
    public void Bind(Func<bool> canHandleInput, Action handleCancel)
    {
        if (canHandleInput == null) throw new ArgumentNullException(nameof(canHandleInput));
        if (handleCancel == null) throw new ArgumentNullException(nameof(handleCancel));
        if (!isActiveAndEnabled || IsBound)
            throw new InvalidOperationException($"{name}: 활성 상태의 미연결 입력 객체 필요");

        _canHandleInput = canHandleInput;
        _handleCancel = handleCancel;
    }

    /// <summary>
    /// 이전 세션의 취소 대상과 콜백 연결 해제
    /// </summary>
    public void Unbind()
    {
        _canHandleInput = null;
        _handleCancel = null;
    }
}
