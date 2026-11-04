using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 승인된 두 스펠의 호환 가능한 반복 방출을 네이티브 파티클 설정으로 통합
/// </summary>
internal static class FantasySpellParticleConsolidator
{
    const string ShapeDirectory = "Assets/_Project/06.Data/VFX/FantasySpellsEffectsPack";
    const float Tolerance = 0.00001f;

    sealed class Group
    {
        internal readonly List<ParticleSystem[]> Copies = new List<ParticleSystem[]>();
        internal string Name;
        internal bool UseCarrier;
    }

    internal sealed class PreparedEffect
    {
        internal GameObject Root;
        internal string Path;
        internal string ShapePath;
        internal readonly List<Mesh> Meshes = new List<Mesh>();
        internal readonly List<string> Changes = new List<string>();
        internal readonly List<string> KeptGroups = new List<string>();
        internal float MaximumTimingError;
        internal int MergedGroups;
        internal bool Changed;
    }

    [Serializable]
    sealed class ChangeReport
    {
        public string Effect;
        public string[] Changes;
        public string[] RemainingRepeatedRoles;
    }

    static bool Near(float left, float right)
    {
        if (left.Equals(right)) return true;
        if (float.IsNaN(left) || float.IsNaN(right) || float.IsInfinity(left) || float.IsInfinity(right)) return false;
        return Mathf.Abs(left - right) <= Tolerance * Mathf.Max(1f, Mathf.Abs(left), Mathf.Abs(right));
    }

    static bool Near(Vector3 left, Vector3 right)
    {
        return (left - right).sqrMagnitude <= Tolerance * Tolerance;
    }

    static bool IgnoreProperty(string path, bool particle)
    {
        if (path == "m_GameObject" || path == "m_ObjectHideFlags" || path == "m_CorrespondingSourceObject"
            || path == "m_PrefabInstance" || path == "m_PrefabAsset") return true;
        if (!particle) return false;
        return path == "randomSeed" || path == "autoRandomSeed" || path == "startDelay"
            || path.StartsWith("startDelay.", StringComparison.Ordinal)
            || (path.StartsWith("SubModule.", StringComparison.Ordinal) && path.EndsWith(".emitter", StringComparison.Ordinal));
    }

    static bool SameValue(JToken left, JToken right, string path, bool particle)
    {
        if (left == null || right == null) return left == right;
        if (left.Type != right.Type) return false;
        if (left is JObject leftObject && right is JObject rightObject)
        {
            if (leftObject.Count != rightObject.Count) return false;
            foreach (JProperty property in leftObject.Properties())
            {
                string childPath = string.IsNullOrEmpty(path) ? property.Name : path + "." + property.Name;
                if (IgnoreProperty(childPath, particle)) continue;
                if (!SameValue(property.Value, rightObject[property.Name], childPath, particle)) return false;
            }
            return true;
        }
        if (left is JArray leftArray && right is JArray rightArray)
        {
            if (leftArray.Count != rightArray.Count) return false;
            for (int index = 0; index < leftArray.Count; index++)
                if (!SameValue(leftArray[index], rightArray[index], path + "[" + index + "]", particle)) return false;
            return true;
        }
        if (left.Type == JTokenType.Float) return Near(left.Value<float>(), right.Value<float>());
        return JToken.DeepEquals(left, right);
    }

    static bool SameSettings(Object left, Object right, bool particle)
    {
        JObject a = JObject.Parse(EditorJsonUtility.ToJson(left));
        JObject b = JObject.Parse(EditorJsonUtility.ToJson(right));
        return SameValue(a.Properties().Single().Value, b.Properties().Single().Value, "", particle);
    }

    static bool CollectGraph(ParticleSystem particle, List<ParticleSystem> nodes, Transform root)
    {
        if (nodes.Contains(particle) || !particle.gameObject.activeSelf || !particle.transform.IsChildOf(root)) return false;
        if (particle.GetComponents<Component>().Any(component => !(component is Transform)
            && !(component is ParticleSystem) && !(component is ParticleSystemRenderer))) return false;
        nodes.Add(particle);
        ParticleSystem.SubEmittersModule sub = particle.subEmitters;
        for (int index = 0; sub.enabled && index < sub.subEmittersCount; index++)
        {
            ParticleSystem child = sub.GetSubEmitterSystem(index);
            if (child == null || !CollectGraph(child, nodes, root)) return false;
        }
        foreach (Transform child in particle.transform)
            if (!nodes.Any(node => node.transform == child)) return false;
        return true;
    }

