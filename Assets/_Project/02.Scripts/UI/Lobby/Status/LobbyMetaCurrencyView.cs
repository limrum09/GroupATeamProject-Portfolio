using TMPro;
using UnityEngine;

/// <summary>
/// 현재 보유한 메타 재화 표시
/// </summary>
[DisallowMultipleComponent]
public class LobbyMetaCurrencyView : MonoBehaviour
{
    [Header("----- 메타 재화 -----")]
    [SerializeField] TextMeshProUGUI _currencyText;

    /// <summary>
    /// 재화 텍스트 연결 검사
    /// </summary>
    public void ValidateBindings()
    {
        if (_currencyText == null)
            throw new MissingReferenceException($"{name}: 메타 재화 텍스트 연결 필요");
    }

    /// <summary>
    /// 현재 보유 재화 표시
    /// </summary>
    /// <param name="balance">현재 메타 재화 잔액</param>
    public void ShowBalance(long balance)
    {
        _currencyText.text = balance.ToString("N0");
    }

    /// <summary>
    /// 메타 재화 정보를 사용할 수 없는 상태 표시
    /// </summary>
    public void ShowUnavailable()
    {
        _currencyText.text = "—";
    }
}