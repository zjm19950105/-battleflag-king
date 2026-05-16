using System.Collections.Generic;

namespace BattleKing.Core
{
    public class TemporalState
    {
        public string Key { get; set; }
        public int RemainingCount { get; set; } = 1;
        public int RemainingTurns { get; set; } = -1;
        public string SourceSkillId { get; set; }
        public List<string> AffectedUnitIds { get; set; } = new List<string>();
    }
}
