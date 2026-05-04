using System.Collections.Generic;

namespace BattleKing.Data
{
    public class ActiveSkillData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int ApCost { get; set; }
        public SkillType Type { get; set; }
        public AttackType AttackType { get; set; }
        public int Power { get; set; }
        public int? HitRate { get; set; }
        public TargetType TargetType { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<SkillEffectData> Effects { get; set; } = new();
        public string LearnCondition { get; set; }
        public int? UnlockLevel { get; set; }
    }
}
