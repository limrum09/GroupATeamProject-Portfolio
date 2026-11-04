using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 편집 모드 탄환 미리보기의 이동·파티클 재생 및 임시 객체 수명 관리
/// </summary>
public class ProjectilePreviewSession : IDisposable
{
    public const string ScenePath = "Assets/_Project/01.Scene/TestScenes/Temp_Skill_Test_Scene.unity";
    public const string PrefabFolder = "Assets/Hovl Studio/AAA Projectiles Vol 2/Prefabs";
    public const int MaximumActiveShots = 24;

    const float SimulationStep = 1f / 60f;
    const float MaximumUpdateDelta = 0.1f;

    class PreviewShot
    {
        public GameObject Instance;
        public ParticleSystem[] Roots;
        public float Age;
    }

    readonly List<PreviewShot> _shots = new List<PreviewShot>();

    GameObject _prefab;
    Scene _scene;
    Vector3 _position;
    Quaternion _rotation;
    float _speed;
    float _duration;
    float _repeatInterval;

    double _lastUpdate;
    float _untilNextShot;
    bool _isRepeating;
    bool _disposed;
    string _error;

    public event Action OnChanged;

    public int ActiveCount => _shots.Count;
    public bool IsRepeating => _isRepeating;
    public string Error => _error;

    /// <summary>
    /// 창 수명에 한정된 에디터 갱신과 정리 이벤트 연결
    /// </summary>
    public ProjectilePreviewSession()
    {
        _lastUpdate = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += Dispose;
        AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        EditorSceneManager.sceneClosing += OnSceneClosing;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float remaining = Mathf.Clamp((float)(now - _lastUpdate), 0f, MaximumUpdateDelta);
        _lastUpdate = now;
        if (_disposed || (_shots.Count == 0 && !_isRepeating)) return;

        if (GetContextError() != null || !_scene.IsValid() || !_scene.isLoaded ||
            SceneManager.GetActiveScene() != _scene)
        {
            Clear();
            return;
        }

        try
        {
            int previousCount = _shots.Count;
            // 에디터 지연 후 몰아서 발사하지 않고 짧은 이동 간격으로 잔상 갱신
            while (remaining > 0f)
            {
                float delta = Mathf.Min(SimulationStep, remaining);
                AdvanceShots(delta);
                if (_isRepeating)
                {
                    _untilNextShot -= delta;
                    if (_untilNextShot <= 0f)
                    {
                        Spawn();
                        _untilNextShot += _repeatInterval;
                    }
                }
                remaining -= delta;
            }

            SceneView.RepaintAll();
            if (_shots.Count != previousCount) OnChanged?.Invoke();
        }
        catch (Exception exception)
        {
            FailPreview(exception);
        }
    }

    void AdvanceShots(float delta)
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            PreviewShot shot = _shots[i];
            if (shot.Instance == null)
            {
                _shots.RemoveAt(i);
                continue;
            }

            float step = Mathf.Min(delta, _duration - shot.Age);
            shot.Age += step;
            shot.Instance.transform.position = _position + _rotation * Vector3.forward * (_speed * shot.Age);
            foreach (ParticleSystem root in shot.Roots)
            {
                root.Simulate(step, true, false, false);
            }

