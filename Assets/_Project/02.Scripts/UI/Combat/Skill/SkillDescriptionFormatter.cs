using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// 스킬 설명의 고정 번호와 표시 레벨에 따른 툴팁 문구
/// </summary>
public static class SkillDescriptionFormatter
{
    static readonly Regex _placeholder = new Regex(@"\{([0-9]+)\}");

    static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Change(float current, float next)
    {
        if (current == next) return Number(current);

        // 표시 자릿수에서 달라진 만큼만 괄호 안에 표기
        double before = Math.Round(current, 2, MidpointRounding.AwayFromZero);
        double after = Math.Round(next, 2, MidpointRounding.AwayFromZero);
        if (before == after) return Number(next);

        return FormattableString.Invariant($"{after:0.##}({before:0.##}{after - before:+0.##;-0.##})");
    }

    static string Damage(float baseAttack, float baseSpell, SkillGrowthBonus current, SkillGrowthBonus next)
    {
        (float attack, float spell) = SkillDamageResolver.GetMultipliers(baseAttack, baseSpell, current);
        (float nextAttack, float nextSpell) = SkillDamageResolver.GetMultipliers(baseAttack, baseSpell, next);
        if (spell == 0f && nextSpell == 0f) return $"공격력의 {Change(attack * 100f, nextAttack * 100f)}%";
        if (attack == 0f && nextAttack == 0f) return $"주문력의 {Change(spell * 100f, nextSpell * 100f)}%";
        return $"(공격력의 {Change(attack * 100f, nextAttack * 100f)}% + 주문력의 {Change(spell * 100f, nextSpell * 100f)}%)";
    }

    static string Damage(AreaEffectDefinition effect, SkillGrowthBonus current, SkillGrowthBonus next)
    {
        return Damage(effect.DamageMultiplier, effect.SpellPowerMultiplier, current, next);
    }

    static string Cooldown(SkillDefinition skill, int level, bool previewUpgrade)
    {
        float current = skill.GetCooldown(level);
        return previewUpgrade ? Change(current, skill.GetCooldown(level + 1)) : Number(current);
    }

    /// <summary>
    /// 스킬 이름과 현재 레벨의 재사용 시간
    /// </summary>
    /// <param name="skill">표시할 스킬 정의</param>
    /// <param name="level">표시할 스킬 레벨</param>
    /// <param name="previewUpgrade">다음 단계 재사용 시간과의 비교 표시 여부</param>
    /// <returns>툴팁 제목</returns>
    public static string FormatTitle(SkillDefinition skill, int level, bool previewUpgrade = false)
    {
        return $"{skill.SkillName}   {Cooldown(skill, level, previewUpgrade)}초";
    }

    /// <summary>
    /// 설명에 사용된 번호를 해당 스킬의 판정과 성장값으로 치환
    /// </summary>
    /// <param name="skill">설명 원문과 판정 데이터를 가진 스킬 정의</param>
    /// <param name="level">누적 성장값을 조회할 레벨</param>
    /// <param name="previewUpgrade">다음 단계 수치와의 비교 표시 여부</param>
    /// <returns>표시용 설명 문구</returns>
    public static string FormatDescription(SkillDefinition skill, int level, bool previewUpgrade = false)
    {
        string template = skill.SkillDescription;
        if (string.IsNullOrEmpty(template) || !template.Contains("{")) return template;

        SkillGrowthBonus growth = skill.GetGrowth(level);
        SkillGrowthBonus nextGrowth = previewUpgrade ? skill.GetGrowth(level + 1) : growth;
        DashStrikeSkillDefinition dash = skill as DashStrikeSkillDefinition;
        TargetDamageSkillDefinition projectile = skill as TargetDamageSkillDefinition;
        AreaEffectDefinition impact = projectile?.OnHitAreaEffect;
        AreaEffectDefinition area = (skill as AreaDamageSkillDefinition)?.AreaEffect;

        return _placeholder.Replace(template, match => match.Groups[1].Value switch
        {
            "0" => Cooldown(skill, level, previewUpgrade),
            "1" => Number(skill.CastTime),
            "2" => Number(skill.PostCastMoveLockDuration),
            "3" => Number(skill.Range),
            "4" when dash != null => Number(dash.Distance),
            "5" when dash != null => Number(dash.ArrivalAreaEffect.HitsPerPulse),
            "6" when dash != null => Damage(dash.ArrivalAreaEffect, growth, nextGrowth),
            "7" when projectile != null => Damage(projectile.DamageMultiplier, projectile.SpellPowerMultiplier, growth, nextGrowth),
            "8" when impact != null && impact.Hitbox.HitShape == CombatHitShape.Sphere => Number(impact.Hitbox.Radius),
            "9" when impact != null => Number(impact.HitsPerPulse),
            "10" when impact != null => Damage(impact, growth, nextGrowth),
            "11" when area != null && area.Hitbox.HitShape == CombatHitShape.Sphere => Number(area.Hitbox.Radius),
            "12" when area != null && area.Hitbox.HitShape == CombatHitShape.Box => Number(area.Hitbox.BoxSize.z),
            "13" when area != null && area.EndMode == AreaEffectEndMode.Duration => Number(area.Duration),
            "14" when area != null => Number(area.PulseInterval),
            "15" when area != null => Number(area.HasImmediatePulse ? 0f : area.PulseInterval),
            "16" when area != null => Number(area.HitsPerPulse),
            "17" when area != null => Damage(area, growth, nextGrowth),
            _ => throw new FormatException($"{skill.name}: 설명 번호 {match.Value}에 맞는 스킬 판정 설정 필요")
        });
    }
}
