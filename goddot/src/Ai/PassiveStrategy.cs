using BattleKing.Data;

namespace BattleKing.Ai
{
    public class PassiveStrategy
    {
        public string SkillId { get; set; }
        public Condition Condition1 { get; set; }
        public Condition Condition2 { get; set; }
        public ConditionMode Mode1 { get; set; }
        public ConditionMode Mode2 { get; set; }
    }
}
