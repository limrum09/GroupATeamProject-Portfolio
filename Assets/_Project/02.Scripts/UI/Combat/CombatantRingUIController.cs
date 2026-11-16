using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 중 플레이어와 전투 구역 적의 발밑 링 대여와 위치 갱신
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class CombatantRingUIController : MonoBehaviour
{
    // 두 PNG에서 좌우 여백을 제외한 불꽃 링의 폭 비율
    const float RingWidthRatio = 0.57f;

    static readonly Quaternion RingRotation = Quaternion.Euler(90f, 0f, 0f);

    [Header("----- 표시 연결 -----")]
    [SerializeField] WaveProgress _waveProgress;
    [SerializeField] Transform _worldRingRoot;
    [SerializeField] Sprite _playerRingSprite;
    [SerializeField] Sprite _enemyRingSprite;
    [SerializeField] Material _ringGlowMaterial;

    [Header("----- 링 설정 -----")]
    [SerializeField] float _rotationDegreesPerSecond = 45f;
    [SerializeField, Min(0.01f)] float _playerDiameter = 1.3f;
    [SerializeField, Min(0.01f)] float _enemyDiameter = 1.5f;
    [SerializeField, Min(0f)] float _heightOffset = 0.025f;

    readonly Stack<SpriteRenderer> _available = new Stack<SpriteRenderer>();
    readonly Dictionary<CombatTarget, RingBinding> _bindings =
        new Dictionary<CombatTarget, RingBinding>();
    readonly HashSet<CombatTarget> _visibleTargets = new HashSet<CombatTarget>();
    readonly List<CombatTarget> _toRelease = new List<CombatTarget>();
    readonly List<CombatantRingAnchor> _anchorMatches = new List<CombatantRingAnchor>();

    EnemySpawnManager _enemies;
    CombatTarget _player;

    struct RingBinding
    {
        public CombatantRingAnchor Anchor;
        public SpriteRenderer Renderer;
    }

    void Awake()
    {
        if (_waveProgress == null || _waveProgress.CombatArea == null ||
            _waveProgress.CombatArea.SpawnManager == null ||
            _worldRingRoot == null || _playerRingSprite == null ||
            _enemyRingSprite == null || _ringGlowMaterial == null)
        {
            throw new MissingReferenceException(
                $"{name}: 웨이브 진행, 소환 관리자, 링 풀과 진영별 Sprite 및 Material 연결 필요");
        }

        if (_worldRingRoot.parent != transform)
            throw new InvalidOperationException($"{name}: 컨트롤러의 자식 링 풀 연결 필요");

        _enemies = _waveProgress.CombatArea.SpawnManager;

        // Overlay Canvas의 스케일과 좌표계를 월드 렌더러에서 분리
        _worldRingRoot.SetParent(null, false);
        _worldRingRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _worldRingRoot.localScale = Vector3.one;
    }

    void LateUpdate()
    {
        if (_player == null)
        {
            ReturnAll();
            return;
        }

        _visibleTargets.Clear();
        if (_player.IsAlive)
            _visibleTargets.Add(_player);

        if (_waveProgress.IsRunning)
        {
            foreach (Enemy enemy in _enemies.LivingEnemies)
            {
                CombatTarget target = enemy.CombatTarget;
                if (target.IsAlive)
                    _visibleTargets.Add(target);
            }
        }

        ReturnUnusedRings();

        float spinAngle = Mathf.Repeat(Time.time * _rotationDegreesPerSecond, 360f);
        Quaternion ringRotation = RingRotation * Quaternion.AngleAxis(spinAngle, Vector3.forward);
        foreach (CombatTarget target in _visibleTargets)
            ShowRing(target, target == _player, ringRotation);
    }

    void OnDisable()
    {
        ReturnAll();
    }

    void OnDestroy()
    {
        if (_worldRingRoot != null && _worldRingRoot.parent == null)
            Destroy(_worldRingRoot.gameObject);
    }

    CombatantRingAnchor FindAnchor(CombatTarget target)
    {
        // 같은 프레임에 회수된 이전 Content의 비활성 앵커 제외
        target.ActorTransform.GetComponentsInChildren(false, _anchorMatches);

        if (_anchorMatches.Count != 1 || !_anchorMatches[0].isActiveAndEnabled)
        {
            throw new InvalidOperationException(
                $"{target.name}: 활성 캐릭터 계층에 CombatantRingAnchor 하나 필요");
        }

        CombatantRingAnchor anchor = _anchorMatches[0];
        _anchorMatches.Clear();
        return anchor;
    }

    void ShowRing(CombatTarget target, bool isPlayer, Quaternion ringRotation)
    {
        if (!_bindings.TryGetValue(target, out RingBinding binding))
        {
            CombatantRingAnchor anchor = FindAnchor(target);
            SpriteRenderer renderer = RentRing();
            Sprite sprite = isPlayer ? _playerRingSprite : _enemyRingSprite;
            float diameter = isPlayer ? _playerDiameter : _enemyDiameter;
            renderer.sprite = sprite;
            renderer.transform.localScale =
                Vector3.one * (diameter / (sprite.bounds.size.x * RingWidthRatio));

            binding = new RingBinding { Anchor = anchor, Renderer = renderer };
            _bindings.Add(target, binding);
        }

        Transform anchorTransform = binding.Anchor.transform;
        binding.Renderer.transform.SetPositionAndRotation(
            anchorTransform.position + anchorTransform.up * _heightOffset,
            anchorTransform.rotation * ringRotation);

        if (!binding.Renderer.gameObject.activeSelf)
            binding.Renderer.gameObject.SetActive(true);
    }

    SpriteRenderer RentRing()
    {
        if (_available.TryPop(out SpriteRenderer ring))
            return ring;

        GameObject ringObject = new GameObject("Combatant Ring", typeof(SpriteRenderer));
        ringObject.SetActive(false);
        ringObject.layer = _worldRingRoot.gameObject.layer;
        ringObject.transform.SetParent(_worldRingRoot, false);

        ring = ringObject.GetComponent<SpriteRenderer>();
        ring.sharedMaterial = _ringGlowMaterial;
        return ring;
    }

    void ReturnRing(SpriteRenderer ring)
    {
        if (ring == null) return;

        ring.gameObject.SetActive(false);
        _available.Push(ring);
    }

    void ReturnUnusedRings()
    {
        // 같은 프레임의 처치와 소환에서도 새 대여 전에 링 반납
        foreach (KeyValuePair<CombatTarget, RingBinding> entry in _bindings)
        {
            CombatantRingAnchor anchor = entry.Value.Anchor;
            if (!_visibleTargets.Contains(entry.Key) || anchor == null || !anchor.isActiveAndEnabled)
                _toRelease.Add(entry.Key);
        }

        foreach (CombatTarget target in _toRelease)
        {
            ReturnRing(_bindings[target].Renderer);
            _bindings.Remove(target);
        }

        _toRelease.Clear();
    }

    void ReturnAll()
    {
        foreach (RingBinding binding in _bindings.Values)
            ReturnRing(binding.Renderer);

        _bindings.Clear();
        _visibleTargets.Clear();
    }

    /// <summary>
    /// 현재 런의 플레이어 전투 대상 연결
    /// </summary>
    /// <param name="player">같은 씬에서 준비된 플레이어</param>
    public void Bind(PlayerActor player)
    {
        if (_player != null)
            throw new InvalidOperationException($"{name}: 이전 플레이어 연결 해제 필요");

        if (player == null || !player.IsPrepared || player.gameObject.scene != gameObject.scene)
            throw new InvalidOperationException($"{name}: 같은 씬의 준비된 플레이어 필요");

        _player = player.Target;
    }

    /// <summary>
    /// 플레이어 연결 해제와 대여 중인 링 반납
    /// </summary>
    public void Unbind()
    {
        _player = null;
        ReturnAll();
    }
}
