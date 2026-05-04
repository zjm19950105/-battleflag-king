using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;
using BattleKing.Utils;

namespace BattleKing.Pipeline
{
    public class DamageCalculator
    {
        public DamageResult Calculate(BattleUnit attacker, BattleUnit defender, ActiveSkill skill, BattleContext ctx)
        {
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill
            };

            // Stage 1: Attack power
            calc.FinalAttackPower = attacker.GetCurrentAttackPower(skill.Type);

            // Stage 2: Defense
            calc.FinalDefense = defender.GetCurrentDefense(skill.Type);

            // Stage 3-6: Base difference * skill power * class trait * character trait
            float baseDmg = calc.BaseDifference * (skill.Power / 100f);
            calc.SkillPowerRatio = skill.Power / 100f;
            calc.ClassTraitMultiplier = GetClassTraitMultiplier(attacker, defender);
            calc.CharacterTraitMultiplier = GetCharacterTraitMultiplier(attacker, defender, skill);

            baseDmg *= calc.ClassTraitMultiplier;
            baseDmg *= calc.CharacterTraitMultiplier;

            // Split physical / magical
            if (skill.HasMixedDamage)
            {
                calc.PhysicalDamage = (int)(baseDmg * 0.7f);
                calc.MagicalDamage = (int)(baseDmg * 0.3f);
            }
            else
            {
                calc.PhysicalDamage = (int)baseDmg;
            }

            // Stage 7: Hit check
            if (!RollHit(attacker, defender, skill))
            {
                calc.IsHit = false;
                calc.PhysicalDamage = 0;
                calc.MagicalDamage = 0;
                return new DamageResult(
                    calc.PhysicalDamage,
                    calc.MagicalDamage,
                    calc.IsHit,
                    calc.IsCritical,
                    calc.IsBlocked,
                    calc.IsEvaded,
                    calc.AppliedAilments
                );
            }

            // Stage 8: Evasion check
            if (RollEvasion(attacker, defender, skill))
            {
                calc.IsEvaded = true;
                calc.PhysicalDamage = 0;
                calc.MagicalDamage = 0;
                return new DamageResult(
                    calc.PhysicalDamage,
                    calc.MagicalDamage,
                    calc.IsHit,
                    calc.IsCritical,
                    calc.IsBlocked,
                    calc.IsEvaded,
                    calc.AppliedAilments
                );
            }

            // Stage 9: Critical hit
            if (RollCrit(attacker, defender, skill))
            {
                calc.IsCritical = true;
                calc.CritMultiplier = Math.Min(3.0f, 1.5f + attacker.Buffs.Where(b => b.TargetStat == "CritDmg").Sum(b => b.Ratio));
                calc.PhysicalDamage = (int)(calc.PhysicalDamage * calc.CritMultiplier);
                calc.MagicalDamage = (int)(calc.MagicalDamage * calc.CritMultiplier);
            }

            // Stage 10: Block (physical only)
            if (skill.HasPhysicalComponent && RollBlock(defender, skill))
            {
                calc.IsBlocked = true;
                calc.BlockReduction = defender.GetBlockReduction();
                calc.PhysicalDamage = (int)(calc.PhysicalDamage * (1 - calc.BlockReduction));
            }

            // Final rounding
            calc.PhysicalDamage = (int)Math.Round((double)calc.PhysicalDamage);
            calc.MagicalDamage = (int)Math.Round((double)calc.MagicalDamage);

            return new DamageResult(
                calc.PhysicalDamage,
                calc.MagicalDamage,
                calc.IsHit,
                calc.IsCritical,
                calc.IsBlocked,
                calc.IsEvaded,
                calc.AppliedAilments
            );
        }

        private float GetClassTraitMultiplier(BattleUnit attacker, BattleUnit defender)
        {
            if (attacker.Data?.Classes == null || defender.Data?.Classes == null)
                return 1.0f;

            if (attacker.Data.Classes.Contains(UnitClass.Cavalry) && defender.Data.Classes.Contains(UnitClass.Infantry))
                return 2.0f;
            if (attacker.Data.Classes.Contains(UnitClass.Flying) && defender.Data.Classes.Contains(UnitClass.Cavalry))
                return 2.0f;
            if (attacker.Data.Classes.Contains(UnitClass.Archer) && defender.Data.Classes.Contains(UnitClass.Flying))
                return 2.0f;

            return 1.0f;
        }

        private float GetCharacterTraitMultiplier(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // TODO: integrate with TraitApplier when character traits are implemented
            return 1.0f;
        }

        private bool RollCrit(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            if (defender.Ailments.Contains(StatusAilment.CritSeal))
                return false;

            int critRate = attacker.GetCurrentCritRate();
            return RandUtil.Roll100() < critRate;
        }

        private bool RollBlock(BattleUnit defender, ActiveSkill skill)
        {
            if (defender.Ailments.Contains(StatusAilment.BlockSeal))
                return false;

            if (!skill.HasPhysicalComponent)
                return false;

            int blockRate = defender.GetCurrentBlockRate();
            return RandUtil.Roll100() < blockRate;
        }

        private bool RollHit(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // TODO: Darkness state check (Darkness makes hit rate 0, except when Frozen/Stun/Guarded/Charging)
            if (attacker.Ailments.Contains(StatusAilment.Darkness))
            {
                // For MVP, darkness always misses (advanced exceptions can be added later)
                return false;
            }

            // Calculate hit rate: skill hit rate + attacker Hit - defender Eva
            int skillHitRate = skill.Data.HitRate ?? 100;
            int hitRate = skillHitRate + attacker.GetCurrentHitRate() - defender.GetCurrentEvasion();
            hitRate = Math.Clamp(hitRate, 0, 100);

            return RandUtil.Roll100() < hitRate;
        }

        private bool RollEvasion(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // Evasion attacks always hit, skip evasion check
            if (skill.AttackType == AttackType.Ranged)
            {
                // TODO: distinguish "evasion attack" type; for MVP, ranged attacks are treated as evasion attacks
                return false;
            }

            // TODO: check if defender has evasion-type passive skill
            // For MVP, no evasion skill exists yet, so evasion never triggers
            return false;
        }
    }
}
