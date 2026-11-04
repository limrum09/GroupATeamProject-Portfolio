using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>테스트용 적 감지 범위와 현재 상태 표시</summary>
public class EnemyRuntimeDebugView : MonoBehaviour
{
    static readonly List<EnemyRuntimeDebugView> _views =
        new List<EnemyRuntimeDebugView>();

    const int SegmentCount = 64;

    [Header("----- 표시 대상 -----")]
    [SerializeField] EnemyAI _ai;
    [SerializeField] NavMeshAgent _agent;

    [Header("----- 디버그 표시 -----")]
    [SerializeField] bool _showRanges = true;
    [SerializeField] bool _showState = true;
    [SerializeField, Min(1f)] float _lineWidth = 2f;
    [SerializeField, Min(0.01f)] float _surfaceSampleDistance = 2f;
    [SerializeField, Min(0.001f)] float _horizontalTolerance = 0.05f;

    readonly Plane[] _frustumPlanes = new Plane[6];
    Camera _camera;
    GUIStyle _labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetViews()
    {
        // Domain Reload 비활성화 시 이전 실행의 참조 제거
        _views.Clear();
    }

    void Awake()
    {
        if (_ai == null || _agent == null)
        {
            throw new MissingReferenceException(
                $"{name}: 표시할 EnemyAI와 같은 적의 NavMeshAgent 연결 필요");
        }

        // 테스트 Scene의 MainCamera를 표시용 카메라로 사용
        _camera = Camera.main;
        if (_camera == null)
        {
            throw new MissingReferenceException(
                $"{name}: MainCamera 태그의 카메라 필요");
        }
    }

    void OnEnable()
    {
        if (_views.Contains(this) == false)
            _views.Add(this);
    }

    void OnDisable()
    {
        _views.Remove(this);
    }

    void OnGUI()
    {
        if (Application.isPlaying == false) return;
        if (Event.current.type != EventType.Repaint) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 16
            };
            _labelStyle.normal.textColor = Color.white;
        }

        int previousDepth = GUI.depth;
        GUI.depth = -100;

        if (_showRanges && CanDisplay())
        {
            // View의 Repaint당 한 번 계산한 절두체 재사용
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanes);
            DrawRanges();
        }

        // 첫 번째 활성 View에서 HUD 목록 한 번만 출력
        if (_views.Count > 0 && _views[0] == this)
            DrawHud();

        GUI.depth = previousDepth;
    }

    bool CanDisplay()
    {
        return _ai.isActiveAndEnabled && _ai.IsInitialized;
    }

    void DrawRanges()
    {
        if (_agent.isActiveAndEnabled == false || _agent.isOnNavMesh == false)
            return;

        Vector3 center = _ai.Position;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };

        // 현재 상태에서 사용하는 전환 거리만 표시
        switch (_ai.CurrentState)
        {
            case EnemyState.Idle:
            case EnemyState.Roaming:
            case EnemyState.Hit:
                DrawCircle(center, _ai.TrackDetectRange, Color.cyan, filter);
                DrawCircle(center, _ai.AttackDetectRange, Color.yellow, filter);
                break;
            case EnemyState.Tracking:
                DrawCircle(center, _ai.TrackDisableDetectRange, Color.blue, filter);
                DrawCircle(center, _ai.AttackDetectRange, Color.yellow, filter);
                break;
            case EnemyState.Attack:
                DrawCircle(center, _ai.TrackDisableDetectRange, Color.blue, filter);
                DrawCircle(center, _ai.AttackDisableDetectRange, Color.red, filter);
                break;
        }
    }

    void DrawCircle(
        Vector3 center,
        float radius,
        Color color,
        NavMeshQueryFilter filter)
    {
        Vector3 previous = Vector3.zero;
        bool hasPrevious = false;
        for (int i = 0; i <= SegmentCount; i++)
        {
            float angle = (i % SegmentCount) * Mathf.PI * 2f / SegmentCount;
            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            if (TryProjectToSurface(candidate, filter, out Vector3 next) == false)
            {
                hasPrevious = false;
                continue;
            }

            // 구멍이나 분리된 NavMesh 영역을 가로지르는 선분 제외
            if (hasPrevious &&
                NavMesh.Raycast(previous, next, out NavMeshHit _, filter) == false)
            {
                DrawWorldLine(previous, next, color);
            }

            previous = next;
            hasPrevious = true;
        }
    }

    bool TryProjectToSurface(
        Vector3 candidate, NavMeshQueryFilter filter, out Vector3 position)
    {
        position = Vector3.zero;
        if (NavMesh.SamplePosition(
            candidate, out NavMeshHit hit, _surfaceSampleDistance, filter) == false)
        {
            return false;
        }

        // 가장자리로 수평 보정된 점을 제외하고 원의 수평 반경 유지
        Vector2 horizontalOffset = new Vector2(
            hit.position.x - candidate.x, hit.position.z - candidate.z);
        if (horizontalOffset.sqrMagnitude > _horizontalTolerance * _horizontalTolerance)
            return false;

        position = hit.position;
        return true;
    }

    void DrawWorldLine(Vector3 from, Vector3 to, Color color)
    {
        // 화면 밖 선분의 과도한 투영 방지를 위한 절두체 클리핑
        foreach (Plane plane in _frustumPlanes)
        {
            float fromDistance = plane.GetDistanceToPoint(from);
            float toDistance = plane.GetDistanceToPoint(to);
            if (fromDistance < 0f && toDistance < 0f) return;

            if (fromDistance < 0f || toDistance < 0f)
            {
                float ratio = fromDistance / (fromDistance - toDistance);
                Vector3 intersection = Vector3.Lerp(from, to, ratio);
                if (fromDistance < 0f)
                    from = intersection;
                else
                    to = intersection;
            }
        }

        Vector3 start = _camera.WorldToScreenPoint(from);
        Vector3 end = _camera.WorldToScreenPoint(to);
        Vector2 startGui = new Vector2(start.x, Screen.height - start.y);
        Vector2 endGui = new Vector2(end.x, Screen.height - end.y);
        Vector2 delta = endGui - startGui;
        if (delta.sqrMagnitude <= 0.0001f) return;

        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, startGui);
        GUI.DrawTexture(
            new Rect(startGui.x, startGui.y - _lineWidth * 0.5f,
                delta.magnitude, _lineWidth),
            Texture2D.whiteTexture);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    void DrawHud()
    {
        GUI.Label(
            new Rect(12f, 12f, 720f, 28f),
            "Enemy | Cyan: Track In | Blue: Track Out"
                + " | Yellow: Attack In | Red: Attack Out",
            _labelStyle);

        float y = 44f;
        foreach (EnemyRuntimeDebugView view in _views)
        {
            if (view._showState == false || view.CanDisplay() == false)
                continue;

            GUI.Label(
                new Rect(12f, y, 480f, 26f),
                $"{view._ai.name} [{view._ai.GetInstanceID()}]"
                    + $" : {view._ai.CurrentState}",
                _labelStyle);
            y += 28f;
        }
    }
}
