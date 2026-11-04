using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>선택한 CSV 에셋의 JSON 변환 메뉴</summary>
public static class CsvToJsonMenu
{
    const string MenuPath = "Tools/Data/CSV To JSON";

    [MenuItem(MenuPath)]
    static void ConvertSelected()
    {
        TextAsset csv = Selection.activeObject as TextAsset;
        string source = AssetDatabase.GetAssetPath(csv);

        try
        {
            // 변환에 성공한 경우에만 저장 위치 선택
            string json = CsvToJsonConverter.Convert(csv.text, source);

            string assetPath = EditorUtility.SaveFilePanelInProject("JSON 저장", Path.GetFileNameWithoutExtension(source), "json", "Assets/_Project/06.Data 아래에 저장", "Assets/_Project/06.Data");

            if (string.IsNullOrEmpty(assetPath))
                return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            string dataRoot = Path.GetFullPath(Path.Combine( projectRoot, "Assets", "_Project", "06.Data"));

            string outputPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));

            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!outputPath.StartsWith(dataRoot + Path.DirectorySeparatorChar, comparison) || !string.Equals( Path.GetExtension(outputPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Assets/_Project/06.Data 아래의 JSON 경로 필요");

            File.WriteAllText(outputPath, json, new UTF8Encoding(false));

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"CSV → JSON 완료: {assetPath}", csv);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, csv);

            EditorUtility.DisplayDialog("CSV 변환 또는 JSON 저장·임포트 실패", exception.Message, "확인");
        }
    }

    [MenuItem(MenuPath, true)]
    static bool CanConvertSelected()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return false;

        if (!(Selection.activeObject is TextAsset csv))
            return false;

        string path = AssetDatabase.GetAssetPath(csv);

        return string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
    }
}