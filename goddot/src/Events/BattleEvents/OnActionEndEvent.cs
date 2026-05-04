using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Events
{
    public class OnActionEndEvent : IBattleEvent
    {
        public BattleUnit Unit { get; set; }
        public ActiveSkill Skill { get; set; }
        public List<BattleUnit> Targets { get; set; }
        public List<DamageResult> Results { get; set; }
    }
}
