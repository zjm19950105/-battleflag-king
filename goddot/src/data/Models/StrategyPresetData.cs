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
        public string SkillId { get; set; }   // 优先使用：精确技能ID（如 "act_sharp_slash"）
        public int SkillIndex { get; set; }   // 备用：技能池索引，SkillId 非空时忽略
        public Condition Condition1 { get; set; }
        public Condition Condition2 { get; set; }
        public ConditionMode Mode1 { get; set; }
        public ConditionMode Mode2 { get; set; }
    }
}
