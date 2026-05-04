using BattleKing.Core;

namespace BattleKing.Events
{
    public class OnActionStartEvent : IBattleEvent
    {
        public BattleUnit Unit { get; set; }
    }
}
