using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Pipeline;
using BattleKing.Utils;

namespace BattleKing.Skills
{
    /// <summary>Executes structured skill effects from JSON for active and passive skills.</summary>
    public class SkillEffectExecutor
    {
        private readonly Action<PendingAction> _enqueueAction;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SkillEffectExecutor(Action<PendingAction> enqueueAction = null)
        {
            _enqueueAction = enqueueAction;
        }

        public List<string> ExecuteActionEffects(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            IReadOnlyList<SkillEffectData> effects,
            string sourceSkillId,
            DamageCalculation calculation = null)
        {
            var logs = new List<string>();
            foreach (var effect in effects ?? Array.Empty<SkillEffectData>())
            {
                if (IsCalculationEffect(effect.EffectType) || IsPostDamageEffect(effect.EffectType))
                    continue;

                ExecuteEffect(context, caster, targets, effect, sourceSkillId, calculation, null, logs);
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

        public List<string> ExecutePostDamageEffects(
            BattleContext context,
            BattleUnit caster,
            BattleUnit damageTarget,
            IReadOnlyList<SkillEffectData> effects,
            string sourceSkillId,
            DamageCalculation calculation,
            DamageResult result,
            bool killed)
        {
            var logs = new List<string>();
            foreach (var effect in effects ?? Array.Empty<SkillEffectData>())
            {
                if (!IsPostDamageEffect(effect.EffectType))
                    continue;

                var parameters = effect.Parameters ?? new Dictionary<string, object>();
                if (!PostDamageConditionMatches(effect.EffectType, parameters, result, killed))
                    continue;

                var nestedEffects = GetNestedEffects(parameters);
                foreach (var nested in nestedEffects)
                {
                    if (IsPostDamageEffect(nested.EffectType))
                        continue;

                    ExecuteEffect(
                        context,
                        caster,
                        damageTarget == null ? Array.Empty<BattleUnit>() : new List<BattleUnit> { damageTarget },
                        nested,
                        sourceSkillId,
                        calculation,
                        new SkillEffectExecutionState(),
                        logs);
                }
            }
            return logs;
        }

        private static bool IsCalculationEffect(string effectType)
        {
            return effectType == "ModifyDamageCalc"
                || effectType == "ConsumeCounter"
                || effectType == "CoverAlly";
        }

        private static bool IsPostDamageEffect(string effectType)
        {
            return effectType == "OnHitEffect"
                || effectType == "OnKillEffect";
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
            if (!EffectConditionsMatch(parameters, caster, targets, calculation))
                return;

            switch (effect.EffectType)
            {
                case "ModifyDamageCalc":
                    ApplyCalculationEffect(parameters, caster, calculation, state, effect.EffectType, logs);
                    break;
                case "AddBuff":
                case "AddDebuff":
                case "AmplifyDebuffs":
                case "RemoveBuff":
                case "RemoveDebuff":
                case "CleanseDebuff":
                    ApplyBuffEffect(context, caster, targets, parameters, sourceSkillId, calculation, effect.EffectType, logs);
                    break;
                case "RecoverAp":
                case "ApDamage":
                case "RecoverPp":
                case "PpDamage":
                    ApplyResourceEffect(context, caster, targets, parameters, calculation, effect.EffectType, logs);
                    break;
                case "TransferResource":
                    ApplyTransferResource(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RecoverHp":
                case "Heal":
                case "HealRatio":
                    ApplyHpEffect(context, caster, targets, parameters, calculation, effect.EffectType, logs);
                    break;
                case "StatusAilment":
                    ApplyStatusAilment(context, caster, targets, parameters, calculation, logs);
                    break;
                case "CleanseAilment":
                    ApplyCleanseAilment(context, caster, targets, parameters, calculation, logs);
                    break;
                case "TemporalMark":
                    ApplyTemporalMark(context, caster, targets, parameters, sourceSkillId, calculation, logs);
                    break;
                case "ForcedTarget":
                    ApplyForcedTarget(context, caster, targets, parameters, sourceSkillId, calculation, logs);
                    break;
                case "ActionOrderPriority":
                    ApplyActionOrderPriority(context, caster, targets, parameters, calculation, logs);
                    break;
                case "ModifyCounter":
                    ApplyModifyCounter(context, caster, targets, parameters, calculation, logs);
                    break;
                case "ConsumeCounter":
                case "CoverAlly":
                    ApplyCalculationEffect(parameters, caster, calculation, state, effect.EffectType, logs);
                    break;
                case "GrantSkill":
                    ApplyGrantSkill(context, caster, targets, parameters, calculation, logs);
                    break;
                case "CounterAttack":
                case "PursuitAttack":
                case "PreemptiveAttack":
                case "BattleEndAttack":
                case "PendingAttack":
                    ApplyPendingActionEffect(caster, targets, parameters, sourceSkillId, effect.EffectType, logs);
                    break;
                case "AugmentCurrentAction":
                    ApplyAugmentCurrentAction(context, caster, parameters, sourceSkillId, logs);
                    break;
                case "AugmentOutgoingActions":
                    ApplyAugmentOutgoingActions(context, caster, parameters, sourceSkillId, logs);
                    break;
                case "OnHitEffect":
                case "OnKillEffect":
                    break;
                default:
                    logs.Add($"{effect.EffectType}: unsupported");
                    break;
            }
        }

        private static void ApplyCalculationEffect(
            Dictionary<string, object> parameters,
            BattleUnit caster,
            DamageCalculation calculation,
            SkillEffectExecutionState state,
            string effectType,
            List<string> logs)
        {
            switch (effectType)
            {
                case "ModifyDamageCalc":
                    ApplyDamageCalculation(parameters, caster, calculation, state, logs);
                    break;
                case "ConsumeCounter":
                    ApplyConsumeCounter(caster, parameters, calculation, state, logs);
                    break;
                case "CoverAlly":
                    ApplyCoverAlly(caster, parameters, calculation, logs);
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

            if (!DamageCalculationConditionsMatch(parameters, caster, calculation))
                return;

            if (GetBool(parameters, "ForceHit", false))
            {
                calculation.ForceHit = true;
                logs.Add("ForceHit");
            }

            if (GetBool(parameters, "ForceCrit", false))
            {
                calculation.ForceCrit = true;
                logs.Add("ForceCrit");
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

            if (TryGetFloat(parameters, "BlockReduction", out float blockReduction))
            {
                calculation.ForcedBlockReduction = Math.Clamp(blockReduction, 0f, 1f);
                logs.Add($"BlockReduction={calculation.ForcedBlockReduction:0.##}");
            }
            else if (TryGetString(parameters, "GuardType", out string guardType)
                && TryGetGuardBlockReduction(guardType, out float guardReduction))
            {
                calculation.ForcedBlockReduction = guardReduction;
                logs.Add($"BlockReduction={calculation.ForcedBlockReduction:0.##}");
            }

            if (GetBool(parameters, "CannotBeBlocked", false))
            {
                calculation.CannotBeBlocked = true;
                logs.Add("CannotBeBlocked");
            }

            if (GetBool(parameters, "CannotCrit", false))
            {
                calculation.CannotCrit = true;
                logs.Add("CannotCrit");
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

            if (TryGetFloat(parameters, "SkillPowerBonus", out float skillPowerBonus))
            {
                calculation.SkillPowerBonus += skillPowerBonus;
                calculation.SkillPowerBonusNotes.Add($"Fixed +{skillPowerBonus:0.##}");
                logs.Add($"PowerBonus={skillPowerBonus:0.##}");
            }

            if (TryGetFloat(parameters, "AdditionalMagicalPower", out float additionalMagicalPower)
                || TryGetFloat(parameters, "additionalMagicalPower", out additionalMagicalPower))
            {
                calculation.AddAdditionalMagicalComponent(caster, additionalMagicalPower);
                var sourceName = caster?.Data?.Name ?? "caster";
                logs.Add($"AdditionalMagicalPower={calculation.AdditionalMagicalPower:0.##}({sourceName})");
            }

            if (TryGetFloat(parameters, "SkillPowerBonusFromTargetHpRatio", out float targetHpRatioMaxBonus))
            {
                var target = calculation.Defender;
                int maxHp = GetMaxHp(target);
                int currentHp = Math.Max(0, target?.CurrentHp ?? 0);
                float ratio = Math.Clamp(currentHp / (float)maxHp, 0f, 1f);
                float bonus = MathF.Floor(targetHpRatioMaxBonus * ratio);
                calculation.SkillPowerBonus += bonus;
                calculation.SkillPowerBonusNotes.Add($"目标HP比例(TargetHpRatio) {currentHp}/{maxHp} +{bonus:0.##}");
                logs.Add($"PowerBonus TargetHpRatio={bonus:0.##} ({currentHp}/{maxHp})");
            }

            if (TryGetFloat(parameters, "DamageMultiplier", out float damageMultiplier))
            {
                calculation.DamageMultiplier *= damageMultiplier;
                logs.Add($"DamageMultiplier={calculation.DamageMultiplier:0.##}");
            }

            if (TryGetFloat(parameters, "FixedDamageFromCasterHpRatio", out float fixedDamageRatio))
            {
                int currentHp = Math.Max(0, caster?.CurrentHp ?? 0);
                float damage = MathF.Floor(currentHp * NormalizeRatio(fixedDamageRatio));
                calculation.FixedPhysicalDamagePerHit = damage;
                logs.Add($"FixedPhysicalDamage={damage:0.##}");
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

            if (GetBool(parameters, "ReflectDamageToAttacker", false))
            {
                calculation.ReflectDamageToAttacker = true;
                logs.Add("ReflectDamageToAttacker");
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

        private static bool DamageCalculationConditionsMatch(
            Dictionary<string, object> parameters,
            BattleUnit caster,
            DamageCalculation calculation)
        {
            if (GetBool(parameters, "targetHasDebuff", false) && !HasDebuff(calculation.Defender))
                return false;

            if (!TargetAilmentConditionMatches(parameters, calculation.Defender))
                return false;

            if (GetBool(parameters, "casterHasBuff", false) && !HasBuff(caster))
                return false;

            if (GetBool(parameters, "requiresCasterFrontRow", false) && caster?.IsFrontRow != true)
                return false;

            if (TryGetString(parameters, "casterRow", out string casterRow)
                && !CasterRowMatches(caster, casterRow))
                return false;

            if (!TargetClassConditionMatches(parameters, calculation.Defender))
                return false;

            if (TryGetFloat(parameters, "casterHpRatioMin", out float casterHpRatioMin)
                && GetHpRatio(caster) < NormalizeRatio(casterHpRatioMin))
                return false;

            if (TryGetFloat(parameters, "casterHpRatioMax", out float casterHpRatioMax)
                && GetHpRatio(caster) > NormalizeRatio(casterHpRatioMax))
                return false;

            return true;
        }

        private static bool EffectConditionsMatch(
            Dictionary<string, object> parameters,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            DamageCalculation calculation)
        {
            if (TryGetFloat(parameters, "casterHpRatioMin", out float casterHpRatioMin)
                && GetHpRatio(caster) < NormalizeRatio(casterHpRatioMin))
                return false;

            if (TryGetFloat(parameters, "casterHpRatioMax", out float casterHpRatioMax)
                && GetHpRatio(caster) > NormalizeRatio(casterHpRatioMax))
                return false;

            var target = GetPrimaryTargetForCondition(targets, calculation);
            if (TryGetFloat(parameters, "targetHpRatioMin", out float targetHpRatioMin)
                && GetHpRatio(target) < NormalizeRatio(targetHpRatioMin))
                return false;

            if (TryGetFloat(parameters, "targetHpRatioMax", out float targetHpRatioMax)
                && GetHpRatio(target) > NormalizeRatio(targetHpRatioMax))
                return false;

            if (!TargetAilmentConditionMatches(parameters, target))
                return false;

            if (GetBool(parameters, "targetHasDebuff", false) && !HasDebuff(target))
                return false;

            if (GetBool(parameters, "targetHasBuff", false) && !HasBuff(target))
                return false;

            return true;
        }

        private static BattleUnit GetPrimaryTargetForCondition(
            IReadOnlyList<BattleUnit> targets,
            DamageCalculation calculation)
        {
            return calculation?.ResolvedDefender
                ?? calculation?.Defender
                ?? targets?.FirstOrDefault(target => target != null);
        }

        private static bool TargetAilmentConditionMatches(
            Dictionary<string, object> parameters,
            BattleUnit target)
        {
            if (GetBool(parameters, "targetHasAnyAilment", false)
                && target?.Ailments.Any() != true)
            {
                return false;
            }

            var requiredAilments = GetStringList(parameters, "targetHasAilments");
            if (TryGetString(parameters, "targetHasAilment", out string targetHasAilment))
                requiredAilments.Add(targetHasAilment);

            foreach (var required in requiredAilments)
            {
                if (!Enum.TryParse<StatusAilment>(required, true, out var ailment)
                    || target?.Ailments.Contains(ailment) != true)
                    return false;
            }

            var forbiddenAilments = GetStringList(parameters, "targetLacksAilments");
            if (TryGetString(parameters, "targetLacksAilment", out string targetLacksAilment))
                forbiddenAilments.Add(targetLacksAilment);

            foreach (var forbidden in forbiddenAilments)
            {
                if (Enum.TryParse<StatusAilment>(forbidden, true, out var ailment)
                    && target?.Ailments.Contains(ailment) == true)
                    return false;
            }

            return true;
        }

        private static bool CasterRowMatches(BattleUnit caster, string row)
        {
            if (caster == null)
                return false;

            return row.Trim().ToLowerInvariant() switch
            {
                "front" => caster.IsFrontRow,
                "frontrow" => caster.IsFrontRow,
                "back" => !caster.IsFrontRow,
                "backrow" => !caster.IsFrontRow,
                _ => false
            };
        }

        private static bool TryGetGuardBlockReduction(string guardType, out float reduction)
        {
            reduction = 0f;
            if (string.IsNullOrWhiteSpace(guardType))
                return false;

            switch (guardType.Trim().ToLowerInvariant())
            {
                case "small":
                case "light":
                case "smallguard":
                case "lightguard":
                    reduction = 0.25f;
                    return true;
                case "medium":
                case "mediumguard":
                    reduction = 0.50f;
                    return true;
                case "large":
                case "largeguard":
                    reduction = 0.75f;
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasDebuff(BattleUnit unit)
        {
            return unit?.Buffs.Any(buff =>
                buff.IsPureBuffOrDebuff
                && (buff.Ratio < 0f || buff.FlatAmount < 0)) == true;
        }

        private static bool HasBuff(BattleUnit unit)
        {
            return unit?.Buffs.Any(buff =>
                buff.IsPureBuffOrDebuff
                && (buff.Ratio > 0f || buff.FlatAmount > 0)) == true;
        }

        private static bool TargetClassConditionMatches(
            Dictionary<string, object> parameters,
            BattleUnit target)
        {
            var targetClasses = GetStringList(parameters, "targetClasses");
            if (TryGetString(parameters, "targetClass", out string targetClass))
                targetClasses.Add(targetClass);

            var requiredClasses = targetClasses
                .Select(value => ParseNullableUnitClass(value))
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToHashSet();

            return requiredClasses.Count == 0
                || target?.GetEffectiveClasses().Any(requiredClasses.Contains) == true;
        }

        private static void ApplyBuffEffect(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            DamageCalculation calculation,
            string effectType,
            List<string> logs)
        {
            switch (effectType)
            {
                case "AddBuff":
                    ApplyBuff(context, caster, targets, parameters, sourceSkillId, calculation, logs);
                    break;
                case "AddDebuff":
                    ApplyBuff(context, caster, targets, parameters, sourceSkillId, calculation, logs, forceDebuff: true);
                    break;
                case "AmplifyDebuffs":
                    ApplyAmplifyDebuffs(context, caster, targets, parameters, calculation, logs);
                    break;
                case "RemoveBuff":
                case "RemoveDebuff":
                case "CleanseDebuff":
                    ApplyRemoveBuff(context, caster, targets, parameters, calculation, effectType, logs);
                    break;
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
            bool canStackWithSameSkill = IsStackPolicy(parameters, "Stack")
                || GetBool(parameters, "stackable", false);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                if (forceDebuff && target.TryConsumeTemporal("DebuffNullify"))
                {
                    logs.Add($"{target.Data.Name}.DebuffNullified");
                    continue;
                }

                int before = target.GetCurrentStat(stat);
                BuffManager.ApplyBuff(target, new Buff
                {
                    SkillId = sourceSkillId,
                    TargetStat = stat,
                    Ratio = ratio,
                    FlatAmount = flatAmount,
                    RemainingTurns = turns,
                    IsOneTime = isOneTime,
                    IsPureBuffOrDebuff = true,
                    CanStackWithSameSkill = canStackWithSameSkill
                });
                logs.Add($"{target.Data.Name}.{stat} {before}->{target.GetCurrentStat(stat)}");
            }
        }

        private static bool IsStackPolicy(Dictionary<string, object> parameters, string expected)
        {
            return TryGetString(parameters, "stackPolicy", out string policy)
                && string.Equals(policy, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyAmplifyDebuffs(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            float multiplier = GetFloat(parameters, "multiplier", 1.5f);
            if (multiplier <= 0f)
                return;

            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                foreach (var buff in target.Buffs.Where(IsAmplifiableDebuff))
                {
                    int before = target.GetCurrentStat(buff.TargetStat);
                    if (buff.Ratio < 0f)
                        buff.Ratio *= multiplier;
                    if (buff.FlatAmount < 0)
                        buff.FlatAmount = (int)Math.Round(buff.FlatAmount * multiplier, MidpointRounding.AwayFromZero);
                    logs.Add($"{target.Data.Name}.{buff.TargetStat} {before}->{target.GetCurrentStat(buff.TargetStat)}");
                }
            }
        }

        private static bool IsAmplifiableDebuff(Buff buff)
        {
            return buff != null
                && buff.IsPureBuffOrDebuff
                && !string.IsNullOrWhiteSpace(buff.TargetStat)
                && (buff.Ratio < 0f || buff.FlatAmount < 0);
        }

        private static void ApplyResourceEffect(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            string effectType,
            List<string> logs)
        {
            switch (effectType)
            {
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
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int amount = GetResourceAmount(parameters, target.CurrentAp);
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
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int amount = GetResourceAmount(parameters, target.CurrentPp);
                int before = target.CurrentPp;
                target.ConsumePp(amount);
                logs.Add($"{target.Data.Name}.PP {before}->{target.CurrentPp}");
            }
        }

        private static void ApplyTransferResource(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            string resource = GetString(parameters, "resource", "PP").ToUpperInvariant();
            var fromParameters = new Dictionary<string, object>(parameters)
            {
                ["target"] = GetString(parameters, "from", "Target")
            };
            var toParameters = new Dictionary<string, object>(parameters)
            {
                ["target"] = GetString(parameters, "to", "Self")
            };

            var sources = SelectTargets(context, caster, targets, fromParameters, calculation, "Target");
            var destinations = SelectTargets(context, caster, targets, toParameters, calculation, "Self");
            if (sources.Count == 0 || destinations.Count == 0)
                return;

            foreach (var source in sources)
            {
                int sourceBefore = GetResource(source, resource);
                int requested = GetResourceAmount(parameters, sourceBefore);
                int moved = Math.Clamp(requested, 0, sourceBefore);
                if (moved <= 0)
                {
                    logs.Add($"{source.Data.Name}.{resource} {sourceBefore}->{sourceBefore}");
                    continue;
                }

                ConsumeResource(source, resource, moved);
                foreach (var destination in destinations)
                {
                    int destinationBefore = GetResource(destination, resource);
                    RecoverResource(destination, resource, moved);
                    logs.Add($"{resource} {source.Data.Name} {sourceBefore}->{GetResource(source, resource)} => {destination.Data.Name} {destinationBefore}->{GetResource(destination, resource)}");
                }
            }
        }

        private static void ApplyHpEffect(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            string effectType,
            List<string> logs)
        {
            switch (effectType)
            {
                case "RecoverHp":
                case "Heal":
                    ApplyRecoverHp(context, caster, targets, parameters, calculation, logs);
                    break;
                case "HealRatio":
                    ApplyHealRatio(context, caster, targets, parameters, calculation, logs);
                    break;
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
                int maxHp = GetMaxHp(target);
                int before = target.CurrentHp;
                int heal = Math.Max(1, (int)(maxHp * percent / 100f));
                target.CurrentHp = HealWithoutLoweringCurrentHp(target.CurrentHp, heal, maxHp);
                logs.Add(FormatHealLog(
                    target,
                    before,
                    target.CurrentHp,
                    heal,
                    $"最大HP{maxHp} x{percent}%"));
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
                int maxHp = GetMaxHp(target);
                int before = target.CurrentHp;
                float effectiveRatio = ratio;
                float appliedMultiplier = 1f;
                if (TryGetFloat(parameters, "lowHpThreshold", out float lowHpThreshold)
                    && TryGetFloat(parameters, "lowHpMultiplier", out float lowHpMultiplier)
                    && GetHpRatio(target) <= NormalizeRatio(lowHpThreshold))
                {
                    appliedMultiplier = lowHpMultiplier;
                    effectiveRatio *= lowHpMultiplier;
                }
                int heal = Math.Max(1, (int)(maxHp * effectiveRatio));
                target.CurrentHp = HealWithoutLoweringCurrentHp(target.CurrentHp, heal, maxHp);
                string formula = $"最大HP{maxHp} x{FormatPercent(ratio)}";
                if (Math.Abs(appliedMultiplier - 1f) > 0.001f)
                    formula += $" x{FormatMultiplier(appliedMultiplier)}";
                logs.Add(FormatHealLog(target, before, target.CurrentHp, heal, formula));
            }
        }

        private static string FormatHealLog(
            BattleUnit target,
            int before,
            int after,
            int formulaHeal,
            string formula)
        {
            int applied = Math.Max(0, after - before);
            return $"{target.Data.Name}.HP {before}->{after} (+{applied}; {formula}={formulaHeal})";
        }

        private static string FormatPercent(float ratio)
        {
            float percent = ratio * 100f;
            return percent.ToString(Math.Abs(percent - MathF.Round(percent)) < 0.01f ? "F0" : "0.#") + "%";
        }

        private static string FormatMultiplier(float multiplier)
        {
            return multiplier.ToString(Math.Abs(multiplier - MathF.Round(multiplier)) < 0.01f ? "F0" : "0.##");
        }

        private static int HealWithoutLoweringCurrentHp(int currentHp, int heal, int maxHp)
        {
            int healed = Math.Min(maxHp, currentHp + Math.Max(1, heal));
            return Math.Max(currentHp, healed);
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
                if (target.TryConsumeTemporal("AilmentNullify"))
                {
                    logs.Add($"{target.Data.Name}.AilmentNullified");
                    continue;
                }

                if (!target.Ailments.Contains(ailment))
                    target.Ailments.Add(ailment);
                if (ailment == StatusAilment.Stun)
                    target.State = UnitState.Stunned;
                calculation?.AppliedAilments.Add(ailment);
                logs.Add($"{target.Data.Name}.{ailment}");
            }
        }

        private static void ApplyCleanseAilment(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            var ailmentNames = GetStringList(parameters, "ailments");
            if (TryGetString(parameters, "ailment", out string singleAilment))
                ailmentNames.Add(singleAilment);

            var requested = ailmentNames
                .Select(name => Enum.TryParse<StatusAilment>(name, true, out var ailment)
                    ? (StatusAilment?)ailment
                    : null)
                .Where(ailment => ailment.HasValue)
                .Select(ailment => ailment.Value)
                .ToHashSet();
            int count = GetInt(parameters, "count", int.MaxValue);

            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Target"))
            {
                int removed = 0;
                for (int i = target.Ailments.Count - 1; i >= 0 && removed < count; i--)
                {
                    var ailment = target.Ailments[i];
                    if (requested.Count > 0 && !requested.Contains(ailment))
                        continue;

                    target.Ailments.RemoveAt(i);
                    removed++;
                }

                if (!target.Ailments.Contains(StatusAilment.Stun) && target.State == UnitState.Stunned)
                    target.State = UnitState.Normal;

                logs.Add($"{target.Data.Name}.AilmentRemoved={removed}");
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

        private static void ApplyForcedTarget(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            DamageCalculation calculation,
            List<string> logs)
        {
            string key = GetString(parameters, "key", "ForcedTarget");
            int count = GetInt(parameters, "count", int.MaxValue);
            int turns = GetInt(parameters, "turns", -1);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                var affectedUnitIds = SelectForcedTargetAffectedUnits(context, target, parameters)
                    .Select(unit => unit.Data?.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();
                target.AddTemporal(key, count, turns, sourceSkillId, affectedUnitIds);
                string affectedText = affectedUnitIds.Count == 0 ? "" : $" affected={FormatList(affectedUnitIds)}";
                logs.Add($"{target.Data.Name}.{key}{affectedText}");
            }
        }

        private static List<BattleUnit> SelectForcedTargetAffectedUnits(
            BattleContext context,
            BattleUnit forcedTarget,
            Dictionary<string, object> parameters)
        {
            if (context == null || forcedTarget == null)
                return new List<BattleUnit>();

            string affectedEnemyRow = GetString(parameters, "affectedEnemyRow", "");
            if (string.IsNullOrWhiteSpace(affectedEnemyRow))
                return new List<BattleUnit>();

            var enemies = context.GetAliveUnits(!forcedTarget.IsPlayer);
            bool? frontRow = affectedEnemyRow.Trim().ToLowerInvariant() switch
            {
                "front" => true,
                "frontrow" => true,
                "back" => false,
                "backrow" => false,
                "auto" => SelectAutoAffectedEnemyRow(enemies),
                "onerow" => SelectAutoAffectedEnemyRow(enemies),
                _ => null
            };

            return frontRow.HasValue
                ? enemies.Where(unit => unit.IsFrontRow == frontRow.Value).ToList()
                : new List<BattleUnit>();
        }

        private static bool? SelectAutoAffectedEnemyRow(List<BattleUnit> enemies)
        {
            if (enemies == null || enemies.Count == 0)
                return null;

            return enemies.Any(unit => unit.IsFrontRow);
        }

        private static void ApplyActionOrderPriority(
            BattleContext context,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            int priority = GetActionOrderPriority(parameters);
            foreach (var target in SelectTargets(context, caster, targets, parameters, calculation, "Self"))
            {
                int before = target.ActionOrderPriority;
                target.ActionOrderPriority = Math.Max(target.ActionOrderPriority, priority);
                logs.Add($"{target.Data.Name}.ActionOrderPriority {before}->{target.ActionOrderPriority}");
            }
        }

        private static int GetActionOrderPriority(Dictionary<string, object> parameters)
        {
            if (TryGetString(parameters, "mode", out string mode)
                && string.Equals(mode, "Fastest", StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            return GetInt(parameters, "priority", 1);
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

        private static void ApplyCoverAlly(
            BattleUnit caster,
            Dictionary<string, object> parameters,
            DamageCalculation calculation,
            List<string> logs)
        {
            if (calculation == null || calculation.CannotBeCovered)
                return;
            if (calculation.CoverTarget != null)
                return;
            if (IsAlsoTargetedInSameAction(caster, calculation))
                return;
            if (!CoverScopeMatches(caster, calculation, parameters))
                return;

            calculation.CoverTarget = caster;
            calculation.CoverScope = GetString(parameters, "scope", "");
            logs.Add($"Cover={caster.Data.Name}");
        }

        private static bool IsAlsoTargetedInSameAction(BattleUnit caster, DamageCalculation calculation)
        {
            return caster != null
                && calculation?.Defender != null
                && caster != calculation.Defender
                && calculation.ActionTargets.Any(target => target == caster);
        }

        private static bool CoverScopeMatches(
            BattleUnit caster,
            DamageCalculation calculation,
            Dictionary<string, object> parameters)
        {
            string scope = GetString(parameters, "scope", "");
            if (string.IsNullOrWhiteSpace(scope))
                return true;

            var defender = calculation?.Defender;
            if (caster == null || defender == null)
                return false;

            return scope.ToLowerInvariant() switch
            {
                "row" => caster.IsFrontRow == defender.IsFrontRow,
                "column" => IsSameColumn(caster.Position, defender.Position),
                _ => true
            };
        }

        private void ApplyPendingActionEffect(
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            string effectType,
            List<string> logs)
        {
            var pendingType = effectType switch
            {
                "CounterAttack" => PendingActionType.Counter,
                "PursuitAttack" => PendingActionType.Pursuit,
                "PreemptiveAttack" => PendingActionType.Preemptive,
                "BattleEndAttack" => PendingActionType.BattleEnd,
                "PendingAttack" => ParseEnum(GetString(parameters, "pendingActionType", "BattleEnd"), PendingActionType.BattleEnd),
                _ => PendingActionType.BattleEnd
            };
            ApplyPendingAction(pendingType, caster, targets, parameters, sourceSkillId, logs);
        }

        private void ApplyPendingAction(
            PendingActionType type,
            BattleUnit caster,
            IReadOnlyList<BattleUnit> targets,
            Dictionary<string, object> parameters,
            string sourceSkillId,
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
                HitCount = Math.Max(1, GetInt(parameters, "HitCount", GetInt(parameters, "hitCount", 1))),
                HitRate = parameters.ContainsKey("hitRate") ? GetInt(parameters, "hitRate", 100) : null,
                SkillType = ParseEnum(
                    GetString(parameters, "skillType",
                        GetString(parameters, "attackSkillType",
                            GetString(parameters, "damageType", "Physical"))),
                    SkillType.Physical),
                DamageType = ParseEnum(GetString(parameters, "damageType", "Physical"), SkillType.Physical),
                AttackType = ParseEnum(GetString(parameters, "attackType", "Melee"), AttackType.Melee),
                TargetType = ParseEnum(GetString(parameters, "targetType", "SingleEnemy"), TargetType.SingleEnemy),
                MaxTargets = TryGetInt(parameters, "maxTargets", out int maxTargets)
                    ? Math.Max(0, maxTargets)
                    : null,
                IgnoreDefenseRatio = Math.Clamp(
                    GetFloat(parameters, "ignoreDefenseRatio", GetFloat(parameters, "IgnoreDefenseRatio", 0f)),
                    0f,
                    1f),
                IgnoreDefenseTargetClass = ParseNullableUnitClass(
                    GetString(parameters, "ignoreDefenseTargetClass", "")),
                Tags = GetStringList(parameters, "tags"),
                SourcePassiveId = sourceSkillId
            };
            _enqueueAction(action);
            logs.Add(FormatPendingActionQueuedLog(action));
        }

        private static string FormatPendingActionQueuedLog(PendingAction action)
        {
            var targets = action.Targets
                .Where(t => t != null)
                .Select(t => t.Data?.Name ?? t.Data?.Id ?? "")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            string actorName = action.Actor?.Data?.Name ?? "";
            string targetNames = targets.Count == 0 ? "目标" : string.Join("、", targets);
            if (action.MaxTargets.HasValue && action.MaxTargets.Value >= 0 && action.MaxTargets.Value < targets.Count)
                targetNames = $"候选 {targetNames} / 最终{action.MaxTargets.Value}体";
            string verb = GetPendingActionQueuedVerb(action);

            return $"{verb}: {actorName} -> {targetNames}（{FormatPendingActionSpec(action)}）";
        }

        private static string GetPendingActionQueuedVerb(PendingAction action)
        {
            if (action.SourcePassiveId?.Contains("pursuit", StringComparison.OrdinalIgnoreCase) == true)
                return "准备追击";

            return action.Type switch
            {
                PendingActionType.Counter => "准备反击",
                PendingActionType.Pursuit => "准备追击",
                PendingActionType.Preemptive => "准备先制攻击",
                PendingActionType.BattleEnd => "准备战斗结束攻击",
                _ => "准备追加攻击"
            };
        }

        private static string FormatPendingIgnoreDefense(PendingAction action)
        {
            if (action.IgnoreDefenseRatio <= 0f)
                return "None";

            return action.IgnoreDefenseTargetClass.HasValue
                ? $"{action.IgnoreDefenseRatio:0.##}@{action.IgnoreDefenseTargetClass.Value}"
                : $"{action.IgnoreDefenseRatio:0.##}";
        }

        private static string FormatPendingActionSpec(PendingAction action)
        {
            string hitRate = action.HitRate.HasValue ? $"{action.HitRate.Value}%" : "100%";
            return $"威力{action.Power} / 命中倍率{hitRate} / {FormatPendingDamageType(action)}{AttackTypeLabel(action.AttackType)} / {Math.Max(1, action.HitCount)}hit";
        }

        private static string FormatPendingDamageType(PendingAction action)
        {
            string skillType = SkillTypeLabel(action.SkillType);
            string damageType = SkillTypeLabel(action.DamageType);
            return action.SkillType == action.DamageType
                ? damageType
                : $"{skillType}/伤害:{damageType}";
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
            AttackType.Melee => "近接",
            AttackType.Ranged => "远隔",
            AttackType.Magic => "魔法",
            _ => type.ToString()
        };

        private static void ApplyAugmentCurrentAction(
            BattleContext context,
            BattleUnit caster,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            List<string> logs)
        {
            if (context == null)
                return;

            if (!CurrentActionRequirementMatches(parameters, context.CurrentActionSkill))
            {
                logs.Add($"AugmentCurrentAction source={sourceSkillId} skipped=currentAction");
                return;
            }

            var augment = new CurrentActionAugment
            {
                Actor = caster,
                SourcePassiveId = sourceSkillId,
                CalculationEffects = GetEffectList(parameters, "calculationEffects"),
                OnHitEffects = GetEffectList(parameters, "onHitEffects"),
                QueuedActionEffects = GetEffectList(parameters, "queuedActions"),
                Tags = GetStringList(parameters, "tags")
            };

            context.CurrentActionAugments.Add(augment);
            logs.Add(
                "AugmentCurrentAction"
                + $" source={sourceSkillId}"
                + $" calc={FormatEffectTypes(augment.CalculationEffects)}"
                + $" onHit={FormatEffectTypes(augment.OnHitEffects)}"
                + $" queued={FormatEffectTypes(augment.QueuedActionEffects)}"
                + $" tags={FormatList(augment.Tags)}");
        }

        private static void ApplyAugmentOutgoingActions(
            BattleContext context,
            BattleUnit caster,
            Dictionary<string, object> parameters,
            string sourceSkillId,
            List<string> logs)
        {
            if (context == null || caster == null)
                return;

            var augment = new OutgoingActionAugment
            {
                IsPlayerSide = caster.IsPlayer,
                SourcePassiveId = sourceSkillId,
                RequirementParameters = new Dictionary<string, object>(parameters),
                CalculationEffects = GetEffectList(parameters, "calculationEffects"),
                OnHitEffects = GetEffectList(parameters, "onHitEffects"),
                Tags = GetStringList(parameters, "tags")
            };

            context.OutgoingActionAugments.Add(augment);
            logs.Add(
                "AugmentOutgoingActions"
                + $" source={sourceSkillId}"
                + $" side={(augment.IsPlayerSide ? "Player" : "Enemy")}"
                + $" calc={FormatEffectTypes(augment.CalculationEffects)}"
                + $" onHit={FormatEffectTypes(augment.OnHitEffects)}"
                + $" tags={FormatList(augment.Tags)}");
        }

        internal static bool CurrentActionRequirementMatches(
            Dictionary<string, object> parameters,
            ActiveSkill currentActionSkill)
        {
            if (!TryGetString(parameters, "requiresCurrentSkillType", out string requiredSkillType)
                && !TryGetString(parameters, "currentSkillType", out requiredSkillType))
            {
                requiredSkillType = null;
            }

            if (!TryGetString(parameters, "requiresCurrentAttackType", out string requiredAttackType)
                && !TryGetString(parameters, "currentAttackType", out requiredAttackType))
            {
                requiredAttackType = null;
            }

            if (!TryGetString(parameters, "requiresCurrentDamageType", out string requiredDamageType)
                && !TryGetString(parameters, "currentDamageType", out requiredDamageType))
            {
                requiredDamageType = null;
            }

            if (string.IsNullOrWhiteSpace(requiredSkillType)
                && string.IsNullOrWhiteSpace(requiredAttackType)
                && string.IsNullOrWhiteSpace(requiredDamageType))
            {
                return true;
            }

            if (currentActionSkill == null)
                return false;

            if (!string.IsNullOrWhiteSpace(requiredSkillType)
                && !EnumRequirementMatches(requiredSkillType, currentActionSkill.Data.Type))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredAttackType)
                && !EnumRequirementMatches(requiredAttackType, currentActionSkill.Data.AttackType))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredDamageType)
                && !DamageTypeRequirementMatches(requiredDamageType, currentActionSkill))
            {
                return false;
            }

            return true;
        }

        private static bool EnumRequirementMatches<TEnum>(string requiredValues, TEnum actual)
            where TEnum : struct, Enum
        {
            var parts = requiredValues
                .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (parts.Count == 0)
                return true;

            foreach (var part in parts)
            {
                if (!Enum.TryParse(part, true, out TEnum parsed))
                    return false;
                if (EqualityComparer<TEnum>.Default.Equals(parsed, actual))
                    return true;
            }

            return false;
        }

        internal static bool IncomingSkillRequirementMatches(
            Dictionary<string, object> parameters,
            ActiveSkill incomingSkill,
            DamageCalculation calculation = null)
        {
            if (!TryGetIncomingRequirement(parameters, "SkillType", out string requiredSkillType))
            {
                requiredSkillType = null;
            }

            if (!TryGetIncomingRequirement(parameters, "DamageType", out string requiredDamageType))
            {
                requiredDamageType = null;
            }

            if (!TryGetIncomingRequirement(parameters, "AttackType", out string requiredAttackType))
            {
                requiredAttackType = null;
            }

            if (string.IsNullOrWhiteSpace(requiredSkillType)
                && string.IsNullOrWhiteSpace(requiredDamageType)
                && string.IsNullOrWhiteSpace(requiredAttackType))
            {
                return true;
            }

            if (incomingSkill == null)
                return false;

            if (!string.IsNullOrWhiteSpace(requiredSkillType)
                && !EnumRequirementMatches(requiredSkillType, incomingSkill.Data.Type))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredDamageType)
                && !DamageTypeRequirementMatches(requiredDamageType, incomingSkill, calculation))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredAttackType)
                && !EnumRequirementMatches(requiredAttackType, incomingSkill.Data.AttackType))
            {
                return false;
            }

            return true;
        }

        private static bool DamageTypeRequirementMatches(
            string requiredValues,
            ActiveSkill skill,
            DamageCalculation calculation = null)
        {
            var parts = requiredValues
                .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (parts.Count == 0)
                return true;

            foreach (var part in parts)
            {
                if (!Enum.TryParse(part, true, out SkillType parsed))
                    return false;
                if (DamageTypeRequirementMatches(parsed, skill, calculation))
                    return true;
            }

            return false;
        }

        private static bool DamageTypeRequirementMatches(
            SkillType required,
            ActiveSkill skill,
            DamageCalculation calculation = null)
        {
            if (skill == null)
                return false;

            return required switch
            {
                SkillType.Physical => skill.HasPhysicalComponent,
                SkillType.Magical => skill.HasMagicalComponent || calculation?.HasAdditionalMagicalComponent == true,
                _ => skill.Data.Type == required
            };
        }

        internal static bool HasIncomingSkillRequirement(Dictionary<string, object> parameters)
        {
            return parameters != null
                && (TryGetIncomingRequirement(parameters, "SkillType", out _)
                    || TryGetIncomingRequirement(parameters, "DamageType", out _)
                    || TryGetIncomingRequirement(parameters, "AttackType", out _));
        }

        private static bool TryGetIncomingRequirement(
            Dictionary<string, object> parameters,
            string suffix,
            out string value)
        {
            return TryGetString(parameters, $"requiresIncoming{suffix}", out value)
                || TryGetString(parameters, $"incoming{suffix}", out value);
        }

        internal static List<SkillEffectData> GetEffectList(Dictionary<string, object> parameters, string key)
        {
            var effects = new List<SkillEffectData>();
            AddNestedEffects(parameters, key, effects);
            return effects.Where(effect => effect != null).ToList();
        }

        private static string FormatEffectTypes(IReadOnlyList<SkillEffectData> effects)
        {
            var names = effects?
                .Select(effect => effect?.EffectType)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? new List<string>();
            return FormatList(names);
        }

        private static string FormatList(IReadOnlyList<string> values)
        {
            return values.Count == 0 ? "None" : string.Join("|", values);
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
            var allies = SelectAliveAllies(context, caster, parameters);
            var selected = targetKind.ToLowerInvariant() switch
            {
                "self" => new List<BattleUnit> { caster },
                "caster" => new List<BattleUnit> { caster },
                "target" => calculation?.ResolvedDefender != null ? new List<BattleUnit> { calculation.ResolvedDefender } : calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "defender" => calculation?.ResolvedDefender != null ? new List<BattleUnit> { calculation.ResolvedDefender } : calculation?.Defender != null ? new List<BattleUnit> { calculation.Defender } : targets.Where(t => t != null).ToList(),
                "attacker" => calculation?.Attacker != null ? new List<BattleUnit> { calculation.Attacker } : targets.Where(t => t != null).ToList(),
                "alltargets" => targets.Where(t => t != null).ToList(),
                "allallies" => allies,
                "allies" => allies,
                "allenemies" => context.GetAliveUnits(!caster.IsPlayer),
                "enemies" => context.GetAliveUnits(!caster.IsPlayer),
                "rowallies" => allies.Where(u => u.IsFrontRow == caster.IsFrontRow).ToList(),
                "frontrowallies" => allies.Where(u => u.IsFrontRow).ToList(),
                "backrowallies" => allies.Where(u => !u.IsFrontRow).ToList(),
                "columnallies" => allies.Where(u => IsSameColumn(u.Position, caster.Position)).ToList(),
                "rowalliesoftarget" => SelectRowAlliesOfTarget(context, targets, calculation),
                "columnalliesoftarget" => SelectColumnAlliesOfTarget(context, targets, calculation),
                "lowesthpally" => allies.OrderBy(u => u.CurrentHp).Take(1).ToList(),
                "highesthpally" => allies.OrderByDescending(u => u.CurrentHp).Take(1).ToList(),
                "randomally" => SelectRandomUnit(allies),
                _ => targets.Where(t => t != null).ToList()
            };

            selected = ApplyTargetFilters(selected, parameters, caster);

            if (TryGetInt(parameters, "maxTargets", out int maxTargets))
                selected = selected.Take(Math.Max(0, maxTargets)).ToList();

            return selected;
        }

        private static List<BattleUnit> SelectAliveAllies(
            BattleContext context,
            BattleUnit caster,
            Dictionary<string, object> parameters)
        {
            if (context == null || caster == null)
                return new List<BattleUnit>();

            var allies = context.GetAliveUnits(caster.IsPlayer);
            return GetBool(parameters, "excludeSelf", false)
                ? allies.Where(unit => unit != caster).ToList()
                : allies;
        }

        private static List<BattleUnit> ApplyTargetFilters(
            List<BattleUnit> selected,
            Dictionary<string, object> parameters,
            BattleUnit caster)
        {
            if (GetBool(parameters, "excludeSelf", false))
                selected = selected.Where(unit => unit != caster).ToList();

            var targetClasses = GetStringList(parameters, "targetClasses");
            if (TryGetString(parameters, "targetClass", out string targetClass))
                targetClasses.Add(targetClass);

            var requiredClasses = targetClasses
                .Select(value => ParseNullableUnitClass(value))
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToHashSet();

            if (requiredClasses.Count == 0)
                return selected;

            return selected
                .Where(unit => unit.GetEffectiveClasses().Any(requiredClasses.Contains))
                .ToList();
        }

        private static List<BattleUnit> SelectRowAlliesOfTarget(
            BattleContext context,
            IReadOnlyList<BattleUnit> targets,
            DamageCalculation calculation)
        {
            var anchors = calculation?.Defender != null
                ? new List<BattleUnit> { calculation.Defender }
                : targets?.Where(t => t != null).ToList() ?? new List<BattleUnit>();

            return anchors
                .SelectMany(anchor => context.GetAliveUnits(anchor.IsPlayer)
                    .Where(unit => unit.IsFrontRow == anchor.IsFrontRow))
                .Distinct()
                .OrderBy(unit => unit.Position)
                .ToList();
        }

        private static List<BattleUnit> SelectColumnAlliesOfTarget(
            BattleContext context,
            IReadOnlyList<BattleUnit> targets,
            DamageCalculation calculation)
        {
            var anchors = calculation?.Defender != null
                ? new List<BattleUnit> { calculation.Defender }
                : targets?.Where(t => t != null).ToList() ?? new List<BattleUnit>();

            return anchors
                .SelectMany(anchor => context.GetAliveUnits(anchor.IsPlayer)
                    .Where(unit => IsSameColumn(unit.Position, anchor.Position)))
                .Distinct()
                .OrderBy(unit => unit.Position)
                .ToList();
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

        private static float GetHpRatio(BattleUnit unit)
        {
            int maxHp = GetMaxHp(unit);
            if (maxHp <= 0)
                return 0f;
            return (float)unit.CurrentHp / maxHp;
        }

        private static int GetMaxHp(BattleUnit unit)
        {
            return Math.Max(1, unit?.GetCurrentStat("HP") ?? 1);
        }

        private static bool IsSameColumn(int a, int b)
        {
            if (a <= 0 || b <= 0)
                return false;
            return (a - 1) % 3 == (b - 1) % 3;
        }

        private static List<BattleUnit> SelectRandomUnit(List<BattleUnit> units)
        {
            return units.Count == 0
                ? new List<BattleUnit>()
                : new List<BattleUnit> { units[Random.Shared.Next(units.Count)] };
        }

        private static bool PostDamageConditionMatches(
            string effectType,
            Dictionary<string, object> parameters,
            DamageResult result,
            bool killed)
        {
            if (effectType == "OnKillEffect")
                return killed;

            if (effectType != "OnHitEffect" || result?.IsHit != true)
                return false;

            bool requireDamage = GetBool(parameters, "requireDamage", false);
            bool requireUnblocked = GetBool(parameters, "requireUnblocked", false)
                || GetBool(parameters, "requireNotBlocked", false);
            bool requireFirstHitUnblocked = GetBool(parameters, "requireFirstHitUnblocked", false);
            bool targetHpBeforeRatioMatches = TargetHpBeforeRatioMatches(parameters, result);
            return (!requireDamage || result.TotalDamage > 0)
                && (!requireUnblocked || !result.IsBlocked)
                && (!requireFirstHitUnblocked || FirstHitWasNotBlocked(result))
                && targetHpBeforeRatioMatches
                && ChanceMatches(parameters);
        }

        private static bool TargetHpBeforeRatioMatches(Dictionary<string, object> parameters, DamageResult result)
        {
            bool hasMin = TryGetFloat(parameters, "targetHpBeforeRatioMin", out float min);
            bool hasMax = TryGetFloat(parameters, "targetHpBeforeRatioMax", out float max);
            if (!hasMin && !hasMax)
                return true;

            var target = result?.ResolvedDefender;
            int maxHp = GetMaxHp(target);
            float beforeRatio = Math.Clamp((result?.DamageReceiverHpBefore ?? 0) / (float)maxHp, 0f, 1f);

            if (hasMin && beforeRatio < NormalizeRatio(min))
            {
                return false;
            }

            if (hasMax && beforeRatio > NormalizeRatio(max))
            {
                return false;
            }

            return true;
        }

        private static bool FirstHitWasNotBlocked(DamageResult result)
        {
            var firstHit = result?.HitResults?.OrderBy(hit => hit.HitIndex).FirstOrDefault();
            return firstHit == null || !firstHit.Blocked;
        }

        private static bool ChanceMatches(Dictionary<string, object> parameters)
        {
            if (!TryGetFloat(parameters, "chance", out float chance))
                return true;

            if (chance <= 1f)
                chance *= 100f;

            chance = Math.Clamp(chance, 0f, 100f);
            if (chance <= 0f)
                return false;
            if (chance >= 100f)
                return true;

            return RandUtil.Roll100() < chance;
        }

        private static List<SkillEffectData> GetNestedEffects(Dictionary<string, object> parameters)
        {
            var nested = new List<SkillEffectData>();
            AddNestedEffects(parameters, "effects", nested);
            AddNestedEffects(parameters, "effect", nested);
            return nested;
        }

        private static void AddNestedEffects(Dictionary<string, object> parameters, string key, List<SkillEffectData> nested)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw == null)
                return;

            switch (raw)
            {
                case SkillEffectData effect:
                    nested.Add(effect);
                    return;
                case List<SkillEffectData> effects:
                    nested.AddRange(effects.Where(effect => effect != null));
                    return;
                case SkillEffectData[] effects:
                    nested.AddRange(effects.Where(effect => effect != null));
                    return;
                case JsonElement element when element.ValueKind == JsonValueKind.Object:
                    nested.Add(JsonSerializer.Deserialize<SkillEffectData>(element.GetRawText(), JsonOptions));
                    return;
                case JsonElement element when element.ValueKind == JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                        nested.Add(JsonSerializer.Deserialize<SkillEffectData>(item.GetRawText(), JsonOptions));
                    return;
            }
        }

        private static int GetResourceAmount(Dictionary<string, object> parameters, int allAmount)
        {
            if (TryGetString(parameters, "amount", out string amountText)
                && string.Equals(amountText, "All", StringComparison.OrdinalIgnoreCase))
            {
                return allAmount;
            }

            return GetInt(parameters, "amount", 1);
        }

        private static int GetResource(BattleUnit unit, string resource)
        {
            return resource == "AP" ? unit.CurrentAp : unit.CurrentPp;
        }

        private static void ConsumeResource(BattleUnit unit, string resource, int amount)
        {
            if (resource == "AP")
                unit.ConsumeAp(amount);
            else
                unit.ConsumePp(amount);
        }

        private static void RecoverResource(BattleUnit unit, string resource, int amount)
        {
            if (resource == "AP")
                unit.RecoverAp(amount);
            else
                unit.RecoverPp(amount);
        }

        private static bool TryGetInt(Dictionary<string, object> parameters, string key, out int value)
        {
            if (parameters.TryGetValue(key, out var raw) && TryConvertInt(raw, out value))
                return true;
            value = 0;
            return false;
        }

        internal static int GetInt(Dictionary<string, object> parameters, string key, int fallback)
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

        internal static float GetFloat(Dictionary<string, object> parameters, string key, float fallback)
        {
            return TryGetFloat(parameters, key, out float value) ? value : fallback;
        }

        private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
        {
            if (parameters.TryGetValue(key, out var raw) && raw != null)
            {
                if (raw is JsonElement element)
                {
                    value = element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.Null => null,
                        JsonValueKind.Undefined => null,
                        _ => element.ToString()
                    };
                }
                else
                {
                    value = raw.ToString();
                }
                return !string.IsNullOrEmpty(value);
            }
            value = null;
            return false;
        }

        internal static string GetString(Dictionary<string, object> parameters, string key, string fallback)
        {
            return TryGetString(parameters, key, out string value) ? value : fallback;
        }

        internal static List<string> GetStringList(Dictionary<string, object> parameters, string key)
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

        internal static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
        {
            return Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : fallback;
        }

        private static UnitClass? ParseNullableUnitClass(string value)
        {
            return Enum.TryParse<UnitClass>(value, true, out var parsed) ? parsed : null;
        }

        internal static bool GetBool(Dictionary<string, object> parameters, string key, bool fallback)
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
