using System.Collections.Generic;
using BattleKing.Ai;

namespace BattleKing.Data
{
    public class StrategyPresetData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<PresetStrategyData> Strategies { get; set; } = new();
    }

    public class PresetStrategyData
    {
        public int SkillIndex { get; set; } // 0=第一个可用技能, 1=第二个...
        public Condition Condition1 { get; set; }
        public Condition Condition2 { get; set; }
        public ConditionMode Mode1 { get; set; }
        public ConditionMode Mode2 { get; set; }
    }
}
