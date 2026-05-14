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

            int atkHit = attacker.GetCurrentStat("Hit");
            int defEva = defender.GetCurrentStat("Eva");
            int skillHit = skill.Data.HitRate ?? 100;
            int rawHitChance = skillHit + atkHit - defEva;
            int modifiedHitChance = rawHitChance;
            string hitFormula = $"技能{skillHit} + {FormatUnitName(attacker)}命中{atkHit} - {FormatUnitName(defender)}回避{defEva}";
            bool defIsFlying = defender.GetEffectiveClasses()?.Contains(UnitClass.Flying) == true;
            bool atkIsGrounded = attacker.GetEffectiveClasses()?.Contains(UnitClass.Flying) != true;
            if (defIsFlying && atkIsGrounded && skill.AttackType == AttackType.Melee)
            {
                modifiedHitChance /= 2;
                hitFormula = "(" + hitFormula + ") / 2(飞行防御)";
            }
            int hitChance = Math.Clamp(modifiedHitChance, 0, 100);
            string clampNote = modifiedHitChance != hitChance ? $"，显示上限/下限后 {hitChance}%" : $"，最终 {hitChance}%";
            lines.Add("  命中率: " + hitFormula + " = " + modifiedHitChance + "%" + clampNote);
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
            if (calc.NullifiedHits > 0)
                lines.Add("  * NULLIFY: 已无效 " + calc.NullifiedHits + " hit");

            string flags = "";
            if (result.IsCritical) flags += "CRIT(x" + calc.CritMultiplier.ToString("F1") + ") ";
            if (result.IsBlocked) flags += "BLOCK(-" + (calc.BlockReduction * 100).ToString("F0") + "%) ";

            int diff = Math.Max(1, calc.FinalAttackPower - calc.FinalDefense);
            float powerRatio = calc.SkillPowerRatio;
            float classMult = calc.ClassTraitMultiplier;

            lines.Add("  Atk:" + calc.FinalAttackPower + " | Def:" + calc.FinalDefense + " | Diff:" + diff);
            if (calc.MagicalDamage > 0)
                lines.Add("  MagicDamage:" + calc.MagicalDamage);

            if (calc.HitCount > 1 && hitResults.Count > 0)
            {
                lines.Add(BuildSingleHitFormula(calc, hitResults));
                lines.Add(BuildMultiHitTotal(calc, result, hitResults));
            }
            else
            {
                string formula = "  Damage:(" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")";
                if (Math.Abs(powerRatio - 1f) > 0.01f) formula += " x " + powerRatio.ToString("F1");
                if (Math.Abs(classMult - 1f) > 0.01f) formula += " x " + classMult.ToString("F1");
                if (calc.MagicalDamage > 0) formula += " + " + calc.MagicalDamage;
                formula += " = " + result.TotalDamage;
                if (flags.Length > 0) formula += " [" + flags.Trim() + "]";
                lines.Add(formula);
            }

            int hpBefore = damageReceiver.CurrentHp + result.TotalDamage;
            int maxHp = Math.Max(1, damageReceiver.GetCurrentStat("HP"));
            if (hpBefore > maxHp)
                hpBefore = maxHp;
            int hpAfter = damageReceiver.CurrentHp;
            string killStr = killed ? " [Knockdown]" : "";
            string ailStr = appliedAilments.Count > 0 ? " | Ailments:" + string.Join(",", appliedAilments) : "";
            lines.Add("  * " + FormatUnitName(damageReceiver) + " HP:" + hpBefore + "->" + hpAfter + "(-" + result.TotalDamage + ")" + killStr + ailStr);

            return lines;
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
                    hit.Nullified,
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
                string label = GetHitGroupLabel(first);
                string modifiers = "";
                if (first.Critical)
                    modifiers += " x" + first.CritMultiplier.ToString("F1");
                if (first.Blocked)
                    modifiers += " -" + (first.BlockReduction * 100).ToString("F0") + "%";
                float total = group.Sum(hit => hit.TotalDamage);
                return label + group.Count() + "hit" + modifiers + "=" + FormatNumber(total);
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
