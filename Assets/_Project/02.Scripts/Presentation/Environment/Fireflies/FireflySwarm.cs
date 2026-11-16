using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영역 수명의 반딧불 생성, 활성 시간 진행 및 자원 정리
/// </summary>
[DisallowMultipleComponent]
public class FireflySwarm : MonoBehaviour
{
    [Header("----- 공통 연결 -----")]
    [SerializeField] FireflyProfile _profile;
    [SerializeField] FireflyGlow _prefab;
    [SerializeField, InspectorName("재현 시드")] int _seed = 1729;

    [Header("----- 경로별 설정 -----")]
    [SerializeField] List<FireflyPathSettings> _paths = new List<FireflyPathSettings>();

    readonly List<ActivePath> _activePaths = new List<ActivePath>();
    bool _initialized;

    class ActivePath
    {
        public FireflySplinePath Path;
        public FireflyPathMotion Motion;
        public FireflyGlow[] Instances;
    }

    public FireflyProfile Profile => _profile;
    public FireflyGlow Prefab => _prefab;
    public IReadOnlyList<FireflyPathSettings> Paths => _paths;

    void Start()
    {
        if (!FireflyValidation.Validate(this, out string error))
        {
            enabled = false;
            throw new InvalidOperationException($"{name}: {error}");
        }

        // 모든 설정 검증 후 생성 시작, 부분 생성 실패 시 보유 자원까지 정리
        try
        {
            for (int i = 0; i < _paths.Count; i++)
            {
                FireflyPathSettings settings = _paths[i];
                if (settings.Count == 0) continue;
                System.Random random = new System.Random(unchecked(_seed + i * 486187739));
                ActivePath active = new ActivePath();
                _activePaths.Add(active);
                active.Path = new FireflySplinePath(settings);
                active.Motion = new FireflyPathMotion(settings, active.Path, random);
                active.Instances = new FireflyGlow[settings.Count];
                for (int j = 0; j < settings.Count; j++)
                {
                    FireflyGlow instance = Instantiate(_prefab, transform);
                    active.Instances[j] = instance;
                    instance.name = $"Firefly_{i + 1}_{j + 1}";
                    instance.Initialize(_profile, random.Next());
                    instance.SetPosition(active.Motion.GetPosition(j));
                }
            }
            _initialized = true;
        }
        catch
        {
            Release();
            enabled = false;
            throw;
        }
    }

    void OnEnable()
    {
        SetVisible(true);
    }

    void OnDisable()
    {
        // 생성된 개체와 네이티브 경로는 유지하고 표시와 시간만 중단
        SetVisible(false);
    }

    void Update()
    {
        if (!_initialized) return;
        float deltaTime = Time.deltaTime;
        foreach (ActivePath active in _activePaths)
        {
            active.Motion.Tick(deltaTime);
            for (int i = 0; i < active.Instances.Length; i++)
            {
                FireflyGlow instance = active.Instances[i];
                instance.SetPosition(active.Motion.GetPosition(i));
                instance.Tick(deltaTime);
            }
        }
    }

    void OnDestroy()
    {
        Release();
    }

    void SetVisible(bool visible)
    {
        foreach (ActivePath active in _activePaths)
        {
            if (active.Instances == null) continue;
            foreach (FireflyGlow instance in active.Instances)
            {
                // 씬 제거 중 먼저 파괴된 자식은 제외
                if (instance != null) instance.gameObject.SetActive(visible);
            }
        }
    }

    void Release()
    {
        foreach (ActivePath active in _activePaths)
        {
            if (active.Instances != null)
            {
                foreach (FireflyGlow instance in active.Instances)
                {
                    if (instance != null) Destroy(instance.gameObject);
                }
            }
            if (active.Path != null) active.Path.Dispose();
        }
        _activePaths.Clear();
        _initialized = false;
    }
}
