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
            var candidates = GetDefaultTargetList(caster, skill);
            candidates = ApplyCondition(candidates, strategy.Condition1, strategy.Mode1, caster);
            candidates = ApplyCondition(candidates, strategy.Condition2, strategy.Mode2, caster);
            candidates = ApplyPositionPriority(candidates, strategy);

            if (candidates.Count == 0)
                return null;

            return skill.TargetType switch
            {
                TargetType.Self => new List<BattleUnit> { caster },
                TargetType.SingleEnemy => new List<BattleUnit> { candidates.First() },
                TargetType.SingleAlly => new List<BattleUnit> { candidates.First() },
                TargetType.TwoEnemies => candidates.Take(2).ToList(),
                TargetType.ThreeEnemies => candidates.Take(3).ToList(),
                TargetType.FrontAndBack => GetPiercingTargets(candidates.First()),
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

            var aliveUnits = pool.Where(u => u != null && u.IsAlive).ToList();

            // Ranged and magic attacks can hit any row — no restriction
            if (skill.AttackType == AttackType.Ranged || skill.AttackType == AttackType.Magic)
            {
                return aliveUnits.OrderBy(u => u.Position).ToList();
            }

            // Melee: can only hit front row UNLESS the attacker is flying
            // (flying units can fly over front row to attack back row)
            if (skill.AttackType == AttackType.Melee && targetIsEnemy)
            {
                bool attackerIsFlying = caster.Data?.Classes?.Contains(UnitClass.Flying) == true;

                if (!attackerIsFlying)
                {
                    var frontRow = aliveUnits.Where(u => u.IsFrontRow).ToList();
                    if (frontRow.Count > 0)
                        aliveUnits = frontRow;
                }
                // Flying melee: can hit anyone (back row included)
            }

            return aliveUnits.OrderBy(u => u.Position).ToList();
        }

        /// <summary>
        /// Piercing attack: hits front row target + the unit directly behind them (same column).
        /// Positions 1-3 = front, 4-6 = back. Column = (pos-1) % 3.
        /// </summary>
        private List<BattleUnit> GetPiercingTargets(BattleUnit first)
        {
            if (first == null) return new List<BattleUnit>();
            var targets = new List<BattleUnit> { first };

            // Find the unit in the opposite row, same column
            int col = (first.Position - 1) % 3;
            bool firstIsFront = first.IsFrontRow;
            int oppositePos = firstIsFront ? (col + 1 + 3) : (col + 1);

            var opposite = _ctx.AllUnits.FirstOrDefault(u =>
                u != null && u.IsAlive && u.Position == oppositePos && u != first);
            if (opposite != null)
                targets.Add(opposite);

            return targets;
        }

        /// <summary>
        /// Column attack: hits all units in the same column (front + back, same (pos-1)%3).
        /// </summary>
        private List<BattleUnit> GetColumnTargets(BattleUnit first)
        {
            if (first == null) return new List<BattleUnit>();
            int col = (first.Position - 1) % 3;
            return _ctx.AllUnits.Where(u =>
                u != null && u.IsAlive && (u.Position - 1) % 3 == col).ToList();
        }

        private List<BattleUnit> GetRowTargets(BattleUnit first)
        {
            if (first == null) return new List<BattleUnit>();
            bool isFront = first.IsFrontRow;
            return _ctx.AllUnits.Where(u => u.IsAlive && u.IsFrontRow == isFront).ToList();
        }

        private List<BattleUnit> ApplyCondition(List<BattleUnit> list, Condition condition, ConditionMode mode, BattleUnit caster)
        {
            if (condition == null || list.Count == 0) return list;

            if (mode == ConditionMode.Only)
                return list.Where(u => _conditionEvaluator.Evaluate(condition, caster, u)).ToList();
            else
            {
                if (condition.Operator == "lowest")
                {
                    return condition.Category switch
                    {
                        ConditionCategory.Hp => list.OrderBy(u => u.CurrentHp).ThenBy(u => u.Position).ToList(),
                        ConditionCategory.ApPp => list.OrderBy(u => u.CurrentAp).ThenBy(u => u.Position).ToList(),
                        _ => list.OrderByDescending(u => _conditionEvaluator.Evaluate(condition, caster, u)).ThenBy(u => u.Position).ToList()
                    };
                }
                if (condition.Operator == "highest")
                {
                    return condition.Category switch
                    {
                        ConditionCategory.Hp => list.OrderByDescending(u => u.CurrentHp).ThenBy(u => u.Position).ToList(),
                        ConditionCategory.ApPp => list.OrderByDescending(u => u.CurrentAp).ThenBy(u => u.Position).ToList(),
                        _ => list.OrderByDescending(u => _conditionEvaluator.Evaluate(condition, caster, u)).ThenBy(u => u.Position).ToList()
                    };
                }
                return list.OrderByDescending(u => _conditionEvaluator.Evaluate(condition, caster, u)).ThenBy(u => u.Position).ToList();
            }
        }

        private List<BattleUnit> ApplyPositionPriority(List<BattleUnit> list, Strategy strategy)
        {
            return list;
        }
    }
}
