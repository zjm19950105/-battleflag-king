using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
    public class HealEffect : ISkillEffect
    {
        public HealEffect(Dictionary<string, object> parameters)
        {
        }

        public void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null)
        {
        }
    }
}
