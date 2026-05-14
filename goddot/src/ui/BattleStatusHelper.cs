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
            var str = u.GetStatBreakdown("Str");
            var def = u.GetStatBreakdown("Def");
            var mag = u.GetStatBreakdown("Mag");
            var mdef = u.GetStatBreakdown("MDef");
            var hit = u.GetStatBreakdown("Hit");
            var eva = u.GetStatBreakdown("Eva");
            var crit = u.GetStatBreakdown("Crit");
            var block = u.GetStatBreakdown("Block");
            var spd = u.GetStatBreakdown("Spd");

            label.AppendText("    SPD" + FormatStatBreakdown(spd.EquippedBaseline, spd.BuffDelta) + " | 物攻" + str.Current + FormatStatBreakdown(str.EquippedBaseline, str.BuffDelta) + " | 物防" + def.Current + FormatStatBreakdown(def.EquippedBaseline, def.BuffDelta) + " | 魔攻" + mag.Current + FormatStatBreakdown(mag.EquippedBaseline, mag.BuffDelta) + " | 魔防" + mdef.Current + FormatStatBreakdown(mdef.EquippedBaseline, mdef.BuffDelta) + "\n");
            label.AppendText("    命中" + hit.Current + FormatStatBreakdown(hit.EquippedBaseline, hit.BuffDelta) + " | 回避" + eva.Current + FormatStatBreakdown(eva.EquippedBaseline, eva.BuffDelta) + " | 会心" + crit.Current + "%" + FormatStatBreakdown(crit.EquippedBaseline, crit.BuffDelta) + " | 格挡" + block.Current + "%" + FormatStatBreakdown(block.EquippedBaseline, block.BuffDelta) + "\n");

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

        private static string FormatStatBreakdown(int baseValue, int delta)
        {
            if (delta > 0) return $"({baseValue}+{delta})";
            if (delta < 0) return $"({baseValue}{delta})";
            return $"({baseValue})";
        }
    }
}
