using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// MapTest 전용 비대칭 평면 공터와 미리보기 생성
/// </summary>
public static class MapTestSceneBuilder
{
    const string ScenePath = "Assets/_Project/01.Scene/TestScenes/MapTest.unity";
    const string OutputRoot = "Assets/_Project/03.Prefab/Environment/MapTest";
    const string PreviewRoot = "Assets/_Project/01.Scene/TestScenes/MapTest";
    const string TextureRoot = "Assets/_ThirdParty/Polytope Studio/Lowpoly_Environments/Sources/Textures";
    const string MapName = "ForestRing";
    const string CameraPrefabPath = "Assets/_Project/03.Prefab/Camera/RunCameras.prefab";
    const float Width = 72.2f;
    const float Depth = 35.3f;
    const float ViewAspect = 16f / 9f;
    const float ClearingHalfWidth = 22f;
    const float ClearingHalfDepth = 7.5f;
    const int GroundLayer = 3;

    static readonly string[] PropNames =
    {
        "PT_Generic_Rock_01", "PT_Menhir_Rock_02", "PT_River_Rock_Pile_02",
        "PT_Pine_Tree_03_green", "PT_Fruit_Tree_01_dead", "PT_Generic_Shrub_01_green",
        "PT_Grass_02", "PT_Caesars_Mushroom_01"
    };

