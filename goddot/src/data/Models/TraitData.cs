using System.Collections.Generic;

namespace BattleKing.Data
{
    public class TraitData
    {
        public string TraitType { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
