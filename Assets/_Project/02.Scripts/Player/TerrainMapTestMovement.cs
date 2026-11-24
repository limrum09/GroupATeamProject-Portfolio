using System;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 지형 테스트 씬의 우클릭 이동 입력과 초기 배치 연결
/// </summary>
[DisallowMultipleComponent]
public class TerrainMapTestMovement : MonoBehaviour
{
    const float EntrySampleRadius = 2f;

    [Header("----- 씬 연결 -----")]
    [SerializeField] Camera _camera;
    [SerializeField] Transform _entry;
    [SerializeField] NavMeshSurface _surface;
    [SerializeField] NavMeshAgent _agent;
    [SerializeField] PlayerMovement _movement;
    [SerializeField] PlayerAnimation _animation;
    [SerializeField] PlayerDefinition _definition;

    bool _ready;

    void Start()
    {
        if (_camera == null || _entry == null || _surface == null || _agent == null ||
            _movement == null || _animation == null || _definition == null)
        {
            throw new MissingReferenceException($"{name}: 지형 테스트 이동에 필요한 씬 참조 연결 필요");
        }

        if (_surface.navMeshData == null)
        {
            throw new InvalidOperationException($"{_surface.name}: 프리팹의 NavMesh Surface에서 Bake 필요");
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        if (!NavMesh.SamplePosition(_entry.position, out NavMeshHit start, EntrySampleRadius, filter))
        {
            throw new InvalidOperationException($"{name}: PlayerEntry 주변에 이동 가능한 지형 필요");
        }

        // 베이크된 NavMesh의 시작점에 에이전트 배치
        transform.SetPositionAndRotation(start.position, _entry.rotation);
        _agent.enabled = true;
        if (!_movement.TryPlace(new Pose(start.position, _entry.rotation)))
        {
            throw new InvalidOperationException($"{name}: 지형 테스트 플레이어 배치 실패");
        }

        _animation.Prepare();
        _ready = true;
    }

    void Update()
    {
        if (!_ready)
            return;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, _camera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                hit.collider is TerrainCollider && _movement.TryPrepareMove(hit.point, out _))
            {
                _movement.ResumeMove(_definition.MoveSpeed);
            }
        }

        _movement.Tick(_definition.MoveSpeed, true);
        _animation.Present(true, _movement.LocomotionSpeed, _movement.LocomotionDirection, _definition.MoveSpeed);
    }
}
