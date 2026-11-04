using System;
using UnityEngine;

/// <summary>
/// 전투 숫자 표시 대상
/// </summary>
public enum CombatTextTarget
{
    Everyone,
    PlayerOnly,
    EnemyOnly
}

/// <summary>
/// 로컬 옵션과 화면 편집본 데이터
/// </summary>
[Serializable]
public class SettingsData
{
    public int Version = 1;

    internal const float MinCursorScale = 0.75f;
    internal const float MaxCursorScale = 2f;

    public float CursorScale = 1f;
    public float CursorHue;
    public CombatTextTarget CombatText = CombatTextTarget.Everyone;
    public float MasterVolume = 1f;
    public float MusicVolume = 1f;
    public float UIVolume = 1f;
    public float EffectsVolume = 1f;
    public float AmbientVolume = 1f;
    public int ResolutionWidth = 1920;
    public int ResolutionHeight = 1080;
    public FullScreenMode ScreenMode = FullScreenMode.ExclusiveFullScreen;
    public int FrameLimit = -1;

    internal bool HasSameGameplay(SettingsData other)
    {
        return CursorScale == other.CursorScale && CursorHue == other.CursorHue &&
            CombatText == other.CombatText;
    }

    internal bool HasSameAudio(SettingsData other)
    {
        return MasterVolume == other.MasterVolume && MusicVolume == other.MusicVolume &&
            UIVolume == other.UIVolume && EffectsVolume == other.EffectsVolume &&
            AmbientVolume == other.AmbientVolume;
    }

    internal bool HasSameGraphics(SettingsData other)
    {
        return ResolutionWidth == other.ResolutionWidth &&
            ResolutionHeight == other.ResolutionHeight &&
            ScreenMode == other.ScreenMode && FrameLimit == other.FrameLimit;
    }
    public string[] BindingPaths = new string[9];

    /// <summary>
    /// 현재 옵션의 독립 복사본 생성
    /// </summary>
    /// <returns>옵션 복사본</returns>
    public SettingsData Copy()
    {
        return new SettingsData
        {
            Version = Version,
            CursorScale = CursorScale,
            CursorHue = CursorHue,
            CombatText = CombatText,
            MasterVolume = MasterVolume,
            MusicVolume = MusicVolume,
            UIVolume = UIVolume,
            EffectsVolume = EffectsVolume,
            AmbientVolume = AmbientVolume,
            ResolutionWidth = ResolutionWidth,
            ResolutionHeight = ResolutionHeight,
            ScreenMode = ScreenMode,
            FrameLimit = FrameLimit,
            BindingPaths = BindingPaths == null ? null : (string[])BindingPaths.Clone()
        };
    }
}
