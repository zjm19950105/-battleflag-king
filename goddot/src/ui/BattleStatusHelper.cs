using System.Collections.Generic;
using System.Linq;
using Godot;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Ui
{
    public static class BattleStatusHelper
    {
        public static string ClassColor(UnitClass c) => c switch
        {
            UnitClass.Infantry => "#aaaaaa", UnitClass.Cavalry => "#4488ff",
            UnitClass.Flying => "#44ff88", UnitClass.Heavy => "#ff8844",
            UnitClass.Scout => "#ff44ff", UnitClass.Archer => "#88ff44",
            UnitClass.Mage => "#8844ff", _ => "#cccccc"
        };

        public static void AppendUnit(RichTextLabel label, BattleUnit u)
        {
            if (u == null) return;
            if (!u.IsAlive) { label.AppendText("  [s]x " + u.Data.Name + "[/s]\n"); return; }

            int maxHp = System.Math.Max(1, u.GetCurrentStat("HP"));
            int hpPct = u.CurrentHp * 100 / maxHp;
            string hpBar = new string('█', System.Math.Min(10, hpPct / 10)) + new string('░', System.Math.Max(0, 10 - hpPct / 10));

            // Class tags with color
            var classes = u.GetEffectiveClasses();
            string classStr = "";
            if (classes?.Count > 0)
                classStr = "[" + string.Join(" ", classes.Select(c => "[color=" + ClassColor(c) + "]" + c + "[/color]")) + "] ";

            string ccStr = u.IsCc ? " [color=yellow]CC[/color]" : "";

            label.AppendText("  [" + u.Position + "] [color=#88ff88]" + hpBar + "[/color] " + classStr + "[color=white]" + u.Data.Name + "[/color]" + ccStr + " HP:" + u.CurrentHp + "/" + maxHp + " [color=red]AP:" + StarStr(u.CurrentAp, u.MaxAp) + "[/color] [color=blue]PP:" + StarStr(u.CurrentPp, u.MaxPp) + "[/color]\n");

            // Combat stats
            int spd = u.GetCurrentStat("Spd");
            int bStr = u.Data.BaseStats.GetValueOrDefault("Str", 0);
            int bDef = u.Data.BaseStats.GetValueOrDefault("Def", 0);
            int bMag = u.Data.BaseStats.GetValueOrDefault("Mag", 0);
            int bMDef = u.Data.BaseStats.GetValueOrDefault("MDef", 0);
            int bHit = u.Data.BaseStats.GetValueOrDefault("Hit", 0);
            int bEva = u.Data.BaseStats.GetValueOrDefault("Eva", 0);
            int bCrit = u.Data.BaseStats.GetValueOrDefault("Crit", 0);
            int bBlock = u.Data.BaseStats.GetValueOrDefault("Block", 0);
            int eqPAtk = u.Equipment.GetTotalStat("phys_atk");
            int eqPDef = u.Equipment.GetTotalStat("phys_def");
            int eqMAtk = u.Equipment.GetTotalStat("mag_atk");
            int eqMDef = u.Equipment.GetTotalStat("mag_def");
            int eqHit = u.Equipment.GetTotalStat("Hit");
            int eqEva = u.Equipment.GetTotalStat("Eva");
            int eqCrit = u.Equipment.GetTotalStat("Crit");
            int eqBlock = u.Equipment.GetTotalStat("Block");
            int eqSpd = u.Equipment.GetTotalStat("Spd");

            string atkStr = eqPAtk != 0 ? "(" + bStr + "+" + eqPAtk + ")" : "(" + bStr + ")";
            string defStr = eqPDef != 0 ? "(" + bDef + "+" + eqPDef + ")" : "(" + bDef + ")";
            string magStr = eqMAtk != 0 ? "(" + bMag + "+" + eqMAtk + ")" : "(" + bMag + ")";
            string mdefStr = eqMDef != 0 ? "(" + bMDef + "+" + eqMDef + ")" : "(" + bMDef + ")";
            string spdStr = eqSpd != 0 ? "(" + (spd - eqSpd) + "+" + eqSpd + ")" : "(" + spd + ")";

            label.AppendText("    SPD" + spdStr + " | 物攻" + (bStr + eqPAtk) + atkStr + " | 物防" + (bDef + eqPDef) + defStr + " | 魔攻" + (bMag + eqMAtk) + magStr + " | 魔防" + (bMDef + eqMDef) + mdefStr + "\n");
            label.AppendText("    命中" + (bHit + eqHit) + "(" + bHit + "+" + eqHit + ") | 回避" + (bEva + eqEva) + "(" + bEva + "+" + eqEva + ") | 会心" + (bCrit + eqCrit) + "%(" + bCrit + "+" + eqCrit + ") | 格挡" + (bBlock + eqBlock) + "%(" + bBlock + "+" + eqBlock + ")\n");

            // Buffs
            var buffs = u.Buffs.Where(b => b.Ratio > 0).ToList();
            if (buffs.Count > 0)
            {
                string buffStr = string.Join(" ", buffs.Select(b => {
                    string turns = b.RemainingTurns == -1 ? "∞" : b.RemainingTurns.ToString();
                    return "[color=#88ff88]" + b.TargetStat + "+" + ((int)(b.Ratio * 100)) + "%[" + turns + "回合][/color]";
                }));
                label.AppendText("    Buff: " + buffStr + "\n");
            }

            // Debuffs
            var debuffs = u.Buffs.Where(b => b.Ratio < 0).ToList();
            if (debuffs.Count > 0)
            {
                string debuffStr = string.Join(" ", debuffs.Select(b => {
                    string turns = b.RemainingTurns == -1 ? "∞" : b.RemainingTurns.ToString();
                    return "[color=#ff8888]" + b.TargetStat + ((int)(b.Ratio * 100)) + "%[" + turns + "回合][/color]";
                }));
                label.AppendText("    Debuff: " + debuffStr + "\n");
            }

            // Ailments
            if (u.Ailments.Count > 0)
            {
                string ailStr = string.Join(" ", u.Ailments.Select(a => "[color=#ff4444]" + a + "[/color]"));
                label.AppendText("    异常: " + ailStr + "\n");
            }

            // Temporal states
            if (u.TemporalStates.Count > 0)
            {
                string tmpStr = string.Join(" ", u.TemporalStates.Select(t => "[color=#ffaa44]" + t.Key + "(" + t.RemainingCount + ")[/color]"));
                label.AppendText("    标记: " + tmpStr + "\n");
            }

            // Passives
            var pv = u.GetEquippedPassiveSkills();
            if (pv.Count > 0)
                label.AppendText("    被动: [color=#888888]" + string.Join(", ", pv.Select(p => p.Name)) + "[/color]\n");
        }

        public static string StarStr(int current, int max)
        {
            if (max <= 0) return "";
            string s = "";
            for (int i = 0; i < current; i++) s += "★";
            for (int i = current; i < max; i++) s += "☆";
            return s;
        }
    }
}
