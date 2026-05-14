using BattleKing.Core;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Events
{
    public enum BattleActionSourceKind
    {
        ActiveAttack,
        PendingAction
    }

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

    public class AfterActiveCostEvent : IBattleEvent
    {
        public BattleUnit Caster { get; set; }
        public ActiveSkill Skill { get; set; }
        public BattleContext Context { get; set; }
    }

    public class BeforeAttackCalculationEvent : IBattleEvent
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

        /// <summary>Mutable calculation context — passive skills can modify this before damage is resolved</summary>
        public DamageCalculation Calc { get; set; }
        public BattleActionSourceKind SourceKind { get; set; } = BattleActionSourceKind.ActiveAttack;
        public bool IsPending => SourceKind == BattleActionSourceKind.PendingAction;
    }

    public class AfterHitEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public int DamageDealt { get; set; }
        public bool IsHit { get; set; }
        public BattleContext Context { get; set; }
        public BattleActionSourceKind SourceKind { get; set; } = BattleActionSourceKind.ActiveAttack;
        public bool IsPending => SourceKind == BattleActionSourceKind.PendingAction;
    }

    public class AfterActiveUseEvent : IBattleEvent
    {
        public BattleUnit Caster { get; set; }
        public ActiveSkill Skill { get; set; }
        public BattleContext Context { get; set; }
    }

    public class OnKnockdownEvent : IBattleEvent
    {
        public BattleUnit Victim { get; set; }
        public BattleUnit Killer { get; set; }
        public BattleContext Context { get; set; }
    }

    public class BattleEndEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
        public BattleResult Result { get; set; }
    }
}
