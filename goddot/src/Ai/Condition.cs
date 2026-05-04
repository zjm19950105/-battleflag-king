using BattleKing.Data;

namespace BattleKing.Ai
{
    public class Condition
    {
        public ConditionCategory Category { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; }
    }
}
