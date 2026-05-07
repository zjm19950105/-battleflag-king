using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Core
{
    public class BattleContext
    {
        public GameDataRepository GameData { get; private set; }
        public List<BattleUnit> PlayerUnits { get; set; } = new List<BattleUnit>();
        public List<BattleUnit> EnemyUnits { get; set; } = new List<BattleUnit>();
        public int TurnCount { get; set; } = 0;
        public bool IsDaytime { get; set; } = true;

        public List<BattleUnit> AllUnits => PlayerUnits.Concat(EnemyUnits).Where(u => u != null).ToList();

        public BattleContext(GameDataRepository gameData)
        {
            GameData = gameData;
        }

        public List<BattleUnit> GetAliveUnits(bool isPlayer) =>
            (isPlayer ? PlayerUnits : EnemyUnits).Where(u => u != null && u.IsAlive).ToList();

        public BattleUnit GetUnitAtPosition(bool isPlayer, int position) =>
            (isPlayer ? PlayerUnits : EnemyUnits).FirstOrDefault(u => u != null && u.Position == position);

        public bool HasEnemyClass(UnitClass unitClass) =>
            EnemyUnits.Any(u => u != null && u.IsAlive && u.Data.Classes.Contains(unitClass));

        public int GetAliveCount(bool isPlayer) =>
            GetAliveUnits(isPlayer).Count;

        /// <summary>The damage calculation currently being processed (set by BattleEngine before BeforeHitEvent).</summary>
        public DamageCalculation CurrentCalc { get; set; }

        /// <summary>Get a specific stat value from a unit by name (for AttributeRank condition).</summary>
        public static int GetStatValue(BattleUnit unit, string statName)
        {
            if (unit?.Data?.BaseStats == null) return 0;
            return unit.Data.BaseStats.TryGetValue(statName, out int val) ? val : 0;
        }
    }
}
