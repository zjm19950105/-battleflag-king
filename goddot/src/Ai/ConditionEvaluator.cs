using System;
using System.Collections.Generic;
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

        public bool Evaluate(Condition condition, BattleUnit subject, BattleUnit target = null, ActiveSkillData skill = null)
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
                ConditionCategory.AttackAttribute => EvaluateAttackAttribute(condition, subject, target, skill),
                ConditionCategory.TeamSize => EvaluateTeamSize(condition, subject),
                ConditionCategory.SelfState => EvaluateSelfState(condition, subject, target),
                ConditionCategory.SelfHp => EvaluateSelfHp(condition, subject),
                ConditionCategory.SelfApPp => EvaluateSelfApPp(condition, subject),
                ConditionCategory.EnemyClassExists => EvaluateEnemyClassExists(condition, subject),
                ConditionCategory.AttributeRank => EvaluateAttributeRank(condition, subject, target),
                _ => false
            };
        }

        private bool EvaluatePosition(Condition c, BattleUnit subject, BattleUnit target)
        {
            string value = c.Value?.ToString() ?? "";
            if (string.Equals(value, "daytime", StringComparison.OrdinalIgnoreCase))
                return c.Operator == "equals" && _ctx.IsDaytime;
            if (string.Equals(value, "nighttime", StringComparison.OrdinalIgnoreCase))
                return c.Operator == "equals" && !_ctx.IsDaytime;

            if (target == null) return true;
            return c.Operator switch
            {
                "equals" => value switch
                {
                    "front" => target.IsFrontRow,
                    "back" => !target.IsFrontRow,
                    "front_and_back" => HasAliveOppositeRowUnitInSameColumn(target),
                    _ => false
                },
                "greater_or_equal" => EvaluateRowCount(value, target, (count, threshold) => count >= threshold),
                "less_or_equal" => EvaluateRowCount(value, target, (count, threshold) => count <= threshold),
                _ => false
            };
        }

        private bool EvaluateRowCount(string value, BattleUnit target, Func<int, int, bool> compare)
        {
            // Compatibility: early local docs translated the original row-based
            // condition as "column", so saved data/tests may still contain the
            // old column_units prefix. Treat it as front/back row population.
            if (!TryParsePrefixedInt(value, "row_units:", out int threshold)
                && !TryParsePrefixedInt(value, "column_units:", out threshold))
            {
                return false;
            }

            int count = GetAliveRowUnitCount(target);
            return compare(count, threshold);
        }

        private bool HasAliveOppositeRowUnitInSameColumn(BattleUnit target)
        {
            int column = (target.Position - 1) % 3;
            return _ctx.GetAliveUnits(target.IsPlayer)
                .Any(unit => unit != target
                    && unit.IsFrontRow != target.IsFrontRow
                    && (unit.Position - 1) % 3 == column);
        }

        private bool EvaluateUnitClass(Condition c, BattleUnit subject, BattleUnit target)
        {
            if (target == null) return true;
            string value = c.Value?.ToString() ?? "";
            if (!Enum.TryParse<UnitClass>(value, true, out var unitClass))
                return false;

            return c.Operator switch
            {
                "equals" => target.GetEffectiveClasses().Contains(unitClass),
                "contains" => target.GetEffectiveClasses().Contains(unitClass),
                _ => false
            };
        }

        private bool EvaluateHp(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit?.Data == null) return true;

            float hpRatio = GetHpRatio(unit);

            return c.Operator switch
            {
                "less_than" => hpRatio < ToFloat(c.Value),
                "greater_than" => hpRatio > ToFloat(c.Value),
                "less_or_equal" => hpRatio <= ToFloat(c.Value),
                "greater_or_equal" => hpRatio >= ToFloat(c.Value),
                "equals" => Math.Abs(hpRatio - ToFloat(c.Value)) < 0.001f,
                "less_than_average" => hpRatio < GetAverageHpRatio(unit.IsPlayer),
                "greater_than_average" => hpRatio > GetAverageHpRatio(unit.IsPlayer),
                _ => false
            };
        }

        private bool EvaluateApPp(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit == null) return true;

            if (!TryParseResourceThreshold(c.Value, out string resource, out int threshold))
                return false;

            int current = GetResourceValue(unit, resource);

            return c.Operator switch
            {
                "less_than" => current < threshold,
                "greater_than" => current > threshold,
                "less_or_equal" => current <= threshold,
                "greater_or_equal" => current >= threshold,
                "equals" => current == threshold,
                _ => false
            };
        }

        private bool EvaluateStatus(Condition c, BattleUnit subject, BattleUnit target)
        {
            var unit = target ?? subject;
            if (unit == null) return true;

            string value = c.Value?.ToString() ?? "";

            // Unknown values return false so malformed strategy data cannot silently
            // turn into an always-true condition.
            bool negate = value.StartsWith("not:", StringComparison.OrdinalIgnoreCase);
            if (negate) value = value.Substring("not:".Length);

            bool result = c.Operator switch
            {
                "equals" => HasStatus(unit, value),
                "not_equals" => !HasStatus(unit, value),
                _ => false
            };

            return negate ? !result : result;
        }

        private bool EvaluateSelfState(Condition c, BattleUnit subject, BattleUnit target)
        {
            if (subject == null) return true;
            string value = c.Value?.ToString() ?? "";

            return c.Operator switch
            {
                "equals" => value.ToLower() switch
                {
                    "self" => target == null || ReferenceEquals(target, subject),
                    "not_self" => target != null && !ReferenceEquals(target, subject),
                    "buff" => subject.Buffs.Any(b => b.Ratio > 0 || b.FlatAmount > 0),
                    "debuff" => subject.Buffs.Any(b => b.Ratio < 0 || b.FlatAmount < 0),
                    "charging" => subject.State == UnitState.Charging,
                    "stunned" => subject.State == UnitState.Stunned,
                    "frozen" => subject.State == UnitState.Frozen,
                    "darkness" => subject.State == UnitState.Darkness,
                    _ => EvaluateActionCount(value, subject)
                },
                _ => false
            };
        }

        private bool EvaluateEnemyClassExists(Condition c, BattleUnit subject)
        {
            if (subject == null) return false;

            string value = c.Value?.ToString() ?? "";
            if (!Enum.TryParse<UnitClass>(value, true, out var unitClass))
                return false;

            var enemyUnits = subject.IsPlayer ? _ctx.EnemyUnits : _ctx.PlayerUnits;
            bool exists = enemyUnits.Any(u =>
                u != null
                && u.IsAlive
                && u.GetEffectiveClasses().Contains(unitClass));
            return c.Operator switch
            {
                "equals" => exists,
                "contains" => exists,
                "not_equals" => !exists,
                _ => false
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
                "less_or_equal" => hpRatio <= ToFloat(c.Value),
                "greater_or_equal" => hpRatio >= ToFloat(c.Value),
                "equals" => Math.Abs(hpRatio - ToFloat(c.Value)) < 0.001f,
                _ => false
            };
        }

        private bool EvaluateSelfApPp(Condition c, BattleUnit subject)
        {
            if (subject == null) return true;
            if (!TryParseResourceThreshold(c.Value, out string resource, out int threshold))
                return false;

            int current = GetResourceValue(subject, resource);
            return c.Operator switch
            {
                "less_than" => current < threshold,
                "greater_than" => current > threshold,
                "less_or_equal" => current <= threshold,
                "greater_or_equal" => current >= threshold,
                "equals" => current == threshold,
                _ => false
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

            int targetVal = GetAttributeRankValue(target, statName);

            return c.Operator switch
            {
                "highest" => alive.All(u => GetAttributeRankValue(u, statName) <= targetVal),
                "lowest" => alive.All(u => GetAttributeRankValue(u, statName) >= targetVal),
                "greater_than" => targetVal > ToInt(c.Value),
                "less_than" => targetVal < ToInt(c.Value),
                _ => false
            };
        }

        private static int GetAttributeRankValue(BattleUnit unit, string statName)
        {
            if (unit == null) return 0;

            // HP rank is about current battle state, so it uses CurrentHp.
            // Other attributes include equipment and buffs via GetCurrentStat().
            if (string.Equals(statName, "HP", StringComparison.OrdinalIgnoreCase))
                return unit.CurrentHp;
            if (string.Equals(statName, "MaxHp", StringComparison.OrdinalIgnoreCase))
                return unit.GetCurrentStat("HP");
            if (string.Equals(statName, "MaxAp", StringComparison.OrdinalIgnoreCase))
                return unit.MaxAp;
            if (string.Equals(statName, "MaxPp", StringComparison.OrdinalIgnoreCase))
                return unit.MaxPp;

            return unit.GetCurrentStat(statName);
        }

        /// <summary>
        /// EvaluateTeamSize — checks alive unit count relative to the acting subject.
        /// Value format: "enemy:N" or "ally:N" (e.g. "enemy:2" = at least 2 enemies).
        /// </summary>
        private bool EvaluateTeamSize(Condition c, BattleUnit subject)
        {
            if (subject == null) return false;

            string raw = c.Value?.ToString() ?? "";
            string[] parts = raw.Split(':');
            if (parts.Length != 2) return false;
            string team = parts[0]; // "enemy" or "ally"
            if (!int.TryParse(parts[1], out int threshold)) return false;

            int count;
            if (team == "enemy")
                count = GetRelativeTeam(subject, enemy: true).Count(u => u != null && u.IsAlive);
            else if (team == "ally")
                count = GetRelativeTeam(subject, enemy: false).Count(u => u != null && u.IsAlive);
            else
                return false;

            return c.Operator switch
            {
                "greater_than" => count > threshold,
                "less_than" => count < threshold,
                "greater_or_equal" => count >= threshold,
                "less_or_equal" => count <= threshold,
                "equals" => count == threshold,
                _ => false
            };
        }

        private List<BattleUnit> GetRelativeTeam(BattleUnit subject, bool enemy)
        {
            bool wantPlayerSide = enemy ? !subject.IsPlayer : subject.IsPlayer;
            return wantPlayerSide ? _ctx.PlayerUnits : _ctx.EnemyUnits;
        }

        /// <summary>
        /// EvaluateAttackAttribute — checks the type of attack being made.
        /// Reads from BattleContext.CurrentCalc set by BattleEngine before BeforeHitEvent.
        /// </summary>
        private bool EvaluateAttackAttribute(Condition c, BattleUnit subject, BattleUnit target, ActiveSkillData skillData = null)
        {
            string value = c.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(value)) return true;

            skillData ??= _ctx.CurrentCalc?.Skill?.Data;
            if (skillData == null) return false;

            return c.Operator switch
            {
                "equals" => value.ToLower() switch
                {
                    "physical" => skillData.Type == Data.SkillType.Physical,
                    "magical" => skillData.Type == Data.SkillType.Magical,
                    "row" => skillData.TargetType == Data.TargetType.Row,
                    "column" => skillData.TargetType == Data.TargetType.Column,
                    "front_and_back" => skillData.TargetType == Data.TargetType.FrontAndBack,
                    "all" => skillData.TargetType == Data.TargetType.AllEnemies || skillData.TargetType == Data.TargetType.AllAllies,
                    "melee" => skillData.AttackType == Data.AttackType.Melee,
                    "ranged" => skillData.AttackType == Data.AttackType.Ranged,
                    _ => false
                },
                _ => false
            };
        }

        private int GetAliveRowUnitCount(BattleUnit target)
        {
            if (target == null)
                return 0;

            return _ctx.GetAliveUnits(target.IsPlayer)
                .Count(unit => unit.IsFrontRow == target.IsFrontRow);
        }

        private static float GetHpRatio(BattleUnit unit)
        {
            if (unit?.Data == null)
                return 0f;

            int maxHp = unit.Data.BaseStats.ContainsKey("HP") ? unit.Data.BaseStats["HP"] : 1;
            if (maxHp <= 0) maxHp = 1;
            return (float)unit.CurrentHp / maxHp;
        }

        private float GetAverageHpRatio(bool isPlayer)
        {
            var alive = _ctx.GetAliveUnits(isPlayer);
            if (alive.Count == 0)
                return 0f;

            return alive.Average(GetHpRatio);
        }

        private static bool HasStatus(BattleUnit unit, string value)
        {
            if (unit == null)
                return false;

            return value.ToLower() switch
            {
                "buff" => unit.Buffs.Any(b => b.Ratio > 0 || b.FlatAmount > 0),
                "debuff" => unit.Buffs.Any(b => b.Ratio < 0 || b.FlatAmount < 0),
                "ailment" => unit.Ailments.Count > 0,
                "none" => !HasAnyStatus(unit),
                _ => Enum.TryParse<StatusAilment>(value, true, out var ailment)
                    && unit.Ailments.Contains(ailment)
            };
        }

        private static bool HasAnyStatus(BattleUnit unit)
        {
            return unit.Buffs.Any(b => b.Ratio != 0 || b.FlatAmount != 0)
                || unit.Ailments.Count > 0;
        }

        private static bool EvaluateActionCount(string value, BattleUnit subject)
        {
            if (!TryParsePrefixedInt(value, "action:", out int actionNumber))
                return false;

            return subject.ActionCount + 1 == actionNumber;
        }

        private static int GetResourceValue(BattleUnit unit, string resource)
        {
            return string.Equals(resource, "PP", StringComparison.OrdinalIgnoreCase)
                ? unit.CurrentPp
                : unit.CurrentAp;
        }

        private static bool TryParseResourceThreshold(object rawValue, out string resource, out int threshold)
        {
            resource = "AP";
            threshold = 0;

            if (rawValue is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number)
                {
                    threshold = json.GetInt32();
                    return true;
                }

                if (json.ValueKind == JsonValueKind.String)
                    rawValue = json.GetString();
            }

            if (rawValue is int intValue)
            {
                threshold = intValue;
                return true;
            }

            if (rawValue is long longValue)
            {
                threshold = (int)longValue;
                return true;
            }

            string value = rawValue?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split(':');
            if (parts.Length == 2)
            {
                resource = parts[0];
                return int.TryParse(parts[1], out threshold);
            }

            if (int.TryParse(value, out threshold))
                return true;

            return false;
        }

        private static bool TryParsePrefixedInt(string value, string prefix, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(value.Substring(prefix.Length), out result);
        }
    }
}
