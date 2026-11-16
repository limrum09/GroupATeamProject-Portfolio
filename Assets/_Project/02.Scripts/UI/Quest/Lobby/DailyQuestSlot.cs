using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 일일 퀘스트 한 항목의 표시와 수령 클릭 전달
/// </summary>
[DisallowMultipleComponent]
public class DailyQuestSlot : MonoBehaviour
{
    [Header("----- 퀘스트 표시 -----")]
    [SerializeField] Image _slotBackground;
    [SerializeField] TextMeshProUGUI _questDisplayNameText;
    [SerializeField] TextMeshProUGUI _questDescriptionText;
    [SerializeField] Image _statusIcon;
    [SerializeField] TextMeshProUGUI _questRewardAmountText;

    [Header("----- 보상 수령 -----")]
    [SerializeField] Button _questRewardButton;
    [SerializeField] TextMeshProUGUI _questRewardButtonText;

    [Header("----- 슬롯 배경과 아이콘 -----")]
    [SerializeField] Sprite _activeBackground;
    [SerializeField] Sprite _claimedBackground;
    [SerializeField] Sprite _activeIcon;
    [SerializeField] Sprite _claimedIcon;

    [Header("----- 버튼 배경 -----")]
    [SerializeField] Sprite _inProgressButtonSprite;
    [SerializeField] Sprite _claimableButtonSprite;
    [SerializeField] Sprite _claimedButtonSprite;

    [Header("----- 진행 상태와 수령 표시 -----")]
    [SerializeField] TextMeshProUGUI _questProgressText;
    [SerializeField] Image _rewardFrame;
    [SerializeField] Image _claimedCheckIcon;

    [Header("----- 보상 프레임 -----")]
    [SerializeField] Sprite _activeRewardFrame;
    [SerializeField] Sprite _claimedRewardFrame;

    [Header("----- 버튼 글자색 -----")]
    [SerializeField] Color _inProgressButtonTextColor = new Color32(0x91, 0x91, 0x91, 0xFF);
    [SerializeField] Color _claimedButtonTextColor = new Color32(0x4F, 0x4F, 0x4F, 0xFF);

    [Header("----- 진행 상태 글자색 -----")]
    [SerializeField] Color _inProgressStateColor = new Color32(0xC3, 0xC3, 0xC3, 0xFF);
    [SerializeField] Color _completedStateColor = new Color32(0xF0, 0xE7, 0xD2, 0xFF);

    [Header("----- 수령 완료 글자색 -----")]
    [SerializeField] Color _claimedTitleColor = new Color32(0x60, 0x60, 0x60, 0xFF);
    [SerializeField] Color _claimedDetailColor = new Color32(0x4F, 0x4F, 0x4F, 0xFF);

    Color _titleColor;
    Color _descriptionColor;
    Color _statusColor;
    Color _buttonTextColor;

    DailyQuestInfo _quest;

    public event Action<string, string> ClaimRequested;

    void Awake()
    {
        if (_questDisplayNameText == null || _questDescriptionText == null || _questRewardAmountText == null || _questRewardButton == null || _questRewardButtonText == null)
            throw new MissingReferenceException($"{name}: 일일 퀘스트 표시와 수령 버튼 연결 필요");

        if (_questProgressText == null || _rewardFrame == null || _claimedCheckIcon == null)
            throw new MissingReferenceException($"{name}: 진행 상태 텍스트와 보상 프레임 및 완료 체크 연결 필요");

        if (_activeRewardFrame == null || _claimedRewardFrame == null)
            throw new MissingReferenceException($"{name}: 수령 전후 보상 프레임 스프라이트 연결 필요");

        _titleColor = _questDisplayNameText.color;
        _descriptionColor = _questDescriptionText.color;
        _statusColor = _questRewardAmountText.color;
        _buttonTextColor = _questRewardButtonText.color;
        _claimedCheckIcon.gameObject.SetActive(false);

        _questRewardButton.interactable = false;
        _questRewardButton.onClick.AddListener(HandleClaimClicked);
    }

    void OnDestroy()
    {
        if (_questRewardButton != null)
            _questRewardButton.onClick.RemoveListener(HandleClaimClicked);
    }

    void HandleClaimClicked()
    {
        if (!_questRewardButton.interactable)
            return;

        ClaimRequested?.Invoke(_quest.Definition.QuestId, _quest.PeriodKey);
    }

    /// <summary>
    /// 전달받은 일일 퀘스트 정보로 표시 갱신
    /// </summary>
    /// <param name="quest">현재 일일 퀘스트 표시 정보</param>
    /// <param name="busy">화면 요청 또는 저장 처리 중 여부</param>
    public void Refresh(DailyQuestInfo quest, bool busy)
    {
        _quest = quest;

        QuestDefinition definition = quest.Definition;
        bool claimed = quest.IsClaimed;

        string target = definition.TargetValue.ToString("0.##");
        string progress = $"({quest.CurrentValue:0.##} / {target})";

        _questDisplayNameText.text = definition.DisplayName;

        // 설명에는 목표값만 표시
        string description = definition.Description ?? string.Empty;
        _questDescriptionText.text = description.Replace("{0}", target);

        _questRewardAmountText.text = quest.MetaGoldReward.ToString("N0");

        // 수령 여부에 따른 슬롯과 보상 표시
        _slotBackground.sprite = claimed ? _claimedBackground : _activeBackground;
        _statusIcon.sprite = claimed ? _claimedIcon : _activeIcon;
        _rewardFrame.sprite = claimed ? _claimedRewardFrame : _activeRewardFrame;
        _claimedCheckIcon.gameObject.SetActive(claimed);
        _questDisplayNameText.color = claimed ? _claimedTitleColor : _titleColor;
        _questDescriptionText.color = claimed ? _claimedDetailColor : _descriptionColor;
        _questRewardAmountText.color = claimed ? _claimedDetailColor : _statusColor;

        if (claimed)
        {
            _questProgressText.text = $"수령 완료\n{progress}";
            _questProgressText.color = _claimedDetailColor;

            _questRewardButtonText.text = "수령 완료";
            _questRewardButtonText.color = _claimedButtonTextColor;
            _questRewardButton.image.sprite = _claimedButtonSprite;
        }
        else if (quest.IsCompleted)
        {
            _questProgressText.text = $"진행 완료\n{progress}";
            _questProgressText.color = _completedStateColor;

            _questRewardButtonText.text = busy ? "처리 중" : "보상 수령";
            _questRewardButtonText.color = _buttonTextColor;
            _questRewardButton.image.sprite = _claimableButtonSprite;
        }
        else
        {
            _questProgressText.text = $"진행 중\n{progress}";
            _questProgressText.color = _inProgressStateColor;

            _questRewardButtonText.text = "진행 중";
            _questRewardButtonText.color = _inProgressButtonTextColor;
            _questRewardButton.image.sprite = _inProgressButtonSprite;
        }

        _questRewardButton.interactable = quest.CanClaim && !busy;
    }
}