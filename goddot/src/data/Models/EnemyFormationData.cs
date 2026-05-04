using System.Collections.Generic;

namespace BattleKing.Data
{
    public class EnemyFormationData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Difficulty { get; set; }
        public List<FormationUnitData> Units { get; set; } = new();
    }

    public class FormationUnitData
    {
        public string CharacterId { get; set; }
        public int Position { get; set; }
        public string StrategyPresetId { get; set; }
    }
}
