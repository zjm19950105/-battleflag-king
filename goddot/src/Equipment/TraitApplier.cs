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

            foreach (var trait in traits)
            {
                switch (trait.TraitType)
                {
                    case "PhysAtkVsInfantry2x":
                        // CC 领主: 物理攻击对步兵系2倍
                        if (calc.Skill.Type == SkillType.Physical
                            && calc.Defender.GetEffectiveClasses()?.Contains(UnitClass.Infantry) == true)
                            mult *= 2.0f;
                        break;

                    case "BowVsFlying":
                        // 弓兵特性: 弓攻击对飞行2倍
                        if (calc.Skill.AttackType == AttackType.Ranged
                            && calc.Defender.GetEffectiveClasses()?.Contains(UnitClass.Flying) == true)
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
