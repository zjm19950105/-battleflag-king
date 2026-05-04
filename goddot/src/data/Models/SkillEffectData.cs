using System.Collections.Generic;

namespace BattleKing.Data
{
    public class SkillEffectData
    {
        public string EffectType { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
