using System;
using UnityEngine;

/// <summary>
/// 현재 노드 완료에 따른 포탈 표시와 접근 검사 및 이동 화면 요청
/// </summary>
[DisallowMultipleComponent]
public class WorldPortal : MonoBehaviour, IInteractable
{
    [Header("----- 맵과 진행 연결 -----")]
    [SerializeField] MapBindings _map;
    [SerializeField] SessionProgressManager _sessionProgress;
    [SerializeField] SessionUIController _sessionUi;

    [Header("----- 포탈 구성 -----")]
    [SerializeField] GameObject _presentation;
    [SerializeField] Collider _interactionCollider;
    [SerializeField, Min(0.1f)] float _interactionRange = 3f;

    bool _subscribed;

    void OnEnable()
    {
        ValidateReferences();
        _sessionProgress.OnStateChanged += Refresh;
        _subscribed = true;
        Refresh();
    }

    void OnDisable()
    {
        if (_subscribed && _sessionProgress != null)
            _sessionProgress.OnStateChanged -= Refresh;
        _subscribed = false;
        if (_presentation != null)
            _presentation.SetActive(false);
        if (_interactionCollider != null)
            _interactionCollider.enabled = false;
    }

    void ValidateReferences()
    {
        if (_map == null || _sessionProgress == null || _sessionUi == null ||
            _presentation == null || _interactionCollider == null)
            throw new MissingReferenceException($"{name}: 맵·진행·월드 맵 화면과 외형·Collider 연결 필요");
        if (!transform.IsChildOf(_map.transform) || _presentation == gameObject ||
            !_presentation.transform.IsChildOf(transform) ||
            !_interactionCollider.transform.IsChildOf(transform) ||
            !_interactionCollider.isTrigger ||
            _sessionUi.transform.IsChildOf(transform) ||
            _map.gameObject.scene != _sessionProgress.gameObject.scene ||
            _map.gameObject.scene != _sessionUi.gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 맵 하위 포탈과 별도 화면 및 자식 외형·Trigger Collider 필요");
        if (float.IsNaN(_interactionRange) || float.IsInfinity(_interactionRange) || _interactionRange <= 0f)
            throw new InvalidOperationException($"{name}: 유한한 양수 상호작용 거리 필요");
    }

    bool IsAvailable()
    {
        if (!_sessionProgress.IsReady || _sessionProgress.IsTransitioning)
            return false;

        WorldProgress progress = _sessionProgress.Progress;
        Node current = _sessionProgress.Graph.GetNode(progress.CurrentNodeId);
        // 맵 후보를 여러 노드가 재사용해도 현재 노드의 완료 기록만 사용
        return current.Map == _map && progress.IsCleared(current.Id);
    }

    void Refresh()
    {
        bool available = IsAvailable();
        // 외형만 숨겨 미클리어 상태에서도 진행 이벤트 구독 유지
        _presentation.SetActive(available);
        _interactionCollider.enabled = available;
    }

#if UNITY_EDITOR
    /// <summary>
    /// MapBindings 배치 도구에서 씬 전용 참조 연결
    /// </summary>
    /// <param name="map">포탈을 소유한 맵</param>
    /// <param name="sessionProgress">현재 월드 진행 담당자</param>
    /// <param name="sessionUi">상호작용으로 열 월드 맵 화면</param>
    public void ConfigureForEditor(MapBindings map, SessionProgressManager sessionProgress, SessionUIController sessionUi)
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("편집 모드에서만 포탈 참조 구성 가능");
        _map = map;
        _sessionProgress = sessionProgress;
        _sessionUi = sessionUi;
        ValidateReferences();
    }
#endif

    /// <summary>
    /// 현재 노드 완료와 생존 플레이어의 포탈 접근 거리 검사
    /// </summary>
    /// <param name="actor">상호작용을 요청한 플레이어의 스킬 구성</param>
    /// <returns>현재 포탈 상호작용 가능 여부</returns>
    public bool CanInteract(SkillLoadout actor)
    {
        if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled || !IsAvailable())
            return false;

        Debug.Log("플레이어 확인");

        CombatController combat = actor.Combat;
        return combat.isActiveAndEnabled && combat.Owner.IsAlive &&
            combat.Owner.Team == CombatTeam.Player &&
            actor.gameObject.scene == gameObject.scene &&
            (combat.Owner.Position - transform.position).sqrMagnitude <= _interactionRange * _interactionRange;
    }

    /// <summary>
    /// 접근 조건 재검사 후 일반 지도 또는 보스의 다음 월드 확인창 요청
    /// </summary>
    /// <param name="actor">상호작용을 요청한 플레이어의 스킬 구성</param>
    /// <returns>이동 화면 열기 요청 성공 여부</returns>
    public bool TryInteract(SkillLoadout actor)
    {
        if (!CanInteract(actor))
            return false;

        if (_sessionProgress.Progress.CurrentNodeId == _sessionProgress.Graph.BossNodeId)
        {
            if (_sessionProgress.CanRequestRunClear())
            {
                _sessionUi.OpenRunClear(this, actor);
                return true;
            }
            if (!_sessionProgress.CanEnterNextWorld())
                return false;

            _sessionUi.OpenNextWorldConfirmation(this, actor);
            return true;
        }

        _sessionUi.OpenWorldGraph();
        return true;
    }
}
