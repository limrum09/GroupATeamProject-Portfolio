using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 미니맵 이미지 크기와 색상 옵션
/// </summary>
public readonly struct MinimapImageOptions
{
    public const int DefaultOutlineWidth = 3;

    // 참고 미니맵 이미지 기준 기본 색상
    public static readonly Color DefaultFillColor = new Color32(0x3C, 0x66, 0x7F, 0xFF);
    public static readonly Color DefaultOutlineColor = new Color32(0x5F, 0xAF, 0xCC, 0xFF);

    /// <summary>
    /// 미니맵 이미지 옵션 생성
    /// </summary>
    /// <param name="longSide">긴 변 픽셀 수</param>
    /// <param name="fillColor">이동 가능 영역 채우기 색상</param>
    /// <param name="outlineColor">이동 가능 영역 안쪽 테두리 색상</param>
    /// <param name="outlineWidth">테두리 두께 픽셀 수</param>
    public MinimapImageOptions(int longSide, Color fillColor, Color outlineColor, int outlineWidth)
    {
        LongSide = longSide;
        FillColor = fillColor;
        OutlineColor = outlineColor;
        OutlineWidth = outlineWidth;
    }

    public int LongSide { get; }
    public Color FillColor { get; }
    public Color OutlineColor { get; }
    public int OutlineWidth { get; }
}

/// <summary>
/// 이동 가능 비중 격자의 미니맵 이미지 생성과 PNG 저장
/// </summary>
public static class MinimapImageExporter
{
    public const string ExportFolder = "Assets/_Project/04.Art/Minimaps";

    static bool TryValidatePath(string assetPath, out string error)
    {
        if (!assetPath.StartsWith(ExportFolder + "/", StringComparison.Ordinal) ||
            !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            error = $"{ExportFolder}/ 아래의 .png 경로를 지정하세요.\n현재 경로: {assetPath}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    static float[] Erode(float[] values, int width, int height, int radius)
    {
        // 십자·사각 이웃 최솟값 교대 적용의 팔각형 근사 침식, 이미지 밖은 0 취급
        float[] current = (float[])values.Clone();
        float[] next = new float[values.Length];
        for (int step = 0; step < radius; step++)
        {
            bool square = step % 2 == 1;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float min = current[z * width + x];
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (!square && offsetX != 0 && offsetZ != 0) continue;

                            int sampleX = x + offsetX;
                            int sampleZ = z + offsetZ;
                            bool inside = sampleX >= 0 && sampleX < width && sampleZ >= 0 && sampleZ < height;
                            min = Mathf.Min(min, inside ? current[sampleZ * width + sampleX] : 0f);
                        }
                    }

                    next[z * width + x] = min;
                }
            }

            (current, next) = (next, current);
        }

        return current;
    }

    static void ConfigureImporter(string assetPath)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// 월드 범위 비율을 유지한 이미지 픽셀 크기 계산
    /// </summary>
    /// <param name="worldRect">월드 범위 (x, y = 월드 X, Z)</param>
    /// <param name="longSide">긴 변 픽셀 수</param>
    /// <returns>이미지 가로·세로 픽셀 수</returns>
    public static Vector2Int GetImageSize(Rect worldRect, int longSide)
    {
        int width = longSide;
        int height = longSide;
        if (worldRect.width >= worldRect.height)
            height = Mathf.Max(1, Mathf.RoundToInt(longSide * worldRect.height / worldRect.width));
        else
            width = Mathf.Max(1, Mathf.RoundToInt(longSide * worldRect.width / worldRect.height));

        return new Vector2Int(width, height);
    }

    /// <summary>
    /// 미니맵 폴더 기준 PNG 저장 경로 선택 대화상자
    /// </summary>
    /// <param name="defaultName">기본 파일 이름</param>
    /// <returns>선택 경로, 취소 시 빈 문자열</returns>
    public static string AskSavePath(string defaultName)
    {
        return EditorUtility.SaveFilePanelInProject("미니맵 PNG 저장", defaultName, "png",
            "미니맵 PNG를 저장할 위치를 선택하세요.", ExportFolder);
    }

    /// <summary>
    /// 투명 배경, 채우기, 안쪽 테두리 적용 미니맵 텍스처 생성
    /// </summary>
    /// <param name="coverage">픽셀별 이동 가능 비중 0~1, 첫 행이 월드 -Z 쪽</param>
    /// <param name="width">가로 픽셀 수</param>
    /// <param name="height">세로 픽셀 수</param>
    /// <param name="options">색상과 테두리 옵션</param>
    /// <returns>호출자가 해제해야 하는 텍스처</returns>
    public static Texture2D CreateTexture(float[] coverage, int width, int height, MinimapImageOptions options)
    {
        float[] inner = Erode(coverage, width, height, options.OutlineWidth);
        Color[] pixels = new Color[coverage.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            float alpha = coverage[i];
            if (alpha <= 0f)
            {
                pixels[i] = Color.clear;
                continue;
            }

            // 침식 후 남은 비중은 채우기, 깎인 가장자리 비중은 테두리
            float outlineRatio = Mathf.Clamp01((alpha - inner[i]) / alpha);
            Color color = Color.Lerp(options.FillColor, options.OutlineColor, outlineRatio);
            color.a *= alpha;
            pixels[i] = color;
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// 이동 가능 비중 격자의 미니맵 PNG 저장
    /// </summary>
    /// <param name="coverage">픽셀별 이동 가능 비중 0~1</param>
    /// <param name="width">가로 픽셀 수</param>
    /// <param name="height">세로 픽셀 수</param>
    /// <param name="worldRect">이미지가 덮는 월드 범위</param>
    /// <param name="options">색상과 테두리 옵션</param>
    /// <param name="assetPath">미니맵 폴더 아래 PNG 저장 경로</param>
    /// <param name="message">저장 결과와 월드 범위 또는 실행 불가 사유</param>
    /// <returns>검증과 저장 처리 성공 여부</returns>
    public static bool TryExport(float[] coverage, int width, int height, Rect worldRect,
        MinimapImageOptions options, string assetPath, out string message)
    {
        if (!TryValidatePath(assetPath, out message)) return false;

        Texture2D texture = CreateTexture(coverage, width, height, options);
        try
        {
            // 프로젝트 루트 기준 경로, PC별 설치 위치와 무관
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureImporter(assetPath);

        message = $"{assetPath} 저장 ({width}x{height})\n" +
                  $"월드 범위: 원점 ({worldRect.x:0.##}, {worldRect.y:0.##}) · 크기 ({worldRect.width:0.##}, {worldRect.height:0.##})";
        return true;
    }
}
