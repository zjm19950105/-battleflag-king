using BattleKing.Core;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Events
{
    public class AfterAttackEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public DamageResult Result { get; set; }
    }
}
