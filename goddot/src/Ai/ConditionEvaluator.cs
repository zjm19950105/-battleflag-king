using System;
using System.Linq;
using System.Text.Json;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Ai
{
    public class ConditionEvaluator
    {
        private BattleContext _ctx;

        public ConditionEvaluator(BattleContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>Condition.Value is `object` — System.Text.Json stores numbers as JsonElement</summary>
        private static float ToFloat(object value)
        {
            if (value is JsonElement je) return je.GetSingle();
            return Convert.ToSingle(value);
        }
        private static int ToInt(object value)
        {
            if (value is JsonElement je) return je.GetInt32();
            return Convert.ToInt32(value);
        }

        public bool Evaluate(Condition condition, BattleUnit subject, BattleUnit target = null)
        {
            if (condition == null)
                return true;

            return condition.Category switch
            {
                ConditionCategory.Position => EvaluatePosition(condition, subject, target),
                ConditionCategory.UnitClass => EvaluateUnitClass(condition, subject, target),
                ConditionCategory.Hp => EvaluateHp(condition, subject, target),
                ConditionCategory.ApPp => EvaluateApPp(condition, subject, target),
                ConditionCategory.Status => EvaluateStatus(condition, subject, target),
                ConditionCategory.AttackAttribute => EvaluateAttackAttribute(condition, subject, target),
                ConditionCategory.TeamSize => EvaluateTeamSize(condition, subject),
                ConditionCategory.SelfState => EvaluateSelfState(condition, subject),
                ConditionCategory.SelfHp => EvaluateSelfHp(condition, subject),
                ConditionCategory.SelfApPp => EvaluateSelfApPp(condition, subject),
                ConditionCategory.EnemyClassExists => EvaluateEnemyClassExists(condition),
                ConditionCategory.AttributeRank => EvaluateAttributeRank(condition, subject, target),
                _ => true
            };
        }

        private bool EvaluatePosition(Condition c, BattleUnit subject, BattleUnit target)
        {
            if (target == null) return true;
            string value = c.Value?.ToString() ?? "";
            return c.Operator switch
            {
                "equals" => value switch
                {
                    "front" => target.IsFrontRow,
                    "back" => !target.IsFrontRow,
                    _ => true
                },
                _ => true
            };
        }

        private bool EvaluateUnitClass(Condition c, BattleUnit subject, BattleUnit target)
        {
            if (target == null) return true;
            string value = c.Value?.ToString() ?? "";
            if (!Enum.TryParse<UnitClass>(value, true, out var unitClass))
                return true;

            return c.Operator switch
            {
                "equals" => target.Data.Classes.Contains(unitClass),
                "contains" => target.Data.Classes.Contains(unitClass),
                _ => true
            };
        }

        private bool EvaluateHp(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit?.Data == null) return true;

            int maxHp = unit.Data.BaseStats.ContainsKey("HP") ? unit.Data.BaseStats["HP"] : 1;
            if (maxHp <= 0) maxHp = 1;
            float hpRatio = (float)unit.CurrentHp / maxHp;

            return c.Operator switch
            {
                "less_than" => hpRatio < ToFloat(c.Value),
                "greater_than" => hpRatio > ToFloat(c.Value),
                "equals" => Math.Abs(hpRatio - ToFloat(c.Value)) < 0.001f,
                _ => true
            };
        }

        private bool EvaluateApPp(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit == null) return true;

            string resource = c.Value?.ToString() ?? "";
            int current = resource.ToLower() == "pp" ? unit.CurrentPp : unit.CurrentAp;
            int max = resource.ToLower() == "pp" ? unit.MaxPp : unit.MaxAp;
            if (max <= 0) max = 1;
            float ratio = (float)current / max;

            return c.Operator switch
            {
                "less_than" => ratio < Convert.ToSingle(c.Value),
                "greater_than" => ratio > Convert.ToSingle(c.Value),
                "equals" => Math.Abs(ratio - Convert.ToSingle(c.Value)) < 0.001f,
                _ => true
            };
        }

        private bool EvaluateStatus(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit == null) return true;

            string value = c.Value?.ToString() ?? "";
            return c.Operator switch
            {
                "equals" => value.ToLower() switch
                {
                    "buff" => unit.Buffs.Any(b => b.Ratio > 0),
                    "debuff" => unit.Buffs.Any(b => b.Ratio < 0),
                    _ => unit.Ailments.Any(a => a.ToString().Equals(value, StringComparison.OrdinalIgnoreCase))
                },
                _ => true
            };
        }

        private bool EvaluateSelfState(Condition c, BattleUnit subject)
        {
            if (subject == null) return true;
            string value = c.Value?.ToString() ?? "";

            return c.Operator switch
            {
                "equals" => value.ToLower() switch
                {
                    "charging" => subject.State == UnitState.Charging,
                    "stunned" => subject.State == UnitState.Stunned,
                    "frozen" => subject.State == UnitState.Frozen,
                    "darkness" => subject.State == UnitState.Darkness,
                    _ => true
                },
                _ => true
            };
        }

        private bool EvaluateEnemyClassExists(Condition c)
        {
            string value = c.Value?.ToString() ?? "";
            if (!Enum.TryParse<UnitClass>(value, true, out var unitClass))
                return true;

            bool exists = _ctx.EnemyUnits.Any(u => u.IsAlive && u.Data.Classes.Contains(unitClass));
            return c.Operator switch
            {
                "equals" => exists,
                "contains" => exists,
                _ => true
            };
        }

        private bool EvaluateSelfHp(Condition c, BattleUnit subject)
        {
            if (subject?.Data == null) return true;
            int maxHp = subject.Data.BaseStats.ContainsKey("HP") ? subject.Data.BaseStats["HP"] : 1;
            if (maxHp <= 0) maxHp = 1;
            float hpRatio = (float)subject.CurrentHp / maxHp;
            return c.Operator switch
            {
                "less_than" => hpRatio < ToFloat(c.Value),
                "greater_than" => hpRatio > ToFloat(c.Value),
                "equals" => Math.Abs(hpRatio - ToFloat(c.Value)) < 0.001f,
                _ => true
            };
        }

        private bool EvaluateSelfApPp(Condition c, BattleUnit subject)
        {
            if (subject == null) return true;
            int threshold = c.Value != null ? ToInt(c.Value) : 0;
            return c.Operator switch
            {
                "less_than" => subject.CurrentAp < threshold,
                "greater_than" => subject.CurrentAp > threshold,
                "equals" => subject.CurrentAp == threshold,
                _ => true
            };
        }

        private bool EvaluateAttributeRank(Condition c, BattleUnit subject, BattleUnit target)
        {
            if (target == null) return true;
            string statName = c.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(statName)) return true;

            // Determine comparison pool: all alive enemies (or allies, depending on target)
            bool targetIsEnemy = !target.IsPlayer;
            var pool = targetIsEnemy ? _ctx.EnemyUnits : _ctx.PlayerUnits;
            var alive = pool.Where(u => u != null && u.IsAlive).ToList();
            if (alive.Count == 0) return true;

            int targetVal = BattleContext.GetStatValue(target, statName);

            return c.Operator switch
            {
                "highest" => alive.All(u => BattleContext.GetStatValue(u, statName) <= targetVal),
                "lowest" => alive.All(u => BattleContext.GetStatValue(u, statName) >= targetVal),
                "greater_than" => targetVal > ToInt(c.Value),
                "less_than" => targetVal < ToInt(c.Value),
                _ => true
            };
        }

        /// <summary>
        /// EvaluateTeamSize — checks number of alive units on a team.
        /// Value format: "enemy:N" or "ally:N" (e.g. "enemy:2" = 2+ enemies, "ally:3" = 3+ allies).
        /// </summary>
        private bool EvaluateTeamSize(Condition c, BattleUnit subject)
        {
            string raw = c.Value?.ToString() ?? "";
            string[] parts = raw.Split(':');
            if (parts.Length < 2) return true;
            string team = parts[0]; // "enemy" or "ally"
            if (!int.TryParse(parts[1], out int threshold)) return true;

            int count;
            if (team == "enemy")
                count = _ctx.EnemyUnits.Count(u => u != null && u.IsAlive);
            else
                count = _ctx.PlayerUnits.Count(u => u != null && u.IsAlive);

            return c.Operator switch
            {
                "greater_than" => count > threshold,
                "less_than" => count < threshold,
                "equals" => count == threshold,
                _ => true
            };
        }

        /// <summary>
        /// EvaluateAttackAttribute — checks the type of attack being made.
        /// Reads from BattleContext.CurrentCalc set by BattleEngine before BeforeHitEvent.
        /// </summary>
        private bool EvaluateAttackAttribute(Condition c, BattleUnit subject, BattleUnit target)
        {
            string value = c.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(value)) return true;

            var calc = _ctx.CurrentCalc;
            if (calc == null) return true;

            return c.Operator switch
            {
                "equals" => value.ToLower() switch
                {
                    "physical" => calc.Skill.Type == Data.SkillType.Physical,
                    "magical" => calc.Skill.Type == Data.SkillType.Magical,
                    "row" => calc.Skill.TargetType == Data.TargetType.Row,
                    "column" => calc.Skill.TargetType == Data.TargetType.Column,
                    "all" => calc.Skill.TargetType == Data.TargetType.AllEnemies,
                    "melee" => calc.Skill.AttackType == Data.AttackType.Melee,
                    "ranged" => calc.Skill.AttackType == Data.AttackType.Ranged,
                    _ => true
                },
                _ => true
            };
        }
    }
}
