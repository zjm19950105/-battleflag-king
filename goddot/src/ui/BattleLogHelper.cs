using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Ui
{
    /// <summary>Formats multi-line battle log output.</summary>
    public static class BattleLogHelper
    {
        public static string FormatUnitName(BattleUnit unit)
        {
            if (unit == null)
                return "";

            return unit.IsPlayer
                ? "{{P:" + unit.Data.Name + "}}"
                : "{{E:" + unit.Data.Name + "}}";
        }

        public static List<string> FormatAttack(BattleUnit attacker, BattleUnit defender, ActiveSkill skill,
            DamageCalculation calc, DamageResult result, bool killed, List<StatusAilment> appliedAilments)
        {
            var lines = new List<string>();
            var damageReceiver = result.ResolvedDefender ?? calc.ResolvedDefender ?? defender;

            lines.Add("  " + FormatUnitName(attacker) + " -> " + FormatUnitName(defender) + " [" + skill.Data.Name + "] - " + skill.Data.EffectDescription);
            if (damageReceiver != defender)
                lines.Add("  Cover: " + FormatUnitName(defender) + " -> " + FormatUnitName(damageReceiver));

            var hitChance = HitChanceCalculator.Calculate(attacker, defender, skill);
            string hitFormula = "(" + FormatUnitName(attacker) + "命中" + hitChance.AttackerHit
                + " - " + FormatUnitName(defender) + "回避" + hitChance.DefenderEvasion + ")";
            if (hitChance.FlyingPenaltyApplied)
                hitFormula = "(" + hitFormula + " / 2(飞行防御))";
            hitFormula += " x 技能命中倍率" + hitChance.SkillHitRate + "%";
            string clampNote;
            if (hitChance.RawChance < 0f || hitChance.RawChance > 100f)
                clampNote = "，显示上限/下限后 " + hitChance.FinalChance + "%";
            else if (Math.Abs(hitChance.RawChance - hitChance.FinalChance) > 0.001f)
                clampNote = "，向下取整后 " + hitChance.FinalChance + "%";
            else
                clampNote = "，最终 " + hitChance.FinalChance + "%";
            lines.Add("  命中率: " + hitFormula + " = " + FormatNumber(hitChance.RawChance) + "%" + clampNote);
            int defEva = hitChance.DefenderEvasion;
            lines.Add("  判定数据: " + FormatUnitName(defender) + "回避 " + defEva + "% | " + FormatUnitName(damageReceiver) + "格挡 " + damageReceiver.GetCurrentBlockRate() + "% | " + FormatUnitName(attacker) + "暴击 " + attacker.GetCurrentCritRate() + "%");
            var hitResults = result.HitResults.Count > 0 ? result.HitResults : calc.HitResults;
            if (calc.HitCount > 1)
            {
                int criticalHits = hitResults.Count(hit => hit.Landed && hit.Critical);
                int blockedHits = hitResults.Count(hit => hit.Landed && hit.Blocked);
                lines.Add("  段数: " + calc.HitCount
                    + " hit | 命中 " + calc.LandedHits
                    + " | 暴击 " + criticalHits
                    + " | 格挡 " + blockedHits
                    + " | 未命中 " + calc.MissedHits
                    + " | 回避 " + calc.EvadedHits
                    + " | 无效 " + calc.NullifiedHits);
            }

            if (!result.IsHit)
            {
                lines.Add(result.IsEvaded ? "  * EVADE" : "  * MISS");
                return lines;
            }
            if (result.IsEvaded)
                lines.Add("  * EVADE: 已回避 " + calc.EvadedHits + " hit，其余命中段继续结算");
            int displayedNullifiedHits = GetDisplayedNullifiedHits(calc, result, hitResults);
            if (displayedNullifiedHits > 0)
                lines.Add("  * NULLIFY: 已无效 " + displayedNullifiedHits + " hit");

            string flags = "";
            if (displayedNullifiedHits > 0) flags += "NULLIFY ";
            if (result.IsCritical) flags += "CRIT(x" + calc.CritMultiplier.ToString("F1") + ") ";
            if (result.IsBlocked) flags += "BLOCK(-" + (calc.BlockReduction * 100).ToString("F0") + "%) ";

            lines.AddRange(FormatDamageFormulaLines(skill, calc, result, flags.Trim()));

            bool hasRecordedHp = result.DamageReceiverHpBefore.HasValue && result.DamageReceiverHpAfter.HasValue;
            int hpBefore = hasRecordedHp
                ? result.DamageReceiverHpBefore.Value
                : damageReceiver.CurrentHp;
            int hpAfter = hasRecordedHp
                ? result.DamageReceiverHpAfter.Value
                : damageReceiver.CurrentHp;
            int hpLost = hasRecordedHp ? result.AppliedHpDamage : 0;
            string killStr = killed ? " [Knockdown]" : "";
            string deathResistStr = result.LethalDamageResisted ? " [DeathResist]" : "";
            string ailStr = appliedAilments.Count > 0 ? " | Ailments:" + string.Join(",", appliedAilments) : "";
            lines.Add("  * " + FormatUnitName(damageReceiver) + " HP:" + hpBefore + "->" + hpAfter + "(-" + hpLost + ")" + deathResistStr + killStr + ailStr);

            return lines;
        }

        public static List<string> FormatDamageFormulaLines(ActiveSkill skill, DamageCalculation calc, DamageResult result, string flags = "")
        {
            var lines = new List<string>();
            var hitResults = result.HitResults.Count > 0 ? result.HitResults : calc.HitResults;
            int diff = Math.Max(1, calc.FinalAttackPower - calc.FinalDefense);
            float powerRatio = calc.SkillPowerRatio;
            float classMult = calc.ClassTraitMultiplier;

            lines.Add("  Atk:" + calc.FinalAttackPower + " | Def:" + calc.FinalDefense + " | Diff:" + diff);
            if (Math.Abs(calc.SkillPowerBonus) > 0.01f || Math.Abs(calc.CounterPowerBonus) > 0.01f)
                lines.Add(BuildPowerBonusLine(skill, calc));
            if (calc.MagicalDamage > 0)
                lines.Add("  MagicDamage:" + calc.MagicalDamage);

            if (calc.HitCount > 1 && hitResults.Count > 0)
            {
                lines.Add(BuildSingleHitFormula(calc, hitResults));
                lines.AddRange(BuildMultiHitDetails(calc, hitResults));
                lines.Add(BuildMultiHitTotal(calc, result, hitResults));
            }
            else
            {
                string formula = "  Damage:(" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")";
                if (Math.Abs(powerRatio - 1f) > 0.01f) formula += " x " + powerRatio.ToString("F1");
                if (Math.Abs(classMult - 1f) > 0.01f) formula += " x 兵种" + classMult.ToString("F1");
                if (Math.Abs(calc.CharacterTraitMultiplier - 1f) > 0.01f) formula += " x 特性" + calc.CharacterTraitMultiplier.ToString("F1");
                if (calc.MagicalDamage > 0) formula += " + " + calc.MagicalDamage;
                formula += " = " + result.TotalDamage;
                if (!string.IsNullOrWhiteSpace(flags)) formula += " [" + flags + "]";
                lines.Add(formula);
            }

            return lines;
        }

        private static string BuildPowerBonusLine(ActiveSkill skill, DamageCalculation calc)
        {
            var parts = new List<string>();
            if (calc.SkillPowerBonusNotes.Count > 0)
                parts.AddRange(calc.SkillPowerBonusNotes);
            else if (Math.Abs(calc.SkillPowerBonus) > 0.01f)
                parts.Add("Fixed +" + FormatNumber(calc.SkillPowerBonus));
            if (Math.Abs(calc.CounterPowerBonus) > 0.01f)
                parts.Add("Counter +" + FormatNumber(calc.CounterPowerBonus));

            return "  PowerBonus: base " + skill.Power
                + " + " + string.Join(" + ", parts)
                + " = " + FormatNumber(calc.EffectivePower);
        }

        private static string BuildSingleHitFormula(DamageCalculation calc, IReadOnlyList<DamageHitResult> hitResults)
        {
            var sample = hitResults.FirstOrDefault(hit => hit.BaseTotalDamage > 0f) ?? hitResults.First();
            int diff = Math.Max(1, calc.FinalAttackPower - calc.FinalDefense);
            string formula = "  单段基础: (" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")";
            if (Math.Abs(calc.SkillPowerRatio - 1f) > 0.01f)
                formula += " x 威力" + FormatPercent(calc.SkillPowerRatio);
            if (Math.Abs(calc.SkillPowerMultiplier - 1f) > 0.01f)
                formula += " x 技能倍率" + FormatNumber(calc.SkillPowerMultiplier);
            if (Math.Abs(calc.ClassTraitMultiplier - 1f) > 0.01f)
                formula += " x 兵种" + FormatNumber(calc.ClassTraitMultiplier);
            if (Math.Abs(calc.CharacterTraitMultiplier - 1f) > 0.01f)
                formula += " x 特性" + FormatNumber(calc.CharacterTraitMultiplier);
            formula += " = " + FormatNumber(sample.BaseTotalDamage);
            return formula;
        }

        private static IEnumerable<string> BuildMultiHitDetails(DamageCalculation calc, IReadOnlyList<DamageHitResult> hitResults)
        {
            int diff = Math.Max(1, calc.FinalAttackPower - calc.FinalDefense);
            foreach (var hit in hitResults.OrderBy(hit => hit.HitIndex))
            {
                if (hit.Missed)
                {
                    yield return "  hit" + hit.HitIndex + " MISS: 0";
                    continue;
                }
                if (hit.Evaded)
                {
                    yield return "  hit" + hit.HitIndex + " EVADE: 0";
                    continue;
                }
                if (IsDisplayedNullified(calc, hit))
                {
                    yield return "  hit" + hit.HitIndex + " NULLIFY: 0";
                    continue;
                }

                string line = "  hit" + hit.HitIndex + " " + GetHitDetailLabel(hit)
                    + ": (" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")"
                    + " x 威力" + FormatPercent(calc.SkillPowerRatio)
                    + " = " + FormatNumber(hit.BaseTotalDamage);
                if (hit.Critical)
                    line += " x" + hit.CritMultiplier.ToString("F1");
                if (hit.Blocked)
                    line += " -" + (hit.BlockReduction * 100).ToString("F0") + "%";
                line += " = " + FormatNumber(GetRawTotalDamage(hit))
                    + " -> " + GetAppliedTotalDamage(hit);
                yield return line;
            }
        }

        private static string GetHitDetailLabel(DamageHitResult hit)
        {
            if (hit.Critical && hit.Blocked)
                return "暴击格挡";
            if (hit.Critical)
                return "暴击";
            if (hit.Blocked)
                return "格挡";
            return "普通";
        }

        private static string BuildMultiHitTotal(DamageCalculation calc, DamageResult result, IReadOnlyList<DamageHitResult> hitResults)
        {
            var landed = hitResults.Where(hit => hit.Landed).ToList();
            if (landed.Count == 0)
                return "  合计: 无命中 => 0";

            var groups = landed
                .GroupBy(hit => new
                {
                    hit.Critical,
                    hit.Blocked,
                    Nullified = IsDisplayedNullified(calc, hit),
                    Crit = Math.Round(hit.CritMultiplier, 3),
                    Block = Math.Round(hit.BlockReduction, 3)
                })
                .OrderBy(group => GetGroupOrder(group.Key.Critical, group.Key.Blocked, group.Key.Nullified))
                .ThenBy(group => group.Key.Crit)
                .ThenBy(group => group.Key.Block)
                .ToList();

            var parts = groups.Select(group =>
            {
                var first = group.First();
                string label = group.Key.Nullified ? "无效" : GetHitGroupLabel(first);
                string modifiers = "";
                if (first.Critical)
                    modifiers += " x" + first.CritMultiplier.ToString("F1");
                if (first.Blocked)
                    modifiers += " -" + (first.BlockReduction * 100).ToString("F0") + "%";
                int total = group.Sum(GetAppliedTotalDamage);
                return label + group.Count() + "hit" + modifiers + "=" + total;
            }).ToList();

            string line = "  合计: " + string.Join(" + ", parts);
            if (Math.Abs(calc.DamageMultiplier - 1f) > 0.01f)
                line += "，最终倍率 x" + FormatNumber(calc.DamageMultiplier);
            line += " => " + result.TotalDamage;
            return line;
        }

        private static string GetHitGroupLabel(DamageHitResult hit)
        {
            if (hit.Nullified)
                return "无效";
            if (hit.Critical && hit.Blocked)
                return "暴击格挡";
            if (hit.Critical)
                return "暴击";
            if (hit.Blocked)
                return "格挡";
            return "普通";
        }

        private static int GetGroupOrder(bool critical, bool blocked, bool nullified)
        {
            if (nullified)
                return 3;
            if (critical && blocked)
                return 2;
            if (blocked)
                return 1;
            if (critical)
                return 1;
            return 0;
        }

        private static float GetRawTotalDamage(DamageHitResult hit)
        {
            return Math.Abs(hit.RawTotalDamage) > 0.001f
                ? hit.RawTotalDamage
                : hit.TotalDamage;
        }

        private static int GetAppliedTotalDamage(DamageHitResult hit)
        {
            return hit.RoundedTotalDamage != 0 || Math.Abs(hit.RawTotalDamage) > 0.001f
                ? hit.AppliedTotalDamage
                : (int)Math.Round(hit.TotalDamage, MidpointRounding.AwayFromZero);
        }

        private static int GetDisplayedNullifiedHits(
            DamageCalculation calc,
            DamageResult result,
            IReadOnlyList<DamageHitResult> hitResults)
        {
            if (calc.NullifiedHits > 0)
                return calc.NullifiedHits;

            if (hitResults.Count > 0)
                return hitResults.Count(hit => IsDisplayedNullified(calc, hit));

            return result.IsHit && CalculationHasNullifyFlag(calc) ? 1 : 0;
        }

        private static bool IsDisplayedNullified(DamageCalculation calc, DamageHitResult hit)
        {
            if (hit.Nullified)
                return true;

            if (calc == null || !hit.Landed || !CalculationHasNullifyFlag(calc))
                return false;

            bool physicalNullified = calc.NullifyPhysicalDamage && hit.BasePhysicalDamage > 0f && hit.PhysicalDamage == 0f;
            bool magicalNullified = calc.NullifyMagicalDamage && hit.BaseMagicalDamage > 0f && hit.MagicalDamage == 0f;
            return hit.TotalDamage <= 0.001f && (physicalNullified || magicalNullified);
        }

        private static bool CalculationHasNullifyFlag(DamageCalculation calc)
        {
            return calc?.NullifyPhysicalDamage == true || calc?.NullifyMagicalDamage == true;
        }

        private static string FormatPercent(float ratio)
        {
            return (ratio * 100f).ToString(Math.Abs(ratio * 100f - MathF.Round(ratio * 100f)) < 0.01f ? "F0" : "F1") + "%";
        }

        private static string FormatNumber(float value)
        {
            return Math.Abs(value - MathF.Round(value)) < 0.01f
                ? MathF.Round(value).ToString("F0")
                : value.ToString("0.##");
        }
    }
}
