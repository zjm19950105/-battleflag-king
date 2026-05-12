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
            var resolvedDefender = calc.CoverTarget != null && !calc.CannotBeCovered
                ? calc.CoverTarget
                : defender;
            calc.ResolvedDefender = resolvedDefender;

            // Stage 1: Attack power
            calc.FinalAttackPower = attacker.GetCurrentAttackPower(skill.Type);

            // Stage 2: Defense (with ignore-defense ratio)
            float defMult = 1.0f - calc.IgnoreDefenseRatio;
            calc.FinalDefense = (int)(resolvedDefender.GetCurrentDefense(skill.Type) * defMult);

            // Stage 3-6: Base difference * effective power * traits
            float effectivePower = calc.EffectivePower;  // includes CounterPowerBonus
            float baseDmgPerHit = calc.BaseDifference * (effectivePower / 100f);
            calc.SkillPowerRatio = effectivePower / 100f;
            baseDmgPerHit *= calc.SkillPowerMultiplier;

            calc.ClassTraitMultiplier = GetClassTraitMultiplier(attacker, resolvedDefender);
            Equipment.TraitApplier.ApplyTraitsToDamage(calc);  // CC traits (PhysAtkVsInfantry2x, BowVsFlying, etc.)

            baseDmgPerHit *= calc.ClassTraitMultiplier;
            baseDmgPerHit *= calc.CharacterTraitMultiplier;

            // Run multi-hit pipeline: all damage kept as float, only rounded at end
            float totalPhysical = 0f;
            float totalMagical = 0f;
            bool anyHit = false;
            bool anyCritical = false;
            bool anyBlocked = false;
            bool anyEvaded = false;
            int hitsLanded = 0;
            int hitsMissed = 0;
            int hitsEvaded = 0;
            int hitsNullified = 0;

            for (int hit = 0; hit < calc.HitCount; hit++)
            {
                float hitPhysical, hitMagical;
                if (skill.HasMixedDamage)
                {
                    hitPhysical = baseDmgPerHit * 0.7f;
                    hitMagical = baseDmgPerHit * 0.3f;
                }
                else
                {
                    hitPhysical = baseDmgPerHit;
                    hitMagical = 0f;
                }

                // Stage 7: Hit check (skip if ForceHit)
                if (!calc.ForceHit)
                {
                    if (!RollHit(attacker, defender, skill))
                    {
                        calc.IsHit = false;
                        hitsMissed++;
                        continue;  // this hit missed, try next hit
                    }
                }

                // Stage 7.5: Evasion check (ForceEvasion evades the first hit only)
                if ((calc.ForceEvasion && hit == 0) || RollEvasion(attacker, defender, skill))
                {
                    calc.IsEvaded = true;
                    anyEvaded = true;
                    hitsEvaded++;
                    continue;  // this hit evaded, try next hit
                }

                // Stage 9: Critical hit
                if (RollCrit(attacker, defender, skill))
                {
                    calc.IsCritical = true;
                    anyCritical = true;
                    calc.CritMultiplier = Math.Min(3.0f, 1.5f + attacker.Buffs.Where(b => b.TargetStat == "CritDmg").Sum(b => b.Ratio));
                    hitPhysical *= calc.CritMultiplier;
                    hitMagical *= calc.CritMultiplier;
                }

                // Stage 10: Block (physical only)
                bool blockThisHit = false;
                if (skill.HasPhysicalComponent && !calc.CannotBeBlocked && calc.ForceBlock != false)
                {
                    if (calc.ForceBlock == true || RollBlock(resolvedDefender, skill))
                    {
                        blockThisHit = true;
                        anyBlocked = true;
                        calc.BlockReduction = resolvedDefender.GetBlockReduction();
                        hitPhysical *= (1f - calc.BlockReduction);
                    }
                }

                if (hit == 0 && blockThisHit)
                    calc.IsBlocked = true;

                // Damage immunity
                if (calc.NullifyPhysicalDamage) hitPhysical = 0f;
                if (calc.NullifyMagicalDamage) hitMagical = 0f;
                if (skill.AttackType == AttackType.Melee
                    && skill.HasPhysicalComponent
                    && resolvedDefender.TryConsumeTemporal("MeleeHitNullify"))
                {
                    hitPhysical = 0f;
                    hitsNullified++;
                }

                totalPhysical += hitPhysical;
                totalMagical += hitMagical;
                hitsLanded++;
                anyHit = true;
            }

            // Final damage multiplier
            totalPhysical *= calc.DamageMultiplier;
            totalMagical *= calc.DamageMultiplier;

            // Round once at the very end (四捨五入 is the final step per original game formula)
            calc.PhysicalDamage = (int)Math.Round((double)totalPhysical);
            calc.MagicalDamage = (int)Math.Round((double)totalMagical);
            calc.IsHit = anyHit;
            calc.IsCritical = anyCritical;
            calc.LandedHits = hitsLanded;
            calc.MissedHits = hitsMissed;
            calc.EvadedHits = hitsEvaded;
            calc.NullifiedHits = hitsNullified;
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
                calc.AppliedAilments,
                resolvedDefender
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
            var aClasses = attacker?.GetEffectiveClasses();
            var dClasses = defender?.GetEffectiveClasses();
            if (aClasses == null || dClasses == null) return 1.0f;

            if (aClasses.Contains(UnitClass.Cavalry) && dClasses.Contains(UnitClass.Infantry))
                return 2.0f;
            if (aClasses.Contains(UnitClass.Flying) && dClasses.Contains(UnitClass.Cavalry))
                return 2.0f;
            if (aClasses.Contains(UnitClass.Archer) && dClasses.Contains(UnitClass.Flying))
                return 2.0f;

            return 1.0f;
        }

        private float GetCharacterTraitMultiplier(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // Wired through TraitApplier which reads GetEffectiveTraits()
            // Called from Calculate() via calc.CharacterTraitMultiplier assignment
            return 1.0f;  // TraitApplier.ApplyTraitsToDamage sets calc.CharacterTraitMultiplier directly
        }

        private bool RollCrit(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            if (attacker.Ailments.Contains(StatusAilment.CritSeal))
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
            bool defenderIsFlying = defender?.GetEffectiveClasses()?.Contains(UnitClass.Flying) == true;
            bool attackerIsGrounded = attacker?.GetEffectiveClasses()?.Contains(UnitClass.Flying) != true;
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
