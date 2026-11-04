using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 전투 구역의 웨이브 배치 준비와 적 소환 요청
/// </summary>
public class CombatAreaProgress : MonoBehaviour
{
    [Header("----- 소환 연결 -----")]
    [SerializeField] EnemySpawnManager _spawnManager;

    [Header("----- 지면 검사 -----")]
    [SerializeField] LayerMask _groundMask = 1 << 3;
    [SerializeField] LayerMask _obstacleMask = (1 << 0) | (1 << 6) | (1 << 7);
    [SerializeField, Min(0.01f)] float _probeHeight = 10f;
    [SerializeField, Min(0.01f)] float _probeDepth = 30f;
    [SerializeField, Min(0.01f)] float _navMeshSampleDistance = 0.5f;
    [SerializeField, Min(0.001f)] float _maximumHeightAdjustment = 0.3f;

    [Header("----- 배치 탐색 -----")]
    [SerializeField, Min(1)] int _attemptsPerEnemy = 64;
    [SerializeField, Min(0f)] float _enemySpacing = 0.25f;

    IReadOnlyDictionary<(string EncounterId, int WaveIndex), WaveDifficultyDefinition>
        _difficultyDefinitions;

    public EnemySpawnManager SpawnManager => _spawnManager;

    void ValidateSettings()
    {
        if (_spawnManager == null || _spawnManager.Spawner == null)
            throw new MissingReferenceException($"{name}: 소환 Manager와 Spawner 연결 필요");

        if (_groundMask.value == 0 || _obstacleMask.value == 0 ||
            (_groundMask.value & _obstacleMask.value) != 0 ||
            !IsPositiveFinite(_probeHeight) || !IsPositiveFinite(_probeDepth) ||
            !IsPositiveFinite(_probeHeight + _probeDepth) ||
            !IsPositiveFinite(_navMeshSampleDistance) || !IsPositiveFinite(_maximumHeightAdjustment) ||
            _attemptsPerEnemy < 1 || !IsFinite(_enemySpacing) || _enemySpacing < 0f)
        {
            throw new InvalidOperationException($"{name}: 지면·장애물 Mask와 유한한 탐색 설정 확인 필요");
        }
    }

    void ValidateArea(MapBindings map, MapBindings.CombatAreaBinding area)
    {
        if (area.SpawnAreas == null || area.SpawnAreas.Count == 0)
            throw new MissingReferenceException($"{map.name}: 양수 소환 요청의 범위 연결 필요");

        foreach (SphereCollider sphere in area.SpawnAreas)
        {
            if (sphere == null || sphere.enabled || !sphere.gameObject.activeInHierarchy ||
                sphere.gameObject.scene != map.gameObject.scene ||
                !sphere.transform.IsChildOf(area.Root))
            {
                throw new InvalidOperationException($"{map.name}: 활성 구역 하위의 비활성 SphereCollider 연결 필요");
            }

            Vector3 scale = sphere.transform.lossyScale;
            Vector3 center = sphere.transform.TransformPoint(sphere.center);
            if (!IsPositiveFinite(scale.x) || !Mathf.Approximately(scale.x, scale.y) ||
                !Mathf.Approximately(scale.x, scale.z) || !IsPositiveFinite(sphere.radius * scale.x) ||
                !IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(center.z))
            {
                throw new InvalidOperationException($"{sphere.name}: 유한 중심·반경과 양수 균일 Scale 필요");
            }
        }
    }

    bool TryFindPose(
        MapBindings map, SphereCollider sphere, Vector3 playerPosition,
        float minimumPlayerDistance, NavMeshQueryFilter filter, float radius, float height,
        List<Pose> prepared, out Pose pose)
    {
        for (int attempt = 0; attempt < _attemptsPerEnemy; attempt++)
        {
            Vector3 center = sphere.transform.TransformPoint(sphere.center);
            float areaRadius = sphere.radius * sphere.transform.lossyScale.x;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * areaRadius;
            Vector3 origin = center + new Vector3(offset.x, _probeHeight, offset.y);

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit ground,
                _probeHeight + _probeDepth, _groundMask, QueryTriggerInteraction.Ignore) ||
                !ground.collider.transform.IsChildOf(map.transform))
            {
                continue;
            }

            if (!NavMesh.SamplePosition(ground.point, out NavMeshHit navigation,
                _navMeshSampleDistance, filter) ||
                Mathf.Abs(navigation.position.y - ground.point.y) > _maximumHeightAdjustment)
            {
                continue;
            }

            Vector3 position = navigation.position;
            // 보정된 XZ의 실제 지면도 같은 맵에 속하는지 재검사
            float probeOffset = _maximumHeightAdjustment + 0.01f;
            if (!Physics.Raycast(position + Vector3.up * probeOffset, Vector3.down,
                out RaycastHit correctedGround, probeOffset * 2f,
                _groundMask, QueryTriggerInteraction.Ignore) ||
                !correctedGround.collider.transform.IsChildOf(map.transform) ||
                Mathf.Abs(position.y - correctedGround.point.y) > _maximumHeightAdjustment)
            {
                continue;
            }

            // NavMesh 보정 이후에도 선택한 원과 플레이어 이격 조건 유지
            if (HorizontalDistanceSquared(position, center) > areaRadius * areaRadius ||
                HorizontalDistanceSquared(position, playerPosition) < minimumPlayerDistance * minimumPlayerDistance)
            {
                continue;
            }

