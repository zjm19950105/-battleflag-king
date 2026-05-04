using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;

namespace BattleKing.Core
{
    public class BattleContext
    {
        public GameDataRepository GameData { get; private set; }
        public List<BattleUnit> PlayerUnits { get; set; } = new List<BattleUnit>();
        public List<BattleUnit> EnemyUnits { get; set; } = new List<BattleUnit>();
        public int TurnCount { get; set; } = 0;
        public bool IsDaytime { get; set; } = true;

        public List<BattleUnit> AllUnits => PlayerUnits.Concat(EnemyUnits).ToList();

        public BattleContext(GameDataRepository gameData)
        {
            GameData = gameData;
        }

        public List<BattleUnit> GetAliveUnits(bool isPlayer) =>
            (isPlayer ? PlayerUnits : EnemyUnits).Where(u => u.IsAlive).ToList();

        public BattleUnit GetUnitAtPosition(bool isPlayer, int position) =>
            (isPlayer ? PlayerUnits : EnemyUnits).FirstOrDefault(u => u.Position == position);

        public bool HasEnemyClass(UnitClass unitClass) =>
            EnemyUnits.Any(u => u.IsAlive && u.Data.Classes.Contains(unitClass));

        public int GetAliveCount(bool isPlayer) =>
            GetAliveUnits(isPlayer).Count;
    }
}
