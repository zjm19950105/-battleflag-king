using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
    /// <summary>Executes structured skill effects from JSON for active and passive skills.</summary>
    public class SkillEffectExecutor
    {
        public List<string> ExecuteActionEffects(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            IReadOnlyList<SkillEffectData> effects,
            string sourceSkillId)
        {
            var logs = new List<string>();
            foreach (var effect in effects ?? Array.Empty<SkillEffectData>())
            {
                if (IsCalculationEffect(effect.EffectType))
                    continue;

                ExecuteEffect(context, caster, targets, effect, sourceSkillId, null, null, logs);
            }
            return logs;
        }

        public List<string> ExecuteCalculationEffects(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            IReadOnlyList<SkillEffectData> effects,
            string sourceSkillId,
            DamageCalculation calculation,
            SkillEffectExecutionState state)
        {
            var logs = new List<string>();
            foreach (var effect in effects ?? Array.Empty<SkillEffectData>())
            {
                if (!IsCalculationEffect(effect.EffectType))
                    continue;

                ExecuteEffect(context, caster, targets, effect, sourceSkillId, calculation, state, logs);
            }
            return logs;
        }

        private static bool IsCalculationEffect(string effectType)
        {
            return effectType == "ModifyDamageCalc" || effectType == "ConsumeCounter";
        }

        private void ExecuteEffect(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            SkillEffectData effect,
            string sourceSkillId,
            DamageCalculation calculation,
            SkillEffectExecutionState state,
            List<string> logs)
        {
            var parameters = effect.Parameters ?? new Dictionary<string, object>();

            switch (effect.EffectType)
            {
                case "ModifyDamageCalc":
                    ApplyDamageCalculation(parameters, caster, calculation, state, logs);
                    break;
                case "AddBuff":
                    ApplyBuff(context, caster, targets, parameters, sourceSkillId, calculation, logs);
                    break;
                case "RecoverAp":
                    ApplyRecoverAp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RecoverPp":
                    ApplyRecoverPp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RecoverHp":
                case "Heal":
                    ApplyRecoverHp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "StatusAilment":
                    ApplyStatusAilment(context, caster, targets, parameters, calculation, logs);
                    break;
                case "ModifyCounter":
                    ApplyModifyCounter(caster, parameters, logs);
                    break;
                case "ConsumeCounter":
                    ApplyConsumeCounter(caster, parameters, calculation, state, logs);
                    break;
                default:
                    logs.Add($"{effect.EffectType}: unsupported");
                    break;
            }
        }

        private static void ApplyDamageCalculation(
            Dictionary<string, object> parameters,
            BattleUnit caster,
            DamageCalculation calculation,
            SkillEffectExecutionState state,
            List<string> logs)
        {
            if (calculation == null)
                return;

            if (GetBool(parameters, "ForceHit", false))
            {
                calculation.ForceHit = true;
                logs.Add("ForceHit");
            }

            if (GetBool(parameters, "ForceEvasion", false))
            {
                calculation.ForceEvasion = true;
                logs.Add("ForceEvasion");
            }

            if (GetBool(parameters, "CannotBeBlocked", false))
            {
                calculation.CannotBeBlocked = true;
                logs.Add("CannotBeBlocked");
            }

            if (GetBool(parameters, "CannotBeCovered", false))
            {
                calculation.CannotBeCovered = true;
                logs.Add("CannotBeCovered");
            }

            if (TryGetInt(parameters, "HitCount", out int hitCount))
            {
                calculation.HitCount = Math.Max(1, hitCount);
                logs.Add($"HitCount={calculation.HitCount}");
            }

            if (TryGetFloat(parameters, "IgnoreDefenseRatio", out float ignoreDefenseRatio))
            {
                calculation.IgnoreDefenseRatio = Math.Clamp(ignoreDefenseRatio, 0f, 1f);
                logs.Add($"IgnoreDefense={calculation.IgnoreDefenseRatio:0.##}");
            }

            if (TryGetFloat(parameters, "SkillPowerMultiplier", out float skillPowerMultiplier))
            {
                calculation.SkillPowerMultiplier *= skillPowerMultiplier;
                logs.Add($"PowerMultiplier={calculation.SkillPowerMultiplier:0.##}");
            }

            if (TryGetString(parameters, "CounterPowerBonus", out string counterKey)
                && !string.IsNullOrWhiteSpace(counterKey)
                && state?.HasConsumedCounter(counterKey) != true)
            {
                int powerPerCounter = GetInt(parameters, "powerPerCounter", 30);
                int current = caster.GetCounter(counterKey);
                calculation.CounterPowerBonus += current * powerPerCounter;
                logs.Add($"{counterKey}Power+{current * powerPerCounter}");
            }
        }

        private static void ApplyBuff(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            DamageCalculation calculation,
            List<string> logs)
        {
            string stat = GetString(parameters, "stat", "Str");
            float ratio = GetFloat(parameters, "ratio", 0.2f);
            int turns = GetInt(parameters, "turns", 1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int before = target.GetCurrentStat(stat);
                BuffManager.ApplyBuff(target, new Buff
                {
                    SkillId = sourceSkillId,
                    TargetStat = stat,
                    Ratio = ratio,
                    RemainingTurns = turns,
                    IsPureBuffOrDebuff = true
                });
                logs.Add($"{target.Data.Name}.{stat} {before}->{target.GetCurrentStat(stat)}");
            }
        }

        private static void ApplyRecoverAp(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int amount = GetInt(parameters, "amount", 1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int before = target.CurrentAp;
                target.RecoverAp(amount);
                logs.Add($"{target.Data.Name}.AP {before}->{target.CurrentAp}");
            }
        }

        private static void ApplyRecoverPp(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int amount = GetInt(parameters, "amount", 1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int before = target.CurrentPp;
                target.RecoverPp(amount);
                logs.Add($"{target.Data.Name}.PP {before}->{target.CurrentPp}");
            }
        }

        private static void ApplyRecoverHp(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int percent = GetInt(parameters, "amount", 25);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int maxHp = target.Data.BaseStats.GetValueOrDefault("HP", 1);
                int before = target.CurrentHp;
                int heal = Math.Max(1, (int)(maxHp * percent / 100f));
                target.CurrentHp = Math.Min(maxHp, target.CurrentHp + heal);
                logs.Add($"{target.Data.Name}.HP {before}->{target.CurrentHp}");
            }
        }

        private static void ApplyStatusAilment(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            string ailmentName = GetString(parameters, "ailment", GetString(parameters, "status", ""));
            if (!Enum.TryParse<StatusAilment>(ailmentName, true, out var ailment))
                return;

            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                if (!target.Ailments.Contains(ailment))
                    target.Ailments.Add(ailment);
                calculation?.AppliedAilments.Add(ailment);
                logs.Add($"{target.Data.Name}.{ailment}");
            }
        }

        private static void ApplyModifyCounter(
            BattleUnit caster,
            Dictionary<string, object> parameters,
            List<string> logs)
        {
            string key = GetString(parameters, "key", GetString(parameters, "counter", "sprite"));
            int delta = GetInt(parameters, "delta", GetInt(parameters, "amount", 1));
            caster.ModifyCounter(key, delta);
            logs.Add($"{key}={caster.GetCounter(key)}");
        }

        private static void ApplyConsumeCounter(
            BattleUnit caster,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            SkillEffectExecutionState state,
            List<string> logs)
        {
            if (calculation == null)
                return;

            string key = GetString(parameters, "key", GetString(parameters, "counter", "sprite"));
            int powerPerCounter = GetInt(parameters, "powerPerCounter", 30);
            int consumed = state?.ConsumeCounterOnce(caster, key) ?? caster.ConsumeCounter(key);
            calculation.CounterPowerBonus += consumed * powerPerCounter;
            logs.Add($"{key}Consumed={consumed}");
        }

        private static List<BattleUnit> SelectTargets(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            string defaultTarget)
        {
            string targetKind = GetString(parameters, "target", defaultTarget);
            return targetKind switch
            {
                "Self" => new List<BattleUnit> { caster },
                "Caster" => new List<BattleUnit> { caster },
                "Target" => calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "Defender" => calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "Attacker" => calculation?.Attacker != null ? new List<BattleUnit> { calculation.Attacker } : new List<BattleUnit> { caster },
                "AllTargets" => targets.Where(t => t != null).ToList(),
                "AllAllies" => context.GetAliveUnits(caster.IsPlayer),
                "AllEnemies" => context.GetAliveUnits(!caster.IsPlayer),
                _ => targets.Where(t => t != null).ToList()
            };
        }

        private static bool TryGetInt(Dictionary<string, object> parameters, string key, out int value)
        {
            if (parameters.TryGetValue(key, out var raw) && TryConvertInt(raw, out value))
                return true;
            value = 0;
            return false;
        }

        private static int GetInt(Dictionary<string, object> parameters, string key, int fallback)
        {
            return TryGetInt(parameters, key, out int value) ? value : fallback;
        }

        private static bool TryGetFloat(Dictionary<string, object> parameters, string key, out float value)
        {
            if (parameters.TryGetValue(key, out var raw) && TryConvertFloat(raw, out value))
                return true;
            value = 0f;
            return false;
        }

        private static float GetFloat(Dictionary<string, object> parameters, string key, float fallback)
        {
            return TryGetFloat(parameters, key, out float value) ? value : fallback;
        }

        private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
        {
            if (parameters.TryGetValue(key, out var raw) && raw != null)
            {
                value = raw is JsonElement element ? element.GetString() : raw.ToString();
                return !string.IsNullOrEmpty(value);
            }
            value = null;
            return false;
        }

        private static string GetString(Dictionary<string, object> parameters, string key, string fallback)
        {
            return TryGetString(parameters, key, out string value) ? value : fallback;
        }

        private static bool GetBool(Dictionary<string, object> parameters, string key, bool fallback)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw == null)
                return fallback;

            if (raw is bool b) return b;
            if (raw is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.True) return true;
                if (element.ValueKind == JsonValueKind.False) return false;
            }
            return bool.TryParse(raw.ToString(), out bool parsed) ? parsed : fallback;
        }

        private static bool TryConvertInt(object raw, out int value)
        {
            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = (int)l;
                    return true;
                case double d:
                    value = (int)d;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.Number:
                    return element.TryGetInt32(out value);
                default:
                    return int.TryParse(raw.ToString(), out value);
            }
        }

        private static bool TryConvertFloat(object raw, out float value)
        {
            switch (raw)
            {
                case float f:
                    value = f;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case int i:
                    value = i;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.Number:
                    value = element.GetSingle();
                    return true;
                default:
                    return float.TryParse(raw.ToString(), out value);
            }
        }
    }
}
