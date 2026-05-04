namespace BattleKing.Data
{
    public enum TargetType
    {
        Self,
        SingleEnemy,
        SingleAlly,
        TwoEnemies,
        ThreeEnemies,
        FrontAndBack,   // 前后列贯通
        Column,         // 一列
        Row,            // 一排
        AllEnemies,
        AllAllies
    }
}
