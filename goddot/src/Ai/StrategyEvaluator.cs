using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;

namespace BattleKing.Ai
{
    public class StrategyEvaluator
    {
        private BattleContext _ctx;
        private ConditionEvaluator _conditionEvaluator;
        private TargetSelector _targetSelector;

        public StrategyEvaluator(BattleContext ctx)
        {
            _ctx = ctx;
            _conditionEvaluator = new ConditionEvaluator(ctx);
            _targetSelector = new TargetSelector(ctx);
        }

        public (ActiveSkill, List<BattleUnit>) Evaluate(BattleUnit unit)
        {
            var availableSkillIds = unit.GetAvailableActiveSkillIds();

            foreach (var strategy in unit.Strategies)
            {
                if (!availableSkillIds.Contains(strategy.SkillId))
                    continue;

                var skillData = _ctx.GameData.GetActiveSkill(strategy.SkillId);
                var activeSkill = new ActiveSkill(skillData, _ctx.GameData);

                if (!unit.CanUseActiveSkill(activeSkill))
                    continue;

                var targets = _targetSelector.SelectTargets(unit, strategy, skillData);
                if (targets != null && targets.Count > 0)
                    return (activeSkill, targets);
            }

            return (null, null);
        }

        /// <summary>Module 6: Select targets for a specific skill (used for Charge resolution)</summary>
        public List<BattleUnit> SelectTargetsForSkill(BattleUnit unit, ActiveSkillData skillData)
        {
            // Find the strategy that matches this skill
            foreach (var strategy in unit.Strategies)
            {
                if (strategy.SkillId == skillData.Id)
                {
                    var targets = _targetSelector.SelectTargets(unit, strategy, skillData);
                    if (targets != null && targets.Count > 0)
                        return targets;
                }
            }

            // Fallback: use first strategy with default targeting
            if (unit.Strategies.Count > 0)
            {
                return _targetSelector.SelectTargets(unit, unit.Strategies[0], skillData);
            }

            // Absolute fallback: target front-row enemies
            var enemies = unit.IsPlayer ? _ctx.EnemyUnits : _ctx.PlayerUnits;
            return enemies.Where(u => u != null && u.IsAlive).OrderBy(u => u.Position).Take(1).ToList();
        }
    }
}
