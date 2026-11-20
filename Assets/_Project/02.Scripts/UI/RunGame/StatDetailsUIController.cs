using System;
using UnityEngine;

/// <summary>
/// 스킬 강화 표시 상태에 따른 스탯 상세 패널 개폐
/// </summary>
[DisallowMultipleComponent]
public class StatDetailsUIController : MonoBehaviour
{
    [Header("----- 표시 상태 연결 -----")]
    [SerializeField] SkillBarView _skillBarView;

    [Header("----- 스탯 상세 패널 -----")]
    [SerializeField] GameObject _statDetailsPanel;

    void OnEnable()
    {
        ValidateReferences();

        _skillBarView.OnUpgradeModeChanged += HandleUpgradeModeChanged;

        // 재활성화 시 현재 스킬바 표시 상태와 동기화
        HandleUpgradeModeChanged(_skillBarView.IsUpgradeMode);
    }

    void OnDisable()
    {
        // 씬 종료 중 먼저 제거된 참조 제외
        if (_skillBarView != null)
            _skillBarView.OnUpgradeModeChanged -= HandleUpgradeModeChanged;

        if (_statDetailsPanel != null)
            _statDetailsPanel.SetActive(false);
    }

    void HandleUpgradeModeChanged(bool enabled)
    {
        _statDetailsPanel.SetActive(enabled);
    }

    void ValidateReferences()
    {
        if (_skillBarView == null || _statDetailsPanel == null)
            throw new MissingReferenceException($"{name}: SkillBarView와 스탯 상세 패널 연결 필요");

        if (!_skillBarView.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 같은 HUD 하위의 SkillBarView 연결 필요");

        if (_statDetailsPanel == gameObject || !_statDetailsPanel.transform.IsChildOf(transform))
            throw new InvalidOperationException($"{name}: 별도 자식 오브젝트인 스탯 상세 패널 연결 필요");

        if (_skillBarView.transform.IsChildOf(_statDetailsPanel.transform))
            throw new InvalidOperationException($"{name}: 스탯 상세 패널에 스킬바를 포함할 수 없음");
    }
}