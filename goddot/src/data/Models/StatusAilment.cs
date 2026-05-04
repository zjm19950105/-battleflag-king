namespace BattleKing.Data
{
    public enum StatusAilment
    {
        Poison,     // 毒：下次行动损失最大HP30%
        Burn,       // 炎上：固定20HP，每层多结算一次
        Freeze,     // 冻结：无法行动，受击解除，回避率=0
        Darkness,   // 黑暗：下次攻击命中率0
        Stun,       // 气绝：跳过一次行动
        BlockSeal,  // 格挡封印
        CritSeal    // 无法暴击
    }
}
