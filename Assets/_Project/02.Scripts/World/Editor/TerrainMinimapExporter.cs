using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Terrain 높이 판정 기준 이동 가능 영역의 미니맵 PNG 저장
/// </summary>
public static class TerrainMinimapExporter
{
    static bool TryValidateTargets(IReadOnlyList<Terrain> targets, out string error)
    {
        if (targets == null || targets.Count == 0)
        {
            error = "내보낼 Terrain을 하나 이상 선택하세요.";
            return false;
        }

        foreach (Terrain target in targets)
        {
            if (target == null || target.terrainData == null)
            {
                error = "대상 Terrain 또는 TerrainData 연결이 없습니다.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 대상 Terrain 전체 범위의 이동 가능 영역 PNG 저장
    /// </summary>
    /// <param name="targets">내보낼 Terrain 목록</param>
    /// <param name="settings">판정 기준</param>
    /// <param name="smoothRadius">경계 부드럽게 처리 픽셀 수</param>
    /// <param name="options">이미지 크기와 색상 옵션</param>
    /// <param name="assetPath">미니맵 폴더 아래 PNG 저장 경로</param>
    /// <param name="message">저장 결과와 월드 범위 또는 실행 불가 사유</param>
    /// <returns>검증과 저장 처리 성공 여부</returns>
    public static bool TryExport(IReadOnlyList<Terrain> targets, TerrainWalkableSettings settings,
        int smoothRadius, MinimapImageOptions options, string assetPath, out string message)
    {
        if (!TryValidateTargets(targets, out message)) return false;

        TerrainWalkableMask mask = TerrainWalkableMask.BuildForArea(targets, options.LongSide, settings);
        float[] coverage = mask.GetWeights(TerrainWalkableCategory.Walkable, smoothRadius);
        return MinimapImageExporter.TryExport(coverage, mask.Width, mask.Height, mask.WorldRect, options,
            assetPath, out message);
    }
}