            if (shot.Age >= _duration)
            {
                Object.DestroyImmediate(shot.Instance);
                _shots.RemoveAt(i);
            }
        }
    }

    void Spawn()
    {
        if (_shots.Count == MaximumActiveShots)
        {
            Object.DestroyImmediate(_shots[0].Instance);
            _shots.RemoveAt(0);
        }

        GameObject instance = Object.Instantiate(_prefab, _position, _rotation);
        // 초기화 도중 실패해도 전체 정리 대상에 포함
        PreviewShot shot = new PreviewShot { Instance = instance };
        _shots.Add(shot);
        instance.name = "[Projectile Preview] " + _prefab.name;
        if (instance.scene != _scene) SceneManager.MoveGameObjectToScene(instance, _scene);

        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        List<ParticleSystem> roots = new List<ParticleSystem>();
        foreach (ParticleSystem particle in particles)
        {
            ParticleSystem.MainModule main = particle.main;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            // 자식 파티클의 중복 시뮬레이션 방지
            bool hasParticleParent = false;
            for (Transform parent = particle.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<ParticleSystem>() != null)
                {
                    hasParticleParent = true;
                    break;
                }
            }

            if (!hasParticleParent && particle.gameObject.activeInHierarchy)
                roots.Add(particle);
        }

        shot.Roots = roots.ToArray();
        foreach (ParticleSystem root in shot.Roots)
        {
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.Simulate(0f, true, true, false);
        }
    }

    string GetSettingsError(GameObject prefab, Vector3 position, Vector3 eulerAngles,
        float speed, float duration, float repeatInterval)
    {
        if (prefab == null) return "미리보기할 탄환 프리팹을 선택하세요.";
        string path = AssetDatabase.GetAssetPath(prefab);
        if (!path.StartsWith(PrefabFolder + "/Projectile ", StringComparison.Ordinal) ||
            !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
            !AssetDatabase.IsMainAsset(prefab))
            return "AAA Projectiles Vol 2의 Prefabs 폴더에 있는 Projectile 프리팹만 지원합니다.";

        if (!IsFinite(position) || !IsFinite(eulerAngles))
            return "발사 위치와 회전에는 유한한 숫자가 필요합니다.";
        if (!IsFinite(speed) || speed <= 0f || speed > 500f)
            return "이동 속도는 0 초과, 500 이하로 설정하세요.";
        if (!IsFinite(duration) || duration < 0.1f || duration > 30f)
            return "최대 재생 시간은 0.1~30초로 설정하세요.";
        if (!IsFinite(repeatInterval) || repeatInterval < 0.1f || repeatInterval > 10f)
            return "반복 발사 간격은 0.1~10초로 설정하세요.";
        if (!prefab.activeSelf || prefab.GetComponentsInChildren<ParticleSystem>().Length == 0)
            return "활성 상태의 ParticleSystem을 포함한 탄환 프리팹이 필요합니다.";

        // 외부 스크립트와 물리·애니메이션 동작을 편집 모드에서 실행하지 않는 시각 전용 계약
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
        {
            if (component == null) return "선택한 프리팹에 Missing Script가 있습니다.";
            if (!(component is Transform || component is ParticleSystem || component is ParticleSystemRenderer ||
                  component is MeshRenderer || component is MeshFilter || component is Light))
                return "이 미리보기에서 지원하지 않는 컴포넌트: " + component.GetType().Name;
        }

        return null;
    }

    bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    void FailPreview(Exception exception)
    {
        // 오류가 난 미리보기만 폐기하고 창에 원인 표시, 원본 예외는 Console에 유지
        Clear();
        _error = "미리보기를 중단했습니다: " + exception.Message;
        Debug.LogException(exception);
        OnChanged?.Invoke();
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        Clear();
    }

    void OnSceneClosing(Scene scene, bool removingScene)
    {
        if (scene == _scene) Clear();
    }

    void OnSceneSaving(Scene scene, string path)
    {
        if (scene == _scene) Clear();
    }

    void OnActiveSceneChanged(Scene previous, Scene next)
    {
        Clear();
    }

    /// <summary>
    /// 현재 편집 컨텍스트의 미리보기 제한 사유 조회
    /// </summary>
    /// <returns>제한 사유, 실행 가능하면 null</returns>
    public static string GetContextError()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return "Play Mode와 컴파일이 끝난 뒤 편집 모드에서 사용하세요.";
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            return "Prefab Mode를 닫고 테스트 씬으로 돌아오세요.";

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            return "Temp_Skill_Test_Scene을 열고 활성 씬으로 지정하세요.";
        return null;
    }

    /// <summary>
    /// 기존 미리보기 정리 후 지정된 발사 조건으로 새 재생 시작
    /// </summary>
    /// <param name="prefab">원본 시각 전용 탄환 프리팹</param>
    /// <param name="position">월드 발사 위치</param>
    /// <param name="eulerAngles">정면 +Z 기준 월드 회전</param>
    /// <param name="speed">초당 이동 거리</param>
    /// <param name="duration">각 탄환의 최대 재생 시간</param>
    /// <param name="repeatInterval">반복 발사 간격</param>
    /// <param name="repeat">반복 발사 여부</param>
    public void Play(GameObject prefab, Vector3 position, Vector3 eulerAngles,
        float speed, float duration, float repeatInterval, bool repeat)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProjectilePreviewSession));
        Clear();
        _error = GetContextError() ?? GetSettingsError(prefab, position, eulerAngles, speed, duration, repeatInterval);
        if (_error != null)
        {
            OnChanged?.Invoke();
            return;
        }

        _scene = SceneManager.GetActiveScene();
        _prefab = prefab;
        _position = position;
        _rotation = Quaternion.Euler(eulerAngles);
        _speed = speed;
        _duration = duration;
        _repeatInterval = repeatInterval;
        _untilNextShot = repeatInterval;
        _isRepeating = repeat;
        _lastUpdate = EditorApplication.timeSinceStartup;

        try
        {
            Spawn();
            SceneView.RepaintAll();
            OnChanged?.Invoke();
        }
        catch (Exception exception)
        {
            FailPreview(exception);
        }
    }

    /// <summary>
    /// 이미 발사된 탄환을 유지하며 반복 발사 중지
    /// </summary>
    public void StopRepeating()
    {
        _isRepeating = false;
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 현재 세션이 소유한 임시 탄환과 오류 상태 정리
    /// </summary>
    public void Clear()
    {
        _isRepeating = false;
        foreach (PreviewShot shot in _shots)
        {
            if (shot.Instance != null) Object.DestroyImmediate(shot.Instance);
        }
        _shots.Clear();
        _prefab = null;
        _scene = default;
        _error = null;
        SceneView.RepaintAll();
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 에디터 이벤트 구독 해제와 모든 임시 객체 정리
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.quitting -= Dispose;
        AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
        EditorSceneManager.sceneClosing -= OnSceneClosing;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
        Clear();
        OnChanged = null;
    }
}
