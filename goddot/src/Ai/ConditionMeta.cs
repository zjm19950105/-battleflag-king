using System.Collections.Generic;
using BattleKing.Data;

namespace BattleKing.Ai
{
    /// <summary>Metadata for driving cascading condition editor dropdowns in the strategy UI.</summary>
    public static class ConditionMeta
    {
        public static readonly List<ConditionCategory> AllCategories = new()
        {
            // Position — filtered out for display (handled by TargetSelector default rules)
            ConditionCategory.UnitClass,
            ConditionCategory.Hp,
            ConditionCategory.ApPp,
            ConditionCategory.SelfHp,
            ConditionCategory.SelfApPp,
            ConditionCategory.Status,
            ConditionCategory.EnemyClassExists,
            ConditionCategory.AttributeRank,
            ConditionCategory.TeamSize,
            ConditionCategory.AttackAttribute,
        };

        public static string CategoryLabel(ConditionCategory cat) => cat switch
        {
            ConditionCategory.UnitClass => "兵种",
            ConditionCategory.Hp => "HP",
            ConditionCategory.ApPp => "AP·PP",
            ConditionCategory.SelfHp => "自身HP",
            ConditionCategory.SelfApPp => "自身AP·PP",
            ConditionCategory.Status => "状态",
            ConditionCategory.EnemyClassExists => "敌兵种存在",
            ConditionCategory.AttributeRank => "属性排名",
            ConditionCategory.TeamSize => "编成人数",
            ConditionCategory.AttackAttribute => "攻击属性",
            _ => cat.ToString()
        };

        public static List<string> GetOperators(ConditionCategory cat) => cat switch
        {
            ConditionCategory.UnitClass => new() { "等于" },
            ConditionCategory.Hp => new() { "低于", "高于", "最低", "最高" },
            ConditionCategory.ApPp => new() { "低于", "高于", "最低", "最高" },
            ConditionCategory.SelfHp => new() { "低于", "高于" },
            ConditionCategory.SelfApPp => new() { "低于", "高于" },
            ConditionCategory.Status => new() { "等于" },
            ConditionCategory.EnemyClassExists => new() { "有", "无" },
            ConditionCategory.AttributeRank => new() { "最高", "最低" },
            ConditionCategory.TeamSize => new() { "以上", "以下" },
            ConditionCategory.AttackAttribute => new() { "等于" },
            _ => new() { "等于" }
        };

        public static List<string> GetValues(ConditionCategory cat, string op) => cat switch
        {
            ConditionCategory.UnitClass => new() { "步兵", "骑兵", "飞行", "重装", "斥候", "弓兵", "术士", "精灵", "兽人", "有翼人" },
            ConditionCategory.Hp => op == "最低" || op == "最高"
                ? new() { "-" }
                : new() { "25%", "50%", "75%", "100%" },
            ConditionCategory.ApPp => op == "最低" || op == "最高"
                ? new() { "AP", "PP" }
                : new() { "1", "2", "3", "4" },
            ConditionCategory.SelfHp => new() { "25%", "50%", "75%", "100%" },
            ConditionCategory.SelfApPp => new() { "1", "2", "3", "4" },
            ConditionCategory.Status => new() { "buff", "debuff", "毒", "炎上", "冻结", "气绝", "黑暗", "被动封印", "格挡封印", "非毒", "非炎上", "非冻结", "非气绝", "非黑暗" },
            ConditionCategory.EnemyClassExists => new() { "步兵", "骑兵", "飞行", "重装", "斥候", "弓兵", "术士", "精灵", "兽人", "有翼人" },
            ConditionCategory.AttributeRank => new() { "HP", "物攻", "魔攻", "物防", "魔防", "速度", "命中", "回避", "会心", "格挡" },
            ConditionCategory.TeamSize => op == "以上"
                ? new() { "敌2体", "敌3体", "敌4体", "敌5体", "友2体", "友3体", "友4体", "友5体" }
                : new() { "敌1体", "敌2体", "敌3体", "敌4体", "友1体", "友2体", "友3体", "友4体" },
            ConditionCategory.AttackAttribute => new() { "物理", "魔法", "近接", "远程", "列", "全体" },
            _ => new() { "-" }
        };

        /// <summary>Convert UI selections into a Condition object.</summary>
        public static Condition BuildCondition(ConditionCategory cat, string op, string val, bool isOnly)
        {
            if (cat == ConditionCategory.Hp && (op == "最低" || op == "最高"))
                // For target sorting operators, use "lowest"/"highest"
                return new Condition { Category = cat, Operator = op == "最低" ? "lowest" : "highest", Value = null };

            if (cat == ConditionCategory.ApPp && (op == "最低" || op == "最高"))
                return new Condition { Category = cat, Operator = op == "最低" ? "lowest" : "highest", Value = val }; // "AP" or "PP"

            string actualOp = op switch
            {
                "低于" => "less_than",
                "高于" => "greater_than",
                "等于" => "equals",
                "以上" => "greater_than",
                "以下" => "less_than",
                "有" => "equals",
                "无" => "equals",  // negated in evaluation
                _ => op
            };

            object actualVal = cat switch
            {
                ConditionCategory.Hp => val == "25%" ? 0.25f : val == "50%" ? 0.5f : val == "75%" ? 0.75f : 1.0f,
                ConditionCategory.SelfHp => val == "25%" ? 0.25f : val == "50%" ? 0.5f : val == "75%" ? 0.75f : 1.0f,
                ConditionCategory.SelfApPp => int.Parse(val),
                ConditionCategory.TeamSize => val, // "敌2体" etc — parsed by evaluator
                ConditionCategory.AttributeRank => MapAttributeRank(val),
                _ => val
            };

            return new Condition { Category = cat, Operator = actualOp, Value = actualVal };
        }

        private static string MapAttributeRank(string label) => label switch
        {
            "HP" => "HP", "物攻" => "Str", "魔攻" => "Mag", "物防" => "Def", "魔防" => "MDef",
            "速度" => "Spd", "命中" => "Hit", "回避" => "Eva", "会心" => "Crit", "格挡" => "Block",
            _ => label
        };
    }
}
