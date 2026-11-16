using UnityEngine;

/// <summary>
/// 반딧불의 공통 색상, 크기 및 발광 주기 설정
/// </summary>
[CreateAssetMenu(menuName = "Project/Presentation/Firefly Profile")]
public class FireflyProfile : ScriptableObject
{
    [Header("----- 빛점 표현 -----")]
    [SerializeField, ColorUsage(false, true)] Color _color = new Color(0.65f, 1f, 0.2f);
    [SerializeField] Vector2 _size = new Vector2(0.04f, 0.07f);
    [SerializeField] Vector2 _peakIntensity = new Vector2(2f, 5f);

    [Header("----- 깜빡임 시간 (초) -----")]
    [SerializeField] Vector2 _darkDuration = new Vector2(1f, 3f);
    [SerializeField] Vector2 _fadeInDuration = new Vector2(0.5f, 1f);
    [SerializeField] Vector2 _brightDuration = new Vector2(0.2f, 0.8f);
    [SerializeField] Vector2 _fadeOutDuration = new Vector2(0.5f, 1f);

    public Color Color => _color;
    public Vector2 Size => _size;
    public Vector2 PeakIntensity => _peakIntensity;
    public Vector2 DarkDuration => _darkDuration;
    public Vector2 FadeInDuration => _fadeInDuration;
    public Vector2 BrightDuration => _brightDuration;
    public Vector2 FadeOutDuration => _fadeOutDuration;
}
