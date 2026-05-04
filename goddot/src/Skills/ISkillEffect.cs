using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
    public interface ISkillEffect
    {
        void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null);
    }
}
