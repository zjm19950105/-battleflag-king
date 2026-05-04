using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Equipment
{
    public class TraitApplier
    {
        public void ApplyTraitsToDamage(DamageCalculation calc, BattleUnit attacker)
        {
            foreach (var trait in attacker.Data.Traits)
            {
                // Stub: create ITrait instances based on trait.TraitType and apply
            }
        }

        public AttackType ModifyAttackType(AttackType baseType, BattleUnit attacker)
        {
            var result = baseType;
            foreach (var trait in attacker.Data.Traits)
            {
                // Stub: e.g. Snow Ranger "add ranged to melee without ranged skill"
            }
            return result;
        }
    }
}