            float spacing = radius * 2f + _enemySpacing;
            bool overlaps = false;
            foreach (Pose previous in prepared)
            {
                if (HorizontalDistanceSquared(position, previous.position) < spacing * spacing)
                {
                    overlaps = true;
                    break;
                }
            }

            Vector3 bottom = position + Vector3.up * (radius + 0.01f);
            Vector3 top = position + Vector3.up * (height - radius + 0.01f);
            if (overlaps || Physics.CheckCapsule(bottom, top, radius,
                _obstacleMask, QueryTriggerInteraction.Collide))
            {
                continue;
            }

            Vector3 direction = playerPosition - position;
            direction.y = 0f;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.identity;
            pose = new Pose(position, rotation);
            return true;
        }

        pose = default;
        return false;
    }

    static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.x - second.x;
        float z = first.z - second.z;
        return x * x + z * z;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool IsPositiveFinite(float value)
    {
        return IsFinite(value) && value > 0f;
    }

    /// <summary>
    /// 해당 웨이브와 같은 순번의 범위에서 전체 위치 확보 후 적 소환 요청
    /// </summary>
    /// <param name="map">선택한 맵</param>
    /// <param name="areaIndex">맵의 구역 인덱스</param>
    /// <param name="waveIndex">구역의 웨이브 인덱스</param>
    /// <returns>전체 위치 확보와 소환 요청 완료 여부</returns>
    public bool TrySpawnWave(MapBindings map, int areaIndex, int waveIndex)
    {
        ValidateSettings();
        if (map == null || map.gameObject.scene != gameObject.scene || !map.isActiveAndEnabled)
            throw new MissingReferenceException($"{name}: 같은 씬의 활성 MapBindings 연결 필요");

        MapBindings.CombatAreaBinding area = map.GetCombatArea(areaIndex);
        WaveSpawnData[] waves = area.SpawnData.WaveSpawnDatas;
        if (waves == null || area.SpawnAreas == null || waves.Length != area.SpawnAreas.Count)
            throw new InvalidOperationException($"{map.name}: 소환 범위와 웨이브 목록의 1:1 구성 필요");

        if (waveIndex < 0 || waveIndex >= waves.Length || waves[waveIndex] == null)
            throw new ArgumentOutOfRangeException(nameof(waveIndex), "구역에 연결된 웨이브 인덱스 필요");

        WaveSpawnData wave = waves[waveIndex];
        WaveSpawnPlan plan = CreateSpawnPlan(area.SpawnData.EncounterId, waveIndex, wave);
        int population = plan.TotalCount;
        EnemySpawner spawner = _spawnManager.Spawner;
        CombatTarget player = spawner.PlayerTarget;
        if (player == null || !player.IsAlive)
            throw new InvalidOperationException($"{name}: Spawner의 생존 플레이어 연결 필요");

        List<Pose> prepared = new List<Pose>(population);
        if (population > 0)
        {
            ValidateArea(map, area);
            spawner.GetSpawnAgentSettings(out NavMeshQueryFilter filter, out float radius, out float height);
            Physics.SyncTransforms();
            Vector3 playerPosition = player.Position;
            for (int i = 0; i < population; i++)
            {
                if (!TryFindPose(map, area.SpawnAreas[waveIndex], playerPosition, wave.MinimumPlayerDistance,
                    filter, radius, height, prepared, out Pose pose))
                {
                    // 대여 이전 실패, 준비된 위치도 이번 요청과 함께 폐기
                    return false;
                }

                prepared.Add(pose);
            }
        }

        _spawnManager.GetRequestWaveStart(plan, map.WorldType, prepared);
        return true;
    }

    /// <summary>
    /// 선택 난이도의 웨이브 설정 연결
    /// </summary>
    /// <param name="definitions">구역과 순번별 최종 소환 수량</param>
    public void BindDifficulty(
        IReadOnlyDictionary<(string EncounterId, int WaveIndex), WaveDifficultyDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
            throw new ArgumentException("웨이브 설정 필요", nameof(definitions));

        if (_difficultyDefinitions != null)
            throw new InvalidOperationException("이전 웨이브 설정 해제 필요");

        _difficultyDefinitions = definitions;
    }

    /// <summary>
    /// 월드 정리 이후 웨이브 설정 해제
    /// </summary>
    public void UnbindDifficulty()
    {
        _difficultyDefinitions = null;
    }

    /// <summary>
    /// 원본 배치 조건과 확정 소환 수량으로 계획 생성
    /// </summary>
    /// <param name="encounterId">구역 식별자</param>
    /// <param name="waveIndex">구역의 웨이브 순번</param>
    /// <param name="source">원본 웨이브</param>
    /// <returns>배치와 소환에 사용할 계획</returns>
    public WaveSpawnPlan CreateSpawnPlan(string encounterId, int waveIndex, WaveSpawnData source)
    {
        if (_difficultyDefinitions == null)
            throw new InvalidOperationException("웨이브 난이도 설정 연결 필요");

        if (!_difficultyDefinitions.TryGetValue(
                (encounterId, waveIndex), out WaveDifficultyDefinition counts))
        {
            throw new InvalidOperationException($"웨이브 설정 누락 {encounterId}/{waveIndex}");
        }

        return new WaveSpawnPlan(source, counts);
    }

}
