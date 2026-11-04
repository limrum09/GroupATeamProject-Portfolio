using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 공용 적 객체의 풀 대여와 소환 구성 및 반환
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("----- 컴포넌트 -----")]
    [SerializeField] EnemyPool _enemyPool;	// 월드와 등급별 적 데이터 후보 조회 담당

    [Header("----- 씬 이펙트 재생기 -----")]
    [SerializeField] CombatEffect _effectPlayer;

    [Header("----- 공용 적 객체 -----")]
    [SerializeField] Enemy _enemyPrefab;	// 모든 적 종류가 공유하는 기본 객체 프리팹
    [SerializeField] Transform _poolRoot;	// 신규 공용 객체의 비활성 생성 위치

    IReadOnlyDictionary<string, EnemyDifficultyDefinition> _difficultyDefinitions;

    // 런 준비 단계에서 전달받은 적의 감지·공격 대상
    CombatTarget _playerTarget;

    public CombatTarget PlayerTarget => _playerTarget;

    // 재대여 가능한 비활성 공용 객체
    readonly Stack<Enemy> _available = new Stack<Enemy>();

    // 대여 중인 공용 객체, 중복 반환 차단
    readonly HashSet<Enemy> _rented = new HashSet<Enemy>();

    void Awake()
    {
        if (_enemyPool == null || _enemyPrefab == null || _poolRoot == null ||
            _effectPlayer == null)
        {
            throw new MissingReferenceException(
                $"{name}: EnemyPool, EnemyPrefab, PoolRoot, CombatEffectPlayer가 필요합니다.");
        }

        // Spawner와 함께 제거되는 비활성 생성 계층
        if (_poolRoot == transform ||
            _poolRoot.IsChildOf(transform) == false ||
            _poolRoot.gameObject.activeSelf)
        {
            throw new System.InvalidOperationException(
                $"{name}: PoolRoot는 비활성 자식 오브젝트여야 합니다.");
        }
    }

    List<EnemyDefinition> GetValidatedCandidates(EnemyWorldType worldType, EnemyRankType rankType, int population)
    {
        if (population == 0)
            return new List<EnemyDefinition>();

        List<EnemyDefinition> candidates = _enemyPool.GetEnemyDefinitions(worldType, rankType);
        if (candidates.Count == 0)
        {
            throw new System.InvalidOperationException(
                $"{name}: {worldType}/{rankType} 소환 요청 {population}개에 대한 후보 부재");
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            EnemyDefinition definition = candidates[i];
            GetDifficultyDefinition(definition);
            if (definition.Prefab == null)
            {
                throw new MissingReferenceException(
                    $"{name}: {worldType}/{rankType} 후보[{i}] '{definition.name}'의 Prefab 연결 필요");
            }

            EnemyModel model = definition.Prefab.GetComponent<EnemyModel>();
            if (model == null)
            {
                throw new MissingReferenceException(
                    $"{name}: {worldType}/{rankType} 후보[{i}] '{definition.name}'의 Prefab 루트에 EnemyModel 필요");
            }

            model.ValidateConfiguration();
        }

        return candidates;
    }

    /// <summary>
    /// 소환할 적이 사용할 준비된 플레이어 타겟 연결
    /// </summary>
    /// <param name="playerTarget">이번 런의 생존 플레이어 타겟</param>
    public void BindPlayerTarget(CombatTarget playerTarget)
    {
        if (playerTarget == null)
            throw new System.ArgumentNullException(nameof(playerTarget));

        if (_rented.Count != 0)
        {
            throw new System.InvalidOperationException(
                $"{name}: 대여 중인 적 정리 후 타겟 연결 필요");
        }

        if (playerTarget.Team != CombatTeam.Player ||
            !playerTarget.IsAlive ||
            playerTarget.gameObject.scene != gameObject.scene)
        {
            throw new System.InvalidOperationException(
                $"{name}: 같은 씬의 준비된 생존 플레이어 타겟 필요");
        }

        _playerTarget = playerTarget;
    }

    /// <summary>
    /// 모든 대여 적 정리 이후 런의 플레이어 타겟 연결 해제
    /// </summary>
    public void UnbindPlayerTarget()
    {
        if (_rented.Count != 0)
        {
            throw new System.InvalidOperationException(
                $"{name}: 대여 중인 적 정리 후 타겟 연결 해제 필요");
        }

        _playerTarget = null;
    }

    /// <summary>
    /// 공용 적 프리팹의 단일 Agent에서 배치 검사 설정 조회
    /// </summary>
    /// <param name="filter">Agent 종류와 이동 영역</param>
    /// <param name="radius">배치 검사 반경</param>
    /// <param name="height">배치 검사 높이</param>
    public void GetSpawnAgentSettings(out NavMeshQueryFilter filter, out float radius, out float height)
    {
        if (_enemyPrefab == null)
            throw new MissingReferenceException($"{name}: 공용 Enemy 프리팹 연결 필요");

        // 공용 프리팹의 단일 Agent 구성 계약, 씬 객체 자동 탐색과 구분
        NavMeshAgent[] agents = _enemyPrefab.GetComponentsInChildren<NavMeshAgent>(true);
        if (agents.Length != 1)
            throw new System.InvalidOperationException($"{name}: 공용 Enemy 프리팹에 단일 NavMeshAgent 필요");

        NavMeshAgent agent = agents[0];
        Vector3 scale = agent.transform.lossyScale;
        if (!IsPositiveFinite(scale.x) || !Mathf.Approximately(scale.x, scale.y) ||
            !Mathf.Approximately(scale.x, scale.z))
        {
            throw new System.InvalidOperationException($"{name}: Agent 프리팹에 양수 균일 Scale 필요");
        }

        radius = agent.radius * scale.x;
        height = agent.height * scale.x;
        if (!IsPositiveFinite(radius) || !IsPositiveFinite(height) || height < radius * 2f)
            throw new System.InvalidOperationException($"{name}: 유효한 Agent 반경과 높이 필요");

        filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
    }

    /// <summary>
    /// 확정된 전체 위치 검사 후 적 구성과 비활성 대여
    /// </summary>
    /// <param name="plan">웨이브 설정</param>
    /// <param name="worldType">적 후보를 조회할 월드</param>
    /// <param name="spawnPoses">전체 소환 수량에 대응하는 확정 위치와 회전</param>
    /// <returns>구성과 배치가 완료된 비활성 적 목록</returns>
    public List<Enemy> Spawn(
        WaveSpawnPlan plan,
        EnemyWorldType worldType,
        IReadOnlyList<Pose> spawnPoses)
    {
        if (plan == null)
            throw new System.ArgumentNullException(nameof(plan));

        if (_playerTarget == null || !_playerTarget.IsAlive)
            throw new System.InvalidOperationException($"{name}: 소환 전 생존 플레이어 타겟 연결 필요");

        int population = plan.TotalCount;
        if (spawnPoses == null || spawnPoses.Count != population)
            throw new System.ArgumentException("소환 수량과 확정 위치 개수 일치 필요", nameof(spawnPoses));

        _enemyPool.ValidateWorldType(worldType);

        // 호출자 목록 변경에 영향받지 않는 이번 요청의 위치 스냅샷
        Pose[] poses = new Pose[population];
        for (int i = 0; i < population; i++)
        {
            Pose pose = spawnPoses[i];
            float rotationLength = Quaternion.Dot(pose.rotation, pose.rotation);
            if (!IsFinite(pose.position.x) || !IsFinite(pose.position.y) ||
                !IsFinite(pose.position.z) || !IsFinite(rotationLength) ||
                Mathf.Abs(rotationLength - 1f) > 0.001f)
            {
                throw new System.ArgumentException($"확정 위치[{i}]의 유한 좌표와 단위 회전 필요", nameof(spawnPoses));
            }

            poses[i] = pose;
        }

        List<Enemy> spawned = new List<Enemy>(population);
        if (population == 0)
            return spawned;

        List<EnemyDefinition> normalCandidates = GetValidatedCandidates(worldType, EnemyRankType.Normal, plan.Counts.NormalCount);
        List<EnemyDefinition> epicCandidates = GetValidatedCandidates(worldType, EnemyRankType.Epic, plan.Counts.EpicCount);
        List<EnemyDefinition> midBossCandidates = GetValidatedCandidates(worldType, EnemyRankType.MidBoss, plan.Counts.MidBossCount);
        List<EnemyDefinition> bossCandidates = GetValidatedCandidates(worldType, EnemyRankType.Boss, plan.Counts.BossCount);

        try
        {
            SpawnRank(plan.Counts.NormalCount, normalCandidates, poses, spawned);
            SpawnRank(plan.Counts.EpicCount, epicCandidates, poses, spawned);
            SpawnRank(plan.Counts.MidBossCount, midBossCandidates, poses, spawned);
            SpawnRank(plan.Counts.BossCount, bossCandidates, poses, spawned);
            return spawned;
        }
        catch (System.Exception spawnError)
        {
            List<System.Exception> errors = new List<System.Exception> { spawnError };
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                try
                {
                    Release(spawned[i]);
                }
                catch (System.Exception cleanupError)
                {
                    errors.Add(cleanupError);
                }
            }

            if (errors.Count > 1)
                throw new System.AggregateException($"{name}: 소환과 실패 회수 중 오류", errors);

            throw;
        }
    }

    void SpawnRank(int population, List<EnemyDefinition> candidates, Pose[] poses, List<Enemy> spawned)
    {
        for (int i = 0; i < population; i++)
        {
            EnemyDefinition definition = candidates[Random.Range(0, candidates.Count)];
            Pose pose = poses[spawned.Count];
            Enemy enemy = GetPoolEnemy(spawned);
            enemy.transform.SetPositionAndRotation(pose.position, pose.rotation);
            enemy.Init(definition, GetDifficultyDefinition(definition), _playerTarget, _effectPlayer);
        }
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool IsPositiveFinite(float value)
    {
        return IsFinite(value) && value > 0f;
    }

    Enemy GetPoolEnemy(List<Enemy> rentedForRequest)
    {
        Enemy enemy;

        if (_available.Count > 0)
        {
            enemy = _available.Pop();
        }
        else
        {
            // 비활성 부모 아래에서 생성하여 조기 활성화 방지
            enemy = Instantiate(_enemyPrefab, _poolRoot);
        }

        // 위치 변경과 초기화 이전부터 실패 회수 대상에 포함
        rentedForRequest.Add(enemy);
        _rented.Add(enemy);

        enemy.gameObject.SetActive(false);
        enemy.transform.SetParent(transform, false);

        return enemy;
    }

    /// <summary>
    /// 종류별 구성 해제와 공용 객체 반환 및 정리 실패 객체 폐기
    /// </summary>
    /// <param name="enemy">반환할 적 객체</param>
    public void Release(Enemy enemy)
    {
        if (!_rented.Contains(enemy))
            return;

        try
        {
            enemy.gameObject.SetActive(false);
            enemy.Clear();
        }
        catch
        {
            // 정리 실패 객체의 풀 재진입 차단
            Destroy(enemy.gameObject);
            throw;
        }
        finally
        {
            _rented.Remove(enemy);
        }

        // 정리가 완료된 객체만 재사용
        _available.Push(enemy);
    }

    EnemyDifficultyDefinition GetDifficultyDefinition(EnemyDefinition definition)
    {
        if (_difficultyDefinitions == null)
            throw new System.InvalidOperationException("적 난이도 설정 연결 필요");

        if (string.IsNullOrWhiteSpace(definition.EnemyId) ||
            !_difficultyDefinitions.TryGetValue(definition.EnemyId, out EnemyDifficultyDefinition difficulty))
        {
            throw new System.InvalidOperationException(
                $"{definition.name}: 적 난이도 설정 누락 {definition.EnemyId}");
        }

        return difficulty;
    }

    /// <summary>
    /// 선택 난이도의 적 설정 연결
    /// </summary>
    /// <param name="definitions">적 ID별 최종 설정</param>
    public void BindDifficulty(IReadOnlyDictionary<string, EnemyDifficultyDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
            throw new System.ArgumentException("적 설정 필요", nameof(definitions));

        if (_difficultyDefinitions != null || _rented.Count != 0)
            throw new System.InvalidOperationException("이전 적과 난이도 연결 해제 필요");

        _difficultyDefinitions = definitions;
    }

    /// <summary>
    /// 모든 대여 적 반환 이후 설정 해제
    /// </summary>
    public void UnbindDifficulty()
    {
        if (_rented.Count != 0)
            throw new System.InvalidOperationException("대여 적 전체 반환 필요");

        _difficultyDefinitions = null;
    }

    /// <summary>
    /// 양수 수량의 등급별 적 후보와 난이도 설정 검사
    /// </summary>
    /// <param name="worldType">적 후보 월드</param>
    /// <param name="counts">확정 웨이브 수량</param>
    public void ValidateSpawnCandidates(EnemyWorldType worldType, WaveDifficultyDefinition counts)
    {
        GetValidatedCandidates(worldType, EnemyRankType.Normal, counts.NormalCount);
        GetValidatedCandidates(worldType, EnemyRankType.Epic, counts.EpicCount);
        GetValidatedCandidates(worldType, EnemyRankType.MidBoss, counts.MidBossCount);
        GetValidatedCandidates(worldType, EnemyRankType.Boss, counts.BossCount);
    }

}
