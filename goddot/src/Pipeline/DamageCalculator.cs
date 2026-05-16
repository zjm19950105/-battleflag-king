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

            // Stage 1-2: Attack and defense (with ignore-defense ratio)
            float defMult = 1.0f - calc.IgnoreDefenseRatio;
            float physicalBaseDmgPerHit = 0f;
            float magicalBaseDmgPerHit = 0f;
            float additionalMagicalBaseDamage = 0f;
            var additionalMagicalComponents = GetAdditionalMagicalComponents(calc, attacker);
            bool hasPhysicalComponent = calc.HasPhysicalComponent;
            bool hasSkillMagicalComponent = skill.HasMagicalComponent;
            bool hasMagicalComponent = hasSkillMagicalComponent || additionalMagicalComponents.Count > 0;
            var primaryDamageType = skill.DamageType;

            if (calc.HasMixedDamage)
            {
                calc.PhysicalAttackPower = attacker.GetCurrentAttackPower(SkillType.Physical);
                calc.PhysicalDefense = (int)(resolvedDefender.GetCurrentDefense(SkillType.Physical) * defMult);
                calc.MagicalDefense = (int)(resolvedDefender.GetCurrentDefense(SkillType.Magical) * defMult);
                calc.MagicalAttackPower = hasSkillMagicalComponent
                    ? attacker.GetCurrentAttackPower(SkillType.Magical)
                    : GetPrimaryAdditionalMagicalAttackPower(additionalMagicalComponents);

                if (primaryDamageType == SkillType.Magical)
                {
                    calc.FinalAttackPower = calc.MagicalAttackPower;
                    calc.FinalDefense = calc.MagicalDefense;
                }
                else
                {
                    calc.FinalAttackPower = calc.PhysicalAttackPower;
                    calc.FinalDefense = calc.PhysicalDefense;
                }

                calc.PhysicalSkillPowerRatio = calc.EffectivePhysicalPower / 100f;
                calc.MagicalSkillPowerRatio = hasSkillMagicalComponent ? calc.EffectiveMagicalPower / 100f : 0f;
                calc.SkillPowerRatio = primaryDamageType == SkillType.Magical
                    ? calc.MagicalSkillPowerRatio
                    : calc.PhysicalSkillPowerRatio;
                physicalBaseDmgPerHit = calc.PhysicalBaseDifference * calc.PhysicalSkillPowerRatio;
                magicalBaseDmgPerHit = hasSkillMagicalComponent
                    ? calc.MagicalBaseDifference * calc.MagicalSkillPowerRatio
                    : 0f;
            }
            else
            {
                calc.FinalAttackPower = attacker.GetCurrentAttackPower(primaryDamageType);
                calc.FinalDefense = (int)(resolvedDefender.GetCurrentDefense(primaryDamageType) * defMult);
                calc.PhysicalAttackPower = hasMagicalComponent && !hasPhysicalComponent ? 0 : calc.FinalAttackPower;
                calc.PhysicalDefense = hasMagicalComponent && !hasPhysicalComponent ? 0 : calc.FinalDefense;
                calc.MagicalAttackPower = hasMagicalComponent && !hasPhysicalComponent ? calc.FinalAttackPower : 0;
                calc.MagicalDefense = hasMagicalComponent && !hasPhysicalComponent ? calc.FinalDefense : 0;

                float effectivePower = calc.EffectivePower;  // includes CounterPowerBonus
                float baseDmgPerHit = calc.BaseDifference * (effectivePower / 100f);
                calc.SkillPowerRatio = effectivePower / 100f;
                calc.PhysicalSkillPowerRatio = hasMagicalComponent && !hasPhysicalComponent ? 0f : calc.SkillPowerRatio;
                calc.MagicalSkillPowerRatio = hasMagicalComponent && !hasPhysicalComponent ? calc.SkillPowerRatio : 0f;

                if (hasMagicalComponent && !hasPhysicalComponent)
                    magicalBaseDmgPerHit = baseDmgPerHit;
                else
                    physicalBaseDmgPerHit = baseDmgPerHit;
            }

            additionalMagicalBaseDamage = CalculateAdditionalMagicalBaseDamage(calc, additionalMagicalComponents, resolvedDefender, defMult);

            calc.ClassTraitMultiplier = GetClassTraitMultiplier(attacker, resolvedDefender);
            Equipment.TraitApplier.ApplyTraitsToDamage(calc);  // CC traits (PhysAtkVsInfantry2x, BowVsFlying, etc.)

            physicalBaseDmgPerHit *= calc.SkillPowerMultiplier;
            magicalBaseDmgPerHit *= calc.SkillPowerMultiplier;
            additionalMagicalBaseDamage *= calc.SkillPowerMultiplier;
            physicalBaseDmgPerHit *= calc.ClassTraitMultiplier;
            magicalBaseDmgPerHit *= calc.ClassTraitMultiplier;
            additionalMagicalBaseDamage *= calc.ClassTraitMultiplier;
            physicalBaseDmgPerHit *= calc.CharacterTraitMultiplier;
            magicalBaseDmgPerHit *= calc.CharacterTraitMultiplier;
            additionalMagicalBaseDamage *= calc.CharacterTraitMultiplier;

            if (calc.FixedPhysicalDamagePerHit.HasValue)
            {
                physicalBaseDmgPerHit = Math.Max(0f, calc.FixedPhysicalDamagePerHit.Value);
                magicalBaseDmgPerHit = 0f;
            }

            // Run multi-hit pipeline: each hit is rounded before it contributes to totals.
            int totalPhysical = 0;
            int totalMagical = 0;
            bool anyHit = false;
            bool anyCritical = false;
            bool anyBlocked = false;
            bool anyEvaded = false;
            int hitsLanded = 0;
            int hitsMissed = 0;
            int hitsEvaded = 0;
            int hitsNullified = 0;
            bool additionalMagicalApplied = false;
            calc.HitResults.Clear();

            for (int hit = 0; hit < calc.HitCount; hit++)
            {
                float hitPhysical = physicalBaseDmgPerHit;
                float hitMagical = magicalBaseDmgPerHit;
                float baseHitPhysical = hitPhysical;
                float baseHitMagical = hitMagical;
                float hitAdditionalMagicalBaseDamage = 0f;

                // Stage 7: Hit check. ForceHit bypasses accuracy, but Darkness still forces a miss.
                if (attacker.Ailments.Contains(StatusAilment.Darkness)
                    || (!calc.ForceHit && !RollHit(attacker, defender, skill)))
                {
                    calc.IsHit = false;
                    hitsMissed++;
                    calc.HitResults.Add(new DamageHitResult
                    {
                        HitIndex = hit + 1,
                        Missed = true,
                        BasePhysicalDamage = baseHitPhysical,
                        BaseMagicalDamage = baseHitMagical
                    });
                    continue;  // this hit missed, try next hit
                }

                // Stage 7.5: Evasion check (ForceEvasion evades the first hit only; ForceHit suppresses it)
                if (!calc.ForceHit
                    && ((calc.ForceEvasion && hit == 0) || RollEvasion(attacker, defender, skill)))
                {
                    calc.IsEvaded = true;
                    anyEvaded = true;
                    hitsEvaded++;
                    calc.HitResults.Add(new DamageHitResult
                    {
                        HitIndex = hit + 1,
                        Evaded = true,
                        BasePhysicalDamage = baseHitPhysical,
                        BaseMagicalDamage = baseHitMagical
                    });
                    continue;  // this hit evaded, try next hit
                }

                if (!additionalMagicalApplied && additionalMagicalBaseDamage > 0f)
                {
                    hitAdditionalMagicalBaseDamage = additionalMagicalBaseDamage;
                    hitMagical += hitAdditionalMagicalBaseDamage;
                    baseHitMagical = hitMagical;
                    additionalMagicalApplied = true;
                }

                bool criticalThisHit = false;
                float critMultiplierThisHit = 1.0f;
                // Stage 9: Critical hit
                if (RollCrit(calc))
                {
                    calc.IsCritical = true;
                    anyCritical = true;
                    calc.CritMultiplier = Math.Min(3.0f, 1.5f + attacker.Buffs.Where(b => b.TargetStat == "CritDmg").Sum(b => b.Ratio));
                    criticalThisHit = true;
                    critMultiplierThisHit = calc.CritMultiplier;
                    hitPhysical *= calc.CritMultiplier;
                    hitMagical *= calc.CritMultiplier;
                }

                // Stage 10: Block (physical only)
                bool blockThisHit = false;
                if (hasPhysicalComponent && !calc.CannotBeBlocked && calc.ForceBlock != false)
                {
                    if (calc.ForceBlock == true || RollBlock(resolvedDefender, skill))
                    {
                        blockThisHit = true;
                        anyBlocked = true;
                        calc.BlockReduction = calc.ForcedBlockReduction ?? resolvedDefender.GetBlockReduction();
                        hitPhysical *= (1f - calc.BlockReduction);
                    }
                }

                if (hit == 0 && blockThisHit)
                    calc.IsBlocked = true;

                // Damage immunity / reflection
                bool nullifiedThisHit = false;
                bool consumedGeneralNullify = false;
                if (calc.ReflectDamageToAttacker && hasMagicalComponent)
                {
                    calc.ReflectedDamage += RoundHitDamage(hitMagical * calc.DamageMultiplier);
                    hitMagical = 0f;
                    hitsNullified++;
                    nullifiedThisHit = true;
                }

                if (calc.NullifyPhysicalDamage) hitPhysical = 0f;
                if (calc.NullifyMagicalDamage) hitMagical = 0f;
                if (resolvedDefender.TryConsumeTemporal("DamageNullify"))
                {
                    hitPhysical = 0f;
                    hitMagical = 0f;
                    hitsNullified++;
                    nullifiedThisHit = true;
                    consumedGeneralNullify = true;
                }
                else if (hasMagicalComponent
                    && resolvedDefender.TryConsumeTemporal("MagicDamageNullify"))
                {
                    hitMagical = 0f;
                    hitsNullified++;
                    nullifiedThisHit = true;
                }
                if (!consumedGeneralNullify
                    && skill.AttackType == AttackType.Melee
                    && hasPhysicalComponent
                    && resolvedDefender.TryConsumeTemporal("MeleeHitNullify"))
                {
                    hitPhysical = 0f;
                    hitsNullified++;
                    nullifiedThisHit = true;
                }

                hitPhysical *= calc.DamageMultiplier;
                hitMagical *= calc.DamageMultiplier;
                int roundedPhysical = RoundHitDamage(hitPhysical);
                int roundedMagical = RoundHitDamage(hitMagical);
                totalPhysical += roundedPhysical;
                totalMagical += roundedMagical;
                hitsLanded++;
                anyHit = true;
                calc.HitResults.Add(new DamageHitResult
                {
                    HitIndex = hit + 1,
                    Landed = true,
                    Critical = criticalThisHit,
                    CritMultiplier = critMultiplierThisHit,
                    Blocked = blockThisHit,
                    BlockReduction = blockThisHit ? calc.BlockReduction : 0f,
                    Nullified = nullifiedThisHit,
                    AdditionalMagicalDamageApplied = hitAdditionalMagicalBaseDamage > 0f,
                    AdditionalMagicalBaseDamage = hitAdditionalMagicalBaseDamage,
                    BasePhysicalDamage = baseHitPhysical,
                    BaseMagicalDamage = baseHitMagical,
                    RawPhysicalDamage = hitPhysical,
                    RawMagicalDamage = hitMagical,
                    RoundedPhysicalDamage = roundedPhysical,
                    RoundedMagicalDamage = roundedMagical,
                    PhysicalDamage = hitPhysical,
                    MagicalDamage = hitMagical
                });
            }

            // Totals are already rounded per hit.
            calc.PhysicalDamage = totalPhysical;
            calc.MagicalDamage = totalMagical;
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
                resolvedDefender,
                calc.HitResults
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

        private static int RoundHitDamage(float value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static List<AdditionalMagicalDamageComponent> GetAdditionalMagicalComponents(
            DamageCalculation calc,
            BattleUnit fallbackSource)
        {
            if (calc.AdditionalMagicalComponents.Count > 0)
                return calc.AdditionalMagicalComponents;

            if (calc.AdditionalMagicalPower <= 0f)
                return new List<AdditionalMagicalDamageComponent>();

            return new List<AdditionalMagicalDamageComponent>
            {
                new AdditionalMagicalDamageComponent
                {
                    Source = fallbackSource,
                    Power = calc.AdditionalMagicalPower
                }
            };
        }

        private static int GetPrimaryAdditionalMagicalAttackPower(
            IReadOnlyList<AdditionalMagicalDamageComponent> components)
        {
            return components
                .Select(component => GetPanelMagicalAttackPower(component.Source))
                .FirstOrDefault();
        }

        private static float CalculateAdditionalMagicalBaseDamage(
            DamageCalculation calc,
            IReadOnlyList<AdditionalMagicalDamageComponent> components,
            BattleUnit resolvedDefender,
            float defenseMultiplier)
        {
            calc.AdditionalMagicalBreakdowns.Clear();
            if (components.Count == 0)
                return 0f;

            int magicalDefense = (int)(resolvedDefender.GetCurrentDefense(SkillType.Magical) * defenseMultiplier);
            float total = 0f;
            foreach (var component in components)
            {
                var source = component.Source ?? calc.Attacker;
                int magicalAttack = GetPanelMagicalAttackPower(source);
                float powerRatio = component.Power / 100f;
                float baseDamage = Math.Max(1, magicalAttack - magicalDefense) * powerRatio;
                total += baseDamage;
                calc.AdditionalMagicalBreakdowns.Add(new AdditionalMagicalDamageBreakdown
                {
                    Source = source,
                    Power = component.Power,
                    MagicalAttackPower = magicalAttack,
                    MagicalDefense = magicalDefense,
                    SkillPowerRatio = powerRatio,
                    BaseDamagePerHit = baseDamage,
                    SourceSkillId = component.SourceSkillId
                });
            }

            return total;
        }

        private static int GetPanelMagicalAttackPower(BattleUnit source)
        {
            return Math.Max(0, source?.GetStatBreakdown("Mag").EquippedBaseline ?? 0);
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

        private bool RollCrit(DamageCalculation calc)
        {
            var attacker = calc.Attacker;
            if (calc.CannotCrit)
                return false;

            if (attacker.Ailments.Contains(StatusAilment.CritSeal))
                return false;

            if (calc.ForceCrit)
                return true;

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

            int hitRate = HitChanceCalculator.Calculate(attacker, defender, skill).FinalChance;
            return RandUtil.Roll100() < hitRate;
        }

        private bool RollEvasion(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // Evasion skill (e.g. 回避步伐) — always evades, bypasses everything
            // This is set by passive effects via DamageCalculation.ForceEvasion
            // Handled in Calculate() before calling this method

            // Normal evasion is already factored into hit rate formula:
            //   hitRate = (attacker.Hit - defender.Eva) * skillHitRate / 100
            // So no separate evasion roll needed here.

            return false;
        }
    }
}
