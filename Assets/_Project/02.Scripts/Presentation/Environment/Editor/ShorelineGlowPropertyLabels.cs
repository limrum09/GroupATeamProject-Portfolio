using System.Collections.Generic;

/// <summary>
/// ShorelineGlow 셰이더별 머티리얼 인스펙터 한글 라벨과 섹션 구성
/// </summary>
public static class ShorelineGlowPropertyLabels
{
    /// <summary>
    /// 인스펙터 한 섹션의 제목과 표시 순서대로의 프로퍼티 라벨 목록
    /// </summary>
    public class Section
    {
        readonly (string ReferenceName, string Label)[] _entries;

        /// <summary>
        /// 섹션 생성
        /// </summary>
        /// <param name="title">섹션 제목</param>
        /// <param name="entries">Reference Name과 한글 라벨 쌍, 표시 순서</param>
        public Section(string title, params (string ReferenceName, string Label)[] entries)
        {
            Title = title;
            _entries = entries;
        }

        public string Title { get; }
        public IReadOnlyList<(string ReferenceName, string Label)> Entries => _entries;
    }

    static readonly Section[] WaterSections =
    {
        new Section("물 색상",
            ("_DeepColor", "깊은 물 색상"),
            ("_ShallowColor", "얕은 물 색상"),
            ("_DepthFadeDistance", "수심 색상 전환 거리"),
            ("_WaterOpacity", "물 불투명도"),
            ("_Smoothness", "수면 광택")),
        new Section("물가 발광",
            ("_EdgeWidth", "최대 발광 폭"),
            ("_EdgeStrength", "굵은 구간 발광 강도"),
            ("_ThickEdgeColor", "굵은 구간 발광 색상"),
            ("_ThinEdgeStrength", "얇은 구간 발광 강도"),
            ("_EdgeColor", "얇은 구간 청색"),
            ("_WidthBias", "굵은 구간 비중"),
            ("_MinimumWidthRatio", "최소 폭 비율"),
            ("_NoiseScale", "완만한 폭 변화 밀도"),
            ("_AngularScale", "각진 꺾임 밀도"),
            ("_AngularAmount", "각진 꺾임 크기"),
            ("_CoreInset", "경계선 안쪽 거리"),
            ("_HaloWidth", "경계 근처 번짐 폭"),
            ("_BaseGlowStrength", "경계 근처 번짐 강도"),
            ("_FlowSpeed", "무늬 흐름 속도")),
        new Section("접촉 암부",
            ("_ContactDarkness", "접촉 암부 강도"),
            ("_LipWidth", "접촉 암부 폭")),
        new Section("주변 표면",
            ("_SpillStrength", "주변 표면 빛 강도"),
            ("_SpillHeight", "주변 표면 빛 높이"),
            ("_SurfaceShadeWidth", "주변 표면 접촉 암부 폭"),
            ("_SurfaceShadeStrength", "주변 표면 접촉 암부 강도")),
        CreateSwellSection()
    };

    static readonly Section[] DotWaterSections =
    {
        new Section("물 색상",
            ("_DeepColor", "깊은 물 색상"),
            ("_ShallowColor", "얕은 물 색상"),
            ("_DepthFadeDistance", "수심 색상 전환 거리"),
            ("_WaterOpacity", "물 불투명도"),
            ("_Smoothness", "수면 광택")),
        new Section("점 발광",
            ("_ThickEdgeColor", "큰 점 발광 색상"),
            ("_DotHighStrength", "큰 점 발광 강도"),
            ("_EdgeColor", "작은 점 발광 색상"),
            ("_DotLowStrength", "작은 점 발광 강도"),
            ("_DotMaxWidth", "최대 발광 굵기 (월드)"),
            ("_DotTransitionDistance", "굵기 전환 거리 (월드)"),
            ("_DotNeighborRatio", "인접 굵기 최소 비율"),
            ("_DotCellDensity", "발광 덩어리 배치 밀도"),
            ("_DotFadeStart", "소멸 시작 굵기 (최대 대비)")),
        new Section("접촉 암부",
            ("_ContactDarkness", "접촉 암부 강도"),
            ("_LipWidth", "접촉 암부 폭")),
        CreateSwellSection()
    };

    static readonly Section[] ContactSurfaceSections =
    {
        new Section("표면",
            ("_BaseColor", "표면 색상"),
            ("_Smoothness", "표면 광택"))
    };

    // Shader Graph 셰이더 이름: 그래프 경로 + 에셋 이름
    static readonly Dictionary<string, Section[]> SectionsByShader = new Dictionary<string, Section[]>
    {
        { "ShorelineGlow/ShorelineGlow_Water", WaterSections },
        { "ShorelineGlow/ShorelineGlow_DotWater", DotWaterSections },
        { "ShorelineGlow/ShorelineGlow_ContactSurface", ContactSurfaceSections }
    };

    static Section CreateSwellSection()
    {
        return new Section("잔잔한 너울 (GPU)",
            ("_SwellAmplitudeA", "너울 A 높이"),
            ("_SwellWavelengthA", "너울 A 간격"),
            ("_SwellPeriodA", "너울 A 주기 (초)"),
            ("_SwellAmplitudeB", "너울 B 높이"),
            ("_SwellWavelengthB", "너울 B 간격"),
            ("_SwellPeriodB", "너울 B 주기 (초)"));
    }

    /// <summary>
    /// 셰이더 이름에 해당하는 섹션 구성 조회
    /// </summary>
    /// <param name="shaderName">Shader.name 값</param>
    /// <param name="sections">해당 셰이더의 섹션 목록</param>
    /// <returns>등록된 셰이더 여부</returns>
    public static bool TryGetSections(string shaderName, out IReadOnlyList<Section> sections)
    {
        bool found = SectionsByShader.TryGetValue(shaderName, out Section[] array);
        sections = array;
        return found;
    }
}
