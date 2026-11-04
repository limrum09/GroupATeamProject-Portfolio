using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 스킬 종류와 옵션에 필요한 설정만 표시하는 Inspector
/// </summary>
[CustomEditor(typeof(SkillDefinition), true)]
public class SkillDefinitionEditor : Editor
{
    readonly Dictionary<string, SerializedProperty> _properties =
        new Dictionary<string, SerializedProperty>();

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
        EditorGUILayout.PropertyField(
            _properties[propertyName],
            true);
    }

    bool HasReference(string propertyName)
    {
        return _properties[propertyName].objectReferenceValue != null;
    }

    bool UsesTargetProjectile()
    {
        return target is TargetDamageSkillDefinition &&
            _properties["_useProjectile"].boolValue;
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
        Draw("_cooldown");
        Draw("motionClip");
    }

    void DrawTargeting()
    {
        Draw("_targetMode");

        SkillTargetMode targetMode =
            (SkillTargetMode)_properties["_targetMode"].intValue;

        bool selectsTarget = targetMode != SkillTargetMode.None;

        // 공격 스킬의 사거리는 적 AI와 기본 공격에서도 사용
        if (!(target is MovementSkillDefinition) || selectsTarget)
            Draw("_range");

        bool usesTargetMask =
            selectsTarget ||
            UsesTargetProjectile() ||
            target is ChargeAttackSkillDefinition;

        if (usesTargetMask)
            Draw("_targetMask");

        Draw("_aimMode");
        Draw("_rotationMode");
    }

    void DrawTargetDamageSettings()
    {
        Draw("_damageMultiplier");
        Draw("_spellPowerMultiplier");
        Draw("_blockingMask");
        Draw("_onHitAreaEffect");

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

        AreaPlacementMode placementMode =
            (AreaPlacementMode)_properties["_placementMode"].intValue;

        AreaDeliveryMode deliveryMode =
            (AreaDeliveryMode)_properties["_deliveryMode"].intValue;

        AreaTrackingMode trackingMode =
            (AreaTrackingMode)_properties["_trackingMode"].intValue;

        if (trackingMode == AreaTrackingMode.FollowCaster)
        {
            AreaEffectDefinition effect =
                _properties["_areaEffect"].objectReferenceValue
                as AreaEffectDefinition;

            if (placementMode != AreaPlacementMode.CasterPosition
                || deliveryMode != AreaDeliveryMode.Direct
                || effect == null
                || effect.DamageMode != AreaEffectDamageMode.Repeat)
            {
                EditorGUILayout.HelpBox(
                    "FollowCaster는 CasterPosition / Direct 배치와 "
                    + "Repeat 방식의 Area Effect 연결 필요",
                    MessageType.Error);
            }
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
        Draw("_distance");

        MovementSkillMode mode =
            (MovementSkillMode)_properties["_mode"].intValue;

        if (mode == MovementSkillMode.Dash)
            Draw("_dashDuration");

        Draw("_blockingMask");
    }

    void DrawChargeSettings()
    {
        Draw("_distance");
        Draw("_duration");
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
                Draw("_effectOffset");
        }

        // 범위 스킬은 공통 Hit Effect를 사용하지 않으며
        // 이동 스킬은 피해 적중 이벤트를 발생시키지 않음
        bool usesHitEffect =
            target is TargetDamageSkillDefinition ||
            target is ChargeAttackSkillDefinition;

        if (usesHitEffect)
        {
            Draw("_hitEffectPrefab");

            if (HasReference("_hitEffectPrefab"))
                Draw("_hitEffectRotation");
        }

        if (target is MovementSkillDefinition)
            Draw("_arrivalEffectPrefab");
    }

    void DrawConfigurationHelp()
    {
        SkillTargetMode targetMode =
            (SkillTargetMode)_properties["_targetMode"].intValue;

        SkillAimMode aimMode =
            (SkillAimMode)_properties["_aimMode"].intValue;

        if (aimMode == SkillAimMode.SelectedTarget &&
            targetMode == SkillTargetMode.None)
        {
            EditorGUILayout.HelpBox(
                "Aim Mode가 Selected Target이면 " +
                "Target Mode에서 대상 선택 방식을 지정해야 합니다.",
                MessageType.Error);
        }

        bool requiresRequestedDirection =
            target is MovementSkillDefinition ||
            target is ChargeAttackSkillDefinition;

        if (target is AreaDamageSkillDefinition)
        {
            AreaPlacementMode placementMode =
                (AreaPlacementMode)_properties["_placementMode"].intValue;

            AreaDeliveryMode deliveryMode =
                (AreaDeliveryMode)_properties["_deliveryMode"].intValue;

            if (placementMode == AreaPlacementMode.RequestedWorldPosition)
                requiresRequestedDirection = true;

            if (deliveryMode == AreaDeliveryMode.Projectile &&
                placementMode != AreaPlacementMode.RequestedWorldPosition)
            {
                EditorGUILayout.HelpBox(
                    "범위 스킬의 Projectile 전달 방식은 " +
                    "Requested World Position 배치 방식이 필요합니다.",
                    MessageType.Error);
            }
        }

        if (requiresRequestedDirection &&
            (targetMode != SkillTargetMode.None ||
             aimMode != SkillAimMode.RequestedDirection))
        {
            EditorGUILayout.HelpBox(
                "이 스킬 설정에서는 Target Mode를 None, " +
                "Aim Mode를 Requested Direction으로 지정해야 합니다.",
                MessageType.Error);
        }
    }

    /// <summary>
    /// 공통 설정과 스킬 종류별 조건부 항목 표시
    /// </summary>
    public override void OnInspectorGUI()
    {
        Type definitionType = target.GetType();

        bool supported =
            definitionType == typeof(TargetDamageSkillDefinition) ||
            definitionType == typeof(AreaDamageSkillDefinition) ||
            definitionType == typeof(MovementSkillDefinition) ||
            definitionType == typeof(ChargeAttackSkillDefinition);

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

        DrawEffects();
        DrawConfigurationHelp();

        serializedObject.ApplyModifiedProperties();
    }
}