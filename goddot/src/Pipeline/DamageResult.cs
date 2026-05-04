using System.Collections.Generic;
using BattleKing.Data;

namespace BattleKing.Pipeline
{
    public class DamageResult
    {
        public int PhysicalDamage { get; }
        public int MagicalDamage { get; }
        public int TotalDamage => PhysicalDamage + MagicalDamage;
        public bool IsHit { get; }
        public bool IsCritical { get; }
        public bool IsBlocked { get; }
        public bool IsEvaded { get; }
        public IReadOnlyList<StatusAilment> AppliedAilments { get; }

        public DamageResult(
            int physicalDamage,
            int magicalDamage,
            bool isHit,
            bool isCritical,
            bool isBlocked,
            bool isEvaded,
            List<StatusAilment> appliedAilments)
        {
            PhysicalDamage = physicalDamage;
            MagicalDamage = magicalDamage;
            IsHit = isHit;
            IsCritical = isCritical;
            IsBlocked = isBlocked;
            IsEvaded = isEvaded;
            AppliedAilments = appliedAilments != null
                ? new List<StatusAilment>(appliedAilments)
                : new List<StatusAilment>();
        }
    }
}
