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
        private readonly Action<PendingAction> _enqueueAction;

        public SkillEffectExecutor(Action<PendingAction> enqueueAction = null)
        {
            _enqueueAction = enqueueAction;
        }

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
            return effectType == "ModifyDamageCalc"
                || effectType == "ConsumeCounter"
                || effectType == "CoverAlly";
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
                case "AddDebuff":
                    ApplyBuff(context, caster, targets, parameters, sourceSkillId, calculation, logs, forceDebuff: true);
                    break;
                case "RemoveBuff":
                case "RemoveDebuff":
                case "CleanseDebuff":
                    ApplyRemoveBuff(context, caster, targets, parameters, calculation, effect.EffectType, logs);
                    break;
                case "RecoverAp":
                    ApplyRecoverAp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "ApDamage":
                    ApplyApDamage(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RecoverPp":
                    ApplyRecoverPp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "PpDamage":
                    ApplyPpDamage(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RecoverHp":
                case "Heal":
                    ApplyRecoverHp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "HealRatio":
                    ApplyHealRatio(context, caster, targets, parameters, calculation, logs);
                    break;
                case "StatusAilment":
                    ApplyStatusAilment(context, caster, targets, parameters, calculation, logs);
                    break;
                case "TemporalMark":
                    ApplyTemporalMark(context, caster, targets, parameters, sourceSkillId, calculation, logs);
                    break;
                case "ModifyCounter":
                    ApplyModifyCounter(context, caster, targets, parameters, calculation, logs);
                    break;
                case "ConsumeCounter":
                    ApplyConsumeCounter(caster, parameters, calculation, state, logs);
                    break;
                case "GrantSkill":
                    ApplyGrantSkill(context, caster, targets, parameters, calculation, logs);
                    break;
                case "CoverAlly":
                    ApplyCoverAlly(caster, calculation, logs);
                    break;
                case "CounterAttack":
                    ApplyPendingAction(PendingActionType.Counter, caster, targets, parameters, logs);
                    break;
                case "PursuitAttack":
                    ApplyPendingAction(PendingActionType.Pursuit, caster, targets, parameters, logs);
                    break;
                case "PreemptiveAttack":
                    ApplyPendingAction(PendingActionType.Preemptive, caster, targets, parameters, logs);
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

            if (parameters.ContainsKey("ForceBlock"))
            {
                calculation.ForceBlock = GetBool(parameters, "ForceBlock", true);
                logs.Add($"ForceBlock={calculation.ForceBlock}");
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

            if (TryGetFloat(parameters, "DamageMultiplier", out float damageMultiplier))
            {
                calculation.DamageMultiplier *= damageMultiplier;
                logs.Add($"DamageMultiplier={calculation.DamageMultiplier:0.##}");
            }

            if (GetBool(parameters, "NullifyPhysicalDamage", false))
            {
                calculation.NullifyPhysicalDamage = true;
                logs.Add("NullifyPhysicalDamage");
            }

            if (GetBool(parameters, "NullifyMagicalDamage", false))
            {
                calculation.NullifyMagicalDamage = true;
                logs.Add("NullifyMagicalDamage");
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
            List<string> logs,
            bool forceDebuff = false)
        {
            string stat = GetString(parameters, "stat", "Str");
            int flatAmount = GetInt(parameters, "amount", 0);
            float ratio = GetFloat(parameters, "ratio", parameters.ContainsKey("amount") ? 0f : 0.2f);
            if (forceDebuff)
            {
                ratio = -Math.Abs(ratio);
                flatAmount = -Math.Abs(flatAmount);
            }
            int turns = GetInt(parameters, "turns", 1);
            bool isOneTime = GetBool(parameters, "oneTime", false);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int before = target.GetCurrentStat(stat);
                BuffManager.ApplyBuff(target, new Buff
                {
                    SkillId = sourceSkillId,
                    TargetStat = stat,
                    Ratio = ratio,
                    FlatAmount = flatAmount,
                    RemainingTurns = turns,
                    IsOneTime = isOneTime,
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

        private static void ApplyApDamage(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int amount = GetInt(parameters, "amount", 1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int before = target.CurrentAp;
                target.ConsumeAp(amount);
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

        private static void ApplyPpDamage(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int amount = GetInt(parameters, "amount", 1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int before = target.CurrentPp;
                target.ConsumePp(amount);
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

        private static void ApplyHealRatio(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            float ratio = NormalizeRatio(GetFloat(parameters, "ratio", 0.25f));
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int maxHp = target.Data.BaseStats.GetValueOrDefault("HP", 1);
                int before = target.CurrentHp;
                int heal = Math.Max(1, (int)(maxHp * ratio));
                target.CurrentHp = Math.Min(maxHp, target.CurrentHp + heal);
                logs.Add($"{target.Data.Name}.HP {before}->{target.CurrentHp}");
            }
        }

        private static void ApplyRemoveBuff(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            string effectType,
            List<string> logs)
        {
            string kind = GetString(parameters, "kind", effectType == "CleanseDebuff" || effectType == "RemoveDebuff" ? "Debuff" : "All");
            string stat = GetString(parameters, "stat", null);
            int count = GetInt(parameters, "count", int.MaxValue);

            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int removed = 0;
                for (int i = target.Buffs.Count - 1; i >= 0 && removed < count; i--)
                {
                    var buff = target.Buffs[i];
                    if (!MatchesBuffRemoval(buff, kind, stat))
                        continue;

                    target.Buffs.RemoveAt(i);
                    removed++;
                }
                logs.Add($"{target.Data.Name}.BuffRemoved={removed}");
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

        private static void ApplyTemporalMark(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            DamageCalculation calculation,
            List<string> logs)
        {
            string key = GetString(parameters, "key", "OneTimeImmunity");
            int count = GetInt(parameters, "count", 1);
            int turns = GetInt(parameters, "turns", -1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                target.AddTemporal(key, count, turns, sourceSkillId);
                logs.Add($"{target.Data.Name}.{key}={count}");
            }
        }

        private static void ApplyModifyCounter(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            string key = GetString(parameters, "key", GetString(parameters, "counter", "sprite"));
            int delta = GetInt(parameters, "delta", GetInt(parameters, "amount", 1));
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                target.ModifyCounter(key, delta);
                logs.Add($"{target.Data.Name}.{key}={target.GetCounter(key)}");
            }
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

        private static void ApplyGrantSkill(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            var skillIds = GetStringList(parameters, "skillIds");
            if (TryGetString(parameters, "skillId", out string singleSkillId))
                skillIds.Add(singleSkillId);

            bool isPassive = string.Equals(GetString(parameters, "skillType", "Active"), "Passive", StringComparison.OrdinalIgnoreCase);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                foreach (var skillId in skillIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
                {
                    target.GrantTemporarySkill(skillId, isPassive);
                    logs.Add($"{target.Data.Name}.GrantSkill={skillId}");
                }
            }
        }

        private static void ApplyCoverAlly(BattleUnit caster, DamageCalculation calculation, List<string> logs)
        {
            if (calculation == null || calculation.CannotBeCovered)
                return;

            calculation.CoverTarget = caster;
            logs.Add($"Cover={caster.Data.Name}");
        }

        private void ApplyPendingAction(
            PendingActionType type,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            List<string> logs)
        {
            if (_enqueueAction == null)
            {
                logs.Add($"{type}: no queue");
                return;
            }

            var pendingTargets = targets.Where(t => t != null).ToList();
            if (pendingTargets.Count == 0)
                return;

            var action = new PendingAction
            {
                Type = type,
                Actor = caster,
                Targets = pendingTargets,
                Power = GetInt(parameters, "power", 75),
                HitRate = parameters.ContainsKey("hitRate") ? GetInt(parameters, "hitRate", 100) : null,
                DamageType = ParseEnum(GetString(parameters, "damageType", "Physical"), SkillType.Physical),
                AttackType = ParseEnum(GetString(parameters, "attackType", "Melee"), AttackType.Melee),
                TargetType = ParseEnum(GetString(parameters, "targetType", "SingleEnemy"), TargetType.SingleEnemy),
                Tags = GetStringList(parameters, "tags")
            };
            _enqueueAction(action);
            logs.Add($"{type} queued");
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
            return targetKind.ToLowerInvariant() switch
            {
                "self" => new List<BattleUnit> { caster },
                "caster" => new List<BattleUnit> { caster },
                "target" => calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "defender" => calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "attacker" => calculation?.Attacker != null ? new List<BattleUnit> { calculation.Attacker } : targets.Where(t => t != null).ToList(),
                "alltargets" => targets.Where(t => t != null).ToList(),
                "allallies" => context.GetAliveUnits(caster.IsPlayer),
                "allies" => context.GetAliveUnits(caster.IsPlayer),
                "allenemies" => context.GetAliveUnits(!caster.IsPlayer),
                "enemies" => context.GetAliveUnits(!caster.IsPlayer),
                "lowesthpally" => context.GetAliveUnits(caster.IsPlayer).OrderBy(u => u.CurrentHp).Take(1).ToList(),
                "highesthpally" => context.GetAliveUnits(caster.IsPlayer).OrderByDescending(u => u.CurrentHp).Take(1).ToList(),
                "randomally" => SelectRandomUnit(context.GetAliveUnits(caster.IsPlayer)),
                _ => targets.Where(t => t != null).ToList()
            };
        }

        private static bool MatchesBuffRemoval(Buff buff, string kind, string stat)
        {
            if (!string.IsNullOrWhiteSpace(stat) && buff.TargetStat != stat)
                return false;

            return kind.ToLowerInvariant() switch
            {
                "buff" => buff.Ratio > 0,
                "debuff" => buff.Ratio < 0,
                _ => true
            };
        }

        private static float NormalizeRatio(float ratio)
        {
            if (ratio > 1f)
                ratio /= 100f;
            return Math.Clamp(ratio, 0f, 1f);
        }

        private static List<BattleUnit> SelectRandomUnit(List<BattleUnit> units)
        {
            return units.Count == 0
                ? new List<BattleUnit>()
                : new List<BattleUnit> { units[Random.Shared.Next(units.Count)] };
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

        private static List<string> GetStringList(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw == null)
                return new List<string>();

            if (raw is List<string> list)
                return new List<string>(list);

            if (raw is string s)
                return new List<string> { s };

            if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

            return new List<string>();
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
        {
            return Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : fallback;
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
