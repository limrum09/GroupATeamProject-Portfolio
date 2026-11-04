using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 보스 이름과 공용 체력 표현의 화면 구성
/// </summary>
[DisallowMultipleComponent]
public class BossHudView : MonoBehaviour
{
    [Header("----- 보스 화면 연결 -----")]
    [SerializeField] EnemyHudView _healthView;
    [SerializeField] TMP_Text _nameText;

    public EnemyHudView HealthView => _healthView;
    public float Height => ((RectTransform)transform).rect.height;

    void Awake()
    {
        ValidateConfiguration();
    }

    /// <summary>
    /// 보스 이름과 체력 화면의 필수 구성 검사
    /// </summary>
    public void ValidateConfiguration()
    {
        if (_healthView == null || _nameText == null || _nameText.font == null)
            throw new MissingReferenceException($"{name}: 보스 체력 View, 이름 TMP와 폰트 연결 필요");

        if (_healthView.gameObject != gameObject || !_nameText.transform.IsChildOf(transform) ||
            _nameText.raycastTarget || !(transform is RectTransform) || Height <= 0f)
            throw new InvalidOperationException($"{name}: 같은 루트의 체력 View와 비상호작용 이름 및 양수 화면 높이 필요");

        _healthView.ValidateConfiguration();
    }

    /// <summary>
    /// 현재 보스의 표시 이름 반영
    /// </summary>
    /// <param name="displayName">정적 적 데이터의 이름</param>
    public void SetName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("보스 표시 이름 필요", nameof(displayName));

        _nameText.text = displayName;
    }
}
