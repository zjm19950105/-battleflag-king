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
        /// <summary>New signature: Calculate from a DamageCalculation (supports passive skill intervention)</summary>
        public DamageResult Calculate(DamageCalculation calc)
        {
            var attacker = calc.Attacker;
            var defender = calc.Defender;
            var skill = calc.Skill;

            // Stage 1: Attack power
            calc.FinalAttackPower = attacker.GetCurrentAttackPower(skill.Type);

            // Stage 2: Defense (with ignore-defense ratio)
            float defMult = 1.0f - calc.IgnoreDefenseRatio;
            calc.FinalDefense = (int)(defender.GetCurrentDefense(skill.Type) * defMult);

            // Stage 3-6: Base difference * effective power * traits
            float effectivePower = calc.EffectivePower;  // includes CounterPowerBonus
            float baseDmgPerHit = calc.BaseDifference * (effectivePower / 100f);
            calc.SkillPowerRatio = effectivePower / 100f;
            baseDmgPerHit *= calc.SkillPowerMultiplier;

            calc.ClassTraitMultiplier = GetClassTraitMultiplier(attacker, defender);
            calc.CharacterTraitMultiplier = GetCharacterTraitMultiplier(attacker, defender, skill);

            baseDmgPerHit *= calc.ClassTraitMultiplier;
            baseDmgPerHit *= calc.CharacterTraitMultiplier;

            // Run multi-hit pipeline: each hit independently checks hit/evasion/block/crit
            int totalPhysical = 0;
            int totalMagical = 0;
            bool anyHit = false;
            bool anyCritical = false;
            bool anyBlocked = false;
            bool anyEvaded = false;
            int hitsLanded = 0;
            int blocksTriggered = 0;

            for (int hit = 0; hit < calc.HitCount; hit++)
            {
                // Split physical / magical damage per hit
                int hitPhysical, hitMagical;
                if (skill.HasMixedDamage)
                {
                    hitPhysical = (int)(baseDmgPerHit * 0.7f);
                    hitMagical = (int)(baseDmgPerHit * 0.3f);
                }
                else
                {
                    hitPhysical = (int)baseDmgPerHit;
                    hitMagical = 0;
                }

                // Stage 7: Hit check (skip if ForceHit)
                if (!calc.ForceHit)
                {
                    if (!RollHit(attacker, defender, skill))
                    {
                        calc.IsHit = false;
                        continue;  // this hit missed, try next hit
                    }
                }

                // Stage 7.5: Evasion check (ForceEvasion always evades)
                if (calc.ForceEvasion || RollEvasion(attacker, defender, skill))
                {
                    calc.IsEvaded = true;
                    anyEvaded = true;
                    continue;  // this hit evaded, try next hit
                }

                // === Cover processing (before damage) ===
                var hitDefender = defender;
                if (calc.CoverTarget != null && !calc.CannotBeCovered)
                {
                    hitDefender = calc.CoverTarget;
                    // Recalculate defense for cover target
                    calc.FinalDefense = (int)(hitDefender.GetCurrentDefense(skill.Type) * defMult);
                }

                // Stage 9: Critical hit
                if (RollCrit(attacker, defender, skill))
                {
                    calc.IsCritical = true;
                    anyCritical = true;
                    calc.CritMultiplier = Math.Min(3.0f, 1.5f + attacker.Buffs.Where(b => b.TargetStat == "CritDmg").Sum(b => b.Ratio));
                    hitPhysical = (int)(hitPhysical * calc.CritMultiplier);
                    hitMagical = (int)(hitMagical * calc.CritMultiplier);
                }

                // Stage 10: Block (physical only, respect CannotBeBlocked and ForceBlock)
                bool blockThisHit = false;
                if (skill.HasPhysicalComponent && !calc.CannotBeBlocked && calc.ForceBlock != false)
                {
                    if (calc.ForceBlock == true || RollBlock(hitDefender, skill))
                    {
                        blockThisHit = true;
                        anyBlocked = true;
                        blocksTriggered++;
                        calc.BlockReduction = hitDefender.GetBlockReduction();
                        hitPhysical = (int)(hitPhysical * (1 - calc.BlockReduction));
                    }
                }

                // Hit-dependent conditional: first hit blocked can cancel special effects
                // (e.g. "passive steal" — first hit blocked prevents PP steal)
                // This flag lets skill effects check after damage resolution
                if (hit == 0 && blockThisHit)
                    calc.IsBlocked = true;  // "first hit was blocked" flag for skill effects

                // Damage immunity
                if (calc.NullifyPhysicalDamage) hitPhysical = 0;
                if (calc.NullifyMagicalDamage) hitMagical = 0;

                totalPhysical += hitPhysical;
                totalMagical += hitMagical;
                hitsLanded++;
                anyHit = true;
            }

            // Final damage multiplier
            totalPhysical = (int)(totalPhysical * calc.DamageMultiplier);
            totalMagical = (int)(totalMagical * calc.DamageMultiplier);

            // Round
            totalPhysical = (int)Math.Round((double)totalPhysical);
            totalMagical = (int)Math.Round((double)totalMagical);

            calc.PhysicalDamage = totalPhysical;
            calc.MagicalDamage = totalMagical;
            calc.IsHit = anyHit;
            calc.IsCritical = anyCritical;
            if (!anyBlocked && calc.HitCount == 1)
                calc.IsBlocked = false;
            // For multi-hit: IsBlocked = first hit was blocked (set above)

            return new DamageResult(
                calc.PhysicalDamage,
                calc.MagicalDamage,
                calc.IsHit,
                calc.IsCritical,
                calc.IsBlocked || anyBlocked,
                anyEvaded,
                calc.AppliedAilments
            );
        }

        /// <summary>Old signature — kept for backward compatibility. Wraps parameters into DamageCalculation.</summary>
        public DamageResult Calculate(BattleUnit attacker, BattleUnit defender, ActiveSkill skill, BattleContext ctx)
        {
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill
            };
            return Calculate(calc);
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
            if (attacker.Ailments.Contains(StatusAilment.Darkness))
                return false;

            int skillHitRate = skill.Data.HitRate ?? 100;
            int hitRate = skillHitRate + attacker.GetCurrentHitRate() - defender.GetCurrentEvasion();

            // 飞行系防御特性：地上近接攻击命中率半减
            bool defenderIsFlying = defender.Data?.Classes?.Contains(UnitClass.Flying) == true;
            bool attackerIsGrounded = attacker.Data?.Classes?.Contains(UnitClass.Flying) != true;
            bool isMelee = skill.AttackType == AttackType.Melee;
            if (defenderIsFlying && attackerIsGrounded && isMelee)
                hitRate /= 2;

            hitRate = Math.Clamp(hitRate, 0, 100);
            return RandUtil.Roll100() < hitRate;
        }

        private bool RollEvasion(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // Evasion skill (e.g. 回避步伐) — always evades, bypasses everything
            // This is set by passive effects via DamageCalculation.ForceEvasion
            // Handled in Calculate() before calling this method

            // Normal evasion is already factored into hit rate formula:
            //   hitRate = skillHit + attacker.Hit - defender.Eva
            // So no separate evasion roll needed here.

            return false;
        }
    }
}
