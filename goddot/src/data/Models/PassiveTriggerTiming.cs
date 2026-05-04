namespace BattleKing.Data
{
    public enum PassiveTriggerTiming
    {
        BattleStart,           // 战斗开始时
        SelfBeforeAttack,      // 自身攻击前（主动）
        AllyBeforeAttack,      // 友方攻击前（主动）
        AllyBeforeHit,         // 友方被攻击前
        SelfBeforeHit,         // 自身被攻击直前
        SelfBeforeMeleeHit,    // 自身被近接攻击直前（招架等）
        SelfBeforePhysicalHit, // 自身被物理攻击直前（格挡/复仇守护等）
        AllyOnAttacked,        // 友方被攻击时（追击斩等）
        SelfOnActiveUse,       // 自身使用主动技能时（蓄力行动/蛮力等）
        AllyOnActiveUse,       // 友方使用主动技能时（主动礼物等）
        AfterAction,           // 行动后（敌我双方）
        BattleEnd,             // 战斗结束时
        OnHit,                 // 命中时
        OnBeingHit,            // 被命中时
        OnKnockdown            // 击倒时
    }
}
