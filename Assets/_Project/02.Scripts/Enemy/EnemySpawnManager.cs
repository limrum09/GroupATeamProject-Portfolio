using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 대여한 적의 사망 추적과 연출 종료 후 반환
/// </summary>
public class EnemySpawnManager : MonoBehaviour
{
    [Header("----- 컴포넌트 -----")]
    [SerializeField] EnemySpawner _enemySpawner;	// 공용 적 객체의 대여와 반환 담당

    // 추적 중인 적의 사망 이벤트 구독 관리
    // Key: 대여한 Enemy 인스턴스
    // Value: 해당 적에 등록한 사망 콜백, 구독 해제에 사용
    readonly Dictionary<Enemy, Action> _enemies =
        new Dictionary<Enemy, Action>();

    // 사망 연출 종료를 기다리는 적의 이벤트 구독 관리
    // Key: 사망 연출 대기 중인 Enemy 인스턴스
    // Value: 구독 해제에 사용할 사망 애니메이션 종료 콜백
    readonly Dictionary<Enemy, Action> _pendingReturns =
        new Dictionary<Enemy, Action>();

    // 활성화 전 외부 생존 목록 등록에 사용할 소환 결과
    public event Action<WaveSpawnData, IReadOnlyList<Enemy>> OnWaveSpawned;

    // 풀 반환 전 확인된 사망 결과 전달
    public event Action<Enemy> OnEnemyDied;
    public event Action<float> OnPlayerDamageApplied;

    bool _requestInProgress;
    bool _clearing;

    public int EnemyCount => _enemies.Count;
    public EnemySpawner Spawner => _enemySpawner;

    // 활성화 전 등록된 적 포함, 열거 중 소환 및 정리 요청 금지
    public IEnumerable<Enemy> LivingEnemies => _enemies.Keys;

    void Awake()
    {
        if (_enemySpawner == null)
        {
            throw new MissingReferenceException(
                $"{name}: EnemySpawner가 필요합니다.");
        }
    }

    /// <summary>
    /// World에서 확정한 위치로 웨이브 대여와 추적 및 활성화
    /// </summary>
    /// <param name="plan">확정 웨이브 계획</param>
    /// <param name="worldType">적 후보 월드</param>
    /// <param name="spawnPoses">확정된 전체 위치와 회전</param>
    public void GetRequestWaveStart(
        WaveSpawnPlan plan, EnemyWorldType worldType, IReadOnlyList<Pose> spawnPoses)
    {
        ExecuteRequest(plan, worldType, spawnPoses);
    }

    void ExecuteRequest(
        WaveSpawnPlan plan, EnemyWorldType world, IReadOnlyList<Pose> poses)
    {
        if (_requestInProgress || _clearing)
            throw new InvalidOperationException($"{name}: 소환 또는 정리 중 중첩 요청 불가");

        _requestInProgress = true;
        List<Enemy> spawned = null;
        try
        {
            spawned = _enemySpawner.Spawn(plan, world, poses);
            RegisterAndActivate(plan.Source, spawned);
        }
        catch (Exception requestError)
        {
            List<Exception> errors = new List<Exception> { requestError };
            if (spawned != null)
            {
                foreach (Enemy enemy in spawned)
                {
                    enemy.CombatTarget.OnDamaged -= HandleEnemyDamaged;

                    if (_enemies.TryGetValue(enemy, out Action died))
                    {
                        enemy.Status.OnDied -= died;
                        _enemies.Remove(enemy);
                    }

                    if (_pendingReturns.TryGetValue(enemy, out Action ended))
                    {
                        enemy.OnDeathAnimationEnded -= ended;
                        _pendingReturns.Remove(enemy);
                    }

                    try
                    {
                        _enemySpawner.Release(enemy);
                    }
                    catch (Exception cleanupError)
                    {
                        errors.Add(cleanupError);
                    }
                }
            }

            if (errors.Count > 1)
                throw new AggregateException($"{name}: 웨이브 등록과 회수 중 오류", errors);

            throw;
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    void RegisterAndActivate(WaveSpawnData waveSpawnData, List<Enemy> spawned)
    {
        foreach (Enemy enemy in spawned)
        {
            // 동일 콜백을 보관하여 사망 구독 해제에 사용
            Action handler = () => HandleEnemyDied(enemy);

            _enemies.Add(enemy, handler);
            enemy.Status.OnDied += handler;
            enemy.CombatTarget.OnDamaged += HandleEnemyDamaged;
        }

        // 외부에서 생존 목록 등록 후 활성화
        OnWaveSpawned?.Invoke(waveSpawnData, spawned);

        foreach (Enemy enemy in spawned)
        {
            if (_enemies.ContainsKey(enemy))
                enemy.gameObject.SetActive(true);
        }
    }

    void HandleEnemyDamaged(DamageRequest request, float appliedDamage)
    {
        if (request.Source.Team == CombatTeam.Player && appliedDamage > 0f)
            OnPlayerDamageApplied?.Invoke(appliedDamage);
    }

    void HandleEnemyDied(Enemy enemy)
    {
        if (_enemies.TryGetValue(enemy, out Action handler) == false)
            return;

        enemy.Status.OnDied -= handler;
        _enemies.Remove(enemy);

        // AI 사망 처리와의 구독 순서에 관계없이 반환 대기 등록
        Action endHandler = () => HandleDeathAnimationEnded(enemy);
        _pendingReturns.Add(enemy, endHandler);
        enemy.OnDeathAnimationEnded += endHandler;

        // 처치 결과는 즉시 전달하고 실제 반환은 연출 종료까지 대기
        OnEnemyDied?.Invoke(enemy);
    }

    void HandleDeathAnimationEnded(Enemy enemy)
    {
        if (_pendingReturns.TryGetValue(enemy, out Action handler) == false)
            return;

        enemy.OnDeathAnimationEnded -= handler;
        _pendingReturns.Remove(enemy);
        enemy.CombatTarget.OnDamaged -= HandleEnemyDamaged;
        _enemySpawner.Release(enemy);
    }

    /// <summary>
    /// 생존 적과 사망 연출 대기 적의 전체 반환 시도 후 정리 오류 일괄 보고
    /// </summary>
    public void ClearSessionEnemies()
    {
        if (_clearing)
            return;

        _clearing = true;
        try
        {
            ClearTrackedEnemies();
        }
        finally
        {
            _clearing = false;
        }
    }

    void ClearTrackedEnemies()
    {
        List<Enemy> enemies = new List<Enemy>(_enemies.Count + _pendingReturns.Count);

        foreach (KeyValuePair<Enemy, Action> entry in _enemies)
        {
            entry.Key.Status.OnDied -= entry.Value;
            enemies.Add(entry.Key);
        }

        foreach (KeyValuePair<Enemy, Action> entry in _pendingReturns)
        {
            entry.Key.OnDeathAnimationEnded -= entry.Value;
            enemies.Add(entry.Key);
        }

        // 반환 콜백과 관리 목록 순회를 분리할 대상 목록
        _enemies.Clear();
        _pendingReturns.Clear();

        List<Exception> errors = new List<Exception>();

        foreach (Enemy enemy in enemies)
        {
            try
            {
                enemy.CombatTarget.OnDamaged -= HandleEnemyDamaged;
                _enemySpawner.Release(enemy);
            }
            catch (Exception error)
            {
                // 첫 객체와 중간 객체의 실패에도 남은 적 반환 계속
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(
                $"{name}: 전체 적 반환 시도 중 정리 실패",
                errors);
        }
    }
}
