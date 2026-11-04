using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>외부 스펠 생성 규칙을 사전 조립 프리팹으로 변환</summary>
public static class FantasySpellBaker
{
    public const string Output = "Assets/_Project/03.Prefab/VFX/FantasySpellsEffectsPack/Optimized";
    const string Source = "Assets/_ThirdParty/FantasySpellsEffectsPack/AllEffects/Optimized";
    const float Forever = float.MaxValue;
    const float Dt = BakedSpellDefinition.StepSeconds;
    static readonly Dictionary<string, GameObject> ProjectCopies = new Dictionary<string, GameObject>();

    [Serializable]
    sealed class Result
    {
        public string SourcePath;
        public string PrefabPath;
        public string DefinitionPath;
        public string Error;
        public int ParticleCount;
        public int MeshCount;
        public int MotionCount;
        public int ExpandedPartCount;
        public float LastSpawnSeconds;
        public float FiniteEndBoundSeconds;
        public bool Persistent;
        public string[] Notes;
    }

    [Serializable]
    sealed class Report
    {
        public string Basis = "60fps; Update-based delays quantized to 1/60 second; fixed random seed; conservative particle lifetime upper bounds; persistent effects have no finite end; original shader time follows Unity time";
        public Result[] Effects;
    }

    sealed class Spawn
    {
        public float Time;
        public int Order;
        public string Kind;
        public Transform Parent;
        public Transform Target;
        public GameObject Prefab;
        public Vector3 RandomPosition;
        public Vector3 RandomRotation;
        public float Radius;
        public int Index;
        public int Count;
        public float Spacing;
        public float SubtractY;
        public bool Cross;
    }

    sealed class MotionEntry
    {
        public SpellMotion Motion;
        public Transform Transform;
        public Transform Target;
        public SpellMotionEvaluator.State State;
    }

    sealed class BakeState
    {
        public GameObject Root;
        public readonly List<Transform> Bindings = new List<Transform>();
        public readonly List<SpellPose> Poses = new List<SpellPose>();
        public readonly Dictionary<Transform, int> BindingIndex = new Dictionary<Transform, int>();
        public readonly Dictionary<Transform, float> Birth = new Dictionary<Transform, float>();
        public readonly Dictionary<Transform, float> Death = new Dictionary<Transform, float>();
        public readonly List<MotionEntry> Motions = new List<MotionEntry>();
        public readonly List<Spawn> Pending = new List<Spawn>();
        public readonly List<MeshRenderer> Surfaces = new List<MeshRenderer>();
        public readonly List<SpellSurface> SurfaceData = new List<SpellSurface>();
        public readonly List<ParticleSystem> Particles = new List<ParticleSystem>();
        public readonly List<WindZone> Winds = new List<WindZone>();
        public readonly List<SpellWind> WindData = new List<SpellWind>();
        public readonly HashSet<string> Notes = new HashSet<string>();
        public readonly System.Random Random = new System.Random(17041);
        public int SpawnCount;
        public int NextOrder;
        public int Frame;
        public float LastSpawn;
    }

