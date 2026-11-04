using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로그인 버튼 사용 가능 상태와 진행 문구 표시
/// </summary>
public class LoginUIController : MonoBehaviour
{
    [Header("----- 로그인 연결 -----")]
    [SerializeField] GameBootstrap _bootstrap;
    [SerializeField] Button _googleLoginButton;
    [SerializeField] TextMeshProUGUI _statusText;

    void Awake()
    {
        if (_bootstrap == null || _googleLoginButton == null || _statusText == null)
            throw new MissingReferenceException($"{name}: Bootstrap, 로그인 버튼, 상태 텍스트 연결 필요");
    }

    void OnEnable()
    {
        _bootstrap.OnStateChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (_bootstrap != null)
            _bootstrap.OnStateChanged -= Refresh;
    }

    void Refresh()
    {
        _googleLoginButton.interactable = _bootstrap.CanRequestGoogleLogin;

        _statusText.text = _bootstrap.StatusMessage;
    }
}