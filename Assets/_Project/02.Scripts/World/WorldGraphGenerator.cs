using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 삼각 격자의 배치·분기·합류와 최종 거리 및 표시 좌표 생성
/// </summary>
public class WorldGraphGenerator
{
    const float NodeClearance = 0.25f;

    void Connect(List<List<int>> connections, int first, int second)
    {
        connections[first].Add(second);
        connections[second].Add(first);
    }

    void AddNode(List<Vector2Int> coordinates, List<List<int>> connections, Vector2Int coordinate)
    {
        coordinates.Add(coordinate);
        connections.Add(new List<int>());
    }

    void CreateMainRoute(WorldGraphGenerationSettings settings, System.Random random,
        List<Vector2Int> coordinates, List<List<int>> connections, List<int> mainRoute)
    {
        AddNode(coordinates, connections, Vector2Int.zero);
        mainRoute.Add(0);
        for (int step = 0; step < settings.BossDistance; step++)
        {
            // 화면상 오른쪽으로 전진하는 세 방향의 기본 경로
            int choice = random.Next(3);
            int direction = choice == 2 ? 5 : choice;
            Vector2Int coordinate = TriangularGrid.GetNeighbor(coordinates[step], direction);
            AddNode(coordinates, connections, coordinate);
            Connect(connections, step, step + 1);
            mainRoute.Add(step + 1);
        }
    }

    bool TryCreateBranches(WorldGraphGenerationSettings settings, System.Random random,
        int additionalCount, List<Vector2Int> coordinates, List<List<int>> connections, int bossNodeId)
    {
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>(coordinates);
        int minimumRow = 0;
        int maximumRow = 0;
        foreach (Vector2Int coordinate in coordinates)
        {
            minimumRow = Math.Min(minimumRow, coordinate.y);
            maximumRow = Math.Max(maximumRow, coordinate.y);
        }
        minimumRow -= settings.BranchRowPadding;
        maximumRow += settings.BranchRowPadding;
        float maximumX = TriangularGrid.ToPosition(coordinates[bossNodeId]).x + 1f;

        for (int i = 0; i < additionalCount; i++)
        {
            List<(int Parent, Vector2Int Position)> candidates = new List<(int, Vector2Int)>();
            for (int parent = 0; parent < coordinates.Count; parent++)
            {
                if (parent == bossNodeId || connections[parent].Count >= settings.MaximumConnections)
                    continue;

                for (int direction = 0; direction < TriangularGrid.NeighborCount; direction++)
                {
                    Vector2Int position = TriangularGrid.GetNeighbor(coordinates[parent], direction);
                    float x = TriangularGrid.ToPosition(position).x;
                    if (!occupied.Contains(position) && position.y >= minimumRow &&
                        position.y <= maximumRow && x >= -1f && x <= maximumX)
                    {
                        candidates.Add((parent, position));
                    }
                }
            }

            if (candidates.Count == 0)
                return false;

            (int Parent, Vector2Int Position) candidate = candidates[random.Next(candidates.Count)];
            int id = coordinates.Count;
            AddNode(coordinates, connections, candidate.Position);
            occupied.Add(candidate.Position);
            Connect(connections, candidate.Parent, id);
        }
        return true;
    }

