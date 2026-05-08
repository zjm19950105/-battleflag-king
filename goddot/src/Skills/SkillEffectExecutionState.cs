using System.Collections.Generic;
using BattleKing.Core;

namespace BattleKing.Skills
{
    /// <summary>Keeps per-skill-use state for effects that must run once but affect every hit calculation.</summary>
    public class SkillEffectExecutionState
    {
        private readonly Dictionary<string, int> _consumedCounters = new();

        public bool HasConsumedCounter(string key) => _consumedCounters.ContainsKey(key);

        public int ConsumeCounterOnce(BattleUnit unit, string key)
        {
            if (_consumedCounters.TryGetValue(key, out int existing))
                return existing;

            int consumed = unit.ConsumeCounter(key);
            _consumedCounters[key] = consumed;
            return consumed;
        }
    }
}
