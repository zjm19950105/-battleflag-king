using System.Linq;
using BattleKing.Core;

namespace BattleKing.Equipment
{
    public static class BuffManager
    {
        public static void ApplyBuff(BattleUnit target, Buff newBuff)
        {
            var existing = target.Buffs.FirstOrDefault(b =>
                b.SkillId == newBuff.SkillId && b.TargetStat == newBuff.TargetStat);

            // Same skill and same stat pure buff/debuff -> skip duplicate.
            // Different stats from one skill, such as SpdDown + EvaDown, must coexist.
            if (existing != null && newBuff.IsPureBuffOrDebuff)
                return;

            // Same effect different skills → stack
            target.Buffs.Add(newBuff);
        }

        /// <summary>Remove one-time buffs after the unit acts</summary>
        public static void CleanupAfterAction(BattleUnit unit)
        {
            unit.Buffs.RemoveAll(b => b.IsOneTime);
        }

        /// <summary>Decrement turn counters at end of turn. Remove expired buffs.</summary>
        public static void CleanupEndOfTurn(BattleUnit unit)
        {
            for (int i = unit.Buffs.Count - 1; i >= 0; i--)
            {
                var b = unit.Buffs[i];
                if (b.RemainingTurns <= 0 || b.RemainingTurns == -1) continue; // -1 = battle-long
                b.RemainingTurns--;
                if (b.RemainingTurns <= 0)
                    unit.Buffs.RemoveAt(i);
            }
        }

        public static float GetTotalBuffRatio(BattleUnit unit, string statName)
        {
            return unit.Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
        }
    }
}
