using System;
using UnityEngine;


/// <summary>
/// 로비 화면의 공통 표시 기능
/// </summary>
public interface ILobbyView
{
    /// <summary>
    /// 화면 열기
    /// </summary>
    void Open();

    /// <summary>
    /// 화면 닫기
    /// </summary>
    void Close();
}

/// <summary>
/// 로비에서 전환할 화면 종류
/// </summary>
public enum LobbyPanelType
{
    Home,
    Character,
    Story,
    Quest,
    MetaUpgrade,
    Menu,
    Settings
}

/// <summary>
/// 로비 화면 연결과 선택적인 카메라 이동 지점
/// </summary>
[Serializable]
public class LobbyPanel
{
    [Header("----- 패널 설정 -----")]
    [SerializeField] LobbyPanelType _type;
    [SerializeField] GameObject _viewRoot;

    [Header("----- 선택 기능: 카메라 이동 -----")]
    [Tooltip("비워 두면 현재 카메라 구도 유지")]
    [SerializeField] Transform _cameraTarget;

    ILobbyView _view;

    public LobbyPanelType Type => _type;
    public GameObject ViewRoot => _viewRoot;
    public Transform CameraTarget => _cameraTarget;

    /// <summary>
    /// 연결한 오브젝트의 로비 View 확인과 참조 보관
    /// </summary>
    public void ValidateBindings()
    {
        if (_viewRoot == null)
            throw new MissingReferenceException($"{_type}: 화면 오브젝트 연결 필요");

        _view = _viewRoot.GetComponent<ILobbyView>();

        if (_view == null)
            throw new MissingReferenceException($"{_viewRoot.name}: ILobbyView를 구현한 View 연결 필요");
    }

    /// <summary>
    /// 연결된 View의 화면 표시 변경
    /// </summary>
    /// <param name="visible">화면 표시 여부</param>
    public void SetVisible(bool visible)
    {
        if (visible)
            _view.Open();
        else
            _view.Close();
    }
}