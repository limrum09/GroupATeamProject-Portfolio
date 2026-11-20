using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 에디터 도구의 TerrainData 수정 가능 여부 검증
/// </summary>
public static class TerrainDataEditGuard
{
    /// <summary>
    /// 프로젝트 소유이며 편집 가능한 TerrainData 여부 확인
    /// </summary>
    /// <param name="terrain">수정 대상 Terrain</param>
    /// <param name="error">수정 불가 사유</param>
    /// <returns>수정 가능 여부</returns>
    public static bool TryValidate(Terrain terrain, out string error)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            error = "대상 Terrain 또는 TerrainData 연결이 없습니다.";
            return false;
        }

        string path = AssetDatabase.GetAssetPath(terrain.terrainData);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal))
        {
            error = $"{terrain.name}: Assets 아래에 저장된 TerrainData를 지정하세요.\n현재 위치: {path}";
            return false;
        }

        if (path.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Assets/_ThirdParty/", StringComparison.OrdinalIgnoreCase))
        {
            error = $"{terrain.name}: 외부 에셋 원본은 수정할 수 없습니다. 프로젝트용 TerrainData 복사본을 지정하세요.\n현재 위치: {path}";
            return false;
        }

        if (!AssetDatabase.IsOpenForEdit(terrain.terrainData))
        {
            error = $"{terrain.name}: TerrainData를 편집할 수 없습니다. 파일 잠금 또는 버전 관리 상태를 확인하세요.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
