using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;

namespace BattleKing.Pipeline
{
    public class DamageCalculation
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }

        public int FinalAttackPower { get; set; }
        public int FinalDefense { get; set; }

        public int BaseDifference => System.Math.Max(1, FinalAttackPower - FinalDefense);

        public float SkillPowerRatio { get; set; } = 1.0f;
        public float ClassTraitMultiplier { get; set; } = 1.0f;
        public float CharacterTraitMultiplier { get; set; } = 1.0f;

        public bool IsHit { get; set; } = true;
        public bool IsCritical { get; set; } = false;
        public bool IsBlocked { get; set; } = false;
        public bool IsEvaded { get; set; } = false;
        public float CritMultiplier { get; set; } = 1.5f;
        public float BlockReduction { get; set; } = 0f;

        public int PhysicalDamage { get; set; }
        public int MagicalDamage { get; set; }
        public int TotalDamage => PhysicalDamage + MagicalDamage;
        public int LandedHits { get; set; }
        public int MissedHits { get; set; }
        public int EvadedHits { get; set; }
        public int NullifiedHits { get; set; }
        public List<DamageHitResult> HitResults { get; } = new List<DamageHitResult>();

        public List<StatusAilment> AppliedAilments { get; set; } = new List<StatusAilment>();

        // === Module 1: Mutable params for passive skill intervention ===

        /// <summary>Force hit (skip hit check, but evasion skills still work)</summary>
        public bool ForceHit { get; set; } = false;

        /// <summary>Force critical hit if the attacker is allowed to crit</summary>
        public bool ForceCrit { get; set; } = false;

        /// <summary>Force evasion (skip hit, always evade)</summary>
        public bool ForceEvasion { get; set; } = false;

        /// <summary>Force block (true) or force no block (false); null = normal roll</summary>
        public bool? ForceBlock { get; set; } = null;

        /// <summary>Additional multiplier on skill power (e.g. Death Blade: HP<50% → +0.5)</summary>
        public float SkillPowerMultiplier { get; set; } = 1.0f;

        /// <summary>Final damage multiplier after all other calculations</summary>
        public float DamageMultiplier { get; set; } = 1.0f;

        /// <summary>Ignore defense ratio (0~1, e.g. Heavy Breaker ignores 100%)</summary>
        public float IgnoreDefenseRatio { get; set; } = 0f;

        /// <summary>Nullify physical damage (1-hit immunity, etc.)</summary>
        public bool NullifyPhysicalDamage { get; set; } = false;

        /// <summary>Nullify magical damage (magic barrier, etc.)</summary>
        public bool NullifyMagicalDamage { get; set; } = false;

        /// <summary>If set, attack redirects to this unit (cover)</summary>
        public BattleUnit CoverTarget { get; set; } = null;

        /// <summary>Final unit that receives damage after cover is resolved</summary>
        public BattleUnit ResolvedDefender { get; set; } = null;

        /// <summary>This attack cannot be covered</summary>
        public bool CannotBeCovered { get; set; } = false;

        /// <summary>This attack cannot be blocked (guard-seal)</summary>
        public bool CannotBeBlocked { get; set; } = false;

        // === Module 1: Multi-hit support ===

        /// <summary>Number of hits for this attack (1 for normal, N for multi-hit)</summary>
        public int HitCount { get; set; } = 1;

        /// <summary>Bonus power from custom counters (e.g. sprite counter × 30)</summary>
        public float CounterPowerBonus { get; set; } = 0f;

        /// <summary>Bonus power from structured calculation effects.</summary>
        public float SkillPowerBonus { get; set; } = 0f;

        /// <summary>Effective skill power = base power + structured/counter bonuses.</summary>
        public float EffectivePower => Skill.Power + SkillPowerBonus + CounterPowerBonus;
    }

    public class DamageHitResult
    {
        public int HitIndex { get; set; }
        public bool Landed { get; set; }
        public bool Missed { get; set; }
        public bool Evaded { get; set; }
        public bool Nullified { get; set; }
        public bool Critical { get; set; }
        public bool Blocked { get; set; }
        public float CritMultiplier { get; set; } = 1.0f;
        public float BlockReduction { get; set; } = 0f;
        public float BasePhysicalDamage { get; set; }
        public float BaseMagicalDamage { get; set; }
        public float PhysicalDamage { get; set; }
        public float MagicalDamage { get; set; }
        public float BaseTotalDamage => BasePhysicalDamage + BaseMagicalDamage;
        public float TotalDamage => PhysicalDamage + MagicalDamage;
    }
}
