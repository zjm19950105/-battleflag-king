using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Equipment
{
    public static class TraitApplier
    {
        /// <summary>Applies character traits to damage calculation. Returns final trait multiplier.</summary>
        public static float ApplyTraitsToDamage(DamageCalculation calc)
        {
            float mult = 1.0f;
            var traits = calc.Attacker.GetEffectiveTraits();
            var attackerClasses = calc.Attacker.GetEffectiveClasses();
            var defenderClasses = calc.Defender.GetEffectiveClasses();

            foreach (var trait in traits)
            {
                switch (trait.TraitType)
                {
                    case "PhysAtkVsInfantry2x":
                        if (calc.Skill.Type == SkillType.Physical
                            && defenderClasses?.Contains(UnitClass.Infantry) == true
                            && attackerClasses?.Contains(UnitClass.Cavalry) != true)
                            mult *= 2.0f;
                        break;

                    case "PhysAtkVsCavalry2x":
                        if (calc.Skill.Type == SkillType.Physical
                            && defenderClasses?.Contains(UnitClass.Cavalry) == true
                            && attackerClasses?.Contains(UnitClass.Flying) != true)
                            mult *= 2.0f;
                        break;

                    case "BowVsFlying":
                        if (calc.Skill.AttackType == AttackType.Ranged
                            && defenderClasses?.Contains(UnitClass.Flying) == true
                            && attackerClasses?.Contains(UnitClass.Archer) != true)
                            mult *= 2.0f;
                        break;
                }
            }

            calc.CharacterTraitMultiplier = mult;
            return mult;
        }

        public static AttackType ModifyAttackType(AttackType baseType, BattleUnit attacker)
        {
            var traits = attacker.GetEffectiveTraits();
            foreach (var trait in traits)
            {
                if (trait.TraitType == "BowVsFlying" || trait.TraitType == "AddRanged")
                {
                    // 弓兵/雪游侠: 无遠隔技能附加遠隔
                    if (baseType == AttackType.Melee)
                        return AttackType.Ranged;
                }
            }
            return baseType;
        }
    }
}