    /// <summary>전체 이펙트 변환과 결과 파일 생성</summary>
    [MenuItem("Tools/VFX/Fantasy Spells/전체 프리팹 다시 생성")]
    public static void BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode를 종료한 뒤 변환 필요");
        Directory.CreateDirectory(Output + "/_Timing");
        Directory.CreateDirectory(Output + "/_Reports");
        Directory.CreateDirectory(Output + "/_Parts");
        ProjectCopies.Clear();
        AssetDatabase.Refresh();
        string[] paths = Directory.GetDirectories(Source).SelectMany(directory => Directory.GetFiles(directory, "*.prefab"))
            .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        List<Result> results = new List<Result>();
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Fantasy Spells 변환", Path.GetFileName(paths[i]), (float)i / paths.Length);
                Result result = BakeOne(paths[i]);
                results.Add(result);
                Directory.CreateDirectory("Temp/FantasySpellBake");
                File.WriteAllText("Temp/FantasySpellBake/progress.json", JsonUtility.ToJson(result, true));
            }
            Report report = new Report { Effects = results.ToArray() };
            File.WriteAllText(Output + "/_Reports/EffectCatalog.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            WriteHtml(report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            int failures = results.Count(result => !string.IsNullOrEmpty(result.Error));
            if (failures != 0) throw new InvalidOperationException($"{failures}개 변환 실패: EffectCatalog.json 확인 필요");
            Debug.Log($"Fantasy Spells: {results.Count}개 사전 조립 프리팹 생성 완료 — {Output}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>일괄 생성 후 참조와 대표 재생 검증</summary>
    public static void BakeAndValidate()
    {
        BakeAll();
        FantasySpellValidator.ValidateAll();
    }

    static Result BakeOne(string sourcePath)
    {
        string name = Path.GetFileNameWithoutExtension(sourcePath).Replace("(Opt)", "")
            .Replace("(ForEffectScene)", "").Replace("(ForEffectsScene)", "").Replace("(ForeEffectScene)", "");
        Result result = new Result
        {
            SourcePath = sourcePath,
            PrefabPath = Output + "/" + name + ".prefab",
            DefinitionPath = Output + "/_Timing/" + name + ".asset"
        };
        Scene scene = EditorSceneManager.NewPreviewScene();
        BakeState state = new BakeState();
        BakedSpellDefinition definition = null;
        try
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("원본 프리팹 가져오기 실패");
            state.Root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(state.Root, scene);
            GameObject content = (GameObject)PrefabUtility.InstantiatePrefab(ProjectCopy(source), scene);
            content.transform.SetPositionAndRotation(Vector3.zero, source.transform.rotation);
            content.transform.SetParent(state.Root.transform, true);
            content.name = "Composition";
            Register(content, 0f, state);
            while (state.Pending.Count > 0)
            {
                state.Pending.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order.CompareTo(b.Order));
                Spawn spawn = state.Pending[0];
                state.Pending.RemoveAt(0);
                if (spawn.Time >= AncestorDeath(spawn.Parent, state)) continue;
                if (state.SpawnCount > 20000 || spawn.Time > 3600f)
                    throw new InvalidOperationException("변환 크기 또는 생성 시간이 검사 한도 초과");
                AdvancePose(state, spawn.Time);
                Expand(spawn, state);
            }

            definition = ScriptableObject.CreateInstance<BakedSpellDefinition>();
            definition.SourcePath = sourcePath;
            definition.Poses = state.Poses.ToArray();
            definition.LastSpawnSeconds = state.LastSpawn;
            foreach (MotionEntry entry in state.Motions)
            {
                SpellMotion motion = entry.Motion;
                motion.Death = AncestorDeath(entry.Transform, state);
                entry.Motion = motion;
            }
            definition.Motions = state.Motions.Select(entry => entry.Motion).ToArray();
            bool persistent = false;
            float end = 0.1f;
            for (int i = 0; i < state.SurfaceData.Count; i++)
            {
                SpellSurface surface = state.SurfaceData[i];
                surface.Death = AncestorDeath(state.Surfaces[i].transform, state);
                state.SurfaceData[i] = surface;
                if (!surface.InitiallyEnabled) continue;
                if (surface.Death == Forever) persistent = true;
                else end = Mathf.Max(end, surface.Death);
            }
            definition.Surfaces = state.SurfaceData.ToArray();
            for (int i = 0; i < state.Winds.Count; i++)
            {
                SpellWind wind = state.WindData[i];
                wind.Death = AncestorDeath(state.Winds[i].transform, state);
                state.WindData[i] = wind;
            }
            definition.Winds = state.WindData.ToArray();
            List<SpellParticleTiming> timings = new List<SpellParticleTiming>();
            foreach (ParticleSystem particle in state.Particles)
            {
                float birth = state.Birth[particle.transform];
                float death = AncestorDeath(particle.transform, state);
                SpellParticleTiming timing = ConfigureParticle(particle, birth, death, state);
                timings.Add(timing);
                if (timing.End == Forever) persistent = true;
                else end = Mathf.Max(end, timing.End);
            }
            definition.Particles = timings.ToArray();
            definition.IsPersistent = persistent;
            definition.EndSeconds = Mathf.Max(end, state.LastSpawn);

            // 원본 생성기 및 무동작 Animator의 제거 완료 상태 확인
            foreach (MonoBehaviour behaviour in state.Root.GetComponentsInChildren<MonoBehaviour>(true))
                throw new InvalidOperationException($"변환되지 않은 스크립트: {behaviour}");
            foreach (Animator animator in state.Root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null) throw new InvalidOperationException("애니메이터 별도 변환 필요");
                Object.DestroyImmediate(animator);
            }
            foreach (Collider collider in state.Root.GetComponentsInChildren<Collider>(true))
            {
                state.Notes.Add("파티클 충돌 모듈 미사용: 원본 메시 콜라이더 제거");
                Object.DestroyImmediate(collider);
            }
            ParticleSystem clock = state.Root.AddComponent<ParticleSystem>();
            clock.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule clockMain = clock.main;
            clockMain.loop = persistent;
            clockMain.duration = persistent ? 5f : Mathf.Max(0.1f, definition.EndSeconds);
            clockMain.startLifetime = persistent ? 5f : Mathf.Max(0.1f, definition.EndSeconds);
            clockMain.startSpeed = 0f;
            clockMain.startSize = 0f;
            clockMain.playOnAwake = true;
            clockMain.maxParticles = 1;
            clockMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule clockEmission = clock.emission;
            clockEmission.rateOverTime = 0f;
            clockEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule clockShape = clock.shape;
            clockShape.enabled = false;
            state.Root.GetComponent<ParticleSystemRenderer>().enabled = false;

            BakedSpellDefinition saved = AssetDatabase.LoadAssetAtPath<BakedSpellDefinition>(result.DefinitionPath);
            if (saved == null)
            {
                AssetDatabase.CreateAsset(definition, result.DefinitionPath);
                saved = definition;
            }
            else
            {
                EditorUtility.CopySerialized(definition, saved);
                EditorUtility.SetDirty(saved);
                Object.DestroyImmediate(definition);
                definition = saved;
            }
            BakedSpellPlayer player = state.Root.AddComponent<BakedSpellPlayer>();
            player.Configure(saved, state.Bindings.ToArray(), state.Surfaces.ToArray(), state.Particles.ToArray(), state.Winds.ToArray(), clock);
            player.Restart(true);
            foreach (Transform transform in state.Root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(transform)) continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                foreach (Component component in transform.GetComponents<Component>())
                    if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            if (PrefabUtility.SaveAsPrefabAsset(state.Root, result.PrefabPath) == null)
                throw new InvalidOperationException("완성 프리팹 저장 실패");

            result.ParticleCount = state.Particles.Count;
            result.MeshCount = state.Surfaces.Count;
            result.MotionCount = state.Motions.Count;
            result.ExpandedPartCount = state.SpawnCount;
            result.LastSpawnSeconds = state.LastSpawn;
            result.FiniteEndBoundSeconds = saved.EndSeconds;
            result.Persistent = saved.IsPersistent;
            result.Notes = state.Notes.OrderBy(note => note).ToArray();
        }
        catch (Exception exception)
        {
            // 개별 실패를 결과 목록에 기록하고 다른 이펙트 변환 계속
            result.Error = exception.ToString();
            Debug.LogError($"{sourcePath}: {exception}");
            if (definition != null && !AssetDatabase.Contains(definition)) Object.DestroyImmediate(definition);
        }
        finally
        {
            if (state.Root != null) Object.DestroyImmediate(state.Root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return result;
    }

    static void Register(GameObject instance, float birth, BakeState state)
    {
        Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            state.BindingIndex.Add(transform, state.Bindings.Count);
            state.Bindings.Add(transform);
            state.Poses.Add(new SpellPose { Position = transform.localPosition, Rotation = transform.localRotation, Scale = transform.localScale });
            state.Birth.Add(transform, birth);
        }
        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (particle.gameObject.activeInHierarchy) state.Particles.Add(particle);
        }
        foreach (WindZone wind in instance.GetComponentsInChildren<WindZone>(true))
        {
            if (!wind.gameObject.activeInHierarchy) continue;
            state.Winds.Add(wind);
            state.WindData.Add(new SpellWind { Birth = birth, Death = Forever, Main = wind.windMain, Turbulence = wind.windTurbulence, PulseMagnitude = wind.windPulseMagnitude });
            state.Notes.Add("바람 파츠의 활성 구간 유지; 바람 맥동은 Unity 현재 시간 사용");
        }
        MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
        Dictionary<Transform, MonoBehaviour> fades = new Dictionary<Transform, MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null) throw new InvalidOperationException("원본 Missing Script 발견");
            if (behaviour.enabled && behaviour.gameObject.activeInHierarchy && behaviour.GetType().Name == "Material_Change")
            {
                fades.Add(behaviour.transform, behaviour);
                float delay = Read<float>(behaviour, "m_timeToReduce");
                float factor = Read<float>(behaviour, "m_reduceFactor");
                state.Death[behaviour.transform] = DissolveEnd(birth, delay, factor);
            }
        }
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.gameObject.activeInHierarchy) continue;
            SpellSurface surface = new SpellSurface { Birth = birth, Death = Forever, InitiallyEnabled = renderer.enabled };
            if (fades.TryGetValue(renderer.transform, out MonoBehaviour fade))
            {
                Material material = Read<Material>(fade, "m_inputMaterial");
                if (material == null) throw new InvalidOperationException("소멸 재질 참조 누락");
                renderer.sharedMaterial = material;
                surface.Dissolve = true;
                surface.FadeDelay = Read<float>(fade, "m_timeToReduce");
                surface.FadeFactor = Read<float>(fade, "m_reduceFactor");
            }
            state.Surfaces.Add(renderer);
            state.SurfaceData.Add(surface);
        }
        foreach (MonoBehaviour behaviour in behaviours)
        {
            string kind = behaviour.GetType().Name;
            if (behaviour.enabled && behaviour.gameObject.activeInHierarchy)
            {
                if (kind.EndsWith("ObjectMake", StringComparison.Ordinal)) Schedule(behaviour, birth, state);
                else if (kind != "Material_Change") RegisterMotion(behaviour, birth, state);
            }
            Object.DestroyImmediate(behaviour);
        }
    }

    static void RegisterMotion(MonoBehaviour behaviour, float birth, BakeState state)
    {
        SpellMotion motion = new SpellMotion { Binding = state.BindingIndex[behaviour.transform], Birth = birth, Death = Forever, Target = -1 };
        Transform target = null;
        switch (behaviour.GetType().Name)
        {
            case "MoveToObject":
                motion.Kind = SpellMotionKind.MoveToObject;
                target = Read<Transform>(behaviour, "m_movePos");
                if (target == null || !state.BindingIndex.TryGetValue(target, out motion.Target))
                    throw new InvalidOperationException($"이동 목표 참조 누락: {behaviour.name}");
                motion.Delay = Read<float>(behaviour, "m_startDelay");
                motion.Duration = Read<float>(behaviour, "m_durationTime");
                motion.Power = Read<float>(behaviour, "m_lerpValue");
                motion.Look = Read<float>(behaviour, "m_lookValue");
                break;
            case "TranslateMove":
                motion.Kind = SpellMotionKind.Translate;
                motion.Power = Read<float>(behaviour, "m_power");
                motion.Delay = Read<float>(behaviour, "m_reduceTime");
                motion.Axes = new Vector3(Read<bool>(behaviour, "m_rightMove") ? 1 : 0, Read<bool>(behaviour, "m_upMove") ? 1 : 0, Read<bool>(behaviour, "m_fowardMove") ? 1 : 0);
                state.Notes.Add("원본 프레임 의존 이동을 60fps 기준으로 고정");
                break;
            case "RotateObject":
                motion.Kind = SpellMotionKind.Rotate;
                motion.Delay = Read<float>(behaviour, "m_delay");
                motion.Vector = Read<Vector3>(behaviour, "RotateOffset");
                state.Notes.Add("원본 프레임 의존 회전을 60fps 기준으로 고정");
                break;
            case "ScaleChange":
                motion.Kind = SpellMotionKind.Scale;
                motion.Delay = Read<float>(behaviour, "m_startTime");
                motion.Power = Read<float>(behaviour, "m_Weight");
                state.Notes.Add("원본 누적 크기 변화를 60fps 기준으로 고정");
                break;
            default:
                throw new InvalidOperationException($"지원되지 않은 원본 동작: {behaviour.GetType().Name}");
        }
        motion.Death = AncestorDeath(behaviour.transform, state);
        state.Motions.Add(new MotionEntry { Motion = motion, Transform = behaviour.transform, Target = target });
    }

    static void Schedule(MonoBehaviour behaviour, float birth, BakeState state)
    {
        string kind = behaviour.GetType().Name;
        GameObject[] prefabs = Read<GameObject[]>(behaviour, "m_makeObjs");
        Transform target = Read<Transform>(behaviour, "m_movePos");
        int count = 1;
        float first = 0f;
        float interval = 0f;
        Vector3 randomPosition = Vector3.zero;
        Vector3 randomRotation = Vector3.zero;
        float radius = 0f;
        float spacing = 0f;
        float subtract = 0f;
        bool cross = false;
        switch (kind)
        {
            case "SimpleObjectMake": randomRotation = Read<Vector3>(behaviour, "m_randomRotationValue"); break;
            case "DelayObjectMake": first = After(Read<float>(behaviour, "m_startDelay")); break;
            case "MultiObjectMake":
                count = Read<int>(behaviour, "m_makeCount");
                interval = After(Read<float>(behaviour, "m_makeDelay"));
                first = After(Mathf.Max(Read<float>(behaviour, "m_startDelay"), Read<float>(behaviour, "m_makeDelay")));
                randomPosition = Read<Vector3>(behaviour, "m_randomPos");
                randomRotation = Read<Vector3>(behaviour, "m_randomRot");
                break;
            case "MultiCircleObjectMake":
                count = Read<int>(behaviour, "m_makeCount");
                interval = After(Read<float>(behaviour, "m_makeDelay"));
                first = Mathf.Max(Ceil(Read<float>(behaviour, "m_startDelay")), interval);
                radius = Read<float>(behaviour, "m_interval");
                break;
            case "FowardObjectMake":
                count = Mathf.CeilToInt(Read<float>(behaviour, "m_makeCount"));
                interval = After(Read<float>(behaviour, "m_makeDelay"));
                first = interval;
                spacing = Read<float>(behaviour, "m_objectSize");
                subtract = Read<float>(behaviour, "m_subtractYValue");
                cross = Read<bool>(behaviour, "m_isCrossMake");
                break;
            default: throw new InvalidOperationException($"지원되지 않은 생성기: {kind}");
        }
        if (count > 20000 || count < 0) throw new InvalidOperationException("잘못된 반복 횟수");
        for (int i = 0; i < count; i++)
        {
            Vector3 positionOffset = RandomVector(randomPosition, state.Random);
            Vector3 rotationOffset = RandomVector(randomRotation, state.Random);
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) throw new InvalidOperationException("생성 대상 프리팹 누락");
                state.Pending.Add(new Spawn
                {
                    Time = birth + first + i * interval, Order = state.NextOrder++, Kind = kind,
                    Parent = behaviour.transform, Target = target, Prefab = prefab,
                    RandomPosition = positionOffset, RandomRotation = kind == "SimpleObjectMake" ? RandomVector(randomRotation, state.Random) : rotationOffset,
                    Radius = radius, Index = i, Count = count, Spacing = spacing, SubtractY = subtract, Cross = cross
                });
            }
        }
        if (randomPosition != Vector3.zero || randomRotation != Vector3.zero)
            state.Notes.Add("반복 배치 난수 고정: 동일한 프리팹은 항상 동일한 배치로 재생");
    }

    static void Expand(Spawn spawn, BakeState state)
    {
        if (AssetDatabase.GetAssetPath(spawn.Prefab).Contains("/Original/"))
            state.Notes.Add("Optimized 원본이 참조하는 Original 파츠 연결 유지");
        Vector3 position = spawn.Parent.position + spawn.RandomPosition;
        Quaternion rotation = spawn.Parent.rotation * Quaternion.Euler(spawn.RandomRotation);
        if (spawn.Kind == "FowardObjectMake")
        {
            int cross = spawn.Cross ? (spawn.Index % 2 == 0 ? 1 : -1) : 0;
            position = spawn.Parent.position - Vector3.up * spawn.SubtractY + spawn.Parent.forward * spawn.Spacing * spawn.Index + spawn.Parent.right * cross;
            rotation = spawn.Parent.rotation * Quaternion.Euler(-90f, 0f, 0f);
        }
        else if (spawn.Kind == "MultiCircleObjectMake")
        {
            float angle = 2f * Mathf.PI / spawn.Count * spawn.Index;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * spawn.Radius, 0f, Mathf.Sin(angle) * spawn.Radius);
            position = spawn.Parent.position + offset;
            if (offset == Vector3.zero) throw new InvalidOperationException("원형 생성 반지름이 0");
            rotation = Quaternion.LookRotation(offset) * spawn.Prefab.transform.rotation;
        }
        GameObject child = (GameObject)PrefabUtility.InstantiatePrefab(ProjectCopy(spawn.Prefab), state.Root.scene);
        child.transform.SetPositionAndRotation(position, rotation);
        child.transform.SetParent(spawn.Parent, true);
        child.name = spawn.Prefab.name.Replace("(Opt)", "") + "_" + state.SpawnCount.ToString("D4");
        if (spawn.Target != null)
        {
            MonoBehaviour move = child.GetComponents<MonoBehaviour>().FirstOrDefault(component => component != null && component.GetType().Name == "MoveToObject");
            if (move != null) move.GetType().GetField("m_movePos").SetValue(move, spawn.Target);
        }
        state.SpawnCount++;
        state.LastSpawn = Mathf.Max(state.LastSpawn, spawn.Time);
        Register(child, spawn.Time, state);
    }

    static void AdvancePose(BakeState state, float until)
    {
        int frame = Mathf.RoundToInt(until / Dt);
        while (state.Frame < frame)
        {
            state.Frame++;
            foreach (MotionEntry entry in state.Motions)
                SpellMotionEvaluator.Step(entry.Motion, entry.Transform, entry.Target, ref entry.State, state.Frame * Dt);
        }
    }

    static SpellParticleTiming ConfigureParticle(ParticleSystem particle, float birth, float death, BakeState state)
    {
        ParticleSystem.MainModule main = particle.main;
        if (main.simulationSpeed <= 0f) throw new InvalidOperationException("시뮬레이션 속도가 0 이하인 파티클");
        if (particle.subEmitters.enabled && particle.subEmitters.subEmittersCount > 0)
            throw new InvalidOperationException("SubEmitter 종료 시간 별도 계산 필요");
        if (particle.lifetimeByEmitterSpeed.enabled)
            throw new InvalidOperationException("LifetimeByEmitterSpeed 별도 계산 필요");
        if (particle.collision.enabled) throw new InvalidOperationException("충돌 파티클의 콜라이더 유지 검토 필요");
        float speed = main.simulationSpeed;
        Vector2 delay = CurveRange(main.startDelay) / speed;
        bool prewarm = main.prewarm && main.loop;
        if (prewarm && Mathf.Abs(delay.x - delay.y) > 0.00001f)
            throw new InvalidOperationException("가변 딜레이를 사용하는 예열 파티클 별도 계산 필요");
        if (prewarm)
        {
            main.prewarm = false;
            state.Notes.Add("사전 예열을 실제 시작 시점에 실행하여 딜레이 이전 노출 방지");
        }
        Vector2 lifetime = CurveRange(main.startLifetime) / speed;
        ParticleSystem.EmissionModule emission = particle.emission;
        float lastEmission = 0f;
        bool emits = false;
        if (emission.enabled)
        {
            if (CurveRange(emission.rateOverTime).y > 0f || CurveRange(emission.rateOverDistance).y > 0f)
            {
                emits = true;
                lastEmission = main.duration;
            }
            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            foreach (ParticleSystem.Burst burst in bursts)
            {
                if (burst.probability <= 0f || CurveRange(burst.count).y <= 0f) continue;
                emits = true;
                float burstEnd = burst.cycleCount == 0 ? main.duration : burst.time + Mathf.Max(0, burst.cycleCount - 1) * burst.repeatInterval;
                lastEmission = Mathf.Max(lastEmission, Mathf.Min(main.duration, burstEnd));
            }
        }
        float trail = particle.trails.enabled && !particle.trails.dieWithParticles ? CurveRange(particle.trails.lifetime).y / speed : 0f;
        if (trail > 0f && particle.trails.sizeAffectsLifetime)
            throw new InvalidOperationException("크기 비례 트레일 수명 별도 계산 필요");
        float end = emits ? birth + delay.y + lastEmission / speed + lifetime.y + trail : birth;
        if (emits && main.loop) end = Forever;
        end = Mathf.Min(end, death);
        if (main.stopAction != ParticleSystemStopAction.None)
            state.Notes.Add("원본 stopAction 제거: 사전 조립 계층의 참조 유지");
        main.stopAction = ParticleSystemStopAction.None;
        main.startDelay = OffsetCurve(main.startDelay, birth * speed);
        main.playOnAwake = true;
        particle.useAutoRandomSeed = false;
        particle.randomSeed = (uint)(17041 + state.Particles.IndexOf(particle) * 37);
        return new SpellParticleTiming
        {
            Name = particle.name, Birth = birth, StartMin = birth + delay.x, StartMax = birth + delay.y,
            End = end, Death = death, Looping = main.loop && emits, Prewarm = prewarm
        };
    }

    static ParticleSystem.MinMaxCurve OffsetCurve(ParticleSystem.MinMaxCurve curve, float amount)
    {
        if (amount == 0f) return curve;
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(curve.constant + amount);
            case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(curve.constantMin + amount, curve.constantMax + amount);
            case ParticleSystemCurveMode.Curve:
                return curve.curveMultiplier == 0f ? new ParticleSystem.MinMaxCurve(amount) : new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Shift(curve.curve, amount / curve.curveMultiplier));
            case ParticleSystemCurveMode.TwoCurves:
                return curve.curveMultiplier == 0f ? new ParticleSystem.MinMaxCurve(amount) : new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Shift(curve.curveMin, amount / curve.curveMultiplier), Shift(curve.curveMax, amount / curve.curveMultiplier));
            default: throw new InvalidOperationException("알 수 없는 파티클 곡선 모드");
        }
    }

    static AnimationCurve Shift(AnimationCurve source, float amount)
    {
        Keyframe[] keys = source.keys;
        for (int i = 0; i < keys.Length; i++) keys[i].value += amount;
        return new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
    }

    /// <summary>비가중 Hermite 곡선의 극값을 포함한 범위 계산</summary>
    /// <param name="curve">파티클 값 곡선</param>
    /// <returns>최솟값과 최댓값</returns>
    public static Vector2 CurveRange(ParticleSystem.MinMaxCurve curve)
    {
        if (curve.mode == ParticleSystemCurveMode.Constant) return Vector2.one * curve.constant;
        if (curve.mode == ParticleSystemCurveMode.TwoConstants) return new Vector2(Mathf.Min(curve.constantMin, curve.constantMax), Mathf.Max(curve.constantMin, curve.constantMax));
        Vector2 range = AnimationRange(curve.mode == ParticleSystemCurveMode.Curve ? curve.curve : curve.curveMin);
        if (curve.mode == ParticleSystemCurveMode.TwoCurves)
        {
            Vector2 second = AnimationRange(curve.curveMax);
            range = new Vector2(Mathf.Min(range.x, second.x), Mathf.Max(range.y, second.y));
        }
        range *= curve.curveMultiplier;
        return new Vector2(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    static Vector2 AnimationRange(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0) return Vector2.zero;
        Keyframe[] keys = curve.keys;
        float min = keys.Min(key => key.value);
        float max = keys.Max(key => key.value);
        for (int i = 0; i + 1 < keys.Length; i++)
        {
            Keyframe left = keys[i];
            Keyframe right = keys[i + 1];
            if (left.weightedMode != WeightedMode.None || right.weightedMode != WeightedMode.None)
                throw new InvalidOperationException("가중 곡선의 수명 극값 계산 미지원");
            double span = right.time - left.time;
            if (span <= 0 || float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent)) continue;
            double a = 2 * left.value - 2 * right.value + span * (left.outTangent + right.inTangent);
            double b = -3 * left.value + 3 * right.value - span * (2 * left.outTangent + right.inTangent);
            double c = span * left.outTangent;
            List<double> roots = new List<double>();
            if (Math.Abs(a) < 1e-12) { if (Math.Abs(b) > 1e-12) roots.Add(-c / (2 * b)); }
            else
            {
                double discriminant = 4 * b * b - 12 * a * c;
                if (discriminant >= 0)
                {
                    roots.Add((-2 * b + Math.Sqrt(discriminant)) / (6 * a));
                    roots.Add((-2 * b - Math.Sqrt(discriminant)) / (6 * a));
                }
            }
            foreach (double t in roots)
            {
                if (t <= 0 || t >= 1) continue;
                float value = (float)(((a * t + b) * t + c) * t + left.value);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
        }
        return new Vector2(min, max);
    }

    static float AncestorDeath(Transform transform, BakeState state)
    {
        float death = Forever;
        for (Transform current = transform; current != null && current != state.Root.transform; current = current.parent)
            if (state.Death.TryGetValue(current, out float value)) death = Mathf.Min(death, value);
        return death;
    }

    static float DissolveEnd(float birth, float delay, float factor)
    {
        if (factor <= 0f) return Forever;
        int low = 0;
        int high = 1;
        while (SpellMotionEvaluator.Dissolve(delay + high * Dt, delay, factor) < 1f && high < 1000000) high *= 2;
        if (high >= 1000000) throw new InvalidOperationException("재질 소멸 시간 계산 한도 초과");
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (SpellMotionEvaluator.Dissolve(delay + middle * Dt, delay, factor) >= 1f) high = middle;
            else low = middle;
        }
        return birth + Mathf.Floor(Mathf.Max(0f, delay) / Dt + 0.0001f) * Dt + high * Dt;
    }

    static float After(float seconds) => (Mathf.Floor(Mathf.Max(0f, seconds) / Dt + 0.0001f) + 1f) * Dt;
    static float Ceil(float seconds) => Mathf.Ceil(Mathf.Max(0f, seconds) / Dt - 0.0001f) * Dt;
    static Vector3 RandomVector(Vector3 maximum, System.Random random) => new Vector3((float)(random.NextDouble() * 2 - 1) * maximum.x, (float)(random.NextDouble() * 2 - 1) * maximum.y, (float)(random.NextDouble() * 2 - 1) * maximum.z);

    static T Read<T>(MonoBehaviour component, string name)
    {
        FieldInfo field = component.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field == null) throw new MissingFieldException(component.GetType().Name, name);
        return (T)field.GetValue(component);
    }

    static GameObject ProjectCopy(GameObject source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (ProjectCopies.TryGetValue(sourcePath, out GameObject existing)) return existing;
        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string name = Path.GetFileNameWithoutExtension(sourcePath).Replace("(Opt)", "");
        string targetPath = Output + "/_Parts/" + name + "_" + guid.Substring(0, 8) + ".prefab";
        GameObject copy = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        if (copy == null)
        {
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath)) throw new IOException($"프로젝트 파츠 복사 실패: {sourcePath}");
            copy = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        }
        if (copy == null) throw new IOException($"프로젝트 파츠 읽기 실패: {targetPath}");
        ProjectCopies.Add(sourcePath, copy);
        return copy;
    }

    static void WriteHtml(Report report)
    {
        StringBuilder html = new StringBuilder("<!doctype html><html lang='ko'><meta charset='utf-8'><title>Fantasy Spells 변환 결과</title><style>body{font:15px system-ui;margin:40px;color:#20252b;background:#f6f8fa}table{border-collapse:collapse;background:white;width:100%}td,th{padding:10px;border:1px solid #d9e0e7;text-align:left}th{position:sticky;top:0;background:#e7eef5}small{color:#58616c}input{padding:12px;width:420px;margin:15px 0}</style><h1>Fantasy Spells · 완성 프리팹 목록</h1><p>60fps 기준 생성 간격·이동·소멸 계산. 난수 배치 고정. 최대 종료는 마지막 방출과 최대 파티클·트레일 수명을 합한 상한이며, 충돌 등으로 실제 종료가 빨라질 수 있습니다. 무한 반복과 영구 메시는 지속형으로 표시합니다.</p><p>각 프리팹의 Baked Spell Player에서 미리보기 열기. 원본 파츠 생성 스크립트는 제거되며, 메시·이동·소멸은 재생 보조 코드로 처리됩니다.</p><input placeholder='이름으로 검색' oninput=\"for(const r of document.querySelectorAll('tbody tr'))r.hidden=!r.textContent.toLowerCase().includes(this.value.toLowerCase())\"><table><thead><tr><th>프리팹</th><th>마지막 생성</th><th>최대 종료</th><th>파티클 / 메시 / 동작</th><th>비고</th></tr></thead><tbody>");
        foreach (Result result in report.Effects)
        {
            string notes = result.Error ?? string.Join(" / ", result.Notes ?? Array.Empty<string>());
            string end = result.Persistent ? "지속형 (종료 없음)" : result.FiniteEndBoundSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
            html.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(Path.GetFileName(result.PrefabPath)))
                .Append("<br><small>").Append(System.Net.WebUtility.HtmlEncode(result.DefinitionPath)).Append("</small></td><td>")
                .Append(result.LastSpawnSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append("s</td><td>").Append(end)
                .Append("</td><td>").Append(result.ParticleCount).Append(" / ").Append(result.MeshCount).Append(" / ").Append(result.MotionCount)
                .Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(notes)).Append("</td></tr>");
        }
        html.Append("</tbody></table></html>");
        File.WriteAllText(Output + "/_Reports/EffectCatalog.html", html.ToString(), new UTF8Encoding(false));
    }
}
