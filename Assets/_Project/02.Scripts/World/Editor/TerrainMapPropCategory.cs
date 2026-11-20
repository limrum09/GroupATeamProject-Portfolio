using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain 배치물 구분
/// </summary>
public enum TerrainMapPropCategory
{
    [InspectorName("나무")] Tree,
    [InspectorName("돌")] Rock,
    [InspectorName("풀")] Grass
}

/// <summary>
/// 배치물 종류별 심기 선택값
/// </summary>
[Serializable]
public class TerrainMapPropSetting
{
    public GameObject Prefab;
    public bool Enabled = true;
    public TerrainMapPropCategory Category;
    public float DensityMultiplier = 1f;    // 레퍼런스 밀도 대비 배율
    public float HeightMultiplier = 1f;     // 레퍼런스 높이 배율에 곱할 배율
    public float WidthMultiplier = 1f;      // 레퍼런스 너비 배율에 곱할 배율

    /// <summary>
    /// 프리팹에 해당하는 선택값 검색
    /// </summary>
    /// <param name="settings">선택값 목록</param>
    /// <param name="prefab">찾을 프리팹</param>
    /// <returns>선택값, 없으면 null</returns>
    public static TerrainMapPropSetting Find(IReadOnlyList<TerrainMapPropSetting> settings, GameObject prefab)
    {
        foreach (TerrainMapPropSetting setting in settings)
        {
            if (setting.Prefab == prefab) return setting;
        }
        return null;
    }
}

/// <summary>
/// 프리팹 이름·폴더·크기 기반 배치물 구분 추정
/// </summary>
public static class TerrainMapPropClassifier
{
    const float TreeMinHeight = 2f;     // 이름으로 모를 때 나무로 볼 최소 높이
    const float GrassMaxSize = 1f;      // 이름으로 모를 때 풀로 볼 최대 크기

    static readonly string[] TreeKeywords = { "tree", "pine", "oak", "birch", "palm", "spruce", "fir", "willow" };
    static readonly string[] RockKeywords = { "rock", "stone", "boulder", "menhir", "pebble", "ore" };
    static readonly string[] GrassKeywords = { "grass", "fern", "bush", "shrub", "flower", "plant", "weed", "reed", "mushroom" };

    static List<string> SplitWords(string text)
    {
        // 영문 외 문자와 소문자→대문자 경계로 단어 분리 (PT_Pine_Tree, HighGrass)
        List<string> words = new List<string>();
        StringBuilder word = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char letter = text[i];
            bool boundary = !char.IsLetter(letter) ||
                            (char.IsUpper(letter) && i > 0 && char.IsLower(text[i - 1]));
            if (boundary && word.Length > 0)
            {
                words.Add(word.ToString().ToLowerInvariant());
                word.Clear();
            }
            if (char.IsLetter(letter)) word.Append(letter);
        }
        if (word.Length > 0) words.Add(word.ToString().ToLowerInvariant());
        return words;
    }

    static bool HasKeyword(List<string> words, string[] keywords)
    {
        foreach (string word in words)
        {
            foreach (string keyword in keywords)
            {
                if (word == keyword || word == keyword + "s") return true;
            }
        }
        return false;
    }

    static bool TryClassifyText(string text, out TerrainMapPropCategory category)
    {
        // 여러 단어가 섞이면 나무, 돌, 풀 순으로 우선 (Fruit_Tree_logs 등)
        List<string> words = SplitWords(text);
        category = TerrainMapPropCategory.Tree;
        if (HasKeyword(words, TreeKeywords)) return true;

        category = TerrainMapPropCategory.Rock;
        if (HasKeyword(words, RockKeywords)) return true;

        category = TerrainMapPropCategory.Grass;
        return HasKeyword(words, GrassKeywords);
    }

    /// <summary>
    /// 프리팹 하위 메시 전체를 덮는 크기 (배율 1 기준)
    /// </summary>
    /// <param name="prefab">배치물 프리팹</param>
    /// <param name="size">크기 (m)</param>
    /// <returns>메시 존재 여부</returns>
    public static bool TryGetSize(GameObject prefab, out Vector3 size)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign));
                if (!found) bounds = new Bounds(point, Vector3.zero);
                else bounds.Encapsulate(point);
                found = true;
            }
        }

        size = bounds.size;
        return found;
    }

    /// <summary>
    /// 프리팹 이름, 폴더, 메시 크기 순서로 배치물 구분 추정
    /// </summary>
    /// <param name="prefab">배치물 프리팹</param>
    /// <returns>추정 구분, 근거가 없으면 나무</returns>
    public static TerrainMapPropCategory Classify(GameObject prefab)
    {
        if (TryClassifyText(prefab.name, out TerrainMapPropCategory category)) return category;

        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab));
        if (!string.IsNullOrEmpty(folder) && TryClassifyText(folder, out category)) return category;

        // 이름과 폴더로 모르면 메시 크기로 추정, 결과는 패널에서 수정 가능
        if (!TryGetSize(prefab, out Vector3 size)) return TerrainMapPropCategory.Tree;
        if (size.y >= TreeMinHeight) return TerrainMapPropCategory.Tree;
        if (Mathf.Max(size.x, size.y, size.z) <= GrassMaxSize) return TerrainMapPropCategory.Grass;
        return TerrainMapPropCategory.Rock;
    }
}