    static bool IsPointEmission(ParticleSystem particle)
    {
        ParticleSystem.MainModule main = particle.main;
        ParticleSystem.EmissionModule emission = particle.emission;
        if (particle.shape.enabled || main.loop || main.prewarm || main.simulationSpeed != 1f
            || main.startDelay.mode != ParticleSystemCurveMode.Constant
            || main.startSpeed.mode != ParticleSystemCurveMode.Constant || main.startSpeed.constant != 0f
            || main.simulationSpace == ParticleSystemSimulationSpace.Custom
            || !emission.enabled || emission.burstCount != 1
            || emission.rateOverTime.mode != ParticleSystemCurveMode.Constant || emission.rateOverTime.constant != 0f
            || emission.rateOverDistance.mode != ParticleSystemCurveMode.Constant || emission.rateOverDistance.constant != 0f) return false;
        // 방출 시점에 따라 달라지는 초기값은 반복 Burst로 옮기면 외형이 변하므로 제외
        ParticleSystem.MinMaxCurve[] initialCurves =
        {
            main.startLifetime, main.startSizeX, main.startSizeY, main.startSizeZ,
            main.startRotationX, main.startRotationY, main.startRotationZ, main.gravityModifier
        };
        if (initialCurves.Any(curve => curve.mode != ParticleSystemCurveMode.Constant
            && curve.mode != ParticleSystemCurveMode.TwoConstants)) return false;
        if (main.startColor.mode != ParticleSystemGradientMode.Color
            && main.startColor.mode != ParticleSystemGradientMode.TwoColors) return false;
        ParticleSystem.Burst burst = emission.GetBurst(0);
        return burst.time == 0f && burst.cycleCount == 1 && burst.probability == 1f
            && burst.count.mode == ParticleSystemCurveMode.Constant && burst.count.constant == 1f;
    }

    static bool CanCarryEmission(ParticleSystem particle)
    {
        ParticleSystem.MainModule main = particle.main;
        ParticleSystem.EmissionModule emission = particle.emission;
        return !main.loop && !main.prewarm && main.simulationSpeed == 1f
            && main.startDelay.mode == ParticleSystemCurveMode.Constant
            && main.simulationSpace != ParticleSystemSimulationSpace.Custom
            && main.stopAction == ParticleSystemStopAction.None && emission.enabled
            && emission.rateOverDistance.mode == ParticleSystemCurveMode.Constant
            && emission.rateOverDistance.constant == 0f
            && particle.GetComponentsInParent<MonoBehaviour>(true).Length == 0
            && particle.GetComponentInParent<Animator>() == null
            && particle.GetComponentInParent<Animation>() == null;
    }

    static string DescribeEmission(ParticleSystem particle)
    {
        ParticleSystem.MainModule main = particle.main;
        ParticleSystem.EmissionModule emission = particle.emission;
        string burst = emission.burstCount == 1
            ? $"{emission.GetBurst(0).time}/{emission.GetBurst(0).cycleCount}/{emission.GetBurst(0).probability}/{emission.GetBurst(0).count.mode}/{emission.GetBurst(0).count.constant}"
            : emission.burstCount.ToString();
        return $"{particle.name}: shape={particle.shape.enabled}, loop={main.loop}, prewarm={main.prewarm}, speed={main.simulationSpeed}, delay={main.startDelay.mode}, startSpeed={main.startSpeed.mode}/{main.startSpeed.constant}, space={main.simulationSpace}, emission={emission.enabled}, rate={emission.rateOverTime.mode}/{emission.rateOverTime.constant}, distance={emission.rateOverDistance.mode}/{emission.rateOverDistance.constant}, burst={burst}, lifetime={main.startLifetime.mode}, size={main.startSizeX.mode}/{main.startSizeY.mode}/{main.startSizeZ.mode}, rotation={main.startRotationX.mode}/{main.startRotationY.mode}/{main.startRotationZ.mode}, gravity={main.gravityModifier.mode}, color={main.startColor.mode}";
    }

