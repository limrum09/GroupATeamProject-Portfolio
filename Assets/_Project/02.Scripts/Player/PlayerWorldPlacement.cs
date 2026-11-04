using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 월드 전환의 플레이어 배치와 조작 상태 복원
/// </summary>
[DisallowMultipleComponent]
public class PlayerWorldPlacement : MonoBehaviour
{
    [Header("----- 플레이어 연결 -----")]
    [SerializeField] PlayerActor _actor;
    [SerializeField] PlayerMovement _movement;
    [SerializeField] NavMeshAgent _agent;

    [Header("----- 배치 설정 -----")]
    [SerializeField, Min(0.01f)] float _sampleRadius = 0.5f;

    Pose _previousPose;
    Guid _runId;
    bool _placing;

    public event Action OnPlaced;
    public CombatTarget Target => _actor.Target;

    void ValidateReferences()
    {
        if (_actor == null || _movement == null || _agent == null)
            throw new MissingReferenceException($"{name}: 플레이어와 이동 및 Agent 연결 필요");

        Transform root = _actor.transform;

        if (_actor.gameObject.scene != gameObject.scene ||
            _agent.transform != root ||
            !_movement.transform.IsChildOf(root) ||
            !_agent.isActiveAndEnabled ||
            !_movement.isActiveAndEnabled ||
            float.IsNaN(_sampleRadius) ||
            float.IsInfinity(_sampleRadius) ||
            _sampleRadius <= 0f)
        {
            throw new InvalidOperationException($"{name}: 같은 플레이어의 활성 이동 구성과 유효한 배치 반경 필요");
        }
    }

    void ClearPlacementState()
    {
        _placing = false;
        _runId = Guid.Empty;
    }

    /// <summary>
    /// 이전 위치 보관과 배치 중 입력 및 전투 차단
    /// </summary>
    public void BeginPlacement()
    {
        ValidateReferences();

        if (_placing || !_actor.IsPrepared || !_actor.Status.IsAlive)
            throw new InvalidOperationException("배치 중이 아닌 준비된 생존 플레이어 필요");

        _previousPose = new Pose(_agent.transform.position, _agent.transform.rotation);

        _runId = _actor.RunId;
        _placing = true;

        _actor.SetPlacementBlocked(true);
        _movement.CancelSkillMovement();
    }

    /// <summary>
    /// 목적 맵의 NavMesh에 플레이어 배치
    /// </summary>
    /// <param name="destination">목적 위치와 회전</param>
    /// <returns>배치 성공 여부</returns>
    public bool TryPlace(Pose destination)
    {
        if (!_placing)
            throw new InvalidOperationException("배치 시작 필요");

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        if (!NavMesh.SamplePosition(destination.position, out NavMeshHit hit, _sampleRadius, filter))
        {
            Debug.LogError(
                $"{name}: 플레이어 배치 실패 [NavMesh.SamplePosition] " +
                $"destination={destination.position.ToString("F3")}, sampleRadius={_sampleRadius}, " +
                $"agentTypeID={filter.agentTypeID}, areaMask={filter.areaMask}, " +
                $"agentPosition={_agent.transform.position.ToString("F3")}, " +
                $"agentActive={_agent.isActiveAndEnabled}, isOnNavMesh={_agent.isOnNavMesh}", this);
            return false;
        }
        if (!_agent.Warp(hit.position))
        {
            Debug.LogError(
                $"{name}: 플레이어 배치 실패 [NavMeshAgent.Warp] " +
                $"destination={destination.position.ToString("F3")}, hitPosition={hit.position.ToString("F3")}, " +
                $"sampleRadius={_sampleRadius}, agentTypeID={filter.agentTypeID}, areaMask={filter.areaMask}, " +
                $"agentPosition={_agent.transform.position.ToString("F3")}, " +
                $"agentActive={_agent.isActiveAndEnabled}, isOnNavMesh={_agent.isOnNavMesh}", this);
            return false;
        }

        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
        _agent.isStopped = true;
        _agent.transform.rotation = destination.rotation;
        Physics.SyncTransforms();
        return true;
    }

    /// <summary>
    /// 이전 맵 활성화 이후 기존 위치 복원
    /// </summary>
    /// <returns>복구 성공 여부</returns>
    public bool TryRestore()
    {
        return TryPlace(_previousPose);
    }

    /// <summary>
    /// 배치 보정 통지 후 같은 런의 기존 조작 상태 복원
    /// </summary>
    public void CompletePlacement()
    {
        if (!_placing) return;

        Guid runId = _runId;

        OnPlaced?.Invoke();

        // 보정 통지 중 취소되거나 배치 문맥이 변경된 경우 제외
        if (!_placing || _runId != runId)
            return;

        if (_actor == null || !_actor.IsPrepared || _actor.RunId != runId || !_actor.Status.IsAlive)
        {
            CancelPlacement();
            return;
        }

        ClearPlacementState();
        _actor.SetPlacementBlocked(false);
    }

    /// <summary>
    /// 현재 런의 배치 취소와 조작 중단 유지
    /// </summary>
    public void CancelPlacement()
    {
        if (!_placing)
            return;

        Guid runId = _runId;
        ClearPlacementState();

        // 이전 런의 배치 정리가 새 런의 조작에 영향을 주지 않도록 제한
        if (_actor == null || !_actor.IsPrepared || _actor.RunId != runId)
            return;

        _actor.DisableControl();
        _actor.SetPlacementBlocked(false);
    }
}
