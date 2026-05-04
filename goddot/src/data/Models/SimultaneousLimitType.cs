namespace BattleKing.Data
{
    public enum SimultaneousLimitType
    {
        BattleStart,            // 战斗开始时（敌我各1人）
        AllyBuffBeforeAction,   // 友方强化（同部队1人）
        EnemyDefendBeforeAction,// 防守方防御（对方部队1人）
        AfterActionCorrespond,  // 行动后对应（敌我各1人）
        None                    // 无限制
    }
}
