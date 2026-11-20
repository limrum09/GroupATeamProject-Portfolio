using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ShorelineGlow Shader Graph 머티리얼 인스펙터
/// Shader Graph의 영문 Display Name 대신 ShorelineGlowPropertyLabels의 한글 라벨로 표시
/// </summary>
/// <remarks>
/// URP의 ShaderGraphLitGUI가 internal이라 BaseShaderGUI를 직접 상속
/// Render Queue는 공개 API인 SetMaterialKeywords 기준으로 항상 자동 계산, Queue Control 선택 UI 미제공
/// </remarks>
public class ShorelineGlowShaderGUI : BaseShaderGUI
{
    const MaterialProperty.PropFlags HiddenFlags =
        MaterialProperty.PropFlags.HideInInspector | MaterialProperty.PropFlags.PerRendererData;

    MaterialProperty[] _properties;

    static bool IsVisible(MaterialProperty property)
    {
        return (property.flags & HiddenFlags) == 0;
    }

    void DrawLabeledSections(IReadOnlyList<ShorelineGlowPropertyLabels.Section> sections, HashSet<string> drawnNames)
    {
        List<string> missingNames = new List<string>();

        for (int i = 0; i < sections.Count; i++)
        {
            ShorelineGlowPropertyLabels.Section section = sections[i];
            if (i > 0) EditorGUILayout.Space();
            EditorGUILayout.LabelField(section.Title, EditorStyles.boldLabel);

            foreach ((string referenceName, string label) in section.Entries)
            {
                MaterialProperty property = FindProperty(referenceName, _properties, false);
                if (property == null)
                {
                    missingNames.Add(referenceName);
                    continue;
                }

                materialEditor.ShaderProperty(property, label);
                drawnNames.Add(referenceName);
            }
        }

        // 라벨 표에는 있으나 셰이더에 없는 Reference Name, 그래프 변경 후 표 미갱신 상태
        if (missingNames.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"셰이더에 없는 프로퍼티가 라벨 표에 있음: {string.Join(", ", missingNames)}",
                MessageType.Warning);
        }
    }

    void DrawUnlabeledProperties(HashSet<string> drawnNames)
    {
        List<MaterialProperty> unlabeled = new List<MaterialProperty>();
        foreach (MaterialProperty property in _properties)
        {
            if (IsVisible(property) && !drawnNames.Contains(property.name))
                unlabeled.Add(property);
        }

        if (unlabeled.Count == 0) return;

        // 라벨 누락 프로퍼티도 편집은 가능하도록 영문 Display Name으로 표시
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "ShorelineGlowPropertyLabels에 한글 라벨이 없는 프로퍼티 (영문 이름으로 표시)",
            MessageType.Warning);
        foreach (MaterialProperty property in unlabeled)
            materialEditor.ShaderProperty(property, property.displayName);
    }

    /// <summary>
    /// 머티리얼 프로퍼티 수집
    /// </summary>
    /// <param name="properties">머티리얼의 전체 프로퍼티</param>
    public override void FindProperties(MaterialProperty[] properties)
    {
        _properties = properties;
        base.FindProperties(properties);
    }

    /// <summary>
    /// 머티리얼 키워드와 Render Queue 갱신
    /// </summary>
    /// <param name="material">대상 머티리얼</param>
    public override void ValidateMaterial(Material material)
    {
        SetMaterialKeywords(material);
    }

    /// <summary>
    /// 한글 라벨 섹션 단위의 Surface Inputs 표시
    /// </summary>
    /// <param name="material">대상 머티리얼</param>
    public override void DrawSurfaceInputs(Material material)
    {
        HashSet<string> drawnNames = new HashSet<string>();

        if (ShorelineGlowPropertyLabels.TryGetSections(material.shader.name,
                out IReadOnlyList<ShorelineGlowPropertyLabels.Section> sections))
        {
            DrawLabeledSections(sections, drawnNames);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"ShorelineGlowPropertyLabels에 '{material.shader.name}' 라벨 구성이 없음",
                MessageType.Error);
        }

        DrawUnlabeledProperties(drawnNames);
    }

    /// <summary>
    /// Advanced 옵션 표시, Sorting Priority와 Instancing 및 GI 설정
    /// </summary>
    /// <param name="material">대상 머티리얼</param>
    public override void DrawAdvancedOptions(Material material)
    {
        base.DrawAdvancedOptions(material);

        // Shader Graph에는 고정 Emission 프로퍼티가 없어 Emission 색상 무시
        materialEditor.DoubleSidedGIField();
        materialEditor.LightmapEmissionFlagsProperty(0, true, true);
    }
}
