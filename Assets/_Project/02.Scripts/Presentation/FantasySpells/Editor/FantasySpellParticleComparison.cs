using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 완성 스펠별 원본 보존, 자동 재생 비교 및 검증 통과 결과 저장
/// </summary>
internal static class FantasySpellParticleComparison
{
    internal const string ReviewDirectory = "Temp/FantasySpellAuthoringReview";
    internal const string PrefabDirectory = "Assets/_Project/03.Prefab/VFX/FantasySpellsEffectsPack/Optimized";
    const string AllDirectory = ReviewDirectory + "/All";
    const float Step = 1f / 60f;
    const int Width = 512;
    const int Height = 384;
    const int GridWidth = 64;
    const int GridHeight = 48;
    static IEnumerator<object> _routine;
    static Request _request;
    static double _nextStatus;
    static string _progress;
    static readonly List<Result> Results = new List<Result>();

    [Serializable]
    sealed class Request
    {
        public string Id;
        public string Action;
    }

    [Serializable]
    sealed class Status
    {
        public string Id;
        public string Action;
        public string State;
        public string Message;
    }

    [Serializable]
    sealed class Manifest
    {
        public int TargetCount;
        public Entry[] Effects;
    }

    [Serializable]
    sealed class Entry
    {
        public string Name;
        public string Path;
        public string Guid;
        public string SourceHash;
        public long RootGameObject;
        public long RootTransform;
        public long RootParticleSystem;
        public string[] Blockers;
    }

    [Serializable]
    sealed class Point
    {
        public string Key;
        public string Role;
        public Vector3 Position;
        public Vector3 Size;
        public Vector3 Rotation;
        public Color32 Color;
        public float Age;
        public float Remaining;
        public float Birth;
    }

    [Serializable]
    sealed class Frame
    {
        public float Time;
        public int Particles;
        public int VisibleParticles;
        public int VisibleRenderers;
        public Point[] Points;
        public string Image;
        public float Energy;
        [NonSerialized] internal float[] Pixels;
    }

    [Serializable]
    sealed class FrameComparison
    {
        public float Time;
        public int BeforeParticles;
        public int AfterParticles;
        public int BeforeMinimum;
        public int BeforeMaximum;
        public float CountDifference;
        public float ImageDifference;
        public float ImageOverlap;
        public float EnergyRatio;
        public float ReferenceTime;
        public float BeforeEnergy;
        public float AfterEnergy;
        public int BoundaryPoints;
        public int UnmatchedPoints;
        public string[] PointIssues;
        public string BeforeImage;
        public string AfterImage;
        public bool Passed;
    }

    [Serializable]
    sealed class Attempt
    {
        public string Mode;
        public int Systems;
        public int Objects;
        public int MergedGroups;
        public float MaximumTimingMilliseconds;
        public string[] Changes;
        public string[] KeptGroups;
        public FrameComparison[] Frames;
        public bool Passed;
        public string Error;
    }

    [Serializable]
    sealed class Result
    {
        public string Name;
        public string Path;
        public string State;
        public string Reason;
        public string Mode;
        public string SourceHash;
        public string FinalHash;
        public int BeforeSystems;
        public int AfterSystems;
        public int BeforeObjects;
        public int AfterObjects;
        public bool PlaybackVerified;
        public bool Persistent;
        public bool GuidAndRootReferencesVerified;
        public float Horizon;
        public float[] SampleTimes;
        public string[] Changes;
        public string[] KeptGroups;
        public Attempt[] Attempts;
        public string[] BaselineImages;
        public string FinishedUtc;
    }

    [Serializable]
    sealed class Report
    {
        public int Targets;
        public int Completed;
        public int Kept;
        public int Incomplete;
        public int BeforeSystems;
        public int AfterSystems;
        public int BeforeObjects;
        public int AfterObjects;
        public Result[] Results;
    }

    sealed class View : IDisposable
    {
        internal readonly PreviewRenderUtility Preview = new PreviewRenderUtility();
        internal readonly GameObject Root;
        internal readonly ParticleSystem Clock;
        internal readonly BakedSpellPlayer Player;
        internal readonly ParticleSystem[] Systems;
        internal readonly Renderer[] Renderers;
        internal readonly HashSet<string> PrimaryRoles;
        internal readonly Dictionary<string, float> Births = new Dictionary<string, float>();
        readonly ParticleSystem[] _immortals;
        readonly Dictionary<int, ParticleSystem.Particle[]> _buffers = new Dictionary<int, ParticleSystem.Particle[]>();
        internal int CurrentFrame;

