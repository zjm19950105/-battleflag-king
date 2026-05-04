namespace BattleKing.Data
{
    public enum ConditionMode
    {
        Only,       // 仅：条件不满足则跳过
        Priority    // 优先：条件不满足仍发动，按默认目标
    }
}
