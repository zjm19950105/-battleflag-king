using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public static class SandboxTooltipHelper
    {
        private const int TooltipFontSize = 15;

        public static void AttachActiveSkillTooltip(Control control, ActiveSkillData skill)
        {
            if (control == null) return;
            control.TooltipText = BuildActiveSkillDetail(skill);
            TryApplyTooltipFontSize(control);
        }

        public static void AttachPassiveSkillTooltip(Control control, PassiveSkillData skill)
        {
            if (control == null) return;
            control.TooltipText = BuildPassiveSkillDetail(skill);
            TryApplyTooltipFontSize(control);
        }

        public static void AttachEquipmentTooltip(Control control, EquipmentData equipment, GameDataRepository gameData = null)
        {
            if (control == null) return;
            control.TooltipText = BuildEquipmentDetail(equipment, gameData);
            TryApplyTooltipFontSize(control);
        }

        public static string BuildActiveSkillDetail(ActiveSkillData skill)
        {
            return skill == null ? "" : BuildActiveSkillTooltip(skill);
        }

        public static string BuildPassiveSkillDetail(PassiveSkillData skill)
        {
            return skill == null ? "" : BuildPassiveSkillTooltip(skill);
        }

        public static string BuildEquipmentDetail(EquipmentData equipment, GameDataRepository gameData = null)
        {
            return equipment == null ? "" : BuildEquipmentTooltip(equipment, gameData);
        }

        private static string BuildActiveSkillTooltip(ActiveSkillData skill)
        {
            var lines = new List<string> { SafeText(skill.Name, skill.Id) };
            int hitCount = ExtractHitCount(skill.Effects);

            lines.Add($"消耗：AP {skill.ApCost}");
            lines.Add($"类型：{SkillTypeLabel(skill.Type)} / {AttackTypeLabel(skill.AttackType)}");
            lines.Add($"威力：{FormatActivePower(skill)}");
            lines.Add($"攻击次数：{hitCount}");
            lines.Add($"命中：{FormatHitRate(skill.HitRate, HasSureHit(skill.Tags, skill.Effects))}");
            lines.Add($"目标：{TargetTypeLabel(skill.TargetType)}");

            AddDescription(lines, skill.EffectDescription);
            AddLearnInfo(lines, skill.LearnCondition, skill.UnlockLevel);

            return string.Join("\n", lines);
        }

        private static string BuildPassiveSkillTooltip(PassiveSkillData skill)
        {
            var lines = new List<string> { SafeText(skill.Name, skill.Id) };
            var attack = ExtractPassiveAttackSpec(skill);

            lines.Add($"消耗：PP {skill.PpCost}");
            lines.Add($"触发时机：{TriggerTimingLabel(skill.TriggerTiming)}");
            lines.Add($"类型：{SkillTypeLabel(skill.Type)}");
            if (attack.Power.HasValue)
                lines.Add($"威力：{FormatPower(attack.Power.Value)}");
            if (attack.Power.HasValue)
                lines.Add($"攻击次数：{attack.HitCount}");
            if (attack.Power.HasValue || attack.HitRate.HasValue || attack.ForceHit)
                lines.Add($"命中：{FormatHitRate(attack.HitRate, attack.ForceHit)}");
            if (skill.HasSimultaneousLimit)
                lines.Add("限制：同一时机只发动一次");

            AddDescription(lines, skill.EffectDescription);
            AddLearnInfo(lines, skill.LearnCondition, skill.UnlockLevel);

            return string.Join("\n", lines);
        }

        private static string BuildEquipmentTooltip(EquipmentData equipment, GameDataRepository gameData)
        {
            var lines = new List<string> { SafeText(equipment.Name, equipment.Id) };

            lines.Add($"类别：{EquipmentCategoryLabel(equipment.Category)}");
            AddBaseStats(lines, equipment.BaseStats);
            AddStringList(lines, "特殊效果", equipment.SpecialEffects);
            AddGrantedSkills(lines, "附带主动", equipment.GrantedActiveSkillIds, id => gameData?.GetActiveSkill(id)?.Name);
            AddGrantedSkills(lines, "附带被动", equipment.GrantedPassiveSkillIds, id => gameData?.GetPassiveSkill(id)?.Name);
            AddUnitClasses(lines, "可用兵种", equipment.UsableByClasses);
            AddStringList(lines, "限制职业", equipment.RestrictedClassIds);

            return string.Join("\n", lines);
        }

        private static void AddDescription(List<string> lines, string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return;
            lines.Add("");
            lines.Add("效果描述：");
            lines.Add(description.Trim());
        }

        private static void AddTagsAndEffects(List<string> lines, List<string> tags, List<SkillEffectData> effects)
        {
            bool hasTags = tags != null && tags.Any(t => !string.IsNullOrWhiteSpace(t));
            bool hasEffects = effects != null && effects.Count > 0;
            if (!hasTags && !hasEffects) return;

            lines.Add("");
            if (hasTags)
                lines.Add("Tags：" + string.Join(", ", tags.Where(t => !string.IsNullOrWhiteSpace(t))));

            if (!hasEffects) return;

            lines.Add("Effects：");
            foreach (var effect in effects)
            {
                if (effect == null) continue;

                string type = SafeText(effect.EffectType, "Unknown");
                string parameters = FormatParameters(effect.Parameters);
                lines.Add(string.IsNullOrEmpty(parameters)
                    ? $"  - {EffectTypeLabel(type)}"
                    : $"  - {EffectTypeLabel(type)}（{parameters}）");
            }
        }

        private static void AddLearnInfo(List<string> lines, string learnCondition, int? unlockLevel)
        {
            var info = new List<string>();
            if (!string.IsNullOrWhiteSpace(learnCondition))
                info.Add(learnCondition.Trim());
            if (unlockLevel.HasValue)
                info.Add($"Lv{unlockLevel.Value}");

            if (info.Count == 0) return;

            lines.Add("");
            lines.Add("习得：" + string.Join(" / ", info));
        }

        private static void AddBaseStats(List<string> lines, Dictionary<string, int> baseStats)
        {
            var stats = baseStats?
                .Where(kv => kv.Value != 0)
                .Select(kv => $"{StatLabel(kv.Key)} {FormatSigned(kv.Value)}")
                .ToList();

            if (stats == null || stats.Count == 0) return;

            lines.Add("");
            lines.Add("装备属性：" + string.Join("，", stats));
        }

        private static void AddStringList(List<string> lines, string label, List<string> values)
        {
            var cleanValues = values?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();

            if (cleanValues == null || cleanValues.Count == 0) return;

            lines.Add($"{label}：" + string.Join("，", cleanValues));
        }

        private static void AddGrantedSkills(List<string> lines, string label, List<string> ids, Func<string, string> resolveName)
        {
            var skillNames = ids?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id =>
                {
                    string cleanId = id.Trim();
                    string name = resolveName?.Invoke(cleanId);
                    return string.IsNullOrWhiteSpace(name) ? cleanId : $"{name}（{cleanId}）";
                })
                .ToList();

            if (skillNames == null || skillNames.Count == 0) return;

            lines.Add($"{label}：" + string.Join("，", skillNames));
        }

        private static void AddUnitClasses(List<string> lines, string label, List<UnitClass> classes)
        {
            if (classes == null || classes.Count == 0) return;
            lines.Add($"{label}：" + string.Join("，", classes.Select(UnitClassLabel)));
        }

        private static string FormatParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0) return "";

            return string.Join("，", parameters
                .Where(kv => kv.Value != null)
                .Select(kv => $"{ParameterLabel(kv.Key)}={FormatParameterValue(kv.Key, kv.Value)}")
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string FormatParameterValue(string key, object value)
        {
            if (value == null) return "";

            if (value is JsonElement json)
                return FormatJsonElement(key, json);

            if (value is bool b)
                return b ? "是" : "否";

            if (value is string s)
                return ParameterValueLabel(key, s);

            if (value is float f)
                return FormatNumber(f);

            if (value is double d)
                return FormatNumber(d);

            if (value is decimal m)
                return FormatNumber(m);

            if (value is IEnumerable enumerable && value is not string)
            {
                var values = new List<string>();
                foreach (var item in enumerable)
                    values.Add(FormatParameterValue(key, item));
                return "[" + string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v))) + "]";
            }

            return value.ToString();
        }

        private static string FormatJsonElement(string key, JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => ParameterValueLabel(key, value.GetString()),
                JsonValueKind.Number => value.TryGetInt32(out int i)
                    ? i.ToString(CultureInfo.InvariantCulture)
                    : FormatNumber(value.GetDouble()),
                JsonValueKind.True => "是",
                JsonValueKind.False => "否",
                JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(v => FormatJsonElement(key, v))) + "]",
                JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(p => $"{ParameterLabel(p.Name)}={FormatJsonElement(p.Name, p.Value)}")) + "}",
                _ => ""
            };
        }

        private static string FormatActivePower(ActiveSkillData skill)
        {
            if (skill.PhysicalPower.HasValue || skill.MagicalPower.HasValue)
            {
                var parts = new List<string>();
                if (skill.PhysicalPower.HasValue)
                    parts.Add("物理 " + FormatPower(skill.PhysicalPower.Value));
                if (skill.MagicalPower.HasValue)
                    parts.Add("魔法 " + FormatPower(skill.MagicalPower.Value));
                return string.Join(" / ", parts);
            }

            return FormatPower(skill.Power);
        }

        private static string FormatPower(int power)
        {
            return power > 0 ? power.ToString(CultureInfo.InvariantCulture) : "无";
        }

        private static string FormatHitRate(int? hitRate, bool forceHit)
        {
            if (forceHit) return "必中";
            return hitRate.HasValue ? hitRate.Value + "%" : "100%（默认）";
        }

        private static int ExtractHitCount(IEnumerable<SkillEffectData> effects)
        {
            foreach (var effect in effects ?? Enumerable.Empty<SkillEffectData>())
            {
                if (TryGetInt(effect.Parameters, "HitCount", out int hitCount)
                    || TryGetInt(effect.Parameters, "hitCount", out hitCount))
                {
                    return Math.Max(1, hitCount);
                }
            }

            return 1;
        }

        private static PassiveAttackSpec ExtractPassiveAttackSpec(PassiveSkillData skill)
        {
            int? power = skill.Power;
            int? hitRate = skill.HitRate;
            int hitCount = 1;
            bool forceHit = HasSureHit(skill.Tags, skill.Effects);

            foreach (var effect in skill.Effects ?? Enumerable.Empty<SkillEffectData>())
            {
                if (effect == null || !IsPassiveAttackEffect(effect.EffectType)) continue;

                if (!power.HasValue && TryGetInt(effect.Parameters, "power", out int effectPower))
                    power = effectPower;
                if (!hitRate.HasValue && TryGetInt(effect.Parameters, "hitRate", out int effectHitRate))
                    hitRate = effectHitRate;
                if (TryGetInt(effect.Parameters, "HitCount", out int effectHitCount)
                    || TryGetInt(effect.Parameters, "hitCount", out effectHitCount))
                    hitCount = Math.Max(1, effectHitCount);

                forceHit |= HasSureHitInParameters(effect.Parameters);
            }

            return new PassiveAttackSpec(power, hitRate, hitCount, forceHit);
        }

        private static bool IsPassiveAttackEffect(string effectType)
        {
            return effectType is "PreemptiveAttack" or "CounterAttack" or "BattleEndAttack";
        }

        private static bool HasSureHit(List<string> tags, IEnumerable<SkillEffectData> effects)
        {
            if (tags?.Any(tag => string.Equals(tag, "SureHit", StringComparison.OrdinalIgnoreCase)) == true)
                return true;

            return effects?.Any(effect =>
                TryGetBool(effect.Parameters, "ForceHit", out bool forceHit) && forceHit
                || HasSureHitInParameters(effect.Parameters)) == true;
        }

        private static bool HasSureHitInParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null) return false;
            if (TryGetBool(parameters, "ForceHit", out bool forceHit) && forceHit)
                return true;
            if (!parameters.TryGetValue("tags", out var value) || value == null)
                return false;

            return EnumerateStringValues(value)
                .Any(tag => string.Equals(tag, "SureHit", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetInt(Dictionary<string, object> parameters, string key, out int value)
        {
            value = default;
            if (parameters == null || !parameters.TryGetValue(key, out var raw) || raw == null)
                return false;

            switch (raw)
            {
                case JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out value):
                    return true;
                case JsonElement { ValueKind: JsonValueKind.String } json:
                    return int.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = (int)l;
                    return true;
                case double d:
                    value = (int)d;
                    return true;
                case float f:
                    value = (int)f;
                    return true;
                case string s:
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    try
                    {
                        value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
            }
        }

        private static bool TryGetBool(Dictionary<string, object> parameters, string key, out bool value)
        {
            value = default;
            if (parameters == null || !parameters.TryGetValue(key, out var raw) || raw == null)
                return false;

            switch (raw)
            {
                case JsonElement { ValueKind: JsonValueKind.True }:
                    value = true;
                    return true;
                case JsonElement { ValueKind: JsonValueKind.False }:
                    value = false;
                    return true;
                case JsonElement { ValueKind: JsonValueKind.String } json:
                    return bool.TryParse(json.GetString(), out value);
                case bool b:
                    value = b;
                    return true;
                case string s:
                    return bool.TryParse(s, out value);
                default:
                    return false;
            }
        }

        private static IEnumerable<string> EnumerateStringValues(object value)
        {
            switch (value)
            {
                case JsonElement { ValueKind: JsonValueKind.Array } json:
                    return json.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!);
                case IEnumerable enumerable when value is not string:
                {
                    var strings = new List<string>();
                    foreach (var item in enumerable)
                    {
                        if (item is string text && !string.IsNullOrWhiteSpace(text))
                            strings.Add(text);
                    }
                    return strings;
                }
                case string text when !string.IsNullOrWhiteSpace(text):
                    return new[] { text };
                default:
                    return Enumerable.Empty<string>();
            }
        }

        private readonly record struct PassiveAttackSpec(int? Power, int? HitRate, int HitCount, bool ForceHit);

        private static string FormatSigned(int value)
        {
            return value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(float value) => FormatNumber((double)value);

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string SafeText(string text, string fallback)
        {
            return string.IsNullOrWhiteSpace(text) ? fallback ?? "" : text.Trim();
        }

        private static string SkillTypeLabel(SkillType type) => type switch
        {
            SkillType.Physical => "物理",
            SkillType.Magical => "魔法",
            SkillType.Assist => "辅助",
            SkillType.Heal => "回复",
            SkillType.Debuff => "妨害",
            _ => type.ToString()
        };

        private static string AttackTypeLabel(AttackType type) => type switch
        {
            AttackType.Melee => "近战",
            AttackType.Ranged => "远程",
            AttackType.Magic => "魔法",
            _ => type.ToString()
        };

        private static string TargetTypeLabel(TargetType type) => type switch
        {
            TargetType.Self => "自身",
            TargetType.SingleEnemy => "敌方单体",
            TargetType.SingleAlly => "友方单体",
            TargetType.TwoEnemies => "敌方2体",
            TargetType.ThreeEnemies => "敌方3体",
            TargetType.FrontAndBack => "前后列贯通",
            TargetType.Column => "一列",
            TargetType.Row => "一排",
            TargetType.AllEnemies => "敌方全体",
            TargetType.AllAllies => "友方全体",
            _ => type.ToString()
        };

        private static string TriggerTimingLabel(PassiveTriggerTiming timing) => timing switch
        {
            PassiveTriggerTiming.BattleStart => "战斗开始时",
            PassiveTriggerTiming.SelfBeforeAttack => "自身攻击前",
            PassiveTriggerTiming.AllyBeforeAttack => "友方攻击前",
            PassiveTriggerTiming.AllyBeforeHit => "友方被命中前",
            PassiveTriggerTiming.SelfBeforeHit => "自身被命中前",
            PassiveTriggerTiming.SelfBeforeMeleeHit => "自身被近战命中前",
            PassiveTriggerTiming.SelfBeforePhysicalHit => "自身被物理命中前",
            PassiveTriggerTiming.AllyOnAttacked => "友方被攻击时",
            PassiveTriggerTiming.SelfOnActiveUse => "自身使用主动技能时",
            PassiveTriggerTiming.AllyOnActiveUse => "友方使用主动技能时",
            PassiveTriggerTiming.AfterAction => "行动后",
            PassiveTriggerTiming.BattleEnd => "战斗结束时",
            PassiveTriggerTiming.OnHit => "命中时",
            PassiveTriggerTiming.OnBeingHit => "被命中时",
            PassiveTriggerTiming.OnKnockdown => "击倒时",
            _ => timing.ToString()
        };

        private static string EquipmentCategoryLabel(EquipmentCategory category) => category switch
        {
            EquipmentCategory.Sword => "剑",
            EquipmentCategory.Axe => "斧",
            EquipmentCategory.Spear => "枪",
            EquipmentCategory.Bow => "弓",
            EquipmentCategory.Staff => "杖",
            EquipmentCategory.Shield => "盾",
            EquipmentCategory.GreatShield => "大盾",
            EquipmentCategory.Accessory => "饰品",
            _ => category.ToString()
        };

        private static string UnitClassLabel(UnitClass unitClass) => unitClass switch
        {
            UnitClass.Infantry => "步兵",
            UnitClass.Cavalry => "骑兵",
            UnitClass.Flying => "飞行",
            UnitClass.Heavy => "重装",
            UnitClass.Scout => "斥候",
            UnitClass.Archer => "弓兵",
            UnitClass.Mage => "术士",
            UnitClass.Elf => "精灵",
            UnitClass.Beastman => "兽人",
            UnitClass.Winged => "有翼人",
            UnitClass.Undead => "不死系",
            _ => unitClass.ToString()
        };

        private static string StatLabel(string stat) => stat switch
        {
            "HP" => "HP",
            "Str" => "物攻",
            "Def" => "物防",
            "Mag" => "魔攻",
            "MDef" => "魔防",
            "Hit" or "hit" or "hit_rate" => "命中",
            "Eva" or "eva" => "回避",
            "Crit" or "crit" => "会心",
            "CritDmg" or "crit_dmg" => "会心伤害",
            "Block" or "block" or "block_rate" => "格挡",
            "Spd" or "spd" => "速度",
            "AP" => "AP",
            "PP" => "PP",
            "phys_atk" => "物攻",
            "phys_def" => "物防",
            "mag_atk" => "魔攻",
            "mag_def" => "魔防",
            _ => stat
        };

        private static string EffectTypeLabel(string effectType) => effectType switch
        {
            "AmplifyDebuffs" => "减益放大",
            "ModifyDamageCalc" => "伤害判定修正",
            "AddBuff" => "增益",
            "AddDebuff" => "减益",
            "RecoverAp" => "回复AP",
            "RecoverPp" => "回复PP",
            "RecoverHp" => "回复HP",
            "TransferResource" => "资源转移",
            "OnHitEffect" => "命中后效果",
            "OnKillEffect" => "击倒后效果",
            "HealRatio" => "比例治疗",
            "TemporalMark" => "临时标记",
            "PreemptiveAttack" => "先制攻击",
            "CounterAttack" => "反击",
            "CoverAlly" => "掩护",
            "BattleEndAttack" => "战斗结束攻击",
            "ModifyCounter" => "计数器修正",
            "ConsumeCounter" => "消耗计数器",
            _ => effectType
        };

        private static string ParameterLabel(string key) => key switch
        {
            "multiplier" => "倍率",
            "ForceHit" => "必中",
            "ForceBlock" => "强制格挡",
            "HitCount" => "段数",
            "IgnoreDefenseRatio" => "无视防御",
            "stat" => "属性",
            "amount" => "数值",
            "ratio" => "倍率",
            "turns" => "回合",
            "oneTime" => "一次性",
            "target" => "目标",
            "maxTargets" => "最大目标",
            "power" => "威力",
            "hitRate" => "命中",
            "damageType" => "伤害类型",
            "attackType" => "攻击方式",
            "targetType" => "目标类型",
            "tags" => "Tags",
            "key" => "标记",
            "count" => "次数",
            _ => key
        };

        private static string ParameterValueLabel(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            return key switch
            {
                "stat" => StatLabel(value),
                "target" => PassiveTargetLabel(value),
                "damageType" => SkillTypeValueLabel(value),
                "attackType" => AttackTypeValueLabel(value),
                "targetType" => TargetTypeValueLabel(value),
                _ => value
            };
        }

        private static string SkillTypeValueLabel(string value)
        {
            return Enum.TryParse(value, out SkillType type) ? SkillTypeLabel(type) : value;
        }

        private static string AttackTypeValueLabel(string value)
        {
            return Enum.TryParse(value, out AttackType type) ? AttackTypeLabel(type) : value;
        }

        private static string TargetTypeValueLabel(string value)
        {
            return Enum.TryParse(value, out TargetType type) ? TargetTypeLabel(type) : value;
        }

        private static string PassiveTargetLabel(string value) => value switch
        {
            "Self" => "自身",
            "Attacker" => "攻击者",
            "Defender" => "防御者",
            "RandomAlly" => "随机友方",
            "LowestHpAlly" => "HP最低友方",
            "HighestHpAlly" => "HP最高友方",
            "AllAllies" or "Allies" => "友方全体",
            "AllEnemies" or "Enemies" => "敌方全体",
            "ColumnAllies" => "同列友方",
            "ColumnAlliesOfTarget" => "目标同列友方",
            _ => value
        };

        private static void TryApplyTooltipFontSize(Control control)
        {
            var theme = control.Theme ?? new Theme();
            theme.SetFontSize("font_size", "TooltipLabel", TooltipFontSize);
            control.Theme = theme;
        }
    }
}