    int[] CalculateDistances(List<List<int>> connections, int startNodeId)
    {
        int[] distances = new int[connections.Count];
        Array.Fill(distances, -1);
        Queue<int> pending = new Queue<int>();
        distances[startNodeId] = 0;
        pending.Enqueue(startNodeId);
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            foreach (int next in connections[current])
            {
                if (distances[next] >= 0)
                    continue;

                distances[next] = distances[current] + 1;
                pending.Enqueue(next);
            }
        }
        return distances;
    }

    List<(int First, int Second)> CreateConnectionCandidates(List<Vector2Int> coordinates,
        List<List<int>> connections, int bossNodeId, System.Random random)
    {
        List<(int First, int Second)> candidates = new List<(int, int)>();
        for (int first = 0; first < coordinates.Count; first++)
        {
            for (int second = first + 1; second < coordinates.Count; second++)
            {
                if (first != bossNodeId && second != bossNodeId &&
                    !connections[first].Contains(second) &&
                    TriangularGrid.AreNeighbors(coordinates[first], coordinates[second]))
                {
                    candidates.Add((first, second));
                }
            }
        }

        // ID 순서로 연결 우선순위가 고정되지 않도록 후보 순서 섞기
        for (int index = candidates.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (int First, int Second) saved = candidates[index];
            candidates[index] = candidates[other];
            candidates[other] = saved;
        }
        return candidates;
    }

    bool TryConnect(WorldGraphGenerationSettings settings, List<Vector2Int> coordinates,
        List<List<int>> connections, int bossNodeId, int first, int second)
    {
        if (first == second || first == bossNodeId || second == bossNodeId ||
            connections[first].Contains(second) ||
            connections[first].Count >= settings.MaximumConnections ||
            connections[second].Count >= settings.MaximumConnections ||
            !TriangularGrid.AreNeighbors(coordinates[first], coordinates[second]))
        {
            return false;
        }

        Connect(connections, first, second);
        int[] distances = CalculateDistances(connections, 0);
        if (distances[bossNodeId] == settings.BossDistance)
            return true;

        // 지정된 시작→보스 최단 거리를 깨는 합류 연결 복구
        connections[first].Remove(second);
        connections[second].Remove(first);
        return false;
    }

    void AddExtraConnections(WorldGraphGenerationSettings settings, System.Random random,
        List<Vector2Int> coordinates, List<List<int>> connections, int bossNodeId)
    {
        List<(int First, int Second)> candidates =
            CreateConnectionCandidates(coordinates, connections, bossNodeId, random);

        foreach ((int First, int Second) candidate in candidates)
        {
            if (random.NextDouble() >= settings.AdditionalConnectionChance)
                continue;

            TryConnect(settings, coordinates, connections, bossNodeId, candidate.First, candidate.Second);
        }
    }

    int CountNodesWithConnectionCount(List<List<int>> connections, int connectionCount)
    {
        int count = 0;
        foreach (List<int> neighbors in connections)
        {
            if (neighbors.Count == connectionCount)
                count++;
        }
        return count;
    }

    int GetExcessCount(WorldGraphGenerationSettings settings, int leafCount, int twoConnectionCount)
    {
        return Math.Max(0, leafCount - settings.MaximumLeafNodes) +
               Math.Max(0, twoConnectionCount - settings.MaximumTwoConnectionNodes);
    }

    bool AreConnectionCountsValid(WorldGraphGenerationSettings settings, List<List<int>> connections)
    {
        int leafCount = CountNodesWithConnectionCount(connections, 1);
        int twoConnectionCount = CountNodesWithConnectionCount(connections, 2);
        return leafCount >= settings.MinimumLeafNodes && leafCount <= settings.MaximumLeafNodes &&
               twoConnectionCount >= settings.MinimumTwoConnectionNodes &&
               twoConnectionCount <= settings.MaximumTwoConnectionNodes;
    }

    bool TryLimitConnectionCounts(WorldGraphGenerationSettings settings, System.Random random,
        List<Vector2Int> coordinates, List<List<int>> connections, int bossNodeId)
    {
        List<(int First, int Second)> candidates =
            CreateConnectionCandidates(coordinates, connections, bossNodeId, random);

        while (true)
        {
            int leafCount = CountNodesWithConnectionCount(connections, 1);
            int twoCount = CountNodesWithConnectionCount(connections, 2);
            // 최솟값 부족은 배치 재생성으로 처리
            if (leafCount < settings.MinimumLeafNodes || twoCount < settings.MinimumTwoConnectionNodes)
                return false;

            int excess = GetExcessCount(settings, leafCount, twoCount);
            if (excess == 0)
                return true;

            bool connected = false;
            // 초과 감소 후보 우선, 이후 초과 합계 유지 후보 검토
            for (int pass = 0; pass < 2 && !connected; pass++)
            {
                foreach ((int First, int Second) candidate in candidates)
                {
                    int first = candidate.First;
                    int second = candidate.Second;
                    if (connections[first].Contains(second))
                        continue;

                    int firstDegree = connections[first].Count;
                    int secondDegree = connections[second].Count;
                    int nextLeafCount = leafCount - (firstDegree == 1 ? 1 : 0) - (secondDegree == 1 ? 1 : 0);
                    int nextTwoCount = twoCount + (firstDegree == 1 ? 1 : firstDegree == 2 ? -1 : 0) +
                        (secondDegree == 1 ? 1 : secondDegree == 2 ? -1 : 0);

                    if (nextLeafCount < settings.MinimumLeafNodes || nextTwoCount < settings.MinimumTwoConnectionNodes)
                        continue;
                    if (nextLeafCount == leafCount && nextTwoCount == twoCount)
                        continue;

                    int nextExcess = GetExcessCount(settings, nextLeafCount, nextTwoCount);
                    if (pass == 0 ? nextExcess >= excess : nextExcess != excess)
                        continue;
                    if (!TryConnect(settings, coordinates, connections, bossNodeId, first, second))
                        continue;

                    connected = true;
                    break;
                }
            }

            if (!connected)
                return false;
        }
    }

    void ValidateTopology(WorldGraphGenerationSettings settings, List<Vector2Int> coordinates,
        List<List<int>> connections, int bossNodeId, int[] distances)
    {
        if (distances[bossNodeId] != settings.BossDistance || connections[bossNodeId].Count != 1)
            throw new InvalidOperationException("지정된 보스 거리와 종단 연결 필요");
        if (!AreConnectionCountsValid(settings, connections))
            throw new InvalidOperationException("연결 1개·2개 노드 수의 최소·최대 조건 불일치");

        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
        for (int id = 0; id < coordinates.Count; id++)
        {
            if (!occupied.Add(coordinates[id]) || distances[id] < 0 ||
                connections[id].Count < 1 || connections[id].Count > settings.MaximumConnections)
            {
                throw new InvalidOperationException($"노드 {id}: 고유 좌표와 연결 가능한 경로 필요");
            }

            HashSet<int> neighbors = new HashSet<int>();
            foreach (int next in connections[id])
            {
                if (next < 0 || next >= coordinates.Count || !neighbors.Add(next) ||
                    !TriangularGrid.AreNeighbors(coordinates[id], coordinates[next]) ||
                    !connections[next].Contains(id))
                {
                    throw new InvalidOperationException($"노드 {id}: 중복 없는 인접 양방향 연결 필요");
                }
            }
        }
    }

    float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 difference = end - start;
        float amount = Mathf.Clamp01(Vector2.Dot(point - start, difference) / difference.sqrMagnitude);
        return Vector2.Distance(point, start + difference * amount);
    }

    bool SegmentsIntersect(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
    {
        float firstSide = Cross(firstEnd - firstStart, secondStart - firstStart);
        float secondSide = Cross(firstEnd - firstStart, secondEnd - firstStart);
        float thirdSide = Cross(secondEnd - secondStart, firstStart - secondStart);
        float fourthSide = Cross(secondEnd - secondStart, firstEnd - secondStart);
        if (firstSide * secondSide < 0f && thirdSide * fourthSide < 0f)
            return true;

        const float tolerance = 0.0001f;
        return DistanceToSegment(firstStart, secondStart, secondEnd) < tolerance ||
               DistanceToSegment(firstEnd, secondStart, secondEnd) < tolerance ||
               DistanceToSegment(secondStart, firstStart, firstEnd) < tolerance ||
               DistanceToSegment(secondEnd, firstStart, firstEnd) < tolerance;
    }

    bool IsLayoutValid(Vector2[] positions, List<List<int>> connections)
    {
        for (int first = 0; first < positions.Length; first++)
        {
            for (int second = first + 1; second < positions.Length; second++)
            {
                if (Vector2.Distance(positions[first], positions[second]) < NodeClearance * 2f)
                    return false;
            }

            foreach (int next in connections[first])
            {
                if (next <= first)
                    continue;

                for (int other = 0; other < positions.Length; other++)
                {
                    if (other != first && other != next &&
                        DistanceToSegment(positions[other], positions[first], positions[next]) < NodeClearance)
                    {
                        return false;
                    }
                }

                for (int other = first + 1; other < positions.Length; other++)
                {
                    foreach (int end in connections[other])
                    {
                        if (end <= other || other == next || end == first || end == next)
                            continue;

                        if (SegmentsIntersect(positions[first], positions[next], positions[other], positions[end]))
                            return false;
                    }
                }
            }
        }
        return true;
    }

    Vector2[] CreateLayout(WorldGraphGenerationSettings settings, int seed,
        List<Vector2Int> coordinates, List<List<int>> connections)
    {
        Vector2[] positions = new Vector2[coordinates.Count];
        for (int id = 0; id < coordinates.Count; id++)
            positions[id] = TriangularGrid.ToPosition(coordinates[id]);

        if (!IsLayoutValid(positions, connections))
            throw new InvalidOperationException("노드 관통과 교차가 없는 기본 격자 배치 필요");

        // 외형 설정 변경과 경로 생성의 난수 흐름 분리
        System.Random random = new System.Random(unchecked(seed ^ 0x4F1BBCDC));
        for (int id = 0; id < positions.Length; id++)
        {
            Vector2 original = positions[id];
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = (float)random.NextDouble() * settings.PositionJitter;
                positions[id] = original + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (IsLayoutValid(positions, connections))
                    break;

                // 선택적 외형 보정이 유효하지 않으면 원래 좌표 유지
                positions[id] = original;
            }
        }
        return positions;
    }

    WorldGraphGenerationResult CreateResult(WorldGraphGenerationSettings settings, int seed,
        System.Random random, List<Vector2Int> coordinates, List<List<int>> connections,
        List<int> mainRoute, int bossNodeId)
    {
        int[] distances = CalculateDistances(connections, 0);
        ValidateTopology(settings, coordinates, connections, bossNodeId, distances);
        Vector2[] positions = CreateLayout(settings, seed, coordinates, connections);
        int mainShopId = mainRoute[random.Next(1, mainRoute.Count - 1)];
        int branchShopId = random.Next(mainRoute.Count, coordinates.Count);
        List<WorldGraphNodeData> nodes = new List<WorldGraphNodeData>();
        for (int id = 0; id < coordinates.Count; id++)
        {
            MapBindings.MapType type = id == 0 ? MapBindings.MapType.Start :
                id == bossNodeId ? MapBindings.MapType.Boss :
                id == mainShopId || id == branchShopId ? MapBindings.MapType.Shop : MapBindings.MapType.Combat;
            nodes.Add(new WorldGraphNodeData(coordinates[id], positions[id], distances[id], type));
        }
        return new WorldGraphGenerationResult(nodes, connections, mainRoute, 0, bossNodeId);
    }

    /// <summary>
    /// 노드 수와 연결 수별 개수 범위를 만족하는 그래프 생성
    /// </summary>
    /// <param name="settings">경로·노드 수·연결·표시 설정</param>
    /// <param name="seed">경로 재현용 시드</param>
    /// <returns>검증과 표시 보정이 완료된 생성 결과</returns>
    public WorldGraphGenerationResult Generate(WorldGraphGenerationSettings settings, int seed)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        settings.Validate();
        for (int attempt = 0; attempt < settings.MaximumAttempts; attempt++)
        {
            int attemptSeed = unchecked(seed + attempt * 104729);
            System.Random random = new System.Random(attemptSeed);
            int additionalCount = random.Next(settings.MinimumAdditionalNodes, settings.MaximumAdditionalNodes + 1);
            int totalNodeCount = settings.BossDistance + 1 + additionalCount;
            if ((long)settings.MinimumLeafNodes + settings.MinimumTwoConnectionNodes > totalNodeCount)
                continue;

            List<Vector2Int> coordinates = new List<Vector2Int>();
            List<List<int>> connections = new List<List<int>>();
            List<int> mainRoute = new List<int>();
            CreateMainRoute(settings, random, coordinates, connections, mainRoute);
            int bossNodeId = mainRoute[mainRoute.Count - 1];
            if (!TryCreateBranches(settings, random, additionalCount, coordinates, connections, bossNodeId))
                continue;

            AddExtraConnections(settings, random, coordinates, connections, bossNodeId);
            if (!TryLimitConnectionCounts(settings, random, coordinates, connections, bossNodeId))
                continue;

            return CreateResult(settings, attemptSeed, random, coordinates, connections, mainRoute, bossNodeId);
        }

        throw new InvalidOperationException(
            $"{settings.MaximumAttempts}회 내 그래프 생성 실패 " +
            $"(시드={seed}, 보스 거리={settings.BossDistance}, " +
            $"추가 노드={settings.MinimumAdditionalNodes}~{settings.MaximumAdditionalNodes}, " +
            $"연결 1개={settings.MinimumLeafNodes}~{settings.MaximumLeafNodes}, " +
            $"연결 2개={settings.MinimumTwoConnectionNodes}~{settings.MaximumTwoConnectionNodes})");
    }
}
