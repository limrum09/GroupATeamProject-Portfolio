using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 커서 설정과 왼쪽 버튼 상태를 반영하는 커서 표시
/// </summary>
[DisallowMultipleComponent]
public class GameplayCursor : MonoBehaviour
{
    [SerializeField] Texture2D _source;

    const float PressedBrightnessMultiplier = 1.35f;

    const float GlowOpacity = 0.58f;

    Texture2D _normalTexture;
    Texture2D _pressedTexture;
    bool _isPressed;
    float _scale;
    float _hue;

    void Update()
    {
        RefreshPressState(Application.isFocused);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        RefreshPressState(hasFocus);
    }

    void RefreshPressState(bool hasFocus)
    {
        if (_normalTexture == null) return;

        Mouse mouse = Mouse.current;
        bool isPressed = hasFocus && mouse != null && mouse.leftButton.isPressed;
        if (_isPressed == isPressed) return;

        ShowCursor(isPressed);
    }

    void ShowCursor(bool isPressed)
    {
        Texture2D texture = isPressed ? _pressedTexture : _normalTexture;
        Cursor.SetCursor(texture, Vector2.zero, CursorMode.ForceSoftware);
        _isPressed = isPressed;
    }

    static Color32[] CreatePressedPixels(Color32[] normalPixels, int width, int height, int radius)
    {
        Color32[] pressedPixels = new Color32[normalPixels.Length];
        for (int index = 0; index < normalPixels.Length; index++)
        {
            Color normalColor = normalPixels[index];
            Color.RGBToHSV(normalColor, out float hue, out float saturation, out float value);
            Color brighterColor = Color.HSVToRGB(hue, saturation,
                Mathf.Min(1f, value * PressedBrightnessMultiplier));
            brighterColor.a = normalColor.a;

            if (normalColor.a < 1f)
            {
                // 커서 뒤쪽에 흰색 후광 합성
                float glowAlpha = GetGlowAlpha(normalPixels, width, height, index, radius) * (1f - normalColor.a);
                float combinedAlpha = normalColor.a + glowAlpha;
                if (glowAlpha > 0f)
                {
                    brighterColor = Color.Lerp(Color.white, brighterColor, normalColor.a / combinedAlpha);
                    brighterColor.a = combinedAlpha;
                }
            }

            pressedPixels[index] = brighterColor;
        }

        return pressedPixels;
    }

    static float GetGlowAlpha(Color32[] pixels, int width, int height, int index, int radius)
    {
        int x = index % width;
        int y = index / width;
        int radiusSquared = radius * radius;
        int minX = Mathf.Max(0, x - radius);
        int maxX = Mathf.Min(width - 1, x + radius);
        int minY = Mathf.Max(0, y - radius);
        int maxY = Mathf.Min(height - 1, y + radius);
        float strongest = 0f;

        for (int sampleY = minY; sampleY <= maxY; sampleY++)
        {
            int offsetY = sampleY - y;
            for (int sampleX = minX; sampleX <= maxX; sampleX++)
            {
                int offsetX = sampleX - x;
                int distanceSquared = offsetX * offsetX + offsetY * offsetY;
                if (distanceSquared >= radiusSquared) continue;

                float falloff = 1f - (float)distanceSquared / radiusSquared;
                byte alpha = pixels[sampleY * width + sampleX].a;
                strongest = Mathf.Max(strongest, alpha * falloff * falloff);
            }
        }

        return strongest * (GlowOpacity / byte.MaxValue);
    }

    internal static Color GetColor(float hue)
    {
        return hue <= 0f ? Color.white : Color.HSVToRGB(hue, 0.8f, 1f);
    }

    internal void Apply(float scale, float hue)
    {
        if (_normalTexture != null && _scale == scale && _hue == hue) return;
        if (_source == null) throw new MissingReferenceException("커서 원본 이미지 연결 필요");

        int width = Mathf.Max(1, Mathf.RoundToInt(_source.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(_source.height * scale));
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Texture2D normalTexture = null;
        Texture2D pressedTexture = null;
        try
        {
            normalTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            pressedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Graphics.Blit(_source, target);
            RenderTexture.active = target;
            normalTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            Color32[] configuredPixels = normalTexture.GetPixels32();
            if (hue > 0f)
            {
                Color tint = GetColor(hue);
                for (int index = 0; index < configuredPixels.Length; index++)
                {
                    Color pixel = configuredPixels[index];
                    configuredPixels[index] = new Color(pixel.r * tint.r, pixel.g * tint.g,
                        pixel.b * tint.b, pixel.a);
                }
            }

            int glowRadius = Mathf.Max(1, Mathf.FloorToInt(4f * scale));
            normalTexture.SetPixels32(configuredPixels);
            pressedTexture.SetPixels32(CreatePressedPixels(configuredPixels, width, height, glowRadius));
            normalTexture.Apply();
            pressedTexture.Apply();
        }
        catch
        {
            Destroy(normalTexture);
            Destroy(pressedTexture);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }

        Texture2D previousNormal = _normalTexture;
        Texture2D previousPressed = _pressedTexture;
        _normalTexture = normalTexture;
        _pressedTexture = pressedTexture;
        _scale = scale;
        _hue = hue;
        ShowCursor(_isPressed);
        Destroy(previousNormal);
        Destroy(previousPressed);
    }

    void OnDestroy()
    {
        if (_normalTexture == null) return;

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Destroy(_normalTexture);
        Destroy(_pressedTexture);
    }
}
