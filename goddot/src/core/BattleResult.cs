namespace BattleKing.Core
{
    public enum BattleResult { PlayerWin, EnemyWin, Draw }
    public enum BattleStepResult { Continue, PlayerWin, EnemyWin, Draw }
    /// <summary>Per-action step: one unit acts, then returns status</summary>
    public enum SingleActionResult { ActionDone, TurnDone, PlayerWin, EnemyWin, Draw }
}
