using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적과 플레이어의 피해 알림 연결 및 숫자 화면 관리
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class DamageTextController : MonoBehaviour
{
    [Header("----- 씬 연결 -----")]
    [SerializeField] EnemySpawnManager _spawnManager;
    [SerializeField] Camera _worldCamera;
    [SerializeField] Canvas _canvas;
    [SerializeField] RectTransform _root;

    [Header("----- 플레이어 연결 -----")]
    [SerializeField] PlayerActor _player;
    [SerializeField] Transform _playerDamageAnchor;

    [Header("----- 숫자 프리팹 -----")]
    [SerializeField] DamageTextView _viewPrefab;

    [Header("----- 적 피해 색상 -----")]
    [SerializeField] Color _enemyNormalColor = Color.white;
    [SerializeField] Color _enemyCriticalColor = new Color(1f, 0.75f, 0.15f);

    [Header("----- 플레이어 피해 색상 -----")]
    [SerializeField] Color _playerNormalColor = new Color(1f, 0.3f, 0.3f);
    [SerializeField] Color _playerCriticalColor = new Color(1f, 0.1f, 0.1f);

    [Header("----- 회복 색상 -----")]
    [SerializeField] Color _healingColor = new Color(0.35f, 1f, 0.45f);

    SettingsService _settings;

    class DamageBinding
    {
        public CombatTarget Target;
        public Transform Anchor;
        // 플레이어 연결에서는 사용하지 않는 소환 수명 참조
        public Enemy Enemy;
        public Color NormalColor;
        public Color CriticalColor;
        public Action<DamageRequest, float> Damaged;
        public Action<float> Healed;
        public CharacterStatus Status;
    }

    struct ActiveText
    {
        public DamageTextView View;
        public Vector3 WorldPosition;
    }

    readonly Dictionary<CombatTarget, DamageBinding> _bindings = new Dictionary<CombatTarget, DamageBinding>();
    readonly Stack<DamageTextView> _available = new Stack<DamageTextView>();
    readonly List<ActiveText> _active = new List<ActiveText>();
    readonly List<CombatTarget> _toRelease = new List<CombatTarget>();

    void OnEnable()
    {
        ValidateConfiguration();
        _settings = SettingsService.Instance;
        _spawnManager.OnWaveSpawned += HandleWaveSpawned;

        try
        {
            Register(_player.Target, _playerDamageAnchor, null, _playerNormalColor, _playerCriticalColor);

            foreach (Enemy enemy in _spawnManager.LivingEnemies)
                RegisterEnemy(enemy);
        }
        catch
        {
            // 부분 연결 실패 시 구독과 출력 화면 회수
            Clear();
            throw;
        }
    }

    void OnDisable()
    {
        Clear();
    }

    void LateUpdate()
    {
        ReleaseFinishedEnemies();
        UpdateTexts();
    }

    void ValidateConfiguration()
    {
        if (_spawnManager == null || _worldCamera == null || _canvas == null || _root == null ||
            _player == null || _playerDamageAnchor == null || _viewPrefab == null)
        {
            throw new MissingReferenceException(
                $"{name}: 소환 Manager, Camera, Canvas, Root, PlayerActor, DamageAnchor와 숫자 프리팹 연결 필요");
        }

        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay || !_canvas.isRootCanvas ||
            _root != transform || !_root.IsChildOf(_canvas.transform) ||
            _root.pivot != Vector2.one * 0.5f ||
            _spawnManager.gameObject.scene != gameObject.scene ||
            _worldCamera.gameObject.scene != gameObject.scene ||
            _canvas.gameObject.scene != gameObject.scene ||
            _player.gameObject.scene != gameObject.scene ||
            _worldCamera.targetDisplay != _canvas.targetDisplay || _worldCamera.targetTexture != null)
        {
            throw new InvalidOperationException(
                $"{name}: 같은 씬과 Display의 Overlay Canvas, 월드 Camera 및 중앙 Pivot Root 필요");
        }

        CombatTarget target = _player.Target;
        if (target == null || target.Team != CombatTeam.Player || target.Status != _player.Status ||
            target.ActorTransform != _player.transform || !_playerDamageAnchor.IsChildOf(_player.transform))
        {
            throw new InvalidOperationException(
                $"{name}: 플레이어 전투 대상과 Player 하위 DamageAnchor 연결 필요");
        }

        _viewPrefab.ValidateConfiguration();
    }

    void HandleWaveSpawned(WaveSpawnData wave, IReadOnlyList<Enemy> enemies)
    {
        foreach (Enemy enemy in enemies)
            RegisterEnemy(enemy);
    }

    void RegisterEnemy(Enemy enemy)
    {
        if (enemy == null)
            throw new MissingReferenceException($"{name}: 소환 결과의 Enemy 참조 필요");

        if (!enemy.Status.IsAlive) return;

        CombatTarget target = enemy.CombatTarget;
        Transform anchor = enemy.DamageAnchor;
        if (target == null || anchor == null || !anchor.IsChildOf(enemy.transform) ||
            target.Status != enemy.Status || target.ActorTransform != enemy.transform ||
            target.Team != CombatTeam.Enemy)
        {
            throw new InvalidOperationException(
                $"{enemy.name}: 적 전투 대상과 현재 Content의 DamageAnchor 연결 필요");
        }

        Register(target, anchor, enemy, _enemyNormalColor, _enemyCriticalColor);
    }

    void Register(CombatTarget target, Transform anchor, Enemy enemy, Color normalColor, Color criticalColor)
    {
        Release(target);

        DamageBinding binding = new DamageBinding
        {
            Target = target,
            Status = target.Status,
            Anchor = anchor,
            Enemy = enemy,
            NormalColor = normalColor,
            CriticalColor = criticalColor
        };
        binding.Damaged = (request, applied) => HandleDamaged(binding, request, applied);

        binding.Healed = restored => ShowNumber(binding, restored, false, _healingColor);

        _bindings.Add(target, binding);
        target.OnDamaged += binding.Damaged;
        binding.Status.OnHealed += binding.Healed;
    }

    void HandleDamaged(DamageBinding binding, DamageRequest request, float applied)
    {
        Color color = request.IsCritical ? binding.CriticalColor : binding.NormalColor;
        ShowNumber(binding, applied, request.IsCritical, color);
    }

    bool CanShow(CombatTeam team)
    {
        return _settings.CombatText switch
        {
            CombatTextTarget.PlayerOnly => team == CombatTeam.Player,
            CombatTextTarget.EnemyOnly => team == CombatTeam.Enemy,
            _ => true
        };
    }

    void ShowNumber(DamageBinding binding, float amount, bool isCritical, Color color)
    {
        if (amount <= 0f || !CanShow(binding.Target.Team)) return;

        if (binding.Anchor == null)
            throw new MissingReferenceException($"{name}: 연결된 DamageAnchor 제거됨");

        // 대상 이동과 풀 반환에 영향을 받지 않는 피격 시점 위치
        Vector3 worldPosition = binding.Anchor.position;
        if (!TryGetPosition(worldPosition, out Vector2 position)) return;

        DamageTextView view = _available.Count > 0
            ? _available.Pop()
            : Instantiate(_viewPrefab, _root, false);

        float horizontalDirection = GetHorizontalDirection(binding);
        view.Begin(amount, isCritical, color, horizontalDirection);
        view.SetPresentation(position, true);
        view.gameObject.SetActive(true);
        view.transform.SetAsLastSibling();

        _active.Add(new ActiveText
        {
            View = view,
            WorldPosition = worldPosition
        });
    }

    float GetHorizontalDirection(DamageBinding binding)
    {
        // 플레이어 피격 숫자는 기존 좌우 분산 유지
        if (binding.Target == _player.Target)
            return UnityEngine.Random.Range(-1f, 1f);

        Vector3 playerScreen = _worldCamera.WorldToScreenPoint(_player.Target.Position);
        Vector3 enemyScreen = _worldCamera.WorldToScreenPoint(binding.Target.Position);
        float difference = enemyScreen.x - playerScreen.x;

        // 화면상 수평 차이가 거의 없으면 수직 상승만 적용
        if (Mathf.Abs(difference) < 0.01f) return 0f;

        return difference > 0f ? 1f : -1f;
    }

    void ReleaseFinishedEnemies()
    {
        _toRelease.Clear();
        foreach (KeyValuePair<CombatTarget, DamageBinding> pair in _bindings)
        {
            DamageBinding binding = pair.Value;
            // 사망 및 일시 비활성화 이후에도 플레이어 구독 유지
            if (binding.Target == _player.Target) continue;

            Enemy enemy = binding.Enemy;
            if (enemy == null || !enemy.gameObject.activeInHierarchy ||
                !enemy.Status.IsAlive || enemy.DamageAnchor != binding.Anchor)
            {
                _toRelease.Add(pair.Key);
            }
        }

        // 사망 이벤트 이후 전달되는 처치 피해 알림까지 수신
        foreach (CombatTarget target in _toRelease)
            Release(target);
    }

    void UpdateTexts()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveText active = _active[i];
            if (active.View.Tick(Time.deltaTime))
            {
                ReturnView(active.View);
                _active.RemoveAt(i);
                continue;
            }

            bool visible = TryGetPosition(active.WorldPosition, out Vector2 position);
            active.View.SetPresentation(position, visible);
        }
    }

    bool TryGetPosition(Vector3 worldPosition, out Vector2 position)
    {
        position = Vector2.zero;
        if (!_canvas.isActiveAndEnabled || !_worldCamera.isActiveAndEnabled) return false;

        Vector3 viewport = _worldCamera.WorldToViewportPoint(worldPosition);
        if (viewport.z < _worldCamera.nearClipPlane || viewport.z > _worldCamera.farClipPlane ||
            viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            return false;
        }

        Vector3 screen = _worldCamera.WorldToScreenPoint(worldPosition);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _root, new Vector2(screen.x, screen.y), null, out position);
    }

    void Release(CombatTarget target)
    {
        if (!_bindings.TryGetValue(target, out DamageBinding binding)) return;

        Unsubscribe(binding);

        _bindings.Remove(target);
    }

    static void Unsubscribe(DamageBinding binding)
    {
        if (binding.Target != null)
            binding.Target.OnDamaged -= binding.Damaged;

        if (binding.Status != null)
            binding.Status.OnHealed -= binding.Healed;
    }

    void ReturnView(DamageTextView view)
    {
        view.SetPresentation(Vector2.zero, false);
        view.gameObject.SetActive(false);
        _available.Push(view);
    }

    void Clear()
    {
        if (_spawnManager != null)
            _spawnManager.OnWaveSpawned -= HandleWaveSpawned;

        foreach (DamageBinding binding in _bindings.Values)
        {
            Unsubscribe(binding);
        }

        _bindings.Clear();
        _toRelease.Clear();
        foreach (ActiveText active in _active)
            ReturnView(active.View);

        _active.Clear();
    }
}
