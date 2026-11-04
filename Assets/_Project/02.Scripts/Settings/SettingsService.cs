using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 앱 수명의 옵션 검증과 적용 상태 관리
/// </summary>
public class SettingsService : Singleton<SettingsService>
{
    internal static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow,
        FullScreenMode.Windowed
    };

    internal static readonly int[] FrameLimits = { -1, 30, 60, 120 };

    static readonly Vector2Int[] _windowResolutions =
    {
        new Vector2Int(1920, 1080), new Vector2Int(1600, 900),
        new Vector2Int(1280, 720), new Vector2Int(1024, 768),
        new Vector2Int(800, 600), new Vector2Int(640, 480)
    };

    [SerializeField] AudioMixer _audioMixer;
    [SerializeField] GameplayCursor _gameplayCursor;

    bool _mixerReady;
    LocalSettingsStore _store;
    SettingsData _current;
    bool _loadAttempted;

    public CombatTextTarget CombatText => _current?.CombatText ?? CombatTextTarget.Everyone;

    public bool IsReady => _current != null;
    public string ErrorMessage { get; private set; }

    static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException || exception is InvalidDataException ||
            exception is UnauthorizedAccessException || exception is ArgumentException ||
            exception is NotSupportedException;
    }

    static string Validate(SettingsData data)
    {
        if (data.Version != 1)
            return "지원하지 않거나 누락된 옵션 파일 버전입니다.";

        if (!IsValidVolume(data.MasterVolume) || !IsValidVolume(data.MusicVolume) ||
            !IsValidVolume(data.UIVolume) || !IsValidVolume(data.EffectsVolume) ||
            !IsValidVolume(data.AmbientVolume))
            return "음량은 0부터 1 사이여야 합니다.";

        if (data.ResolutionWidth < 640 || data.ResolutionHeight < 480 ||
            Array.IndexOf(ScreenModes, data.ScreenMode) < 0 ||
            Array.IndexOf(FrameLimits, data.FrameLimit) < 0)
            return "지원하지 않는 그래픽 설정입니다.";

        if (!(data.CursorScale >= SettingsData.MinCursorScale &&
            data.CursorScale <= SettingsData.MaxCursorScale) || !IsValidVolume(data.CursorHue))
            return "지원하지 않는 커서 설정입니다.";

        if (!Enum.IsDefined(typeof(CombatTextTarget), data.CombatText))
            return "지원하지 않는 전투 숫자 표시 대상입니다.";

        return SettingsBindings.Validate(data.BindingPaths);
    }

    internal static List<Vector2Int> GetResolutions(FullScreenMode mode)
    {
        if (mode != FullScreenMode.ExclusiveFullScreen)
            return new List<Vector2Int>(_windowResolutions);

        HashSet<Vector2Int> sizes = new HashSet<Vector2Int>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            if (resolution.width >= 640 && resolution.height >= 480)
                sizes.Add(new Vector2Int(resolution.width, resolution.height));
        }

        if (sizes.Count == 0)
            return new List<Vector2Int>(_windowResolutions);

        List<Vector2Int> options = new List<Vector2Int>(sizes);
        options.Sort((left, right) =>
        {
            int widthOrder = right.x.CompareTo(left.x);
            return widthOrder != 0 ? widthOrder : right.y.CompareTo(left.y);
        });
        return options;
    }

    internal static Vector2Int ResolveResolution(SettingsData data, List<Vector2Int> options)
    {
        Vector2Int requested = new Vector2Int(data.ResolutionWidth, data.ResolutionHeight);
        Vector2Int best = options[0];
        double distance = double.MaxValue;

        foreach (Vector2Int option in options)
        {
            double dx = (double)option.x - requested.x;
            double dy = (double)option.y - requested.y;
            double candidate = dx * dx + dy * dy;
            if (candidate >= distance) continue;

            distance = candidate;
            best = option;
        }

        return best;
    }

    void ApplyGraphics(SettingsData data)
    {
        if (_current == null || data.ScreenMode != _current.ScreenMode ||
            data.ResolutionWidth != _current.ResolutionWidth ||
            data.ResolutionHeight != _current.ResolutionHeight)
        {
            Vector2Int resolution = ResolveResolution(data, GetResolutions(data.ScreenMode));
            Screen.SetResolution(resolution.x, resolution.y, data.ScreenMode);
        }

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = data.FrameLimit;
    }

    static bool IsValidVolume(float value)
    {
        return value >= 0f && value <= 1f;
    }

    void Start()
    {
        // AudioMixer 파라미터 적용 가능 시점까지 초기 로드값 보관
        _mixerReady = true;
        if (_current != null)
            ApplyMixerVolumes(_current);
    }

    void ApplyMixerVolumes(SettingsData data)
    {
        SetMixerVolume("MusicVolume", data.MusicVolume);
        SetMixerVolume("UIVolume", data.UIVolume);
        SetMixerVolume("EffectsVolume", data.EffectsVolume);
        SetMixerVolume("AmbientVolume", data.AmbientVolume);
    }

    void SetMixerVolume(string parameter, float value)
    {
        float decibels = 20f * Mathf.Log10(Mathf.Max(value, 0.0001f));
        if (!_audioMixer.SetFloat(parameter, decibels))
            Debug.LogError($"AudioMixer 노출 파라미터 연결 필요: {parameter}", this);
    }

    void Apply(SettingsData data)
    {
        SettingsBindings.Apply(data.BindingPaths);
        AudioListener.volume = data.MasterVolume;
        if (_mixerReady)
            ApplyMixerVolumes(data);
        ApplyGraphics(data);
        _gameplayCursor.Apply(data.CursorScale, data.CursorHue);
        _current = data;
    }

    /// <summary>
    /// 로컬 옵션 저장 경로 구성
    /// </summary>
    protected override void OnSingletonAwake()
    {
        if (_gameplayCursor == null)
            throw new MissingReferenceException("커서 표시 담당자 연결 필요");

        if (_audioMixer == null)
            throw new MissingReferenceException("설정 AudioMixer 연결 필요");

        _store = new LocalSettingsStore(Path.Combine(
            Application.persistentDataPath, "settings.json"));
    }

    /// <summary>
    /// 최초 읽기와 현재 옵션 적용
    /// </summary>
    public void Initialize()
    {
        if (_loadAttempted) return;
        _loadAttempted = true;

        SettingsData data;
        try
        {
            data = _store.Load() ?? new SettingsData();
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            ErrorMessage = $"옵션을 불러오지 못했습니다.\n{exception.Message}";
            return;
        }

        ErrorMessage = Validate(data);
        if (ErrorMessage == null)
            Apply(data);
    }

    /// <summary>
    /// 현재 적용값의 화면 편집본 생성
    /// </summary>
    /// <returns>현재 적용값의 독립 복사본</returns>
    public SettingsData CreateDraft()
    {
        if (!IsReady) throw new InvalidOperationException("옵션 읽기 완료 필요");
        return _current.Copy();
    }

    /// <summary>
    /// 검증과 저장 성공 후 편집 옵션 적용
    /// </summary>
    /// <param name="draft">적용할 편집본</param>
    /// <param name="error">실패 안내</param>
    /// <returns>저장과 적용 성공 여부</returns>
    public bool TryApply(SettingsData draft, out string error)
    {
        if (draft == null) throw new ArgumentNullException(nameof(draft));

        if (!IsReady)
        {
            error = ErrorMessage;
            return false;
        }

        error = Validate(draft);
        if (error != null) return false;

        SettingsData candidate = draft.Copy();
        try
        {
            _store.Save(candidate);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            error = $"옵션을 저장하지 못했습니다.\n{exception.Message}";
            return false;
        }

        Apply(candidate);
        return true;
    }
}
