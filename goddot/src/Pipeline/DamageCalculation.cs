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

        public List<StatusAilment> AppliedAilments { get; set; } = new List<StatusAilment>();
    }
}
