using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Skills
{
    public class PendingAction
    {
        public PendingActionType Type { get; set; }
        public BattleUnit Actor { get; set; }
        public List<BattleUnit> Targets { get; set; } = new List<BattleUnit>();

        public int Power { get; set; }
        public int? HitRate { get; set; }
        public SkillType DamageType { get; set; }
        public AttackType AttackType { get; set; }
        public TargetType TargetType { get; set; }
        public float IgnoreDefenseRatio { get; set; } = 0f;
        public UnitClass? IgnoreDefenseTargetClass { get; set; }

        public List<string> Tags { get; set; } = new List<string>();

        public string SourcePassiveId { get; set; }
    }
}