        internal View(GameObject source, HashSet<string> primaryRoles)
        {
            Root = Object.Instantiate(source);
            Root.hideFlags = HideFlags.HideAndDontSave;
            Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Preview.AddSingleGO(Root);
            foreach (AudioSource audio in Root.GetComponentsInChildren<AudioSource>(true)) { audio.mute = true; audio.Stop(); }
            Root.SetActive(true);
            Clock = Root.GetComponent<ParticleSystem>();
            if (Clock == null) throw new InvalidOperationException("루트 Particle System 누락");
            Player = Root.GetComponent<BakedSpellPlayer>();
            if (Player != null)
            {
                using (SerializedObject serialized = new SerializedObject(Player))
                {
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Component component
                            && component.transform != Root.transform && !component.transform.IsChildOf(Root.transform))
                            throw new InvalidOperationException("재생기 바인딩이 효과 외부를 참조");
                }
            }
            Systems = Root.GetComponentsInChildren<ParticleSystem>(true);
            Renderers = Root.GetComponentsInChildren<Renderer>(true);
            PrimaryRoles = primaryRoles ?? new HashSet<string>(Systems
                .Where(particle => !particle.shape.enabled && particle.GetComponent<ParticleSystemRenderer>().enabled)
                .Select(particle => FantasySpellParticleConsolidator.Role(particle.name)));
            _immortals = Systems.Where(particle => PrimaryRoles.Contains(FantasySpellParticleConsolidator.Role(particle.name))
                && float.IsPositiveInfinity(Maximum(particle.main.startLifetime))).ToArray();
            foreach (ParticleSystem particle in Systems)
            {
                // 비교용 복제본에 한정한 자동 난수 고정
                if (!particle.useAutoRandomSeed) continue;
                particle.useAutoRandomSeed = false;
                particle.randomSeed = StableSeed(FantasySpellParticleConsolidator.Role(particle.name));
            }
            Preview.camera.clearFlags = CameraClearFlags.SolidColor;
            Preview.camera.backgroundColor = new Color(0.045f, 0.05f, 0.065f);
            Preview.camera.orthographic = true;
            Preview.ambientColor = Color.gray;
            Preview.lights[0].intensity = 1.2f;
            Preview.lights[0].transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            Preview.lights[1].intensity = 0.8f;
            Reset();
        }

        internal void Reset()
        {
            Clock.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (Player != null) Player.Restart(true);
            else Clock.Simulate(0f, true, true, false);
            CurrentFrame = 0;
            Births.Clear();
            RecordBirths();
        }

        internal void Advance()
        {
            CurrentFrame++;
            if (Player != null) Player.PreviewAt(CurrentFrame * Step);
            else Clock.Simulate(Step, true, false, false);
            RecordBirths();
        }

        void RecordBirths()
        {
            foreach (ParticleSystem particle in _immortals)
            {
                int count = particle.particleCount;
                if (count == 0) continue;
                int id = particle.GetInstanceID();
                if (!_buffers.TryGetValue(id, out ParticleSystem.Particle[] buffer) || buffer.Length < count)
                {
                    buffer = new ParticleSystem.Particle[count];
                    _buffers[id] = buffer;
                }
                count = particle.GetParticles(buffer);
                for (int index = 0; index < count; index++)
                {
                    string key = id + ":" + buffer[index].randomSeed;
                    if (!Births.ContainsKey(key)) Births.Add(key, CurrentFrame * Step);
                }
            }
        }

