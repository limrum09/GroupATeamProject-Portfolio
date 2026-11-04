using UnityEngine;

/// <summary>
/// 지정된 이미지의 크기와 색상을 반영하는 커서 표시
/// </summary>
[DisallowMultipleComponent]
public class GameplayCursor : MonoBehaviour
{
    [SerializeField] Texture2D _source;

    Texture2D _texture;
    float _scale;
    float _hue;

    internal static Color GetColor(float hue)
    {
        return hue <= 0f ? Color.white : Color.HSVToRGB(hue, 0.8f, 1f);
    }

    internal void Apply(float scale, float hue)
    {
        if (_texture != null && _scale == scale && _hue == hue) return;
        if (_source == null) throw new MissingReferenceException("커서 원본 이미지 연결 필요");

        int width = Mathf.Max(1, Mathf.RoundToInt(_source.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(_source.height * scale));
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(_source, target);
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            if (hue > 0f)
            {
                Color tint = GetColor(hue);
                Color32[] pixels = texture.GetPixels32();
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color pixel = pixels[index];
                    pixels[index] = new Color(pixel.r * tint.r, pixel.g * tint.g,
                        pixel.b * tint.b, pixel.a);
                }
                texture.SetPixels32(pixels);
            }

            texture.Apply();
            Cursor.SetCursor(texture, Vector2.zero, CursorMode.ForceSoftware);
        }
        catch
        {
            Destroy(texture);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }

        if (_texture != null) Destroy(_texture);
        _texture = texture;
        _scale = scale;
        _hue = hue;
    }

    void OnDestroy()
    {
        if (_texture == null) return;

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Destroy(_texture);
    }
}