    // 승인된 카메라 기준 재생성 후 제거할 임시 실행 연결
    [InitializeOnLoadMethod]
    static void QueueApprovedCameraBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath ||
                SessionState.GetBool("MapTest.CameraFit.02", false)) return;
            SessionState.SetBool("MapTest.CameraFit.02", true);
            try { BuildForestClearing(); }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("MapTest camera build stopped", exception.Message, "확인");
            }
        };
    }

    static T RequireAsset<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new InvalidOperationException("필수 에셋 부재: " + path);
        return asset;
    }

    static Transform Group(string name, Transform parent)
    {
        GameObject instance = new GameObject(name);
        instance.transform.SetParent(parent, false);
        return instance.transform;
    }

    static Vector2[] CreateOutline()
    {
        Vector2[] knots =
        {
            new Vector2(-32f, -15f), new Vector2(-22f, -17f), new Vector2(-8f, -17f),
            new Vector2(10f, -17f), new Vector2(29f, -15f), new Vector2(35f, -10f),
            new Vector2(36f, 0f), new Vector2(34f, 11f), new Vector2(25f, 16f),
            new Vector2(9f, 17.5f), new Vector2(-10f, 17f), new Vector2(-28f, 15f),
            new Vector2(-35f, 9f), new Vector2(-36f, 0f), new Vector2(-35f, -8f),
            new Vector2(-34f, -12f)
        };
        const int samplesPerSpan = 6;
        Vector2[] outline = new Vector2[knots.Length * samplesPerSpan];
        Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 maximum = new Vector2(float.MinValue, float.MinValue);
        for (int span = 0; span < knots.Length; span++)
        for (int sample = 0; sample < samplesPerSpan; sample++)
        {
            float t = sample / (float)samplesPerSpan;
            Vector2 a = knots[(span + knots.Length - 1) % knots.Length];
            Vector2 b = knots[span];
            Vector2 c = knots[(span + 1) % knots.Length];
            Vector2 d = knots[(span + 2) % knots.Length];
            Vector2 point = 0.5f * (2f * b + (-a + c) * t +
                (2f * a - 5f * b + 4f * c - d) * t * t +
                (-a + 3f * b - 3f * c + d) * t * t * t);
            outline[span * samplesPerSpan + sample] = point;
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        Vector2 center = (minimum + maximum) * 0.5f;
        Vector2 extent = maximum - minimum;
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 point = outline[i] - center;
            outline[i] = new Vector2(point.x * Width / extent.x, point.y * Depth / extent.y);
        }
        // 중심 팬 삼각형이 뒤집히지 않는 단일 외곽 검증
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[(i + 1) % outline.Length];
            if (a.x * b.y - a.y * b.x <= 0f)
                throw new InvalidOperationException("중심에서 보이지 않는 외곽 구간");
        }
        return outline;
    }

    static Mesh SaveMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uv, bool flatTop)
    {
        string path = OutputRoot + "/" + name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.name = name;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (flatTop)
        {
            Bounds bounds = mesh.bounds;
            if (Mathf.Abs(bounds.size.x - Width) > 0.01f || Mathf.Abs(bounds.size.z - Depth) > 0.01f)
                throw new InvalidOperationException("맵 외곽은 승인된 시야 1.5배 크기 필요");
            foreach (Vector3 vertex in vertices)
                if (vertex.y != 0f) throw new InvalidOperationException("바닥 정점의 높이는 모두 0 필요");
        }
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    static Material CreateSurfaceMaterial(string name, string textureName, Color color)
    {
        string path = OutputRoot + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(RequireAsset<Material>(OutputRoot + "/Materials/Rock.mat"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.parent = null;
        material.name = name;
        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        material.shaderKeywords = Array.Empty<string>();
        material.SetTexture("_BaseMap", RequireAsset<Texture2D>(TextureRoot + "/" + textureName + ".png"));
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.05f);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    static void CreateGround(Transform root, Vector2[] outline)
    {
        int count = outline.Length;
        Vector3[] vertices = new Vector3[count + 1];
        Vector2[] uv = new Vector2[count + 1];
        int[] triangles = new int[count * 3];
        for (int i = 0; i < count; i++)
        {
            vertices[i + 1] = new Vector3(outline[i].x, 0f, outline[i].y);
            uv[i + 1] = outline[i] / 14f;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = (i + 1) % count + 1;
            triangles[i * 3 + 2] = i + 1;
        }
        Mesh mesh = SaveMesh("ForestGroundMesh", vertices, triangles, uv, true);
        Transform ground = Group("FlatClearing_72.2x35.3", root);
        ground.gameObject.layer = GroundLayer;
        ground.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.gameObject.AddComponent<MeshRenderer>().sharedMaterial =
            CreateSurfaceMaterial("Ground", "PT_Ground_Generic_03", new Color(0.76f, 0.88f, 0.73f));
        ground.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static void CreateCliff(Transform root, Vector2[] outline)
    {
        int count = outline.Length;
        Vector3[] vertices = new Vector3[count * 4];
        Vector2[] uv = new Vector2[count * 4];
        int[] triangles = new int[count * 6];
        float accumulated = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[(i + 1) % count];
            float length = Vector2.Distance(a, b);
            float depthA = 5.5f + Mathf.Sin(i * 0.57f) * 0.75f;
            float depthB = 5.5f + Mathf.Sin(((i + 1) % count) * 0.57f) * 0.75f;
            int vertex = i * 4;
            vertices[vertex] = new Vector3(a.x, 0f, a.y);
            vertices[vertex + 1] = new Vector3(b.x, 0f, b.y);
            vertices[vertex + 2] = new Vector3(a.x * 0.96f, -depthA, a.y * 0.96f);
            vertices[vertex + 3] = new Vector3(b.x * 0.96f, -depthB, b.y * 0.96f);
            uv[vertex] = new Vector2(accumulated / 5f, 1f);
            uv[vertex + 1] = new Vector2((accumulated + length) / 5f, 1f);
            uv[vertex + 2] = new Vector2(accumulated / 5f, 0f);
            uv[vertex + 3] = new Vector2((accumulated + length) / 5f, 0f);
            int triangle = i * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = vertex + 2;
            accumulated += length;
        }
        Mesh mesh = SaveMesh("ForestCliffMesh", vertices, triangles, uv, false);
        Transform cliff = Group("Cliff_ExactOutline", root);
        cliff.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        cliff.gameObject.AddComponent<MeshRenderer>().sharedMaterial =
            CreateSurfaceMaterial("Cliff", "PT_Rocks_01", new Color(0.40f, 0.49f, 0.53f));
        cliff.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        Transform boundary = Group("Boundary_ExactOutline", root);
        for (int i = 0; i < count; i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[(i + 1) % count];
            Vector2 tangent = (b - a).normalized;
            Vector2 inward = new Vector2(-tangent.y, tangent.x);
            Vector2 center = (a + b) * 0.5f + inward * 0.16f;
            Transform wall = Group("Edge_" + i.ToString("00"), boundary);
            wall.localPosition = new Vector3(center.x, 2f, center.y);
            wall.localRotation = Quaternion.LookRotation(new Vector3(tangent.x, 0f, tangent.y));
            wall.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.32f, 5f, Vector2.Distance(a, b) + 0.08f);
        }
    }

    static bool Inside(Vector2 point, Vector2[] outline)
    {
        bool inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[j];
            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    static Bounds RendererBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException(instance.name + ": Renderer 부재");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static bool FitsBorder(Bounds bounds, Vector2[] outline)
    {
        Vector2 closest = new Vector2(Mathf.Clamp(0f, bounds.min.x, bounds.max.x), Mathf.Clamp(0f, bounds.min.z, bounds.max.z));
        float clearingDistance = closest.x * closest.x / (ClearingHalfWidth * ClearingHalfWidth) +
            closest.y * closest.y / (ClearingHalfDepth * ClearingHalfDepth);
        if (clearingDistance < 1f) return false;
        Vector2[] corners =
        {
            new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.max.x, bounds.min.z),
            new Vector2(bounds.max.x, bounds.max.z), new Vector2(bounds.min.x, bounds.max.z)
        };
        foreach (Vector2 corner in corners) if (!Inside(corner, outline)) return false;
        for (int i = 0; i < corners.Length; i++)
        for (int j = 0; j < outline.Length; j++)
            if (SegmentsCross(corners[i], corners[(i + 1) % corners.Length],
                outline[j], outline[(j + 1) % outline.Length])) return false;
        return true;
    }

    static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a;
        Vector2 cd = d - c;
        float first = (ab.x * (c.y - a.y) - ab.y * (c.x - a.x)) *
            (ab.x * (d.y - a.y) - ab.y * (d.x - a.x));
        float second = (cd.x * (a.y - c.y) - cd.y * (a.x - c.x)) *
            (cd.x * (b.y - c.y) - cd.y * (b.x - c.x));
        return first < -0.00001f && second < -0.00001f;
    }

    static void Place(GameObject prefab, Transform parent, Vector2 point, float size, float yaw,
        bool heightSizing, bool collision, bool trunkCollider, Vector2[] outline)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.localPosition = new Vector3(point.x, 0f, point.y);
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        instance.transform.localScale = Vector3.one;
        Bounds bounds = RendererBounds(instance);
        float extent = heightSizing ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
        if (extent <= 0.001f) throw new InvalidOperationException(prefab.name + ": 유효한 크기 필요");
        instance.transform.localScale = Vector3.one * (size / extent);
        bounds = RendererBounds(instance);
        instance.transform.position -= Vector3.up * bounds.min.y;
        bool fitted = false;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            bounds = RendererBounds(instance);
            if (FitsBorder(bounds, outline)) { fitted = true; break; }
            Vector3 position = instance.transform.localPosition;
            Vector2 inward = new Vector2(-position.x, -position.z).normalized * 0.4f;
            instance.transform.localPosition += new Vector3(inward.x, 0f, inward.y);
        }
        // 공터나 외곽을 침범하는 장식 후보 제외
        if (!fitted) { Object.DestroyImmediate(instance); return; }
        if (!collision) return;
        Transform colliderRoot = Group("Collision", null);
        colliderRoot.position = trunkCollider
            ? new Vector3(instance.transform.position.x, 1.5f, instance.transform.position.z) : bounds.center;
        colliderRoot.SetParent(instance.transform, true);
        if (trunkCollider)
        {
            CapsuleCollider collider = colliderRoot.gameObject.AddComponent<CapsuleCollider>();
            collider.radius = 0.38f;
            collider.height = 3f;
        }
        else
        {
            colliderRoot.gameObject.AddComponent<BoxCollider>().size =
                new Vector3(bounds.size.x * 0.8f, bounds.size.y * 0.9f, bounds.size.z * 0.8f);
        }
    }

    static void CreateBorderDecorations(Transform root, Vector2[] outline, Dictionary<string, GameObject> props)
    {
        Transform trees = Group("Border_TreeGroups", root);
        Transform rocks = Group("Border_RockGroups", root);
        Transform plants = Group("Border_Undergrowth", root);
        System.Random random = new System.Random(2030);
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 edge = outline[i];
            bool foreground = edge.y < -9f;
            float yaw = (float)random.NextDouble() * 360f;
            // 군집 사이 여백과 전경의 낮은 실루엣
            bool gap = i % 23 >= 21;
            if (!gap && i % 2 == 0)
            {
                bool dead = i % 15 == 0 && !foreground;
                Place(props[dead ? "PT_Fruit_Tree_01_dead" : "PT_Pine_Tree_03_green"], trees,
                    edge * 0.88f, foreground ? 3.4f : dead ? 5.5f : 7.2f + (float)random.NextDouble() * 2.5f,
                    yaw, true, true, true, outline);
                Place(props["PT_Pine_Tree_03_green"], trees,
                        outline[(i + 1) % outline.Length] * 0.77f, foreground ? 3.6f : 5.5f + (float)random.NextDouble() * 1.5f,
                        yaw + 49f, true, true, true, outline);
            }
            if (!gap && i % 2 == 0)
            {
                Place(props["PT_Generic_Shrub_01_green"], plants, edge * (0.88f + (float)random.NextDouble() * 0.04f),
                    foreground ? 1.4f : 1.9f, yaw, true, false, false, outline);
                Place(props["PT_Generic_Shrub_01_green"], plants,
                    outline[(i + 1) % outline.Length] * 0.82f, foreground ? 0.8f : 1.2f,
                    yaw + 72f, true, false, false, outline);
            }
            if (i % 7 == 1)
                Place(props[i % 2 == 0 ? "PT_River_Rock_Pile_02" : "PT_Generic_Rock_01"], rocks,
                    edge * 0.90f, foreground ? 2.3f : 3.3f, yaw, false, true, false, outline);
        }
        for (int i = 0; i < 560; i++)
        {
            Vector2 edge = outline[random.Next(outline.Length)];
            Vector2 position = edge * (0.76f + (float)random.NextDouble() * 0.18f);
            position += new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
            bool mushroom = i % 17 == 0;
            Place(props[mushroom ? "PT_Caesars_Mushroom_01" : "PT_Grass_02"], plants, position,
                mushroom ? 0.30f : 0.28f + (float)random.NextDouble() * 0.25f,
                i * 47f, true, false, false, outline);
        }
        // 실제 추적 시야에 들어오는 낮은 안쪽 군집과 불규칙한 여백
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2f / 64f;
            bool foreground = Mathf.Sin(angle) < 0f;
            if (i % 13 >= 10 || (foreground && i % 2 == 1)) continue;
            Vector2 point = new Vector2(
                Mathf.Cos(angle) * (ClearingHalfWidth + 2.2f + (float)random.NextDouble() * 1.8f),
                Mathf.Sin(angle) * (ClearingHalfDepth + 1.8f + (float)random.NextDouble()));
            float yaw = (float)random.NextDouble() * 360f;
            Place(props["PT_Generic_Shrub_01_green"], plants, point,
                foreground ? 0.9f : 1.4f + (float)random.NextDouble() * 0.4f,
                yaw, true, false, false, outline);
            if (!foreground && i % 4 == 0)
                Place(props["PT_Pine_Tree_03_green"], trees, point + point.normalized * 0.7f,
                    3.2f, yaw + 39f, true, true, true, outline);
            Place(props["PT_Grass_02"], plants, point + new Vector2(0.7f, 0.4f),
                0.4f, yaw + 70f, true, false, false, outline);
        }
        Transform markers = Group("Markers_IntegrationPoints", root);
        Group("PlayerEntry", markers).localPosition = new Vector3(-8f, 0.1f, -6f);
        Group("PortalSpawnPoint", markers).localPosition = new Vector3(12f, 0.1f, 6f);
        Group("CombatArea_Clearing", markers).localPosition = new Vector3(0f, 0.1f, 0f);
    }

    static Vector2[] GroundFootprint(Quaternion rotation, float fieldOfView, float height)
    {
        float tangent = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f);
        Vector2[] viewport = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        Vector2[] result = new Vector2[4];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 ray = rotation * new Vector3(viewport[i].x * tangent * ViewAspect, viewport[i].y * tangent, 1f);
            if (ray.y >= -0.001f) throw new InvalidOperationException("모든 화면 모서리가 평면 바닥을 향해야 함");
            float distance = -height / ray.y;
            result[i] = new Vector2(ray.x * distance, ray.z * distance);
        }
        return result;
    }

    static List<Vector2> ClipRegion(List<Vector2> polygon, Vector2 normal, float minimum)
    {
        List<Vector2> result = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float da = Vector2.Dot(normal, a) - minimum;
            float db = Vector2.Dot(normal, b) - minimum;
            if (da >= 0f) result.Add(a);
            if ((da >= 0f) != (db >= 0f)) result.Add(Vector2.LerpUnclamped(a, b, da / (da - db)));
        }
        return result;
    }

    static Vector2[] CalculateCameraRegion(Vector2[] outline, Quaternion rotation, float fov, float height)
    {
        List<Vector2> region = new List<Vector2>
        {
            new Vector2(-200f, -200f), new Vector2(200f, -200f),
            new Vector2(200f, 200f), new Vector2(-200f, 200f)
        };
        Vector2[] low = GroundFootprint(rotation, fov, height - 0.1f);
        Vector2[] high = GroundFootprint(rotation, fov, height + 0.1f);
        // 오목한 외곽의 내부 반평면과 전체 화면 투영을 동시에 만족하는 보수적 허용 영역
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i];
            Vector2 edge = (outline[(i + 1) % outline.Length] - a).normalized;
            Vector2 inward = new Vector2(-edge.y, edge.x);
            float smallest = float.MaxValue;
            for (int corner = 0; corner < 4; corner++)
            {
                smallest = Mathf.Min(smallest, Vector2.Dot(inward, low[corner]));
                smallest = Mathf.Min(smallest, Vector2.Dot(inward, high[corner]));
            }
            region = ClipRegion(region, inward, Vector2.Dot(inward, a) - smallest + 0.6f);
            if (region.Count < 3) throw new InvalidOperationException("현재 화면비와 렌즈가 들어갈 카메라 허용 영역 부재");
        }
        return region.ToArray();
    }

    static MeshCollider CreateCameraVolume(Transform rig, Vector2[] region, float height)
    {
        int count = region.Length;
        Vector3[] vertices = new Vector3[count * 2 + 2];
        Vector2[] uv = new Vector2[vertices.Length];
        List<int> triangles = new List<int>();
        Vector2 center = Vector2.zero;
        foreach (Vector2 point in region) center += point / count;
        vertices[count * 2] = new Vector3(center.x, height - 0.1f, center.y);
        vertices[count * 2 + 1] = new Vector3(center.x, height + 0.1f, center.y);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            vertices[i] = new Vector3(region[i].x, height - 0.1f, region[i].y);
            vertices[i + count] = new Vector3(region[i].x, height + 0.1f, region[i].y);
            triangles.AddRange(new int[]
            {
                count * 2, i, next,
                count * 2 + 1, next + count, i + count,
                i + count, next + count, i,
                next + count, next, i
            });
        }
        Mesh mesh = SaveMesh("ForestCameraBounds", vertices, triangles.ToArray(), uv, false);
        Transform bounds = Group("CameraBounds_16x9", rig);
        bounds.gameObject.layer = 2;
        MeshCollider collider = bounds.gameObject.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = mesh;
        collider.isTrigger = true;
        return collider;
    }

    static void CreatePlayableBoundary(Transform map, Vector2[] outline)
    {
        Transform root = Group("PlayableBoundary_ForestEdge", map);
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = Vector2.Scale(outline[i], new Vector2(0.70f, 0.60f));
            Vector2 b = Vector2.Scale(outline[(i + 1) % outline.Length], new Vector2(0.70f, 0.60f));
            Vector2 tangent = (b - a).normalized;
            Transform wall = Group("ForestEdge_" + i.ToString("00"), root);
            Vector2 center = (a + b) * 0.5f;
            wall.localPosition = new Vector3(center.x, 2f, center.y);
            wall.localRotation = Quaternion.LookRotation(new Vector3(tangent.x, 0f, tangent.y));
            wall.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.3f, 5f, Vector2.Distance(a, b) + 0.08f);
        }
    }

    static void RecordOverride(Object target)
    {
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static Camera ConfigureRunCameras(Scene scene, Vector2[] region, Quaternion rotation, Vector3 offset,
        out CinemachineBrain brain, out CinemachineCamera follow, out Transform playerRoot)
    {
        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(RequireAsset<GameObject>(CameraPrefabPath), scene);
        rig.name = "RunCameras";
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        playerRoot = Group("playerRoot", null);
        playerRoot.position = Vector3.zero;
        Transform cameraTransform = rig.transform.Find("Main Camera");
        Transform followTransform = rig.transform.Find("PlayerFollowCamera");
        Transform miniFollowTransform = rig.transform.Find("MinimapFollowCamera");
        Transform miniTransform = rig.transform.Find("MinimapCamera");
        if (cameraTransform == null || followTransform == null || miniFollowTransform == null || miniTransform == null)
            throw new InvalidOperationException("RunCameras의 지정된 하위 카메라 구성 필요");
        Camera camera = cameraTransform.GetComponent<Camera>();
        brain = cameraTransform.GetComponent<CinemachineBrain>();
        follow = followTransform.GetComponent<CinemachineCamera>();
        CinemachineCamera miniFollow = miniFollowTransform.GetComponent<CinemachineCamera>();
        if (camera == null || brain == null || follow == null || miniFollow == null)
            throw new InvalidOperationException("RunCameras의 출력 및 추적 컴포넌트 연결 필요");
        camera.orthographic = false;
        camera.fieldOfView = follow.Lens.FieldOfView;
        camera.aspect = ViewAspect;
        camera.transform.SetPositionAndRotation(playerRoot.position + offset, rotation);
        camera.allowHDR = true;
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.volumeLayerMask = 1;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        follow.Follow = playerRoot;
        follow.transform.rotation = rotation;
        miniFollow.Follow = playerRoot;
        SerializedObject miniSettings = new SerializedObject(miniFollow);
        SerializedProperty channel = miniSettings.FindProperty("OutputChannel");
        if (channel == null) throw new InvalidOperationException("미니맵 출력 채널 설정 부재");
        channel.intValue = 8;
        miniSettings.ApplyModifiedPropertiesWithoutUndo();
        // UI가 없는 MapTest에서는 미니맵 출력만 비활성화
        miniTransform.gameObject.SetActive(false);
        RecordOverride(miniTransform.gameObject);
        MeshCollider volume = CreateCameraVolume(rig.transform, region, offset.y);
        CinemachineConfiner3D confiner = follow.gameObject.AddComponent<CinemachineConfiner3D>();
        confiner.BoundingVolume = volume;
        confiner.SlowingDistance = 0f;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        RecordOverride(camera);
        RecordOverride(camera.transform);
        RecordOverride(cameraData);
        RecordOverride(brain);
        RecordOverride(follow);
        RecordOverride(follow.transform);
        RecordOverride(miniFollow);
        Physics.SyncTransforms();
        return camera;
    }

    static void ValidateCameraFootprint(Camera camera, Vector2[] outline)
    {
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        Vector2[] viewport = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        Vector2[] footprint = new Vector2[4];
        for (int i = 0; i < viewport.Length; i++)
        {
            Ray ray = camera.ViewportPointToRay(viewport[i]);
            if (!ground.Raycast(ray, out float distance)) throw new InvalidOperationException("시야가 지면과 만나지 않음");
            Vector3 hit = ray.GetPoint(distance);
            footprint[i] = new Vector2(hit.x, hit.z);
            if (!Inside(footprint[i], outline)) throw new InvalidOperationException("카메라 화면 모서리의 외곽 이탈: " + hit);
        }
        for (int i = 0; i < 4; i++)
        for (int j = 0; j < outline.Length; j++)
            if (SegmentsCross(footprint[i], footprint[(i + 1) % 4], outline[j], outline[(j + 1) % outline.Length]))
                throw new InvalidOperationException("화면 변이 오목한 맵 외곽을 가로지름");
    }

    static Texture2D RenderFrame(Camera source, bool overview, int width, int height)
    {
        GameObject cameraObject = new GameObject("MapTestPreviewCamera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(source);
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        camera.aspect = width / (float)height;
        if (overview)
        {
            camera.transform.SetPositionAndRotation(new Vector3(0f, 80f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 24f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.055f, 0.075f);
        }
        UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing = true;
        additional.volumeLayerMask = 1;
        additional.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        bool completed = false;
        try
        {
            UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest();
            request.destination = target;
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("현재 URP의 단일 카메라 미리보기 지원 필요");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            completed = true;
            return image;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(cameraObject);
            if (!completed && image != null) Object.DestroyImmediate(image);
        }
    }

    static void SaveImage(Texture2D image, string name)
    {
        string outputPath = PreviewRoot + "/" + name + ".png";
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputPath));
        File.WriteAllBytes(path, image.EncodeToPNG());
        AssetDatabase.ImportAsset(outputPath);
    }

    static void CaptureViews(Camera camera, CinemachineBrain brain, CinemachineCamera follow,
        Transform playerRoot, Vector2[] outline, Quaternion rotation, Vector3 offset)
    {
        Vector3[] positions =
        {
            Vector3.zero, new Vector3(-23f, 0f, 0f), new Vector3(23f, 0f, 0f),
            new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, 10f)
        };
        CinemachineBrain.UpdateMethods savedMode = brain.UpdateMethod;
        Texture2D contact = new Texture2D(1920, 720, TextureFormat.RGBA32, false);
        int frame = Time.frameCount;
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        try
        {
            for (int i = 0; i < positions.Length; i++)
            {
                playerRoot.position = positions[i];
                follow.PreviousStateIsValid = false;
                follow.ForceCameraPosition(positions[i] + offset, rotation);
                brain.ResetState();
                brain.ManualUpdate(++frame, -1f);
                camera.aspect = ViewAspect;
                if (brain.ActiveVirtualCamera != follow)
                    throw new InvalidOperationException("Main Camera의 플레이어 추적 카메라 선택 실패");
                ValidateCameraFootprint(camera, outline);
                Texture2D tile = RenderFrame(camera, false, 640, 360);
                try { contact.SetPixels((i % 3) * 640, (1 - i / 3) * 360, 640, 360, tile.GetPixels()); }
                finally { Object.DestroyImmediate(tile); }
                if (i == 0)
                {
                    Texture2D main = RenderFrame(camera, false, 1536, 864);
                    try { SaveImage(main, "MapTestPreview"); }
                    finally { Object.DestroyImmediate(main); }
                }
            }
            Texture2D top = RenderFrame(camera, true, 1536, 864);
            try { SaveImage(top, "MapTestTopDown"); }
            finally { Object.DestroyImmediate(top); }
            Texture2D topTile = RenderFrame(camera, true, 640, 360);
            try { contact.SetPixels(1280, 0, 640, 360, topTile.GetPixels()); }
            finally { Object.DestroyImmediate(topTile); }
            contact.Apply();
            SaveImage(contact, "MapTestCameraCoverage");
        }
        finally
        {
            Object.DestroyImmediate(contact);
            try
            {
                playerRoot.position = Vector3.zero;
                follow.PreviousStateIsValid = false;
                follow.ForceCameraPosition(offset, rotation);
                brain.ResetState();
                brain.ManualUpdate(++frame, -1f);
            }
            finally
            {
                brain.UpdateMethod = savedMode;
                RecordOverride(brain);
                RecordOverride(camera);
                RecordOverride(camera.transform);
                RecordOverride(follow);
                RecordOverride(follow.transform);
            }
        }
    }
    [MenuItem("Tools/MapTest/Build Forest Clearing")]
    static void BuildForestClearing()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("편집 모드의 MapTest 씬에서만 실행 가능");
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline) || !pipeline.supportsHDR)
            throw new InvalidOperationException("HDR이 활성화된 URP 설정 필요");
        RequireAsset<Material>(OutputRoot + "/Materials/Rock.mat");
        RequireAsset<Texture2D>(TextureRoot + "/PT_Ground_Generic_03.png");
        RequireAsset<Texture2D>(TextureRoot + "/PT_Rocks_01.png");
        GameObject cameraPrefab = RequireAsset<GameObject>(CameraPrefabPath);
        Transform sourceFollow = cameraPrefab.transform.Find("PlayerFollowCamera");
        if (sourceFollow == null) throw new InvalidOperationException("RunCameras의 PlayerFollowCamera 연결 필요");
        CinemachineCamera sourceCamera = sourceFollow.GetComponent<CinemachineCamera>();
        CinemachineFollow sourceBody = sourceFollow.GetComponent<CinemachineFollow>();
        if (sourceCamera == null || sourceBody == null) throw new InvalidOperationException("RunCameras의 추적 설정 필요");
        Quaternion rotation = sourceFollow.localRotation;
        Vector3 offset = sourceBody.FollowOffset;
        Vector2[] footprint = GroundFootprint(rotation, sourceCamera.Lens.FieldOfView, offset.y);
        Bounds viewBounds = new Bounds(new Vector3(footprint[0].x, 0f, footprint[0].y), Vector3.zero);
        foreach (Vector2 point in footprint) viewBounds.Encapsulate(new Vector3(point.x, 0f, point.y));
        if (Mathf.Abs(Width - viewBounds.size.x * 1.5f) > 0.1f || Mathf.Abs(Depth - viewBounds.size.z * 1.5f) > 0.1f)
            throw new InvalidOperationException("RunCameras 시야가 변경됨: 맵 크기 1.5배 재계산 필요");
        Dictionary<string, GameObject> props = new Dictionary<string, GameObject>();
        foreach (string name in PropNames)
            props.Add(name, RequireAsset<GameObject>(OutputRoot + "/Props/" + name + ".prefab"));
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name != "Main Camera" && root.name != "Directional Light" && root.name != "RunCameras" &&
                root.name != "playerRoot" && root.name != MapName && root.name != "MapTestAtmosphere")
                throw new InvalidOperationException("사용자 오브젝트 보존을 위한 생성 중단: " + root.name);
        Vector2[] outline = CreateOutline();
        Vector2[] cameraRegion = CalculateCameraRegion(outline, rotation, sourceCamera.Lens.FieldOfView, offset.y);
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == MapName || root.name == "Main Camera" || root.name == "RunCameras" || root.name == "playerRoot")
                Object.DestroyImmediate(root);
        Transform map = Group(MapName, null);
        CreateGround(map, outline);
        CreateCliff(map, outline);
        CreateBorderDecorations(map, outline, props);
        CreatePlayableBoundary(map, outline);
        Camera camera = ConfigureRunCameras(scene, cameraRegion, rotation, offset,
            out CinemachineBrain brain, out CinemachineCamera follow, out Transform playerRoot);
        PrefabUtility.SaveAsPrefabAssetAndConnect(map.gameObject, OutputRoot + "/ForestRing.prefab", InteractionMode.AutomatedAction);
        CaptureViews(camera, brain, follow, playerRoot, outline, rotation, offset);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("MapTest 저장 실패");
        Selection.activeGameObject = playerRoot.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(Vector3.zero, Quaternion.Euler(56.31f, 0f, 0f), 40f, true);
    }
}

