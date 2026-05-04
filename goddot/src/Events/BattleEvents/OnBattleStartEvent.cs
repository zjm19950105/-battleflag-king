using BattleKing.Core;

namespace BattleKing.Events
{
    public class OnBattleStartEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
    }
}