        /// <summary>
        /// 비교용 복제본과 프리뷰 렌더 자원 해제
        /// </summary>
        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            Preview.Cleanup();
        }
    }

    static uint StableSeed(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value) hash = (hash ^ character) * 16777619;
        return hash == 0 ? 1u : hash;
    }

    static float Maximum(ParticleSystem.MinMaxCurve curve)
    {
        return Mathf.Max(0f, FantasySpellBaker.CurveRange(curve).y);
    }

    static bool HasEmission(ParticleSystem particle)
    {
        ParticleSystem.EmissionModule emission = particle.emission;
        if (!emission.enabled) return false;
        if (Maximum(emission.rateOverTime) > 0f) return true;
        for (int index = 0; index < emission.burstCount; index++)
        {
            ParticleSystem.Burst burst = emission.GetBurst(index);
            if (burst.probability > 0f && Maximum(burst.count) > 0f) return true;
        }
        return false;
    }

    static float EmissionEnd(ParticleSystem particle)
    {
        ParticleSystem.EmissionModule emission = particle.emission;
        float end = Maximum(emission.rateOverTime) > 0f ? particle.main.duration : 0f;
        for (int index = 0; index < emission.burstCount; index++)
        {
            ParticleSystem.Burst burst = emission.GetBurst(index);
            if (burst.probability == 0f || Maximum(burst.count) == 0f) continue;
            float last = burst.cycleCount == 0 ? particle.main.duration
                : burst.time + Mathf.Max(0, burst.cycleCount - 1) * burst.repeatInterval;
            end = Mathf.Max(end, Mathf.Min(particle.main.duration, last));
        }
        return end;
    }

    static float Tail(ParticleSystem particle, HashSet<ParticleSystem> visiting)
    {
        if (!visiting.Add(particle)) throw new InvalidOperationException("순환 Sub Emitter 참조");
        float speed = particle.main.simulationSpeed;
        if (speed <= 0f) throw new InvalidOperationException("정지된 시뮬레이션 속도");
        float lifetime = Maximum(particle.main.startLifetime) / speed;
        ParticleSystem.TrailModule trails = particle.trails;
        if (trails.enabled && !trails.dieWithParticles) lifetime += Maximum(trails.lifetime) / speed;
        float tail = lifetime;
        ParticleSystem.SubEmittersModule sub = particle.subEmitters;
        for (int index = 0; sub.enabled && index < sub.subEmittersCount; index++)
        {
            ParticleSystem child = sub.GetSubEmitterSystem(index);
            if (child == null) throw new InvalidOperationException("Sub Emitter 누락");
            float delayedEmission = sub.GetSubEmitterType(index) == ParticleSystemSubEmitterType.Death
                ? EmissionEnd(child) / Mathf.Max(0.001f, child.main.simulationSpeed) : 0f;
            tail = Mathf.Max(tail, lifetime + delayedEmission + Maximum(child.main.startDelay) / Mathf.Max(0.001f, child.main.simulationSpeed) + Tail(child, visiting));
        }
        visiting.Remove(particle);
        return tail;
    }

    static int[] Schedule(View view, string effectName, out bool persistent, out float horizon)
    {
        HashSet<ParticleSystem> subSystems = new HashSet<ParticleSystem>();
        foreach (ParticleSystem particle in view.Systems)
        {
            ParticleSystem.SubEmittersModule sub = particle.subEmitters;
            for (int index = 0; sub.enabled && index < sub.subEmittersCount; index++)
                subSystems.Add(sub.GetSubEmitterSystem(index));
        }
        string timingPath = PrefabDirectory + "/_Timing/" + effectName + ".asset";
        bool declaredPersistent = File.Exists(timingPath) && File.ReadLines(timingPath).Take(40).Any(line => line.Trim() == "IsPersistent: 1");
        persistent = declaredPersistent || view.Renderers.Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.GetComponent<ParticleSystem>() == null);
        float first = float.PositiveInfinity;
        float lastStart = 0f;
        float end = 0f;
        foreach (ParticleSystem particle in view.Systems)
        {
            if (!particle.gameObject.activeInHierarchy || subSystems.Contains(particle) || !HasEmission(particle)) continue;
            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (!renderer.enabled && !particle.subEmitters.enabled) continue;
            float speed = Mathf.Max(0.001f, particle.main.simulationSpeed);
            float delay = Maximum(particle.main.startDelay) / speed;
            first = Mathf.Min(first, delay);
            lastStart = Mathf.Max(lastStart, delay);
            persistent |= particle.main.loop || float.IsPositiveInfinity(Maximum(particle.main.startLifetime));
            end = Mathf.Max(end, delay + EmissionEnd(particle) / speed + Tail(particle, new HashSet<ParticleSystem>()));
        }
        if (float.IsInfinity(first)) first = 0f;
        if ((!persistent && end > 60f) || lastStart > 55f) throw new InvalidOperationException("필요 재생 구간이 60초 검증 한도를 초과; 원본 유지");
        horizon = persistent ? Mathf.Min(60f, Mathf.Max(6f, lastStart + 6f)) : Mathf.Max(1f, end + 0.25f);
        float span = Mathf.Max(0.1f, (persistent ? horizon : end) - first);
        float[] times =
        {
            first + Mathf.Min(0.05f, span * 0.2f),
            first + span * 0.25f,
            first + span * 0.5f,
            first + span * 0.8f,
            Mathf.Min(horizon - 0.1f, lastStart + 0.1f),
            horizon
        };
        float maximumTime = horizon;
        List<int> samples = new List<int>();
        foreach (int frame in times.Where(time => time <= maximumTime).Select(time => Mathf.Max(1, Mathf.RoundToInt(time / Step))).Distinct().OrderBy(value => value))
            if (samples.Count == 0 || frame - samples.Last() >= 3) samples.Add(frame);
        int finalFrame = Mathf.RoundToInt(horizon / Step);
        if (samples.Last() != finalFrame)
        {
            if (finalFrame - samples.Last() < 3) samples.RemoveAt(samples.Count - 1);
            samples.Add(finalFrame);
        }
        return samples.ToArray();
    }

    static void CheckSource(View view)
    {
        if (view.Systems.Length > 6000) throw new InvalidOperationException("단일 효과가 6000개 시스템 검증 자원 한도를 초과");
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(view.Root) > 0
            || view.Root.GetComponentsInChildren<MonoBehaviour>(true).Any(component => !(component is BakedSpellPlayer)))
            throw new InvalidOperationException("사용자 스크립트 재생이 필요한 효과; 원본 유지");
        if (view.Root.GetComponentsInChildren<Animator>(true).Length > 0 || view.Root.GetComponentsInChildren<Animation>(true).Length > 0)
            throw new InvalidOperationException("별도 애니메이션 재생 문맥 필요; 원본 유지");
        foreach (ParticleSystem particle in view.Systems)
        {
            if (!particle.gameObject.activeInHierarchy) continue;
            if (particle.collision.enabled || particle.trigger.enabled)
                throw new InvalidOperationException("충돌 또는 Trigger 환경 문맥 필요; 원본 유지");
            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (!renderer.enabled) continue;
            List<Material> required = new List<Material> { renderer.sharedMaterial };
            if (particle.trails.enabled && particle.trails.ratio > 0f) required.Add(renderer.trailMaterial);
            if (required.Any(material => material == null || material.shader == null || !material.shader.isSupported))
                throw new InvalidOperationException("필수 재질 또는 셰이더 확인: " + particle.name + ", size=" + Maximum(particle.main.startSize)
                    + ", materials=" + string.Join(";", required.Select(material => material == null ? "null"
                        : AssetDatabase.GetAssetPath(material) + " / " + (material.shader == null ? "null" : material.shader.name) + " / " + (material.shader != null && material.shader.isSupported))));
        }
    }

    static bool IncludeBounds(View view, ref Bounds bounds, ref bool hasBounds)
    {
        bool visible = false;
        foreach (Renderer renderer in view.Renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            ParticleSystem particle = renderer.GetComponent<ParticleSystem>();
            if (particle != null && particle.particleCount == 0) continue;
            Bounds current = renderer.bounds;
            if (float.IsNaN(current.center.x) || float.IsInfinity(current.extents.magnitude) || current.extents.magnitude > 100000f)
                throw new InvalidOperationException("유효하지 않은 렌더 범위");
            if (hasBounds) bounds.Encapsulate(current);
            else { bounds = current; hasBounds = true; }
            visible = true;
        }
        return visible;
    }

    static void SetCamera(View view, Bounds bounds)
    {
        Quaternion rotation = Quaternion.Euler(25f, 35f, 0f);
        float halfX = 0f;
        float halfY = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
            Vector3 projected = Quaternion.Inverse(rotation) * offset;
            halfX = Mathf.Max(halfX, Mathf.Abs(projected.x));
            halfY = Mathf.Max(halfY, Mathf.Abs(projected.y));
        }
        Camera camera = view.Preview.camera;
        float distance = Mathf.Max(10f, bounds.extents.magnitude * 3f);
        camera.transform.SetPositionAndRotation(bounds.center + rotation * Vector3.back * distance, rotation);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = distance * 3f + 100f;
        camera.orthographicSize = Mathf.Max(0.5f, Mathf.Max(halfY, halfX * Height / Width) * 1.15f);
        camera.aspect = (float)Width / Height;
    }

    static Frame Sample(View view)
    {
        Frame frame = new Frame { Time = view.CurrentFrame * Step };
        List<Point> points = new List<Point>();
        foreach (ParticleSystem particle in view.Systems)
        {
            frame.Particles += particle.particleCount;
            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (!particle.gameObject.activeInHierarchy || !renderer.enabled || particle.particleCount == 0) continue;
            frame.VisibleParticles += particle.particleCount;
            frame.VisibleRenderers++;
            string role = FantasySpellParticleConsolidator.Role(particle.name);
            if (!view.PrimaryRoles.Contains(role)) continue;
            if (points.Count + particle.particleCount > 4096) throw new InvalidOperationException("주요 요소 비교 한도 4096개 초과");
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[particle.particleCount];
            int count = particle.GetParticles(buffer);
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle value = buffer[index];
                float speed = Mathf.Max(0.001f, particle.main.simulationSpeed);
                Vector3 position = value.position;
                if (particle.main.simulationSpace == ParticleSystemSimulationSpace.Local)
                    position = particle.transform.TransformPoint(position);
                else if (particle.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                    position = particle.main.customSimulationSpace.TransformPoint(position);
                string key = particle.GetInstanceID() + ":" + value.randomSeed;
                bool immortal = float.IsPositiveInfinity(value.startLifetime) && float.IsPositiveInfinity(value.remainingLifetime);
                if (immortal && !view.Births.ContainsKey(key)) throw new InvalidOperationException("무한 수명 입자 생성 기록 누락");
                float age = immortal ? frame.Time - view.Births[key] : (value.startLifetime - value.remainingLifetime) / speed;
                points.Add(new Point
                {
                    Key = key,
                    Role = role, Position = position, Size = value.GetCurrentSize3D(particle), Rotation = value.rotation3D,
                    Color = value.GetCurrentColor(particle), Age = age, Remaining = value.remainingLifetime / speed,
                    Birth = frame.Time - age
                });
            }
        }
        frame.Points = points.ToArray();
        return frame;
    }

    static Frame Render(View view, string path)
    {
        Frame frame = Sample(view);
        view.Preview.BeginStaticPreview(new Rect(0f, 0f, Width, Height));
        view.Preview.Render(true);
        Texture2D texture = view.Preview.EndStaticPreview();
        try
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Color32[] pixels = texture.GetPixels32();
            Color32 background = pixels[0];
            float[] signature = new float[GridWidth * GridHeight * 3];
            float energy = 0f;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Color32 color = pixels[y * Width + x];
                    int cell = ((y / 8) * GridWidth + x / 8) * 3;
                    signature[cell] += (color.r - background.r) / (255f * 64f);
                    signature[cell + 1] += (color.g - background.g) / (255f * 64f);
                    signature[cell + 2] += (color.b - background.b) / (255f * 64f);
                }
            }
            foreach (float value in signature) energy += Mathf.Abs(value);
            frame.Image = path;
            frame.Pixels = signature;
            frame.Energy = energy;
            return frame;
        }
        finally { Object.DestroyImmediate(texture); }
    }

    static float PixelDifference(Frame before, Frame after, out float overlap, out float ratio)
    {
        float difference = 0f;
        int intersection = 0;
        int union = 0;
        for (int index = 0; index < before.Pixels.Length; index += 3)
        {
            float a = Mathf.Abs(before.Pixels[index]) + Mathf.Abs(before.Pixels[index + 1]) + Mathf.Abs(before.Pixels[index + 2]);
            float b = Mathf.Abs(after.Pixels[index]) + Mathf.Abs(after.Pixels[index + 1]) + Mathf.Abs(after.Pixels[index + 2]);
            bool foregroundA = a > 0.035f;
            bool foregroundB = b > 0.035f;
            if (foregroundA || foregroundB) union++;
            if (foregroundA && foregroundB) intersection++;
            difference += Mathf.Abs(before.Pixels[index] - after.Pixels[index])
                + Mathf.Abs(before.Pixels[index + 1] - after.Pixels[index + 1])
                + Mathf.Abs(before.Pixels[index + 2] - after.Pixels[index + 2]);
        }
        overlap = union == 0 ? 1f : (float)intersection / union;
        ratio = before.Energy < 0.01f ? (after.Energy < 0.01f ? 1f : 100f) : after.Energy / before.Energy;
        return difference / Mathf.Max(1f, before.Energy);
    }

    static string Cell(string role, Vector3 position, float tolerance)
    {
        return role + ":" + Mathf.FloorToInt(position.x / tolerance) + ":" + Mathf.FloorToInt(position.y / tolerance) + ":" + Mathf.FloorToInt(position.z / tolerance);
    }

    static Dictionary<string, List<Point>> PointMap(IEnumerable<Point> points, float tolerance)
    {
        Dictionary<string, List<Point>> map = new Dictionary<string, List<Point>>();
        foreach (Point point in points)
        {
            string key = Cell(point.Role, point.Position, tolerance);
            if (!map.TryGetValue(key, out List<Point> list)) { list = new List<Point>(); map.Add(key, list); }
            list.Add(point);
        }
        return map;
    }

    static bool PointValuesMatch(Point point, Point other, float tolerance)
    {
        return Mathf.Abs(point.Birth - other.Birth) <= Step + 0.001f
            && Vector3.Distance(point.Position, other.Position) <= tolerance
            && Vector3.Distance(point.Size, other.Size) <= Mathf.Max(0.001f, point.Size.magnitude * 0.025f)
            && Quaternion.Angle(Quaternion.Euler(point.Rotation), Quaternion.Euler(other.Rotation)) <= 2f
            && Mathf.Abs(point.Color.r - other.Color.r) <= 16 && Mathf.Abs(point.Color.g - other.Color.g) <= 16
            && Mathf.Abs(point.Color.b - other.Color.b) <= 16 && Mathf.Abs(point.Color.a - other.Color.a) <= 16;
    }

    static bool MatchPoint(Point point, Dictionary<string, List<Point>> map, float tolerance, out string key)
    {
        key = null;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    string cellKey = Cell(point.Role, point.Position + new Vector3(x, y, z) * tolerance, tolerance);
                    if (!map.TryGetValue(cellKey, out List<Point> list)) continue;
                    foreach (Point other in list)
                    {
                        if (!PointValuesMatch(point, other, tolerance)) continue;
                        key = other.Key;
                        return true;
                    }
                }
        return false;
    }

    static bool MatchTrajectory(Point point, Frame[] frames, float tolerance, out string key)
    {
        key = null;
        IEnumerable<IGrouping<string, Point>> tracks = frames.SelectMany(frame => frame.Points)
            .Where(other => other.Role == point.Role && Mathf.Abs(other.Birth - point.Birth) <= Step + 0.001f)
            .GroupBy(other => other.Key).Take(512);
        foreach (IGrouping<string, Point> track in tracks)
        {
            Point[] samples = track.OrderBy(other => other.Age).ToArray();
            for (int index = 0; index + 1 < samples.Length; index++)
            {
                Point a = samples[index]; Point b = samples[index + 1];
                if (b.Age - a.Age < 0.0001f || point.Age < a.Age - 0.001f || point.Age > b.Age + 0.001f) continue;
                float amount = (point.Age - a.Age) / (b.Age - a.Age);
                Point expected = new Point
                {
                    Birth = a.Birth,
                    Position = Vector3.LerpUnclamped(a.Position, b.Position, amount),
                    Size = Vector3.LerpUnclamped(a.Size, b.Size, amount),
                    Rotation = Quaternion.SlerpUnclamped(Quaternion.Euler(a.Rotation), Quaternion.Euler(b.Rotation), amount).eulerAngles,
                    Color = Color32.Lerp(a.Color, b.Color, amount)
                };
                if (!PointValuesMatch(point, expected, tolerance)) continue;
                key = track.Key;
                return true;
            }
        }
        return false;
    }

    static FrameComparison Compare(Frame[] before, Frame after, float positionTolerance)
    {
        FrameComparison result = new FrameComparison
        {
            Time = after.Time, BeforeParticles = before[1].VisibleParticles, AfterParticles = after.VisibleParticles,
            BeforeMinimum = before.Min(frame => frame.VisibleParticles), BeforeMaximum = before.Max(frame => frame.VisibleParticles),
            AfterImage = after.Image
        };
        result.AfterEnergy = after.Energy;
        float excess = Mathf.Max(0f, result.BeforeMinimum - after.VisibleParticles, after.VisibleParticles - result.BeforeMaximum);
        result.CountDifference = excess / Mathf.Max(1f, result.BeforeParticles);
        float best = float.PositiveInfinity;
        foreach (Frame frame in before)
        {
            float error = PixelDifference(frame, after, out float overlap, out float ratio);
            if (error >= best) continue;
            best = error;
            result.ImageDifference = error;
            result.ImageOverlap = overlap;
            result.EnergyRatio = ratio;
            result.ReferenceTime = frame.Time;
            result.BeforeImage = frame.Image;
            result.BeforeEnergy = frame.Energy;
        }
        Dictionary<string, List<Point>> sourcePoints = PointMap(before.SelectMany(frame => frame.Points), positionTolerance);
        Dictionary<string, List<Point>> candidatePoints = PointMap(after.Points, positionTolerance);
        HashSet<string> covered = new HashSet<string>();
        List<string> issues = new List<string>();
        foreach (Point point in after.Points)
        {
            if (MatchPoint(point, sourcePoints, positionTolerance, out string key)
                || MatchTrajectory(point, before, positionTolerance, out key)) { covered.Add(key); continue; }
            if (point.Age <= Step + 0.001f || point.Remaining <= Step + 0.001f) result.BoundaryPoints++;
            else
            {
                result.UnmatchedPoints++;
                if (issues.Count < 4)
                {
                    Point nearest = before[1].Points.Where(other => other.Role == point.Role)
                        .OrderBy(other => Vector3.Distance(other.Position, point.Position)).FirstOrDefault();
                    issues.Add("after=" + JsonUtility.ToJson(point) + "; before=" + (nearest == null ? "none" : JsonUtility.ToJson(nearest)));
                }
            }
        }
        foreach (Point point in before[1].Points)
        {
            if (covered.Contains(point.Key) || MatchPoint(point, candidatePoints, positionTolerance, out string key)) continue;
            if (point.Age <= Step + 0.001f || point.Remaining <= Step + 0.001f) result.BoundaryPoints++;
            else
            {
                result.UnmatchedPoints++;
                if (issues.Count < 4) issues.Add("uncovered before=" + JsonUtility.ToJson(point));
            }
        }
        bool empty = before.Any(frame => frame.Energy < 0.05f) && after.Energy < 0.05f;
        result.Passed = excess <= Mathf.Max(2f, result.BeforeParticles * 0.025f)
            && result.UnmatchedPoints == 0
            && (empty || (result.ImageDifference <= 0.25f && result.ImageOverlap >= 0.60f
                && result.EnergyRatio >= 0.78f && result.EnergyRatio <= 1.28f));
        result.PointIssues = issues.ToArray();
        return result;
    }

    static void SaveResult(Result result)
    {
        result.FinishedUtc = DateTime.UtcNow.ToString("O");
        string folder = AllDirectory + "/Effects/" + result.Name;
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "/Result.json", JsonUtility.ToJson(result, true));
        Results.RemoveAll(existing => existing.Name == result.Name);
        Results.Add(result);
        Report report = new Report
        {
            Targets = 90, Results = Results.OrderBy(value => value.Name).ToArray(),
            Completed = Results.Count(value => value.State == "Completed"),
            Kept = Results.Count(value => value.State == "Kept"),
            Incomplete = Results.Count(value => value.State == "Incomplete"),
            BeforeSystems = Results.Sum(value => value.BeforeSystems), AfterSystems = Results.Sum(value => value.AfterSystems),
            BeforeObjects = Results.Sum(value => value.BeforeObjects), AfterObjects = Results.Sum(value => value.AfterObjects)
        };
        File.WriteAllText(AllDirectory + "/Report.json", JsonUtility.ToJson(report, true));
    }

    static IEnumerable<object> Process(Entry entry)
    {
        Result result = new Result { Name = entry.Name, Path = entry.Path, SourceHash = entry.SourceHash, State = "Incomplete" };
        View source = null;
        View candidate = null;
        FantasySpellParticleConsolidator.PreparedEffect prepared = null;
        List<Attempt> attempts = new List<Attempt>();
        string folder = AllDirectory + "/Effects/" + entry.Name;
        Directory.CreateDirectory(folder);
        try
        {
            if (SourceHash(entry.Path) != entry.SourceHash) throw new InvalidOperationException("백업 이후 외부 변경 확인; 원본 유지");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (asset == null) throw new InvalidOperationException("프리팹 불러오기 실패");
            result.BeforeSystems = result.AfterSystems = asset.GetComponentsInChildren<ParticleSystem>(true).Length;
            result.BeforeObjects = result.AfterObjects = asset.GetComponentsInChildren<Transform>(true).Length;
            MonoBehaviour[] scripts = asset.GetComponentsInChildren<MonoBehaviour>(true);
            bool supportedScripts = scripts.Length == 0 || (scripts.Length == 1 && asset.GetComponent<BakedSpellPlayer>() != null);
            if (!supportedScripts || asset.GetComponentsInChildren<Animator>(true).Length > 0
                || asset.GetComponentsInChildren<Animation>(true).Length > 0 || asset.GetComponentsInChildren<Rigidbody>(true).Length > 0
                || asset.GetComponentsInChildren<Rigidbody2D>(true).Length > 0 || asset.GetComponentsInChildren<Cloth>(true).Length > 0)
                throw new InvalidOperationException("스크립트·애니메이션·물리 재생 문맥 필요; 원본 유지");
            source = new View(asset, null);
            result.BeforeSystems = result.AfterSystems = source.Systems.Length;
            result.BeforeObjects = result.AfterObjects = source.Root.GetComponentsInChildren<Transform>(true).Length;
            CheckSource(source);
            int[] frames = Schedule(source, entry.Name, out bool persistent, out float horizon);
            result.Persistent = persistent;
            result.Horizon = horizon;
            result.SampleTimes = frames.Select(frame => frame * Step).ToArray();
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            // 필요한 재생 구간의 화면 범위를 한 차례 확인
            foreach (int target in frames)
            {
                while (source.CurrentFrame < target + 1)
                {
                    source.Advance();
                    if (source.CurrentFrame % 6 == 0)
                    {
                        _progress = entry.Name + " / 화면 범위 / " + (source.CurrentFrame * Step).ToString("0.0") + "s";
                        yield return null;
                    }
                }
                IncludeBounds(source, ref bounds, ref hasBounds);
            }
            if (!hasBounds) throw new InvalidOperationException("독립 자동 재생에서 표시되는 요소 없음; 원본 유지");
            SetCamera(source, bounds);
            float tolerance = Mathf.Max(0.01f, bounds.size.magnitude * 0.0001f);
            bool protectedReference = (entry.Blockers != null && entry.Blockers.Length > 0) || source.Player != null;
            string[] modes = protectedReference ? new[] { "Original" } : new[] { "Full", "PointsOnly", "HierarchyOnly" };
            bool accepted = false;
            foreach (string mode in modes)
            {
                Attempt attempt = new Attempt { Mode = mode };
                attempts.Add(attempt);
                string attemptFolder = folder + "/" + mode;
                Directory.CreateDirectory(attemptFolder);
                if (mode != "Original")
                {
                    prepared = FantasySpellParticleConsolidator.Prepare(entry.Path, mode == "Full", mode != "HierarchyOnly");
                    attempt.Systems = prepared.Root.GetComponentsInChildren<ParticleSystem>(true).Length;
                    attempt.Objects = prepared.Root.GetComponentsInChildren<Transform>(true).Length;
                    attempt.MergedGroups = prepared.MergedGroups;
                    attempt.MaximumTimingMilliseconds = prepared.MaximumTimingError * 1000f;
                    attempt.Changes = prepared.Changes.ToArray();
                    attempt.KeptGroups = prepared.KeptGroups.ToArray();
                    if (prepared.Changed)
                    {
                        candidate = new View(prepared.Root, source.PrimaryRoles);
                        SetCamera(candidate, bounds);
                    }
                }
                source.Reset();
                List<FrameComparison> comparisons = new List<FrameComparison>();
                List<string> baselineImages = new List<string>();
                bool visiblePixels = false;
                bool sourceEnds = false;
                bool candidateEnds = false;
                foreach (int target in frames)
                {
                    while (source.CurrentFrame < target - 1 || (candidate != null && candidate.CurrentFrame < target))
                    {
                        if (source.CurrentFrame < target - 1) source.Advance();
                        if (candidate != null && candidate.CurrentFrame < target) candidate.Advance();
                        if (Mathf.Max(source.CurrentFrame, candidate == null ? 0 : candidate.CurrentFrame) % 6 == 0)
                        {
                            _progress = entry.Name + " / " + mode + " / " + (source.CurrentFrame * Step).ToString("0.0") + "s";
                            yield return null;
                        }
                    }
                    // 같은 Editor 갱신 안에서 전후 이미지를 렌더하여 셰이더 전역 시간 차이 최소화
                    Frame previous = Render(source, attemptFolder + "/before-" + target.ToString("D4") + "-prev.png");
                    source.Advance();
                    Frame central = Render(source, attemptFolder + "/before-" + target.ToString("D4") + ".png");
                    Frame after = candidate == null ? null : Render(candidate, attemptFolder + "/after-" + target.ToString("D4") + ".png");
                    source.Advance();
                    Frame next = Render(source, attemptFolder + "/before-" + target.ToString("D4") + "-next.png");
                    baselineImages.Add(central.Image);
                    visiblePixels |= central.Energy > 0.05f;
                    if (after != null) comparisons.Add(Compare(new[] { previous, central, next }, after, tolerance));
                    if (target == frames.Last())
                    {
                        sourceEnds = next.VisibleParticles == 0;
                        candidateEnds = after == null || after.VisibleParticles == 0;
                    }
                    yield return null;
                }
                result.BaselineImages = baselineImages.ToArray();
                attempt.Frames = comparisons.ToArray();
                attempt.Passed = visiblePixels && (persistent || sourceEnds) && (persistent || candidateEnds)
                    && comparisons.All(value => value.Passed);
                if (!visiblePixels) attempt.Error = "비교 이미지에서 충분한 표시 신호 없음";
                else if (!persistent && !sourceEnds) attempt.Error = "원본이 예상 종료 구간 이후에도 표시 중";
                else if (!persistent && !candidateEnds) attempt.Error = "후보가 예상 종료 구간 이후에도 표시 중";
                else if (comparisons.Any(value => !value.Passed)) attempt.Error = "전후 이미지·입자 수·주요 요소 비교 기준 미통과";
                File.WriteAllText(attemptFolder + "/Comparison.json", JsonUtility.ToJson(attempt, true));
                if (attempt.Passed)
                {
                    result.PlaybackVerified = true;
                    if (prepared != null && prepared.Changed)
                    {
                        FantasySpellParticleConsolidator.Commit(prepared, AllDirectory + "/Backup/" + entry.Name + ".prefab");
                        result.State = "Completed";
                        result.Mode = prepared.MergedGroups == 0 ? "HierarchyOnly" : mode;
                        result.AfterSystems = attempt.Systems;
                        result.AfterObjects = attempt.Objects;
                        result.Changes = attempt.Changes;
                        result.KeptGroups = attempt.KeptGroups;
                        result.GuidAndRootReferencesVerified = true;
                        result.Reason = mode == "HierarchyOnly" ? "연출 비교 통과; 계층 정리 적용, 반복 방출 원형 유지" : "자동 전후 비교 통과";
                    }
                    else
                    {
                        result.State = "Kept";
                        result.Mode = mode;
                        result.GuidAndRootReferencesVerified = AssetDatabase.AssetPathToGUID(entry.Path) == entry.Guid;
                        result.Reason = source.Player != null ? "기존 재생기의 데이터·인덱스 바인딩 원형 유지; 명시적 편집기 재생 검증"
                            : protectedReference ? string.Join("; ", entry.Blockers) : "안전하게 통합할 반복 또는 불필요한 계층 없음";
                        if (prepared != null) result.KeptGroups = prepared.KeptGroups.ToArray();
                    }
                    accepted = true;
                }
                candidate?.Dispose(); candidate = null;
                FantasySpellParticleConsolidator.Release(prepared); prepared = null;
                if (accepted) break;
            }
            if (!accepted) result.Reason = "자동 검증 미통과; 원본 유지";
        }
        finally
        {
            candidate?.Dispose();
            source?.Dispose();
            FantasySpellParticleConsolidator.Release(prepared);
            result.Attempts = attempts.ToArray();
            result.FinalHash = File.Exists(entry.Path) ? SourceHash(entry.Path) : "";
            SaveResult(result);
        }
    }

    static IEnumerable<object> RunBatch(bool pilots)
    {
        Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(AllDirectory + "/Manifest.json"));
        if (manifest == null || manifest.Effects == null || manifest.Effects.Length != manifest.TargetCount)
            throw new InvalidOperationException("대상 백업 목록 불일치");
        foreach (string path in Directory.GetFiles(AllDirectory + "/Effects", "Result.json", SearchOption.AllDirectories))
        {
            Result old = JsonUtility.FromJson<Result>(File.ReadAllText(path));
            if (old != null && !Results.Any(value => value.Name == old.Name)) Results.Add(old);
        }
        Entry[] ordered = manifest.Effects.OrderBy(entry => entry.Name == "Effect_30_ArrowRain" ? 0
            : entry.Name == "Effect_19_MassLightningField" ? 1 : 2).ThenBy(entry => entry.Name).ToArray();
        foreach (Entry entry in ordered)
        {
            if (pilots && entry.Name != "Effect_30_ArrowRain" && entry.Name != "Effect_19_MassLightningField") continue;
            Result existing = Results.FirstOrDefault(value => value.Name == entry.Name);
            if (existing != null && existing.State != "Incomplete" && File.Exists(entry.Path) && existing.FinalHash == SourceHash(entry.Path)) continue;
            _progress = entry.Name + " / 준비";
            IEnumerator<object> process = Process(entry).GetEnumerator();
            Exception failure = null;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = process.MoveNext(); }
                    catch (Exception exception) { failure = exception; more = false; }
                    if (!more) break;
                    yield return process.Current;
                }
            }
            finally { process.Dispose(); }
            if (failure != null)
            {
                Result result = Results.FirstOrDefault(value => value.Name == entry.Name)
                    ?? new Result { Name = entry.Name, Path = entry.Path, SourceHash = entry.SourceHash, FinalHash = SourceHash(entry.Path) };
                result.State = "Incomplete";
                result.Reason = failure.ToString();
                SaveResult(result);
            }
            _progress = entry.Name + " / " + Results.Last(value => value.Name == entry.Name).State;
            yield return null;
        }
        _progress = "대상 판정 완료: " + Results.Count;
    }

    [MenuItem("Tools/VFX/Particle Authoring/전체 효과 자동 정리 및 비교")]
    static void RunApprovedBatch()
    {
        Start(new Request { Id = Guid.NewGuid().ToString("N"), Action = "batch" });
    }

    static void WriteStatus(string state, string message)
    {
        File.WriteAllText(ReviewDirectory + "/automation.status.json", JsonUtility.ToJson(new Status
        {
            Id = _request.Id, Action = _request.Action, State = state, Message = message
        }, true));
    }

    static void Start(Request request)
    {
        if (_routine != null) throw new InvalidOperationException("기존 작업 완료 필요");
        _request = request;
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("편집 모드에서만 실행 가능");
        if (request.Action != "batch") throw new InvalidOperationException("지원되지 않는 작업");
        Directory.CreateDirectory(AllDirectory + "/Effects");
        AssemblyReloadEvents.beforeAssemblyReload -= Interrupt;
        AssemblyReloadEvents.beforeAssemblyReload += Interrupt;
        _progress = request.Action;
        _routine = RunBatch(false).GetEnumerator();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        WriteStatus("running", _progress);
    }

    static void Tick()
    {
        if (_routine != null && File.Exists(ReviewDirectory + "/automation.cancel.json"))
        {
            _routine.Dispose(); _routine = null;
            WriteStatus("stopped", "사용자 중단 요청");
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_routine != null)
        {
            try
            {
                System.Diagnostics.Stopwatch budget = System.Diagnostics.Stopwatch.StartNew();
                bool more;
                int slices = 0;
                do
                {
                    more = _routine.MoveNext();
                    slices++;
                }
                while (more && slices < 16 && budget.Elapsed.TotalMilliseconds < 8d);
                if (!more)
                {
                    _routine.Dispose(); _routine = null;
                    WriteStatus("completed", _progress);
                }
                else if (EditorApplication.timeSinceStartup >= _nextStatus)
                {
                    _nextStatus = EditorApplication.timeSinceStartup + 1d;
                    WriteStatus("running", _progress);
                }
            }
            catch (Exception exception)
            {
                _routine?.Dispose(); _routine = null;
                WriteStatus("failed", exception.ToString());
                Debug.LogException(exception);
            }
            return;
        }
        EditorApplication.update -= Tick;
        AssemblyReloadEvents.beforeAssemblyReload -= Interrupt;
    }

    static void Interrupt()
    {
        if (_routine == null) return;
        _routine.Dispose(); _routine = null;
        WriteStatus("interrupted", "스크립트 갱신으로 중단");
    }

    internal static string SourceHash(string path)
    {
        using (SHA256 algorithm = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }
}
