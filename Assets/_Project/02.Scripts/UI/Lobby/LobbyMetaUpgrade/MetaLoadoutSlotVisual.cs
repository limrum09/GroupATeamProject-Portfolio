using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 왼쪽 장착 슬롯의 프레임과 잠금 아이콘 표시
/// </summary>
[DisallowMultipleComponent]
public class MetaLoadoutSlotVisual : MonoBehaviour
{
    [Header("----- 표시 대상 -----")]
    [SerializeField] Image _frameImage;
    [SerializeField] Image _lockIcon;

    [Header("----- 상태별 프레임 -----")]
    [SerializeField] Sprite _lockedFrame;
    [SerializeField] Sprite _emptyFrame;
    [SerializeField] Sprite _equippedFrame;

    /// <summary>
    /// 슬롯 해금 여부와 강화 값 유무에 따른 표시 갱신
    /// </summary>
    /// <param name="unlocked">슬롯 해금 여부</param>
    /// <param name="hasUpgrade">슬롯에 강화 값이 들어 있는지 여부</param>
    public void Show(bool unlocked, bool hasUpgrade)
    {
        float size = unlocked ? 70f : 48f;
        _frameImage.rectTransform.sizeDelta = new Vector2(size, size);

        if (!unlocked)
            _frameImage.sprite = _lockedFrame;
        else if (hasUpgrade)
            _frameImage.sprite = _equippedFrame;
        else
            _frameImage.sprite = _emptyFrame;

        _lockIcon.enabled = !unlocked;
    }
}