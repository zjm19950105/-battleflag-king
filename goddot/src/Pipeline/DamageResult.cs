using System.Collections.Generic;
using BattleKing.Core;
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
        public BattleUnit ResolvedDefender { get; }
        public IReadOnlyList<StatusAilment> AppliedAilments { get; }
        public IReadOnlyList<DamageHitResult> HitResults { get; }

        public DamageResult(
            int physicalDamage,
            int magicalDamage,
            bool isHit,
            bool isCritical,
            bool isBlocked,
            bool isEvaded,
            List<StatusAilment> appliedAilments,
            BattleUnit resolvedDefender = null,
            IReadOnlyList<DamageHitResult> hitResults = null)
        {
            PhysicalDamage = physicalDamage;
            MagicalDamage = magicalDamage;
            IsHit = isHit;
            IsCritical = isCritical;
            IsBlocked = isBlocked;
            IsEvaded = isEvaded;
            ResolvedDefender = resolvedDefender;
            AppliedAilments = appliedAilments != null
                ? new List<StatusAilment>(appliedAilments)
                : new List<StatusAilment>();
            HitResults = hitResults != null
                ? new List<DamageHitResult>(hitResults)
                : new List<DamageHitResult>();
        }
    }
}
