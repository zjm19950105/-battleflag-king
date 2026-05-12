namespace BattleKing.Data
{
    public enum ConditionCategory
    {
        Position,          // 队列・状况（前排/后排/前后列/列人数/昼夜）
        UnitClass,         // 兵种
        Hp,                // HP
        ApPp,              // AP·PP
        Status,            // 状态（buff/debuff/异常）
        AttackAttribute,   // 攻击属性/目标形状
        TeamSize,          // 编成人数
        SelfState,         // 自身/自身以外/自身 buff/debuff/第N次行动等
        SelfHp,            // 自身HP
        SelfApPp,          // 自身AP·PP
        EnemyClassExists,  // 敌兵种存在
        AttributeRank      // 最高/最低能力值
    }
}
