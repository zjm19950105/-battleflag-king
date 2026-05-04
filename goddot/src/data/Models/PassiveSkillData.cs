using System.Collections.Generic;

namespace BattleKing.Data
{
    public class PassiveSkillData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int PpCost { get; set; }
        public PassiveTriggerTiming TriggerTiming { get; set; }
        public SkillType Type { get; set; }
        public int? Power { get; set; }
        public int? HitRate { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<SkillEffectData> Effects { get; set; } = new();
        public bool HasSimultaneousLimit { get; set; }
        public string LearnCondition { get; set; }
        public int? UnlockLevel { get; set; }
    }
}
