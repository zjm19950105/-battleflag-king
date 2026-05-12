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
            if (calc.HitCount > 1)
                lines.Add("  段数: " + calc.HitCount + " hit | 命中 " + calc.LandedHits + " | 未命中 " + calc.MissedHits + " | 回避 " + calc.EvadedHits + " | 无效 " + calc.NullifiedHits);

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
            float powerRatio = skill.Power / 100f;
            float classMult = calc.ClassTraitMultiplier;

            lines.Add("  Atk:" + calc.FinalAttackPower + " | Def:" + calc.FinalDefense + " | Diff:" + diff);
            if (calc.MagicalDamage > 0)
                lines.Add("  MagicDamage:" + calc.MagicalDamage);

            string formula = "  Damage:(" + calc.FinalAttackPower + "-" + calc.FinalDefense + "=" + diff + ")";
            if (Math.Abs(powerRatio - 1f) > 0.01f) formula += " x " + powerRatio.ToString("F1");
            if (Math.Abs(classMult - 1f) > 0.01f) formula += " x " + classMult.ToString("F1");
            if (calc.MagicalDamage > 0) formula += " + " + calc.MagicalDamage;
            formula += " = " + result.TotalDamage;
            if (flags.Length > 0) formula += " [" + flags.Trim() + "]";
            lines.Add(formula);

            int hpBefore = damageReceiver.CurrentHp + result.TotalDamage;
            if (hpBefore > damageReceiver.Data.BaseStats.GetValueOrDefault("HP", 0))
                hpBefore = damageReceiver.Data.BaseStats.GetValueOrDefault("HP", 0);
            int hpAfter = damageReceiver.CurrentHp;
            string killStr = killed ? " [Knockdown]" : "";
            string ailStr = appliedAilments.Count > 0 ? " | Ailments:" + string.Join(",", appliedAilments) : "";
            lines.Add("  * " + FormatUnitName(damageReceiver) + " HP:" + hpBefore + "->" + hpAfter + "(-" + result.TotalDamage + ")" + killStr + ailStr);

            return lines;
        }
    }
}
