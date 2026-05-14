using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;

namespace BattleKing.Pipeline
{
    public sealed class HitChanceBreakdown
    {
        public int AttackerHit { get; init; }
        public int DefenderEvasion { get; init; }
        public int SkillHitRate { get; init; }
        public int BaseAccuracy { get; init; }
        public float AccuracyAfterFlyingPenalty { get; init; }
        public float RawChance { get; init; }
        public int FinalChance { get; init; }
        public bool FlyingPenaltyApplied { get; init; }
    }

    public static class HitChanceCalculator
    {
        public static HitChanceBreakdown Calculate(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            int attackerHit = attacker.GetCurrentHitRate();
            int defenderEvasion = defender.GetCurrentEvasion();
            int skillHitRate = skill.Data.HitRate ?? 100;
            int baseAccuracy = attackerHit - defenderEvasion;
            float modifiedAccuracy = baseAccuracy;

            bool flyingPenaltyApplied = IsGroundedMeleeAgainstFlying(attacker, defender, skill);
            if (flyingPenaltyApplied)
                modifiedAccuracy *= 0.5f;

            float rawChance = modifiedAccuracy * skillHitRate / 100f;
            int finalChance = Math.Clamp((int)Math.Floor(rawChance), 0, 100);

            return new HitChanceBreakdown
            {
                AttackerHit = attackerHit,
                DefenderEvasion = defenderEvasion,
                SkillHitRate = skillHitRate,
                BaseAccuracy = baseAccuracy,
                AccuracyAfterFlyingPenalty = modifiedAccuracy,
                RawChance = rawChance,
                FinalChance = finalChance,
                FlyingPenaltyApplied = flyingPenaltyApplied
            };
        }

        private static bool IsGroundedMeleeAgainstFlying(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            IReadOnlyCollection<UnitClass> defenderClasses = defender.GetEffectiveClasses();
            IReadOnlyCollection<UnitClass> attackerClasses = attacker.GetEffectiveClasses();
            bool defenderIsFlying = defenderClasses.Contains(UnitClass.Flying);
            bool attackerIsGrounded = !attackerClasses.Contains(UnitClass.Flying);
            return defenderIsFlying && attackerIsGrounded && skill.AttackType == AttackType.Melee;
        }
    }
}