    static bool Compatible(ParticleSystem[] left, ParticleSystem[] right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            ParticleSystem a = left[index];
            ParticleSystem b = right[index];
            if (Role(a.name) != Role(b.name) || !SameSettings(a, b, true)
                || !SameSettings(a.GetComponent<ParticleSystemRenderer>(), b.GetComponent<ParticleSystemRenderer>(), false)
                || Quaternion.Angle(a.transform.rotation, b.transform.rotation) > 0.001f
                || !Near(a.transform.lossyScale, b.transform.lossyScale)) return false;
            if (index > 0 && (!Near(a.main.startDelay.constantMax, b.main.startDelay.constantMax)
                || !Near(a.transform.position - left[0].transform.position, b.transform.position - right[0].transform.position))) return false;
        }
        return true;
    }

    static List<Group> FindGroups(GameObject root)
    {
        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        HashSet<ParticleSystem> subEmitters = new HashSet<ParticleSystem>();
        foreach (ParticleSystem particle in particles)
        {
            ParticleSystem.SubEmittersModule sub = particle.subEmitters;
            for (int index = 0; index < sub.subEmittersCount; index++)
                subEmitters.Add(sub.GetSubEmitterSystem(index));
        }
        List<Group> groups = new List<Group>();
        foreach (ParticleSystem particle in particles)
        {
            if (!particle.gameObject.activeInHierarchy || subEmitters.Contains(particle)) continue;
            bool useCarrier = !IsPointEmission(particle);
            if (useCarrier && !CanCarryEmission(particle)) continue;
            List<ParticleSystem> nodes = new List<ParticleSystem>();
            if (!CollectGraph(particle, nodes, root.transform)) continue;
            ParticleSystem[] graph = nodes.ToArray();
            if (nodes.Any(node => node.lights.enabled && node.lights.ratio > 0f)) continue;
            // 공유 하위 방출기의 소유권이 바뀌는 통합 방지
            if (particles.Except(graph).Any(other =>
            {
                ParticleSystem.SubEmittersModule sub = other.subEmitters;
                for (int index = 0; index < sub.subEmittersCount; index++)
                    if (nodes.Contains(sub.GetSubEmitterSystem(index))) return true;
                return false;
            })) continue;
            Group group = groups.FirstOrDefault(existing => existing.UseCarrier == useCarrier && Compatible(existing.Copies[0], graph));
            if (group == null)
            {
                group = new Group { Name = Role(particle.name), UseCarrier = useCarrier };
                groups.Add(group);
            }
            group.Copies.Add(graph);
        }
        File.WriteAllText(FantasySpellParticleComparison.ReviewDirectory + "/" + root.name + ".groups.json",
            JsonUtility.ToJson(new ChangeReport
            {
                Effect = root.name,
                Changes = groups.Select(group => group.Name + ": " + group.Copies.Count + "벌").ToArray(),
                RemainingRepeatedRoles = particles.Where(particle => particle.name.Contains("Body") || particle.name.Contains("Decal") || particle.name.Contains("Motion"))
                    .Take(4).Select(DescribeEmission).ToArray()
            }, true));
        return groups.Where(group => group.Copies.Count >= 3).ToList();
    }

    static AnimationCurve MeshSpeed(float[] times, float duration, float factor)
    {
        List<Keyframe> keys = new List<Keyframe>();
        for (int index = 0; index < times.Length - 1; index++)
        {
            float interval = times[index + 1] - times[index];
            if (interval <= 0.0001f) throw new InvalidOperationException("동시 방출은 별도 Burst 분포 설계 필요");
            float speed = factor / (times.Length * interval);
            keys.Add(new Keyframe(times[index] / duration, speed, float.PositiveInfinity, float.PositiveInfinity));
        }
        keys.Add(new Keyframe(times[times.Length - 1] / duration, 0f, float.PositiveInfinity, float.PositiveInfinity));
        return new AnimationCurve(keys.ToArray());
    }

    static ParticleSystem.Burst[] RepeatingBursts(float[] times)
    {
        if (times.All(time => Mathf.Abs(time - times[0]) < 0.00001f))
            return new[] { new ParticleSystem.Burst(times[0], (short)times.Length) };
        List<float> remaining = times.ToList();
        List<ParticleSystem.Burst> bursts = new List<ParticleSystem.Burst>();
        while (remaining.Count > 0)
        {
            float start = remaining[0];
            List<float> best = new List<float> { start };
            float bestInterval = 0.1f;
            foreach (float next in remaining.Skip(1))
            {
                float interval = next - start;
                if (interval <= 0.0001f) continue;
                List<float> sequence = new List<float> { start };
                for (int cycle = 1; cycle <= remaining.Count; cycle++)
                {
                    float target = start + cycle * interval;
                    int match = remaining.FindIndex(time => Mathf.Abs(time - target) < 0.0001f);
                    if (match < 0) break;
                    sequence.Add(remaining[match]);
                }
                if (sequence.Count <= best.Count) continue;
                best = sequence;
                bestInterval = interval;
            }
            bursts.Add(new ParticleSystem.Burst(start, (short)1) { cycleCount = best.Count, repeatInterval = bestInterval });
            foreach (float time in best) remaining.Remove(time);
        }
        return bursts.OrderBy(burst => burst.time).ToArray();
    }

    static ParticleSystem TimingProbe(Transform parent, float delay, float duration, float lifetime, int maximum, ParticleSystem.Burst[] bursts)
    {
        GameObject host = new GameObject("TimingProbe") { hideFlags = HideFlags.HideAndDontSave };
        host.transform.SetParent(parent, false);
        ParticleSystem particle = host.AddComponent<ParticleSystem>();
        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particle.main;
        main.loop = false;
        main.startDelay = delay;
        main.duration = duration;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = 0f;
        main.maxParticles = maximum;
        ParticleSystem.ShapeModule shape = particle.shape;
        shape.enabled = false;
        ParticleSystem.EmissionModule emission = particle.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(bursts);
        particle.GetComponent<ParticleSystemRenderer>().enabled = false;
        return particle;
    }

    static float[] TimingDifference(ParticleSystem[][] copies, ParticleSystem template)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject host = new GameObject("TimingComparison") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(host, scene);
        try
        {
            float lifetime = copies.Last()[0].main.startDelay.constant + 10f;
            ParticleSystem clock = TimingProbe(host.transform, 0f, lifetime, lifetime, 1, Array.Empty<ParticleSystem.Burst>());
            ParticleSystem[] originals = copies.Select(copy => TimingProbe(clock.transform,
                copy[0].main.startDelay.constant, copy[0].main.duration, lifetime, 1,
                new[] { new ParticleSystem.Burst(0f, (short)1) })).ToArray();
            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[template.emission.burstCount];
            template.emission.GetBursts(bursts);
            ParticleSystem merged = TimingProbe(clock.transform, template.main.startDelay.constant,
                template.main.duration, lifetime, copies.Length, bursts);
            float[] oldBirth = Enumerable.Repeat(float.NaN, copies.Length).ToArray();
            float[] newBirth = Enumerable.Repeat(float.NaN, copies.Length).ToArray();
            ParticleSystem.Particle[] single = new ParticleSystem.Particle[1];
            ParticleSystem.Particle[] combined = new ParticleSystem.Particle[copies.Length];
            clock.Simulate(0f, true, true, false);
            for (int frame = 1; frame < Mathf.CeilToInt(lifetime * 60f); frame++)
            {
                clock.Simulate(1f / 60f, true, false, false);
                for (int index = 0; index < originals.Length; index++)
                {
                    if (!float.IsNaN(oldBirth[index]) || originals[index].GetParticles(single) == 0) continue;
                    oldBirth[index] = frame / 60f - (single[0].startLifetime - single[0].remainingLifetime);
                }
                int count = merged.GetParticles(combined);
                for (int index = 0; index < count; index++)
                    if (float.IsNaN(newBirth[index]))
                        newBirth[index] = frame / 60f - (combined[index].startLifetime - combined[index].remainingLifetime);
                if (oldBirth.All(value => !float.IsNaN(value)) && newBirth.All(value => !float.IsNaN(value)))
                    return oldBirth.Select((value, index) => value - newBirth[index]).ToArray();
            }
            throw new InvalidOperationException(template.name + ": 생성 시각 비교 미완료");
        }
        finally
        {
            Object.DestroyImmediate(host);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static float MatchTiming(ParticleSystem[][] copies, ParticleSystem merged, float[] times)
    {
        ParticleSystem.Burst[] bursts = RepeatingBursts(times);
        ParticleSystem.EmissionModule emission = merged.emission;
        emission.SetBursts(bursts);
        if (emission.burstCount != bursts.Length)
            throw new InvalidOperationException(merged.name + ": Unity의 Burst 개수 한도 초과");
        float maximum = TimingDifference(copies, merged).Max(value => Mathf.Abs(value));
        if (maximum > 1f / 60f + 0.0002f)
            throw new InvalidOperationException(merged.name + ": 생성 시각 차이가 한 프레임을 초과");
        return maximum;
    }

    static bool MatchesEmissionOrder(ParticleSystem template, Vector3[] positions, float[] times)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject host = new GameObject("EmissionOrderProbe") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(host, scene);
        try
        {
            ParticleSystem probe = host.AddComponent<ParticleSystem>();
            probe.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            EditorUtility.CopySerialized(template, probe);
            ParticleSystem.MainModule main = probe.main;
            main.startDelay = 0f;
            main.startLifetime = times[times.Length - 1] + 10f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.gravityModifier = 0f;
            main.maxParticles = positions.Length + 1;
            ParticleSystem.VelocityOverLifetimeModule velocity = probe.velocityOverLifetime;
            velocity.enabled = false;
            ParticleSystem.ForceOverLifetimeModule force = probe.forceOverLifetime;
            force.enabled = false;
            ParticleSystem.NoiseModule noise = probe.noise;
            noise.enabled = false;
            ParticleSystem.SubEmittersModule sub = probe.subEmitters;
            sub.enabled = false;
            ParticleSystem.Particle[] emitted = new ParticleSystem.Particle[positions.Length + 1];
            probe.Simulate(0f, false, true, false);
            if (times.All(time => Mathf.Abs(time) < 0.00001f))
            {
                probe.Simulate(0.01f, false, false, false);
                int count = probe.GetParticles(emitted);
                if (count != positions.Length) return false;
                List<Vector3> remaining = emitted.Take(count).Select(particle => particle.position).ToList();
                foreach (Vector3 position in positions)
                {
                    int match = remaining.FindIndex(value => Vector3.Distance(value, position) < 0.001f);
                    if (match < 0) return false;
                    remaining.RemoveAt(match);
                }
                return true;
            }
            int currentFrame = 0;
            for (int index = 0; index < times.Length; index++)
            {
                int targetFrame = Mathf.CeilToInt((times[index] + 0.01f) * 600f);
                while (currentFrame < targetFrame)
                {
                    probe.Simulate(1f / 600f, false, false, false);
                    currentFrame++;
                }
                int count = probe.GetParticles(emitted);
                if (count != index + 1 || Vector3.Distance(emitted[index].position, positions[index]) > 0.001f)
                {
                    File.WriteAllText(FantasySpellParticleComparison.ReviewDirectory + "/" + template.transform.parent.name + ".mesh-order.json",
                        JsonUtility.ToJson(new ChangeReport
                        {
                            Effect = template.name,
                            Changes = new[]
                            {
                                "index=" + index + ", time=" + times[index] + ", count=" + count,
                                "expected=" + positions[index].ToString("F6"),
                                "actual=" + (count > index ? emitted[index].position.ToString("F6") : "missing"),
                                "speed=" + template.shape.meshSpawnSpeed.curve.keys[0].value
                            }
                        }, true));
                    return false;
                }
            }
            return true;
        }
        finally
        {
            Object.DestroyImmediate(host);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Merge(Group group, PreparedEffect effect, int number)
    {
        ParticleSystem[][] copies = group.Copies.OrderBy(copy => copy[0].main.startDelay.constant).ToArray();
        ParticleSystem[] source = copies[0];
        float firstDelay = source[0].main.startDelay.constant;
        float[] times = copies.Select(copy => copy[0].main.startDelay.constant - firstDelay).ToArray();
        List<ParticleSystem> clones = new List<ParticleSystem>();
        for (int index = 0; index < source.Length; index++)
        {
            ParticleSystem original = source[index];
            GameObject host = new GameObject(Role(original.name) + "_Shared_" + number.ToString("D2"));
            host.layer = original.gameObject.layer;
            host.transform.SetParent(effect.Root.transform, false);
            int parentIndex = Array.FindIndex(source, node => node.transform == original.transform.parent);
            if (parentIndex >= 0) host.transform.SetParent(clones[parentIndex].transform, false);
            host.transform.SetPositionAndRotation(original.transform.position - source[0].transform.position + effect.Root.transform.position, original.transform.rotation);
            host.transform.localScale = original.transform.localScale;
            if (original.main.scalingMode != ParticleSystemScalingMode.Local)
            {
                Vector3 inherited = host.transform.parent.lossyScale;
                Vector3 scale = original.transform.lossyScale;
                host.transform.localScale = new Vector3(
                    Mathf.Abs(inherited.x) > 0.000001f ? scale.x / inherited.x : original.transform.localScale.x,
                    Mathf.Abs(inherited.y) > 0.000001f ? scale.y / inherited.y : original.transform.localScale.y,
                    Mathf.Abs(inherited.z) > 0.000001f ? scale.z / inherited.z : original.transform.localScale.z);
            }
            ParticleSystem clone = host.AddComponent<ParticleSystem>();
            clone.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            EditorUtility.CopySerialized(original, clone);
            EditorUtility.CopySerialized(original.GetComponent<ParticleSystemRenderer>(), clone.GetComponent<ParticleSystemRenderer>());
            ParticleSystem.MainModule main = clone.main;
            main.maxParticles = checked(main.maxParticles * copies.Length);
            clone.randomSeed = original.randomSeed;
            clone.useAutoRandomSeed = original.useAutoRandomSeed;
            clones.Add(clone);
        }
        for (int index = 0; index < source.Length; index++)
        {
            ParticleSystem.SubEmittersModule sub = clones[index].subEmitters;
            for (int childIndex = 0; childIndex < sub.subEmittersCount; childIndex++)
            {
                int target = Array.IndexOf(source, source[index].subEmitters.GetSubEmitterSystem(childIndex));
                if (target < 0) throw new InvalidOperationException("통합 그래프 외부의 하위 방출기 참조");
                sub.SetSubEmitterSystem(childIndex, clones[target]);
            }
        }
        ParticleSystem merged = clones[0];
        if (group.UseCarrier)
        {
            GameObject carrier = new GameObject(group.Name + "_Emission_" + number.ToString("D2"));
            carrier.transform.SetParent(effect.Root.transform, false);
            carrier.transform.SetPositionAndRotation(effect.Root.transform.position, source[0].transform.rotation);
            carrier.transform.localScale = source[0].transform.localScale;
            ParticleSystem sourceEmitter = merged;
            merged = carrier.AddComponent<ParticleSystem>();
            merged.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule carrierMain = merged.main;
            carrierMain.loop = false;
            carrierMain.playOnAwake = true;
            carrierMain.startDelay = firstDelay;
            carrierMain.startLifetime = source[0].main.duration;
            carrierMain.startSpeed = 0f;
            carrierMain.startSize = 0f;
            carrierMain.maxParticles = copies.Length;
            carrierMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            ParticleSystem.EmissionModule carrierEmission = merged.emission;
            carrierEmission.rateOverTime = 0f;
            merged.GetComponent<ParticleSystemRenderer>().enabled = false;
            merged.randomSeed = source[0].randomSeed;
            merged.useAutoRandomSeed = source[0].useAutoRandomSeed;
            sourceEmitter.transform.SetParent(carrier.transform, true);
            if (sourceEmitter.main.scalingMode == ParticleSystemScalingMode.Local)
                sourceEmitter.transform.localScale = source[0].transform.localScale;
            ParticleSystem.MainModule sourceMain = sourceEmitter.main;
            sourceMain.startDelay = 0f;
            ParticleSystem.SubEmittersModule carrierSub = merged.subEmitters;
            carrierSub.enabled = true;
            carrierSub.AddSubEmitter(sourceEmitter, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            clones.Add(merged);
        }
        bool coincident = copies.All(copy => Vector3.Distance(copy[0].transform.position, source[0].transform.position) < 0.000001f);
        if (coincident) merged.transform.position = source[0].transform.position;
        Vector3[] positions = copies.Select(copy => merged.transform.InverseTransformPoint(copy[0].transform.position)).ToArray();
        Mesh mesh = null;
        if (!coincident)
        {
            mesh = new Mesh
            {
                name = group.Name + "_Emission_" + number.ToString("D2"),
                vertices = positions,
                normals = Enumerable.Repeat(Vector3.forward, positions.Length).ToArray()
            };
            // Vertex 방출에서도 유효한 삼각형 인덱스를 요구하는 Unity 메시 방출기
            if (positions.Length < 3) throw new InvalidOperationException("방출 메시 구성에는 서로 다른 세 위치 이상 필요");
            mesh.SetIndices(Enumerable.Range(1, positions.Length - 2)
                .SelectMany(index => new[] { 0, index, index + 1 }).ToArray(), MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            effect.Meshes.Add(mesh);
        }
        ParticleSystem.MainModule mergedMain = merged.main;
        mergedMain.duration = Mathf.Max(mergedMain.duration, times[times.Length - 1] + 0.1f);
        ParticleSystem.EmissionModule emission = merged.emission;
        effect.MaximumTimingError = Mathf.Max(effect.MaximumTimingError, MatchTiming(copies, merged, times));
        ParticleSystem.ShapeModule shape = merged.shape;
        shape.enabled = !coincident;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = mesh;
        bool simultaneous = times.All(time => Mathf.Abs(time) < 0.00001f);
        shape.meshSpawnMode = simultaneous ? ParticleSystemShapeMultiModeValue.BurstSpread : ParticleSystemShapeMultiModeValue.Loop;
        shape.meshSpawnSpread = 0f;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        shape.scale = Vector3.one;
        shape.normalOffset = 0f;
        shape.randomDirectionAmount = 0f;
        shape.sphericalDirectionAmount = 0f;
        shape.randomPositionAmount = 0f;
        shape.alignToDirection = false;
        bool ordered = coincident;
        // Unity 버전별 Mesh Loop 진행 단위 확인 후 위치와 순서가 일치하는 설정만 채택
        foreach (float factor in new[] { 1f, 1.00001f, (float)positions.Length, positions.Length * 1.00001f })
        {
            if (coincident) break;
            shape.meshSpawnSpeed = simultaneous ? new ParticleSystem.MinMaxCurve(0f)
                : new ParticleSystem.MinMaxCurve(1f, MeshSpeed(times, mergedMain.duration, factor));
            if (!MatchesEmissionOrder(merged, positions, times)) continue;
            ordered = true;
            break;
        }
        if (!ordered) throw new InvalidOperationException(group.Name + ": Mesh Shape의 방출 위치·순서 검증 실패, 저장 중단");
        foreach (ParticleSystem[] copy in copies)
            foreach (ParticleSystem particle in copy.Reverse())
                Object.DestroyImmediate(particle.gameObject);
        effect.Changes.Add(group.Name + ": " + copies.Length + "벌 × " + source.Length + "개 → " + clones.Count + "개");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException(path + ": 폴더 생성 실패");
    }

    static void RemoveEmptyGroups(PreparedEffect effect)
    {
        HashSet<Transform> referenced = new HashSet<Transform>();
        foreach (Component component in effect.Root.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform) continue;
            using (SerializedObject serialized = new SerializedObject(component))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.objectReferenceValue is Transform transform) referenced.Add(transform);
                    else if (property.objectReferenceValue is GameObject gameObject) referenced.Add(gameObject.transform);
                }
            }
        }
        int removed = 0;
        foreach (Transform transform in effect.Root.GetComponentsInChildren<Transform>(true).Reverse())
        {
            if (transform == effect.Root.transform || referenced.Contains(transform) || transform.childCount != 0
                || transform.GetComponents<Component>().Length != 1) continue;
            Object.DestroyImmediate(transform.gameObject);
            removed++;
        }
        ParticleSystem[] steady = effect.Root.GetComponentsInChildren<ParticleSystem>(true)
            .Where(particle => particle.transform.parent == effect.Root.transform && particle.name.EndsWith("_Steady", StringComparison.Ordinal)).ToArray();
        if (steady.Length > 1)
        {
            Transform group = new GameObject("잔상 - 거리 기반 원형 유지").transform;
            group.SetParent(effect.Root.transform, false);
            foreach (ParticleSystem particle in steady) particle.transform.SetParent(group, true);
        }
        effect.Changed |= removed > 0 || steady.Length > 1;
        if (removed > 0) effect.Changes.Add("비어 있는 그룹 " + removed + "개 제거");
    }

    static long[] RootIds(string path)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Object[] objects = { root, root.transform, root.GetComponent<ParticleSystem>() };
        return objects.Select(value =>
        {
            if (value == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id))
                throw new InvalidOperationException(path + ": 외부 루트 참조 식별자 확인 실패");
            return id;
        }).ToArray();
    }

    internal static string Role(string name)
    {
        name = Regex.Replace(name, @"_(Shared|Emission)_\d+$", "");
        return Regex.Replace(name, @"_\d{4}(?=_|$)", "");
    }

    internal static PreparedEffect Prepare(string path, bool allowCarriers, bool merge)
    {
        PreparedEffect effect = new PreparedEffect
        {
            Path = path,
            ShapePath = ShapeDirectory + "/" + System.IO.Path.GetFileNameWithoutExtension(path) + "_EmissionShapes.asset",
            Root = PrefabUtility.LoadPrefabContents(path)
        };
        try
        {
            effect.Root.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (merge)
            {
                List<Group> groups = FindGroups(effect.Root);
                int number = 0;
                foreach (Group original in groups)
                {
                    if (original.UseCarrier && !allowCarriers) continue;
                    // 자원 사용을 제한하면서 같은 역할의 방출 묶음을 유지하는 분할
                    for (int offset = 0; offset < original.Copies.Count; offset += 128)
                    {
                        Group group = new Group { Name = original.Name, UseCarrier = original.UseCarrier };
                        group.Copies.AddRange(original.Copies.Skip(offset).Take(128));
                        if (group.Copies.Count < 3) continue;
                        HashSet<int> before = new HashSet<int>(effect.Root.GetComponentsInChildren<Transform>(true).Select(value => value.GetInstanceID()));
                        int meshStart = effect.Meshes.Count;
                        float timingBefore = effect.MaximumTimingError;
                        try
                        {
                            Merge(group, effect, number++);
                            effect.MergedGroups++;
                            effect.Changed = true;
                        }
                        catch (Exception exception)
                        {
                            foreach (Transform transform in effect.Root.GetComponentsInChildren<Transform>(true).Reverse())
                                if (transform != null && !before.Contains(transform.GetInstanceID())) Object.DestroyImmediate(transform.gameObject);
                            foreach (Mesh mesh in effect.Meshes.Skip(meshStart))
                                if (mesh != null) Object.DestroyImmediate(mesh);
                            effect.Meshes.RemoveRange(meshStart, effect.Meshes.Count - meshStart);
                            effect.MaximumTimingError = timingBefore;
                            effect.KeptGroups.Add(group.Name + " (" + group.Copies.Count + "): " + exception.Message);
                        }
                    }
                }
            }
            RemoveEmptyGroups(effect);
            effect.KeptGroups.AddRange(effect.Root.GetComponentsInChildren<ParticleSystem>(true)
                .Where(particle => !Regex.IsMatch(particle.name, @"_(Shared|Emission)_\d+$"))
                .GroupBy(particle => Role(particle.name)).Where(group => group.Count() > 1)
                .Select(group => group.Key + ": " + group.Count() + "개 원형 유지 (서로 다른 단계·방향, 거리 기반 또는 비활성 요소)"));
            return effect;
        }
        catch
        {
            Release(effect);
            throw;
        }
    }

    internal static void Commit(PreparedEffect effect, string backupPath)
    {
        if (File.Exists(effect.ShapePath)) throw new InvalidOperationException("기존 Shape 에셋 덮어쓰기 중단: " + effect.ShapePath);
        if (FantasySpellParticleComparison.SourceHash(effect.Path) != FantasySpellParticleComparison.SourceHash(backupPath))
            throw new InvalidOperationException("백업 이후 외부 변경 확인: " + effect.Path);
        string guid = AssetDatabase.AssetPathToGUID(effect.Path);
        long[] ids = RootIds(effect.Path);
        bool createdShape = false;
        try
        {
            if (effect.Meshes.Count > 0)
            {
                EnsureFolder(ShapeDirectory);
                AssetDatabase.CreateAsset(effect.Meshes[0], effect.ShapePath);
                createdShape = true;
                foreach (Mesh mesh in effect.Meshes.Skip(1)) AssetDatabase.AddObjectToAsset(mesh, effect.ShapePath);
                AssetDatabase.SaveAssetIfDirty(effect.Meshes[0]);
            }
            effect.Root.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            PrefabUtility.SaveAsPrefabAsset(effect.Root, effect.Path, out bool success);
            if (!success) throw new IOException("프리팹 저장 실패: " + effect.Path);
            AssetDatabase.ImportAsset(effect.Path, ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.AssetPathToGUID(effect.Path) != guid || !ids.SequenceEqual(RootIds(effect.Path)))
                throw new InvalidOperationException("GUID 또는 외부 루트 참조 식별자 변경 확인");
        }
        catch
        {
            File.Copy(backupPath, effect.Path, true);
            File.Copy(backupPath + ".meta", effect.Path + ".meta", true);
            AssetDatabase.ImportAsset(effect.Path, ImportAssetOptions.ForceUpdate);
            if (createdShape) AssetDatabase.DeleteAsset(effect.ShapePath);
            throw;
        }
    }

    internal static void Release(PreparedEffect effect)
    {
        if (effect == null) return;
        if (effect.Root != null) PrefabUtility.UnloadPrefabContents(effect.Root);
        foreach (Mesh mesh in effect.Meshes)
            if (mesh != null && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
    }
}
