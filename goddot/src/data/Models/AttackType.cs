namespace BattleKing.Data
{
    public enum AttackType
    {
        Melee,      // 近接物理（只能打前排）
        Ranged,     // 遠隔物理（可打后排）
        Magic       // 魔法攻击（可打后排）
    }
}
