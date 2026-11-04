using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Overlay Canvas에서 포인터를 따라가는 드래그 아이콘 표시</summary>
[DisallowMultipleComponent]
public sealed class SkillDragIconView : MonoBehaviour
{
    [Header("----- 표시 연결 -----")]
    [SerializeField] Image _image;

    RectTransform _dragPlane;

    void Awake()
    {
        if (_image == null)
            throw new MissingReferenceException($"{name}: 드래그 Image 연결 필요");

        _dragPlane = _image.rectTransform.parent as RectTransform;

        if (_dragPlane == null)
            throw new InvalidOperationException($"{name}: 드래그 Image의 부모 RectTransform 필요");

        _image.raycastTarget = false;
        Hide();
    }

    void OnDisable()
    {
        Hide();
    }

    /// <summary>드래그 아이콘 표시</summary>
    /// <param name="icon">표시할 아이콘, 미지정 시 이미지 숨김</param>
    /// <param name="screenPosition">포인터 화면 좌표</param>
    public void Show(Sprite icon, Vector2 screenPosition)
    {
        _image.sprite = icon;
        _image.enabled = icon != null;
        _image.rectTransform.SetAsLastSibling();

        Move(screenPosition);
    }

    /// <summary>
    /// 화면 좌표를 이미지 부모 기준으로 변환해 드래그 아이콘 이동
    /// </summary>
    /// <param name="screenPosition">포인터 화면 좌표</param>
    public void Move(Vector2 screenPosition)
    {
        if (!_image.isActiveAndEnabled)
            return;

        Canvas canvas = _image.canvas;

        if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            throw new InvalidOperationException($"{name}: 활성 Overlay Canvas 아래의 드래그 Image 필요");

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_dragPlane, screenPosition, null, out Vector2 localPosition))
            return;

        _image.rectTransform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
    }

    /// <summary>드래그 아이콘 숨김</summary>
    public void Hide()
    {
        _image.enabled = false;
        _image.sprite = null;
    }
}