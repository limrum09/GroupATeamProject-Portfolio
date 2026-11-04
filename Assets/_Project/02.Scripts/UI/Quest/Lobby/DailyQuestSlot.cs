using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일일 퀘스트 한 항목의 표시와 수령 클릭 전달
/// </summary>
[DisallowMultipleComponent]
public class DailyQuestSlot : MonoBehaviour
{
    [Header("----- 퀘스트 표시 -----")]
    [SerializeField] TextMeshProUGUI _questDisplayNameText;
    [SerializeField] TextMeshProUGUI _questDescriptionText;
    [SerializeField] TextMeshProUGUI _questRewardCountText;

    [Header("----- 보상 수령 -----")]
    [SerializeField] Button _questRewardButton;
    [SerializeField] TextMeshProUGUI _questRewardButtonText;

    DailyQuestInfo _quest;

    public event Action<string, string> ClaimRequested;

    void Awake()
    {
        if (_questDisplayNameText == null || _questDescriptionText == null || _questRewardCountText == null || _questRewardButton == null || _questRewardButtonText == null)
            throw new MissingReferenceException($"{name}: 일일 퀘스트 표시와 수령 버튼 연결 필요");

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

        _questDisplayNameText.text = definition.DisplayName;

        string progress = $"{quest.CurrentValue:0.##} / {definition.TargetValue:0.##}";

        string description = definition.Description ?? string.Empty;

        if (description.Contains("{0}"))
        {
            _questDescriptionText.text = description.Replace("{0}", progress);
        }
        else
        {
            _questDescriptionText.text = string.IsNullOrEmpty(description) ? progress : $"{description}\n{progress}";
        }

        _questRewardCountText.text = quest.MetaGoldReward.ToString("N0");

        if (quest.IsClaimed)
            _questRewardButtonText.text = "수령 완료";
        else if (!quest.IsCompleted)
            _questRewardButtonText.text = "진행 중";
        else if (busy)
            _questRewardButtonText.text = "처리 중";
        else
            _questRewardButtonText.text = "보상 수령";

        _questRewardButton.interactable = quest.CanClaim && !busy;
    }
}