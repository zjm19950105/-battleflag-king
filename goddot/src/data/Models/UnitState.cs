namespace BattleKing.Data
{
    public enum UnitState
    {
        Normal,     // 正常
        Charging,   // 蓄力中（回避和被动不可用）
        Stunned,    // 气绝（跳过一次行动）
        Frozen,     // 冻结（无法行动，受击解除，回避率=0）
        Darkness,   // 黑暗（下次攻击命中率0）
        BlockSeal,  // 格挡封印
        CritSeal    // 无法暴击
    }
}
