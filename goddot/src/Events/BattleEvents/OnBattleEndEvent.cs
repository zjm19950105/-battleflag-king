using BattleKing.Core;

namespace BattleKing.Events
{
    public class OnBattleEndEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
        public BattleResult Result { get; set; }
    }
}
