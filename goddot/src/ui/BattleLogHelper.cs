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
        public static List<string> FormatAttack(BattleUnit attacker, BattleUnit defender, ActiveSkill skill,
            DamageCalculation calc, DamageResult result, bool killed, List<StatusAilment> appliedAilments)
        {
            var lines = new List<string>();

            // Line 1: attacker → defender [skill] — effect description
            lines.Add("  " + attacker.Data.Name + " → " + defender.Data.Name + " [" + skill.Data.Name + "] — " + skill.Data.EffectDescription);

            // Line 2: hit/evade/crit/block rates
            int atkHit = attacker.GetCurrentStat("Hit");
            int defEva = defender.GetCurrentStat("Eva");
            int skillHit = skill.Data.HitRate ?? 100;
            float hitChance = Math.Max(0, (atkHit - defEva)) * skillHit / 100f;
            lines.Add("  命中:(" + atkHit + "-" + defEva + ")×" + skillHit + "%=" + hitChance.ToString("F0") + "% | 闪避:" + defEva + "% | 格挡:" + defender.GetCurrentBlockRate() + "% | 会心:" + attacker.GetCurrentCritRate() + "%");

            // Hit result
            if (!result.IsHit)
            {
                lines.Add("  ▶ MISS");
                return lines;
            }
            if (result.IsEvaded)
            {
                lines.Add("  ▶ EVADE");
                return lines;
            }

            // Line 3-4: ATK/DEF breakdown + damage formula
            string flags = "";
            if (result.IsCritical) flags += "CRIT(×" + calc.CritMultiplier.ToString("F1") + ") ";
            if (result.IsBlocked) flags += "BLOCK(-" + (calc.BlockReduction * 100).ToString("F0") + "%) ";

            int diff = Math.Max(1, calc.FinalAttackPower - calc.FinalDefense);
            float powerRatio = skill.Power / 100f;
            float classMult = calc.ClassTraitMultiplier;

            lines.Add("  物攻:" + calc.FinalAttackPower + " | 物防:" + calc.FinalDefense + " | 差值=" + diff);
            if (calc.MagicalDamage > 0)
                lines.Add("  魔伤:" + calc.MagicalDamage);

            string formula = "  伤害:(" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")";
            if (Math.Abs(powerRatio - 1f) > 0.01f) formula += " × " + powerRatio.ToString("F1");
            if (Math.Abs(classMult - 1f) > 0.01f) formula += " × " + classMult.ToString("F1");
            if (calc.MagicalDamage > 0) formula += " + " + calc.MagicalDamage;
            formula += " = " + result.TotalDamage;
            if (flags.Length > 0) formula += " [" + flags.Trim() + "]";
            lines.Add(formula);

            // Result
            int hpBefore = defender.CurrentHp + result.TotalDamage;
            if (hpBefore > defender.Data.BaseStats.GetValueOrDefault("HP", 0))
                hpBefore = defender.Data.BaseStats.GetValueOrDefault("HP", 0);
            int hpAfter = defender.CurrentHp;
            string killStr = killed ? " [击倒!]" : "";
            string ailStr = appliedAilments.Count > 0 ? " | 异常:" + string.Join(",", appliedAilments) : "";
            lines.Add("  ▶ " + defender.Data.Name + " HP:" + hpBefore + "→" + hpAfter + "(-" + result.TotalDamage + ")" + killStr + ailStr);

            return lines;
        }
    }
}
