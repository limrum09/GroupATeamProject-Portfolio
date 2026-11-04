using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공용 Overlay Canvas의 적 HUD 연결과 대여 및 위치 갱신
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class EnemyHudController : MonoBehaviour
{
    [Header("----- 씬 연결 -----")]
    [SerializeField] EnemySpawnManager _spawnManager;
    [SerializeField] Camera _worldCamera;
    [SerializeField] Canvas _canvas;

    [Header("----- HUD 화면 -----")]
    [SerializeField] RectTransform _hudRoot;
    [SerializeField] EnemyHudView _viewPrefab;

    [Header("----- 보스 화면 -----")]
    [SerializeField] RectTransform _bossRoot;
    [SerializeField] BossHudView _bossViewPrefab;
    [SerializeField, Min(1f)] float _bossRowSpacing = 84f;

    class HudBinding
    {
        public CharacterStatus Status;
        public Transform Anchor;
        public EnemyHudView View;
        // Boss 등급에서만 사용하는 이름 표시 화면
        public BossHudView BossView;
        public EnemyDefinition Definition;
        public bool IsBoss;
        public bool ShowOnDamage;
        public bool HasTakenDamage;
        public float PreviousHealth;
        public Action<float, float> HealthChanged;
    }

    readonly Dictionary<Enemy, HudBinding> _bindings = new Dictionary<Enemy, HudBinding>();
    readonly Stack<EnemyHudView> _available = new Stack<EnemyHudView>();
    readonly Stack<BossHudView> _availableBossViews = new Stack<BossHudView>();
    readonly List<Enemy> _toRelease = new List<Enemy>();

    void OnEnable()
    {
        ValidateConfiguration();
        _spawnManager.OnWaveSpawned += HandleWaveSpawned;
        _spawnManager.OnEnemyDied += Release;

        try
        {
            foreach (Enemy enemy in _spawnManager.LivingEnemies)
                Register(enemy);
        }
        catch
        {
            // 부분 연결 실패 후 구독과 대여 화면 회수
            ClearBindings();
            throw;
        }
    }

    void OnDisable()
    {
        ClearBindings();
    }

    void LateUpdate()
    {
        _toRelease.Clear();
        int bossIndex = 0;
        foreach (KeyValuePair<Enemy, HudBinding> pair in _bindings)
        {
            Enemy enemy = pair.Key;
            HudBinding binding = pair.Value;

            // 구역 정리, 소환 실패와 같은 사망 외 반환 경로
            if (enemy == null || !enemy.gameObject.activeInHierarchy ||
                !binding.Status.IsAlive || enemy.Definition != binding.Definition ||
                (!binding.IsBoss && enemy.HudAnchor != binding.Anchor))
            {
                _toRelease.Add(enemy);
                continue;
            }

            if (!binding.IsBoss && binding.Anchor == null)
                throw new MissingReferenceException($"{enemy.name}: 연결된 HudAnchor 제거됨");

            UpdatePresentation(binding, bossIndex);
            if (binding.IsBoss) bossIndex++;
        }

        foreach (Enemy enemy in _toRelease)
            Release(enemy);
    }

    void ValidateConfiguration()
    {
        if (_spawnManager == null || _worldCamera == null || _canvas == null ||
            _hudRoot == null || _viewPrefab == null || _bossRoot == null || _bossViewPrefab == null)
            throw new MissingReferenceException($"{name}: 소환 Manager, 월드 Camera, Canvas, 일반 및 보스 HUD Root와 프리팹 연결 필요");

        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay || !_canvas.isRootCanvas ||
            !_hudRoot.IsChildOf(_canvas.transform) || _hudRoot != transform ||
            _hudRoot.pivot != Vector2.one * 0.5f ||
            _spawnManager.gameObject.scene != gameObject.scene ||
            _worldCamera.gameObject.scene != gameObject.scene ||
            _canvas.gameObject.scene != gameObject.scene ||
            _worldCamera.targetDisplay != _canvas.targetDisplay || _worldCamera.targetTexture != null ||
            _bossRoot == _hudRoot || !_bossRoot.IsChildOf(_hudRoot) ||
            _bossRoot.anchorMin != new Vector2(0.5f, 1f) ||
            _bossRoot.anchorMax != new Vector2(0.5f, 1f) ||
            _bossRoot.pivot != new Vector2(0.5f, 1f) ||
            float.IsNaN(_bossRowSpacing) || float.IsInfinity(_bossRowSpacing))
        {
            throw new InvalidOperationException($"{name}: 같은 씬과 Display의 Overlay Canvas, 월드 Camera 및 중앙 Pivot HUD Root 필요");
        }

        _viewPrefab.ValidateConfiguration();
        _bossViewPrefab.ValidateConfiguration();
        if (_bossRowSpacing < _bossViewPrefab.Height)
            throw new InvalidOperationException($"{name}: 보스 화면 높이 이상의 행 간격 필요");
    }

    void HandleWaveSpawned(WaveSpawnData wave, IReadOnlyList<Enemy> enemies)
    {
        foreach (Enemy enemy in enemies)
            Register(enemy);
    }

    void Register(Enemy enemy)
    {
        // 같은 프레임의 반환과 재대여에도 이전 체력 구독 제거
        Release(enemy);

        if (!enemy.Status.IsAlive) return;
        EnemyDefinition definition = enemy.Definition;
        if (definition == null)
            throw new MissingReferenceException($"{enemy.name}: HUD 연결 전 EnemyDefinition 초기화 필요");

        bool isBoss = definition.RankType == EnemyRankType.Boss;
        if (!isBoss && (enemy.HudAnchor == null || !enemy.HudAnchor.IsChildOf(enemy.transform)))
            throw new MissingReferenceException($"{enemy.name}: 현재 Content의 HudAnchor 연결 필요");

        if (isBoss && string.IsNullOrWhiteSpace(definition.DisplayName))
            throw new InvalidOperationException($"{definition.name}: 보스 표시 이름 설정 필요");

        BossHudView bossView = null;
        EnemyHudView view;
        if (isBoss)
        {
            bossView = _availableBossViews.Count > 0
                ? _availableBossViews.Pop()
                : Instantiate(_bossViewPrefab, _bossRoot, false);
            bossView.SetName(definition.DisplayName);
            view = bossView.HealthView;
        }
        else
        {
            view = _available.Count > 0
                ? _available.Pop()
                : Instantiate(_viewPrefab, _hudRoot, false);
        }

        HudBinding binding = new HudBinding
        {
            Status = enemy.Status,
            Anchor = enemy.HudAnchor,
            View = view,
            BossView = bossView,
            Definition = definition,
            IsBoss = isBoss,
            ShowOnDamage = definition.RankType == EnemyRankType.Normal || definition.RankType == EnemyRankType.Epic,
            HasTakenDamage = enemy.Status.CurrentHealth < enemy.Status.MaxHealth,
            PreviousHealth = enemy.Status.CurrentHealth
        };
        binding.HealthChanged = (current, maximum) => HandleHealthChanged(binding, current, maximum);

        // 비활성 적의 초기화 이벤트를 놓친 경우 현재 상태 직접 반영
        view.ResetHealth(binding.Status.CurrentHealth, binding.Status.MaxHealth);
        view.SetPresentation(Vector2.zero, false);
        view.gameObject.SetActive(true);
        binding.Status.OnHealthChanged += binding.HealthChanged;
        _bindings.Add(enemy, binding);
        _bossRoot.SetAsLastSibling();
    }

    void HandleHealthChanged(HudBinding binding, float current, float maximum)
    {
        // 회복이나 동일 체력 알림과 구분한 첫 체력 감소
        if (current < binding.PreviousHealth)
            binding.HasTakenDamage = true;

        binding.PreviousHealth = current;
        binding.View.SetHealth(current, maximum);
    }

    void UpdatePresentation(HudBinding binding, int bossIndex)
    {
        if (binding.IsBoss)
        {
            // 월드 위치 및 카메라 시야와 무관한 화면 상단 고정 표시
            binding.View.SetPresentation(
                new Vector2(0f, -binding.BossView.Height * 0.5f - _bossRowSpacing * bossIndex),
                _canvas.isActiveAndEnabled && _bossRoot.gameObject.activeInHierarchy);
            binding.View.Tick(Time.deltaTime);
            return;
        }

        Vector3 viewport = _worldCamera.WorldToViewportPoint(binding.Anchor.position);
        bool visible = _canvas.isActiveAndEnabled && _worldCamera.isActiveAndEnabled &&
            (!binding.ShowOnDamage || binding.HasTakenDamage) &&
            viewport.z >= _worldCamera.nearClipPlane && viewport.z <= _worldCamera.farClipPlane &&
            viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;

        Vector2 localPosition = Vector2.zero;
        if (visible)
        {
            Vector3 screen = _worldCamera.WorldToScreenPoint(binding.Anchor.position);
            // Overlay Canvas의 화면 좌표 변환에는 UI 카메라 미사용
            visible = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _hudRoot, new Vector2(screen.x, screen.y), null, out localPosition);
        }

        binding.View.SetPresentation(localPosition, visible);
        binding.View.Tick(Time.deltaTime);
    }

    void Release(Enemy enemy)
    {
        if (!_bindings.TryGetValue(enemy, out HudBinding binding)) return;

        _bindings.Remove(enemy);
        if (binding.Status != null)
            binding.Status.OnHealthChanged -= binding.HealthChanged;

        binding.View.SetPresentation(Vector2.zero, false);
        binding.View.ResetHealth(0f, 1f);
        binding.View.gameObject.SetActive(false);
        if (binding.IsBoss)
            _availableBossViews.Push(binding.BossView);
        else
            _available.Push(binding.View);
    }

    void ClearBindings()
    {
        if (_spawnManager != null)
        {
            _spawnManager.OnWaveSpawned -= HandleWaveSpawned;
            _spawnManager.OnEnemyDied -= Release;
        }

        _toRelease.Clear();
        _toRelease.AddRange(_bindings.Keys);
        foreach (Enemy enemy in _toRelease)
            Release(enemy);
        _toRelease.Clear();
    }
}
