using BattleKing.Core;
using BattleKing.Skills;

namespace BattleKing.Events
{
    public class BattleStartEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
    }

    public class BeforeActiveUseEvent : IBattleEvent
    {
        public BattleUnit Caster { get; set; }
        public ActiveSkill Skill { get; set; }
        public BattleContext Context { get; set; }
    }

    public class BeforeHitEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public BattleContext Context { get; set; }
    }

    public class AfterHitEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public int DamageDealt { get; set; }
        public bool IsHit { get; set; }
        public BattleContext Context { get; set; }
    }

    public class AfterActiveUseEvent : IBattleEvent
    {
        public BattleUnit Caster { get; set; }
        public ActiveSkill Skill { get; set; }
        public BattleContext Context { get; set; }
    }

    public class BattleEndEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
        public BattleResult Result { get; set; }
    }
}
