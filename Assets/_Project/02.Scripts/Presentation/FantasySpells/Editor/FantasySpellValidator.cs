using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>생성된 스펠의 참조, 시간 계산, 대표 재생 검사</summary>
public static class FantasySpellValidator
{
    [Serializable]
    sealed class Validation
    {
        public int CheckedPrefabs;
        public int SimulatedPrefabs;
        public string[] Failures;
        public string[] ShaderWarnings;
        public string[] PreviewImages;
        public bool FormulaChecks;
    }

    /// <summary>생성물 전체의 구조 검사와 대표 스펠 재생</summary>
    [MenuItem("Tools/VFX/Fantasy Spells/생성 결과 검증")]
    public static void ValidateAll()
    {
        Validation result = new Validation();
        List<string> failures = new List<string>();
        HashSet<string> warnings = new HashSet<string>();
        List<string> images = new List<string>();
        try { CheckFormula(); result.FormulaChecks = true; }
        catch (Exception exception) { failures.Add(exception.ToString()); }
        string[] paths = Directory.GetFiles(FantasySpellBaker.Output, "*.prefab").OrderBy(path => path).ToArray();
        foreach (string file in paths)
        {
            string path = file.Replace('\\', '/');
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                BakedSpellPlayer player = prefab.GetComponent<BakedSpellPlayer>();
                if (player == null || player.Definition == null || prefab.GetComponent<ParticleSystem>() == null)
                    throw new InvalidOperationException("루트 재생 참조 누락");
                MonoBehaviour[] scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                if (scripts.Length != 1 || scripts[0] != player) throw new InvalidOperationException("원본 생성기 또는 Missing Script 잔존");
                ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
                if (particles.Length < player.Definition.Particles.Length + 1) throw new InvalidOperationException("파티클 바인딩 수 불일치");
                foreach (SpellParticleTiming timing in player.Definition.Particles)
                {
                    if (float.IsNaN(timing.End) || timing.StartMin > timing.StartMax || timing.StartMin < timing.Birth - 0.001f)
                        throw new InvalidOperationException("잘못된 타이밍 값");
                }
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        if (material.shader == null || !material.shader.isSupported)
                            warnings.Add(AssetDatabase.GetAssetPath(material) + ": 현재 렌더 파이프라인에서 셰이더 지원 확인 필요");
                    }
                }
                result.CheckedPrefabs++;
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Effect_01_", StringComparison.Ordinal) || name.StartsWith("Effect_05_", StringComparison.Ordinal) || name == "Effect_70_OrbitalStrike")
                {
                    SimulateAndCapture(prefab, name, images);
                    result.SimulatedPrefabs++;
                }
            }
            catch (Exception exception)
            {
                failures.Add(path + ": " + exception);
            }
        }
        result.Failures = failures.ToArray();
        result.ShaderWarnings = warnings.OrderBy(warning => warning).ToArray();
        result.PreviewImages = images.ToArray();
        File.WriteAllText(FantasySpellBaker.Output + "/_Reports/Validation.json", JsonUtility.ToJson(result, true));
        AssetDatabase.Refresh();
        if (paths.Length != 90 || failures.Count > 0) throw new InvalidOperationException($"검증 불일치: 파일 {paths.Length}개, 실패 {failures.Count}개");
        Debug.Log($"Fantasy Spells: {result.CheckedPrefabs}개 참조 검사, 대표 {result.SimulatedPrefabs}개 재생 검사 완료");
    }

    static void CheckFormula()
    {
        foreach (float delay in new[] { 0f, 0.123f, 1f, 1.5f })
        {
            float submit = 0f;
            float cutout = 0f;
            const float factor = 0.5f;
            for (int frame = 1; frame <= 600; frame++)
            {
                float time = frame * BakedSpellDefinition.StepSeconds;
                if (time > delay + 0.000001f)
                {
                    cutout = Mathf.Clamp01(cutout + submit);
                    submit = Mathf.Lerp(submit, factor, BakedSpellDefinition.StepSeconds / 50f);
                }
                float calculated = SpellMotionEvaluator.Dissolve(time, delay, factor);
                if (Mathf.Abs(calculated - cutout) > 0.0002f)
                    throw new InvalidOperationException($"재질 소멸 계산 불일치: delay={delay}, frame={frame}, {calculated}/{cutout}");
            }
        }
        AnimationCurve overshoot = new AnimationCurve(new Keyframe(0f, 0f, 0f, 4f), new Keyframe(1f, 0f, -4f, 0f));
        Vector2 range = FantasySpellBaker.CurveRange(new ParticleSystem.MinMaxCurve(1f, overshoot));
        if (Mathf.Abs(range.y - 1f) > 0.0001f) throw new InvalidOperationException("Hermite 수명 극값 계산 불일치");
    }

    static void SimulateAndCapture(GameObject prefab, string name, List<string> images)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab);
            preview.AddSingleGO(instance);
            BakedSpellPlayer player = instance.GetComponent<BakedSpellPlayer>();
            player.Restart(true);
            float time = name.StartsWith("Effect_01_", StringComparison.Ordinal) ? 0.7f : name.StartsWith("Effect_05_", StringComparison.Ordinal) ? 0.6f : 1.5f;
            player.PreviewAt(time);
            Vector3[] firstPose = instance.GetComponentsInChildren<Transform>(true).Select(transform => transform.localPosition).ToArray();
            int firstCount = instance.GetComponentsInChildren<ParticleSystem>(true).Sum(particle => particle.particleCount);
            if (firstCount <= 1) throw new InvalidOperationException("대표 시점에 보이는 파티클 없음");
            player.Restart(true);
            player.PreviewAt(time);
            Vector3[] secondPose = instance.GetComponentsInChildren<Transform>(true).Select(transform => transform.localPosition).ToArray();
            int secondCount = instance.GetComponentsInChildren<ParticleSystem>(true).Sum(particle => particle.particleCount);
            if (firstCount != secondCount || firstPose.Where((position, index) => Vector3.Distance(position, secondPose[index]) > 0.0001f).Any())
                throw new InvalidOperationException("재시작 결과가 결정적이지 않음");
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && renderer.bounds.size.sqrMagnitude < 1000000f) bounds.Encapsulate(renderer.bounds);
            preview.cameraFieldOfView = 35f;
            preview.camera.nearClipPlane = 0.05f;
            preview.camera.farClipPlane = 2000f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.065f, 0.075f, 0.095f);
            preview.ambientColor = Color.gray;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            Quaternion rotation = Quaternion.Euler(25f, 35f, 0f);
            preview.camera.transform.SetPositionAndRotation(bounds.center + rotation * Vector3.back * Mathf.Max(8f, bounds.extents.magnitude * 2.7f), rotation);
            preview.BeginStaticPreview(new Rect(0f, 0f, 960f, 640f));
            preview.Render(true);
            Texture2D image = preview.EndStaticPreview();
            string path = FantasySpellBaker.Output + "/_Reports/" + name + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            images.Add(path);
            if (!player.Definition.IsPersistent)
            {
                player.PreviewAt(player.Definition.EndSeconds + 0.2f);
                if (instance.GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.particleCount > 0))
                    throw new InvalidOperationException("계산된 종료 이후 파티클 잔존");
            }
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            preview.Cleanup();
        }
    }
}
