using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshSurface의 구워진 NavMesh를 위에서 본 이동 가능 비중 격자로 변환
/// </summary>
public static class NavMeshMinimapRasterizer
{
    const int SuperSample = 4;
    const float AgentSampleRadius = 0.5f;
    const float AgentMatchDistance = 0.05f;
    const float BoundsMargin = 1f;

    static float Edge(Vector2 a, Vector2 b, Vector2 point) => (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

    static Vector2 ToPixel(Vector3 world, Rect worldRect, Vector2 scale)
    {
        return new Vector2((world.x - worldRect.x) * scale.x, (world.z - worldRect.y) * scale.y);
    }

    static Matrix4x4 GetSurfaceToWorld(NavMeshSurface surface)
    {
        // 패키지가 NavMesh를 배치하는 기준과 같은 현재 Transform 위치·회전, 스케일 제외
        // NavMeshData.position은 Bake 방식에 따라 원점일 수 있어 사용하지 않음
        return Matrix4x4.TRS(surface.transform.position, surface.transform.rotation, Vector3.one);
    }

    static bool BelongsToSurface(Vector3 centroid, Bounds surfaceBounds, Matrix4x4 worldToSurface,
        NavMeshQueryFilter filter)
    {
        // Surface Bake 범위 밖 삼각형 제외, 다른 씬 NavMesh 혼입 방지용 높이 포함
        if (!surfaceBounds.Contains(worldToSurface.MultiplyPoint3x4(centroid))) return false;

        // 다른 Agent Type 전용 삼각형 제외
        if (!NavMesh.SamplePosition(centroid, out NavMeshHit hit, AgentSampleRadius, filter)) return false;
        Vector2 offset = new Vector2(hit.position.x - centroid.x, hit.position.z - centroid.z);
        return offset.sqrMagnitude <= AgentMatchDistance * AgentMatchDistance;
    }

    static void RasterizeTriangle(float[] coverage, int width, int height, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Edge(a, b, c);
        if (Mathf.Approximately(area, 0f)) return;

        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
        int maxX = Mathf.Min(width - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
        int maxY = Mathf.Min(height - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        float sampleWeight = 1f / (SuperSample * SuperSample);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                // 픽셀당 SuperSample² 지점 포함 비율로 경계 안티에일리어싱
                int hits = 0;
                for (int sampleY = 0; sampleY < SuperSample; sampleY++)
                {
                    for (int sampleX = 0; sampleX < SuperSample; sampleX++)
                    {
                        Vector2 point = new Vector2(x + (sampleX + 0.5f) / SuperSample, y + (sampleY + 0.5f) / SuperSample);
                        float w0 = Edge(b, c, point);
                        float w1 = Edge(c, a, point);
                        float w2 = Edge(a, b, point);

                        // 삼각형 감기 방향과 무관한 내부 판정
                        bool inside = area > 0f
                            ? w0 >= 0f && w1 >= 0f && w2 >= 0f
                            : w0 <= 0f && w1 <= 0f && w2 <= 0f;
                        if (inside) hits++;
                    }
                }

                // 겹치는 삼각형(다층 구간) 합산 후 1로 제한
                int index = y * width + x;
                coverage[index] = Mathf.Min(1f, coverage[index] + hits * sampleWeight);
            }
        }
    }

    /// <summary>
    /// 미니맵 추출 가능 Surface 여부 검사
    /// </summary>
    /// <param name="surface">검사 대상 Surface</param>
    /// <param name="error">실행 불가 사유</param>
    /// <returns>Bake 데이터가 있고 씬에 로드된 Surface 여부</returns>
    public static bool TryValidateSurface(NavMeshSurface surface, out string error)
    {
        if (surface == null)
        {
            error = "NavMeshSurface를 지정하세요.";
            return false;
        }

        if (surface.navMeshData == null)
        {
            error = $"{surface.name}: Bake된 NavMesh가 없습니다. 먼저 Bake하세요.";
            return false;
        }

        // 비활성 Surface는 NavMesh 데이터가 로드되지 않아 삼각형 조회 불가
        if (!surface.isActiveAndEnabled)
        {
            error = $"{surface.name}: 활성화된 NavMeshSurface만 내보낼 수 있습니다.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Surface Bake 범위의 월드 XZ 사각형 계산
    /// </summary>
    /// <param name="surface">검사를 통과한 Surface</param>
    /// <returns>월드 범위 (x, y = 월드 X, Z)</returns>
    public static Rect GetWorldRect(NavMeshSurface surface)
    {
        Matrix4x4 surfaceToWorld = GetSurfaceToWorld(surface);
        Bounds bounds = surface.navMeshData.sourceBounds;

        float minX = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxZ = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            // sourceBounds는 Surface 로컬 기준, 8개 꼭짓점 월드 변환 후 XZ 범위
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                (i & 4) == 0 ? bounds.min.z : bounds.max.z);
            Vector3 world = surfaceToWorld.MultiplyPoint3x4(corner);
            minX = Mathf.Min(minX, world.x);
            minZ = Mathf.Min(minZ, world.z);
            maxX = Mathf.Max(maxX, world.x);
            maxZ = Mathf.Max(maxZ, world.z);
        }

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    /// <summary>
    /// Surface Agent Type 기준 이동 가능 비중 격자 생성
    /// </summary>
    /// <param name="surface">검사를 통과한 Surface</param>
    /// <param name="worldRect">격자가 덮는 월드 범위</param>
    /// <param name="width">가로 픽셀 수</param>
    /// <param name="height">세로 픽셀 수</param>
    /// <returns>픽셀별 0~1 비중, 첫 행이 월드 -Z 쪽</returns>
    public static float[] Rasterize(NavMeshSurface surface, Rect worldRect, int width, int height)
    {
        NavMeshData data = surface.navMeshData;
        Matrix4x4 worldToSurface = GetSurfaceToWorld(surface).inverse;
        Bounds surfaceBounds = data.sourceBounds;
        surfaceBounds.Expand(BoundsMargin);
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = surface.agentTypeID,
            areaMask = NavMesh.AllAreas
        };
        Vector2 scale = new Vector2(width / worldRect.width, height / worldRect.height);
        float[] coverage = new float[width * height];

        // Prefab Mode Surface는 패키지가 NavMesh에 로드하지 않아 추출 중에만 임시 로드
        NavMeshDataInstance temporary = EditorSceneManager.IsPreviewSceneObject(surface)
            ? NavMesh.AddNavMeshData(data, surface.transform.position, surface.transform.rotation)
            : new NavMeshDataInstance();
        try
        {
            // 열린 씬에 로드된 모든 NavMesh 삼각형, Agent Type 구분 없음
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Vector3[] vertices = triangulation.vertices;
            int[] indices = triangulation.indices;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];
                if (!BelongsToSurface((a + b + c) / 3f, surfaceBounds, worldToSurface, filter)) continue;

                RasterizeTriangle(coverage, width, height, ToPixel(a, worldRect, scale), ToPixel(b, worldRect, scale),
                    ToPixel(c, worldRect, scale));
            }
        }
        finally
        {
            if (temporary.valid) temporary.Remove();
        }

        return coverage;
    }
}
