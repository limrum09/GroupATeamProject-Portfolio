using TMPro;
using UnityEngine;

/// <summary>
/// 잠긴 메타 슬롯의 해금 레벨 안내와 드래그 아이콘 가시성 표시
/// </summary>
[DisallowMultipleComponent]
public class MetaUnlockHintView : MonoBehaviour
{
    [Header("----- 표시 대상 -----")]
    [SerializeField] CanvasGroup _dragIconGroup;
    [SerializeField] TextMeshProUGUI _hintText;

    [Header("----- 슬롯 오른쪽 여백 -----")]
    [SerializeField] float _spacing = 8f;

    void OnDisable()
    {
        // 화면 종료 중 먼저 파괴된 형제 오브젝트 제외
        if (_hintText != null)
            _hintText.enabled = false;

        if (_dragIconGroup != null)
            _dragIconGroup.alpha = 1f;
    }

    /// <summary>
    /// 잠긴 슬롯 오른쪽에 해금 레벨 표시
    /// </summary>
    /// <param name="slotRect">안내를 표시할 슬롯 영역</param>
    /// <param name="unlockLevel">해금에 필요한 캐릭터 숙련도 레벨</param>
    public void Show(RectTransform slotRect, int unlockLevel)
    {
        Rect rect = slotRect.rect;
        Vector3 textPosition = new Vector3(rect.xMax + _spacing, rect.center.y, 0f);

        _hintText.rectTransform.position = slotRect.TransformPoint(textPosition);
        _hintText.SetText("캐릭터 숙련도 Lv.{0}에 해금", unlockLevel);
        _hintText.enabled = true;
        _dragIconGroup.alpha = 0f;
    }

    /// <summary>
    /// 해금 안내 종료와 드래그 아이콘 투명도 복원
    /// </summary>
    public void Hide()
    {
        _hintText.enabled = false;
        _dragIconGroup.alpha = 1f;
    }
}