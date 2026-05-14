using System.Collections.Generic;

namespace BattleKing.Core
{
    /// <summary>Structured battle log row used by tests and future replay tooling.</summary>
    public class BattleLogEntry
    {
        public int Turn { get; set; }
        public string ActorId { get; set; } = "";
        public string SkillId { get; set; } = "";
        public List<string> TargetIds { get; set; } = new List<string>();
        public int Damage { get; set; }
        public int? HpBefore { get; set; }
        public int? HpAfter { get; set; }
        public int? HpLost { get; set; }
        public List<string> Flags { get; set; } = new List<string>();
        public string Text { get; set; } = "";
    }
}
