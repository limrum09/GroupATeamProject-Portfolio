using UnityEngine;

/// <summary>
/// 다른 로비 화면을 닫은 기본 홈 상태 표시
/// </summary>
[DisallowMultipleComponent]
public class HomeView : MonoBehaviour, ILobbyView
{
    /// <summary>
    /// 홈 화면 활성화
    /// </summary>
    public void Open()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 홈 화면 비활성화
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }
}