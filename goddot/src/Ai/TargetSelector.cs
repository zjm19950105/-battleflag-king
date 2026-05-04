using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Ai
{
    public class TargetSelector
    {
        private BattleContext _ctx;
        private ConditionEvaluator _conditionEvaluator;

        public TargetSelector(BattleContext ctx)
        {
            _ctx = ctx;
            _conditionEvaluator = new ConditionEvaluator(ctx);
        }

        public List<BattleUnit> SelectTargets(BattleUnit caster, Strategy strategy, ActiveSkillData skill)
        {
            // Step 1: Generate default target list
            var candidates = GetDefaultTargetList(caster, skill);

            // Step 2: Apply condition1
            candidates = ApplyCondition(candidates, strategy.Condition1, strategy.Mode1, caster);

            // Step 3: Apply condition2
            candidates = ApplyCondition(candidates, strategy.Condition2, strategy.Mode2, caster);

            // Step 4: Handle "优先前排/优先后排" overriding
            candidates = ApplyPositionPriority(candidates, strategy);

            if (candidates.Count == 0)
                return null;

            // Step 5: Return targets based on TargetType
            return skill.TargetType switch
            {
                TargetType.Self => new List<BattleUnit> { caster },
                TargetType.SingleEnemy => new List<BattleUnit> { candidates.First() },
                TargetType.SingleAlly => new List<BattleUnit> { candidates.First() },
                TargetType.TwoEnemies => candidates.Take(2).ToList(),
                TargetType.ThreeEnemies => candidates.Take(3).ToList(),
                TargetType.FrontAndBack => candidates.Take(2).ToList(),
                TargetType.Column => GetColumnTargets(candidates.First()),
                TargetType.Row => GetRowTargets(candidates.First()),
                TargetType.AllEnemies => candidates.ToList(),
                TargetType.AllAllies => candidates.ToList(),
                _ => new List<BattleUnit> { candidates.First() }
            };
        }

        private List<BattleUnit> GetDefaultTargetList(BattleUnit caster, ActiveSkillData skill)
        {
            bool targetIsEnemy = skill.TargetType switch
            {
                TargetType.Self => false,
                TargetType.SingleAlly => false,
                TargetType.AllAllies => false,
                _ => true
            };

            var pool = targetIsEnemy
                ? (caster.IsPlayer ? _ctx.EnemyUnits : _ctx.PlayerUnits)
                : (caster.IsPlayer ? _ctx.PlayerUnits : _ctx.EnemyUnits);

            var aliveUnits = pool.Where(u => u.IsAlive).ToList();

            // Melee physical: front row priority
            if (skill.AttackType == AttackType.Melee && targetIsEnemy)
            {
                var frontRow = aliveUnits.Where(u => u.IsFrontRow).ToList();
                if (frontRow.Count > 0)
                    aliveUnits = frontRow;
            }

            // Default sort: by position ascending (front row first)
            return aliveUnits.OrderBy(u => u.Position).ToList();
        }

        private List<BattleUnit> ApplyCondition(List<BattleUnit> list, Condition condition, ConditionMode mode, BattleUnit caster)
        {
            if (condition == null || list.Count == 0)
                return list;

            if (mode == ConditionMode.Only)
            {
                // Filter: keep only units that satisfy the condition
                return list.Where(u => _conditionEvaluator.Evaluate(condition, caster, u)).ToList();
            }
            else // Priority
            {
                // Sort: units satisfying condition come first
                return list
                    .OrderByDescending(u => _conditionEvaluator.Evaluate(condition, caster, u))
                    .ThenBy(u => u.Position)
                    .ToList();
            }
        }

        private List<BattleUnit> ApplyPositionPriority(List<BattleUnit> list, Strategy strategy)
        {
            // Stub: "优先前排/优先后排" override logic
            // For MVP, keep default order
            return list;
        }

        private List<BattleUnit> GetColumnTargets(BattleUnit first)
        {
            if (first == null) return new List<BattleUnit>();
            return _ctx.AllUnits.Where(u => u.Position == first.Position && u.IsAlive).ToList();
        }

        private List<BattleUnit> GetRowTargets(BattleUnit first)
        {
            if (first == null) return new List<BattleUnit>();
            bool isFront = first.IsFrontRow;
            return _ctx.AllUnits.Where(u => u.IsAlive && u.IsFrontRow == isFront).ToList();
        }
    }
}
