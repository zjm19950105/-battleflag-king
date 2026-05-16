using System.Collections.Generic;

namespace BattleKing.Data
{
    public class ActiveSkillData
    {
        public string Id { get; set; }
        /// <summary>Canonical English skill name from the datamine source; Id remains the stable reference key.</summary>
        public string EnglishName { get; set; }
        /// <summary>Current Chinese display name shown in UI/logs.</summary>
        public string Name { get; set; }
        public int ApCost { get; set; }
        public SkillType Type { get; set; }
        public SkillType? DamageType { get; set; }
        public AttackType AttackType { get; set; }
        public int Power { get; set; }
        public int? PhysicalPower { get; set; }
        public int? MagicalPower { get; set; }
        public int? HitRate { get; set; }
        public TargetType TargetType { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<SkillEffectData> Effects { get; set; } = new();
        public string LearnCondition { get; set; }
        public int? UnlockLevel { get; set; }
    }
}
