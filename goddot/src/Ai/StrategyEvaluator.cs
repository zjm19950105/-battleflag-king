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
    }
}
