using System.Linq;
using BattleKing.Core;

namespace BattleKing.Equipment
{
    public class BuffManager
    {
        public void ApplyBuff(BattleUnit target, Buff newBuff)
        {
            var existing = target.Buffs.FirstOrDefault(b => b.SkillId == newBuff.SkillId);

            // Rule: same skill pure buff/debuff -> skip duplicate
            if (existing != null && newBuff.IsPureBuffOrDebuff)
                return;

            // Rule: same effect from different skills -> stack
            target.Buffs.Add(newBuff);
        }

        public void RemoveOneTimeBuffsAfterAction(BattleUnit unit)
        {
            unit.Buffs.RemoveAll(b => b.IsOneTime);
        }

        public float GetTotalBuffRatio(BattleUnit unit, string statName)
        {
            return unit.Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
        }
    }
}
