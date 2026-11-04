using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 퀘스트 상태 조회와 화면 갱신 및 일일 보상 수령 연결
/// </summary>
public class LobbyQuestPresenter : IDisposable
{
    readonly LobbyQuestView _view;
    readonly LobbyManager _lobby;
    readonly QuestManager _quests;

    bool _subscribed;
    bool _busy;

    string _localError;

    void Refresh()
    {
        if (!_subscribed)
            return;

        if (!_quests.IsInitialized || !_lobby.IsMetaReady)
        {
            string message = _lobby.HasMetaLoadFailed ? "프로필을 불러오지 못했습니다." : "퀘스트 정보를 불러오는 중입니다.";

            _view.Refresh(Array.Empty<DailyQuestInfo>(), false, message, false);

            return;
        }

        bool busy = _busy || _lobby.IsMetaProcessing;

        IReadOnlyList<DailyQuestInfo> dailyQuests = _quests.GetDailyQuests(!busy && _lobby.QuestActions.CanClaimRewards);

        string status = _localError ?? _lobby.MetaSaveError ?? _lobby.QuestActions.ErrorMessage;

        if (string.IsNullOrEmpty(status))
        {
            if (busy)
                status = "처리 중입니다.";
            else if (dailyQuests.Count == 0)
                status = "등록된 일일 퀘스트가 없습니다.";
        }

        _view.Refresh(dailyQuests, busy, status, !busy && _lobby.CanRetryQuestSave);
    }

    void HandleClaimRequested(string questId, string periodKey)
    {
        Execute(() => _lobby.RequestQuestClaimAsync(questId, periodKey));
    }

    void HandleRetryRequested()
    {
        Execute(() => _lobby.SaveMetaChangesAsync(MetaSavePart.DailyQuests | MetaSavePart.MetaGold));
    }

    async void Execute(Func<Task<bool>> request)
    {
        if (!_subscribed || _busy || !_quests.IsInitialized || !_lobby.IsMetaReady)
            return;

        _busy = true;
        _localError = null;

        try
        {
            Refresh();

            bool succeeded = await request();

            if (!succeeded)
                _localError = _lobby.MetaSaveError ?? _lobby.QuestActions.ErrorMessage ?? "요청을 완료하지 못했습니다.";
        }
        catch (Exception exception)
        {
            _localError = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            _busy = false;

            if (_subscribed)
                Refresh();
        }
    }

    /// <summary>
    /// 퀘스트 화면과 상태 조회 및 요청 대상 연결
    /// </summary>
    /// <param name="view">퀘스트 표시와 사용자 입력 담당</param>
    /// <param name="lobby">로비 준비 상태와 퀘스트 작업 제공자</param>
    /// <param name="quests">퀘스트 상태 조회 대상</param>
    public LobbyQuestPresenter(LobbyQuestView view, LobbyManager lobby, QuestManager quests)
    {
        _view = view;
        _lobby = lobby;
        _quests = quests;
    }

    /// <summary>
    /// 화면 입력과 상태 변경 알림 구독 및 현재 상태 표시
    /// </summary>
    public void Activate()
    {
        if (_subscribed)
            return;

        _quests.OnChanged += Refresh;
        _lobby.OnChanged += Refresh;

        _view.ClaimRequested += HandleClaimRequested;
        _view.RetryRequested += HandleRetryRequested;

        _subscribed = true;
        Refresh();
    }

    /// <summary>
    /// 화면 입력과 상태 변경 알림 구독 해제
    /// </summary>
    public void Deactivate()
    {
        if (!_subscribed)
            return;

        _subscribed = false;

        if (_quests != null)
            _quests.OnChanged -= Refresh;

        if (_lobby != null)
            _lobby.OnChanged -= Refresh;

        if (_view != null)
        {
            _view.ClaimRequested -= HandleClaimRequested;
            _view.RetryRequested -= HandleRetryRequested;
        }
    }

    /// <summary>
    /// Presenter 사용 종료와 구독 정리
    /// </summary>
    public void Dispose()
    {
        Deactivate();
    }
}