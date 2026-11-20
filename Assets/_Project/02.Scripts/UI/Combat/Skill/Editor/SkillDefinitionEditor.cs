using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 스킬 종류와 옵션에 필요한 설정만 표시하는 Inspector
/// </summary>
[CustomEditor(typeof(SkillDefinition), true)]
public class SkillDefinitionEditor : Editor
{
    // Inspector 한글 표시 이름, 스킬 종류별로 의미가 다른 항목은 호출부에서 직접 지정
    static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "m_Script", "스크립트" },

        { "_icon", "아이콘" },
        { "_skillName", "스킬 이름" },
        { "_skillDescription", "스킬 설명" },
        { "_category", "직업 분류" },
        { "_skillUID", "스킬 UID" },
        { "_levelGrowth", "레벨별 성장" },

        { "_castTime", "시전 시간" },
        { "_postCastMoveLockDuration", "발동 후 이동 제한 시간" },
        { "_cooldown", "재사용 대기시간" },
        { "motionClip", "모션 클립" },

        { "_targetMode", "대상 선택 방식" },
        { "_range", "사거리" },
        { "_targetMask", "대상 레이어" },
        { "_aimMode", "공격 방향 방식" },
        { "_rotationMode", "캐릭터 회전 방식" },

        { "_damageMultiplier", "공격력 계수" },
        { "_spellPowerMultiplier", "주문력 계수" },
        { "_onHitAreaEffect", "적중 범위 효과" },
        { "_applyDirectDamage", "직격 피해 적용" },
        { "_includeDirectTargetInArea", "직격 대상도 범위 피해에 포함" },
        { "_playHitEffectOnAreaTargets", "범위 피해 대상에도 타격 이펙트" },

        { "_useProjectile", "탄환 사용" },
        { "_flightMode", "탄환 비행 방식" },
        { "_projectilePrefab", "탄환 프리팹" },
        { "_projectileSpeed", "탄환 속도" },
        { "_projectileRadius", "탄환 반경" },
        { "_projectileLifetime", "탄환 수명" },
        { "_projectileSpawnOffset", "탄환 발사 위치 보정" },
        { "_projectileLaunchEffectPrefab", "발사 이펙트" },

        { "_areaEffect", "범위 효과" },
        { "_placementMode", "배치 방식" },
        { "_deliveryMode", "전달 방식" },
        { "_trackingMode", "위치 추적 방식" },
        { "_maxPlacementDistance", "최대 배치 거리" },
        { "_groundMask", "지면 레이어" },
        { "_groundProbeHeight", "지면 검사 높이" },
        { "_groundProbeDepth", "지면 검사 깊이" },

        { "_mode", "이동 방식" },
        { "_minDistance", "최소 돌진 거리" },
        { "_arrivalAreaEffect", "도착 범위 효과" },
        { "_stopMask", "정지 대상 레이어" },

        { "_useEffectPrefab", "사용 이펙트" },
        { "_useEffectRotation", "사용 이펙트 회전 보정" },
        { "_effectOffset", "사용 이펙트 위치 보정" },
        { "_useEffectAtCaster", "사용 이펙트를 시전자 위치에서 재생" },
        { "_hitEffectPrefab", "피격 이펙트" },
        { "_hitEffectRotation", "피격 이펙트 회전 보정" },
        { "_hitEffectOffset", "피격 이펙트 위치 보정" },
        { "_arrivalEffectPrefab", "도착 이펙트" }
    };

    readonly Dictionary<string, SerializedProperty> _properties = new Dictionary<string, SerializedProperty>();

    public bool IsEmbedded { get; set; }

    void OnEnable()
    {
        _properties.Clear();

        SerializedProperty property = serializedObject.GetIterator();

        if (!property.NextVisible(true))
            return;

        do
        {
            _properties[property.name] = property.Copy();
        }
        while (property.NextVisible(false));
    }

    void Draw(string propertyName)
    {
        // 표에 없는 항목은 Unity 기본 표시 이름 사용
        string label = Labels.TryGetValue(propertyName, out string korean) ? korean : _properties[propertyName].displayName;

        Draw(propertyName, label);
    }

    void Draw(string propertyName, string label)
    {
        SerializedProperty property = _properties[propertyName];

        // 필드에 지정된 Tooltip 유지
        EditorGUILayout.PropertyField(property, new UnityEngine.GUIContent(label, property.tooltip), true);
    }

    bool HasReference(string propertyName)
    {
        return _properties[propertyName].objectReferenceValue != null;
    }

    bool UsesTargetProjectile()
    {
        return target is TargetDamageSkillDefinition && _properties["_useProjectile"].boolValue;
    }

    void DrawCommonSettings()
    {
        Draw("_icon");
        Draw("_skillName");
        Draw("_skillDescription");
        Draw("_category");
        Draw("_skillUID");

        Draw("_levelGrowth");

        Draw("_castTime");

        if (target is TargetDamageSkillDefinition || target is AreaDamageSkillDefinition)
            Draw("_postCastMoveLockDuration");

        Draw("_cooldown");
        Draw("motionClip");
    }

    void DrawTargeting()
    {
        Draw("_targetMode");

        SkillTargetMode targetMode = (SkillTargetMode)_properties["_targetMode"].intValue;

        bool selectsTarget = targetMode != SkillTargetMode.None;

        // 공격 스킬의 사거리는 적 AI와 기본 공격에서도 사용
        if (!(target is MovementSkillDefinition || target is DashStrikeSkillDefinition) || selectsTarget)
            Draw("_range");

        bool usesTargetMask = selectsTarget || UsesTargetProjectile() || target is ChargeAttackSkillDefinition;

        if (usesTargetMask)
            Draw("_targetMask");

        Draw("_aimMode");
        Draw("_rotationMode");
    }

    void DrawTargetDamageSettings()
    {
        Draw("_damageMultiplier");
        Draw("_spellPowerMultiplier");
        Draw("_blockingMask", "장애물 레이어");
        Draw("_onHitAreaEffect");

        if (HasReference("_onHitAreaEffect"))
        {
            Draw("_applyDirectDamage");
            Draw("_includeDirectTargetInArea");
            Draw("_playHitEffectOnAreaTargets");
        }

        Draw("_useProjectile");

        if (!_properties["_useProjectile"].boolValue)
            return;

        Draw("_flightMode");
        Draw("_projectilePrefab");
        Draw("_projectileSpeed");
        Draw("_projectileRadius");
        Draw("_projectileLifetime");
        Draw("_projectileSpawnOffset");
    }

    void DrawAreaDamageSettings()
    {
        Draw("_areaEffect");
        Draw("_placementMode");
        Draw("_deliveryMode");
        Draw("_trackingMode");

        AreaPlacementMode placementMode = (AreaPlacementMode)_properties["_placementMode"].intValue;
        AreaDeliveryMode deliveryMode = (AreaDeliveryMode)_properties["_deliveryMode"].intValue;
        AreaTrackingMode trackingMode = (AreaTrackingMode)_properties["_trackingMode"].intValue;

        if (trackingMode == AreaTrackingMode.FollowCaster)
        {
            AreaEffectDefinition effect = _properties["_areaEffect"].objectReferenceValue as AreaEffectDefinition;

            if (placementMode != AreaPlacementMode.CasterPosition || deliveryMode != AreaDeliveryMode.Direct || effect == null || effect.DamageMode != AreaEffectDamageMode.Repeat)
                EditorGUILayout.HelpBox("FollowCaster는 CasterPosition / Direct 배치와 " + "Repeat 방식의 Area Effect 연결 필요", MessageType.Error);
        }

        if (placementMode == AreaPlacementMode.RequestedWorldPosition)
        {
            Draw("_maxPlacementDistance");
            Draw("_groundMask");
            Draw("_groundProbeHeight");
            Draw("_groundProbeDepth");
        }

        if (deliveryMode == AreaDeliveryMode.Projectile)
        {
            Draw("_projectilePrefab");
            Draw("_projectileSpeed");
            Draw("_projectileLifetime");
            Draw("_projectileSpawnOffset");
            Draw("_projectileLaunchEffectPrefab");
        }
    }

    void DrawMovementSettings()
    {
        Draw("_mode");
        Draw("_distance", "이동 거리");

        MovementSkillMode mode = (MovementSkillMode)_properties["_mode"].intValue;

        if (mode == MovementSkillMode.Dash)
            Draw("_dashDuration", "대쉬 시간");

        Draw("_blockingMask", "이동 차단 레이어");
    }

    void DrawDashStrikeSettings()
    {
        Draw("_minDistance");
        Draw("_distance", "최대 돌진 거리");
        Draw("_dashDuration", "돌진 시간");
        Draw("_blockingMask", "이동 차단 레이어");
        Draw("_arrivalAreaEffect");
    }

    void DrawChargeSettings()
    {
        Draw("_distance", "최소 돌진 거리");
        Draw("_duration", "돌진 시간");
        Draw("_stopMask");
        Draw("_damageMultiplier");
        Draw("_spellPowerMultiplier");
    }

    void DrawEffects()
    {
        Draw("_useEffectPrefab");

        if (HasReference("_useEffectPrefab"))
        {
            Draw("_useEffectRotation");

            // 대상 탄환의 발동 이펙트는 발사 위치를 그대로 사용
            // 범위 스킬의 장판 이펙트는 전달 방식과 관계없이 보정 사용
            if (!UsesTargetProjectile())
            {
                // 탄환 없는 대상 공격은 기본적으로 대상 위치에서 재생
                if (target is TargetDamageSkillDefinition)
                    Draw("_useEffectAtCaster");

                Draw("_effectOffset");
            }
        }

        // 범위 스킬은 공통 Hit Effect를 사용하지 않으며
        // 이동 스킬은 피해 적중 이벤트를 발생시키지 않음
        bool usesHitEffect = target is TargetDamageSkillDefinition || target is ChargeAttackSkillDefinition;

        if (usesHitEffect)
        {
            Draw("_hitEffectPrefab");

            if (HasReference("_hitEffectPrefab"))
            {
                Draw("_hitEffectRotation");
                Draw("_hitEffectOffset");
            }
        }

        if (target is MovementSkillDefinition || target is DashStrikeSkillDefinition)
            Draw("_arrivalEffectPrefab");
    }

    void DrawConfigurationHelp()
    {
        SkillTargetMode targetMode = (SkillTargetMode)_properties["_targetMode"].intValue;

        SkillAimMode aimMode = (SkillAimMode)_properties["_aimMode"].intValue;

        if (aimMode == SkillAimMode.SelectedTarget && targetMode == SkillTargetMode.None)
            EditorGUILayout.HelpBox("Aim Mode가 Selected Target이면 " + "Target Mode에서 대상 선택 방식을 지정해야 합니다.", MessageType.Error);

        bool requiresRequestedDirection = target is MovementSkillDefinition || target is ChargeAttackSkillDefinition || target is DashStrikeSkillDefinition;

        if (target is AreaDamageSkillDefinition)
        {
            AreaPlacementMode placementMode = (AreaPlacementMode)_properties["_placementMode"].intValue;

            AreaDeliveryMode deliveryMode = (AreaDeliveryMode)_properties["_deliveryMode"].intValue;

            if (placementMode == AreaPlacementMode.RequestedWorldPosition)
                requiresRequestedDirection = true;

            if (deliveryMode == AreaDeliveryMode.Projectile && placementMode != AreaPlacementMode.RequestedWorldPosition)
            {
                EditorGUILayout.HelpBox("범위 스킬의 Projectile 전달 방식은 " + "Requested World Position 배치 방식이 필요합니다.", MessageType.Error);
            }
        }

        if (requiresRequestedDirection && (targetMode != SkillTargetMode.None || aimMode != SkillAimMode.RequestedDirection))
        {
            EditorGUILayout.HelpBox("이 스킬 설정에서는 Target Mode를 None, " + "Aim Mode를 Requested Direction으로 지정해야 합니다.", MessageType.Error);
        }
    }

    /// <summary>
    /// 공통 설정과 스킬 종류별 조건부 항목 표시
    /// </summary>
    public override void OnInspectorGUI()
    {
        if (!IsEmbedded && UnityEngine.GUILayout.Button("관리창에서 열기"))
        {
            SkillManagementWindow.Open((SkillDefinition)target);
            UnityEngine.GUIUtility.ExitGUI();
        }

        Type definitionType = target.GetType();

        bool supported = definitionType == typeof(TargetDamageSkillDefinition) || definitionType == typeof(AreaDamageSkillDefinition) || definitionType == typeof(MovementSkillDefinition) || definitionType == typeof(ChargeAttackSkillDefinition) || definitionType == typeof(DashStrikeSkillDefinition);

        // 다른 파생 타입은 기본 Inspector로 모든 필드 표시
        if (!supported)
        {
            DrawDefaultInspector();
            return;
        }

        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            Draw("m_Script");
        }

        DrawCommonSettings();
        DrawTargeting();

        if (target is TargetDamageSkillDefinition)
        {
            DrawTargetDamageSettings();
        }
        else if (target is AreaDamageSkillDefinition)
        {
            DrawAreaDamageSettings();
        }
        else if (target is MovementSkillDefinition)
        {
            DrawMovementSettings();
        }
        else if (target is ChargeAttackSkillDefinition)
        {
            DrawChargeSettings();
        }
        else if (target is DashStrikeSkillDefinition)
        {
            DrawDashStrikeSettings();
        }

        DrawEffects();
        DrawConfigurationHelp();

        serializedObject.ApplyModifiedProperties();
    }
}