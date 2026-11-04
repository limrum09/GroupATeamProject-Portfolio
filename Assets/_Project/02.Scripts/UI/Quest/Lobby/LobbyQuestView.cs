using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트 패널과 일일 슬롯 목록 표시
/// </summary>
[DisallowMultipleComponent]
public class LobbyQuestView : MonoBehaviour
{
    const int SlotCount = 4;

    [Header("----- 일일 퀘스트 슬롯 네 개 -----")]
    [SerializeField]
    DailyQuestSlot[] _slots = new DailyQuestSlot[SlotCount];

    [Header("----- 처리 상태 -----")]
    [SerializeField] TextMeshProUGUI _messageText;
    [SerializeField] Button _retrySaveButton;

    public event Action<string, string> ClaimRequested;
    public event Action RetryRequested;

    void Awake()
    {
        if (_slots == null || _slots.Length != SlotCount)
            throw new InvalidOperationException("일일 퀘스트 슬롯 네 개 필요");

        if (_messageText == null || _retrySaveButton == null)
            throw new MissingReferenceException("안내 문구와 재시도 버튼 연결 필요");

        foreach (DailyQuestSlot slot in _slots)
        {
            if (slot == null)
                throw new MissingReferenceException("일일 퀘스트 슬롯 연결 필요");

            slot.ClaimRequested += HandleClaimRequested;
        }

        _retrySaveButton.onClick.AddListener(HandleRetryClicked);
    }

    void OnDestroy()
    {
        if (_retrySaveButton != null)
            _retrySaveButton.onClick.RemoveListener(HandleRetryClicked);

        foreach (DailyQuestSlot slot in _slots)
        {
            if (slot != null)
                slot.ClaimRequested -= HandleClaimRequested;
        }
    }

    void HandleClaimRequested(string questId, string periodKey)
    {
        ClaimRequested?.Invoke(questId, periodKey);
    }

    void HandleRetryClicked()
    {
        RetryRequested?.Invoke();
    }

    /// <summary>
    /// 전달된 일일 퀘스트 개수에 따른 슬롯 표시와 처리 상태 갱신
    /// </summary>
    /// <param name="quests">표시할 일일 퀘스트 목록, 0개부터 네 개까지 허용</param>
    /// <param name="busy">요청 또는 저장 처리 중 여부</param>
    /// <param name="message">화면 안내 문구</param>
    /// <param name="canRetry">저장 재시도 가능 여부</param>
    public void Refresh(IReadOnlyList<DailyQuestInfo> quests, bool busy, string message, bool canRetry)
    {
        if (quests == null)
            throw new ArgumentNullException(nameof(quests));

        if (quests.Count > SlotCount)
            throw new InvalidOperationException("일일 퀘스트 데이터는 최대 네 개까지 표시 가능");

        for (int i = 0; i < SlotCount; i++)
        {
            bool visible = i < quests.Count;

            _slots[i].gameObject.SetActive(visible);

            if (visible)
                _slots[i].Refresh(quests[i], busy);
        }

        _messageText.text = message ?? string.Empty;
        _messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));

        _retrySaveButton.gameObject.SetActive(canRetry);
        _retrySaveButton.interactable = canRetry && !busy;
    }

    /// <summary>
    /// 퀘스트 화면 표시 여부 설정
    /// </summary>
    /// <param name="visible">화면 표시 여부</param>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}