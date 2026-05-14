using System.Collections.Generic;
using BattleKing.Data;

namespace BattleKing.Ai
{
    /// <summary>Metadata for driving cascading condition editor dropdowns in the strategy UI.</summary>
    public static class ConditionMeta
    {
        public static readonly List<ConditionCategory> AllCategories = new()
        {
            ConditionCategory.Position,
            ConditionCategory.UnitClass,
            ConditionCategory.Hp,
            ConditionCategory.ApPp,
            ConditionCategory.Status,
            ConditionCategory.AttackAttribute,
            ConditionCategory.TeamSize,
            ConditionCategory.SelfState,
            ConditionCategory.SelfHp,
            ConditionCategory.SelfApPp,
            ConditionCategory.EnemyClassExists,
            ConditionCategory.AttributeRank,
        };

        public static string CategoryLabel(ConditionCategory cat) => cat switch
        {
            ConditionCategory.Position => "队列・状况",
            ConditionCategory.UnitClass => "兵种",
            ConditionCategory.Hp => "HP",
            ConditionCategory.ApPp => "AP・PP",
            ConditionCategory.Status => "状态",
            ConditionCategory.AttackAttribute => "攻击属性",
            ConditionCategory.TeamSize => "编成人数",
            ConditionCategory.SelfState => "自身状态",
            ConditionCategory.SelfHp => "自身 HP",
            ConditionCategory.SelfApPp => "自身 AP・PP",
            ConditionCategory.EnemyClassExists => "敌方兵种有无",
            ConditionCategory.AttributeRank => "能力值排名",
            _ => cat.ToString()
        };

        public static List<string> GetOperators(ConditionCategory cat) => cat switch
        {
            ConditionCategory.Position => new() { "等于" },
            ConditionCategory.UnitClass => new() { "等于" },
            ConditionCategory.Hp => new() { "低于", "高于", "最低", "最高" },
            ConditionCategory.ApPp => new() { "低于", "高于", "最低", "最高" },
            ConditionCategory.Status => new() { "等于" },
            ConditionCategory.AttackAttribute => new() { "等于" },
            ConditionCategory.TeamSize => new() { "以上", "以下" },
            ConditionCategory.SelfState => new() { "等于" },
            ConditionCategory.SelfHp => new() { "低于", "高于" },
            ConditionCategory.SelfApPp => new() { "低于", "高于" },
            ConditionCategory.EnemyClassExists => new() { "有", "无" },
            ConditionCategory.AttributeRank => new() { "最高", "最低" },
            _ => new() { "等于" }
        };

        public static List<string> GetValues(ConditionCategory cat, string op) => cat switch
        {
            ConditionCategory.Position => new() { "前排", "后排", "前后排一列", "白天", "夜晚" },
            ConditionCategory.UnitClass => new() { "步兵", "骑马", "飞行", "重装", "斥候", "弓兵", "术师", "精灵", "兽人", "有翼人" },
            ConditionCategory.Hp => op == "最低" || op == "最高"
                ? new() { "-" }
                : new() { "25%", "50%", "75%", "100%" },
            ConditionCategory.ApPp => op == "最低" || op == "最高"
                ? new() { "AP", "PP" }
                : new() { "1", "2", "3", "4" },
            ConditionCategory.Status => new() { "buff", "debuff", "异常", "无状态", "毒", "炎上", "冻结", "气绝", "黑暗", "被动封印", "格挡封印", "非毒", "非炎上", "非冻结", "非气绝", "非黑暗", "非被动封印", "非格挡封印" },
            ConditionCategory.AttackAttribute => new() { "物理", "魔法", "近接", "远程", "横排", "纵列", "前后排", "全体" },
            ConditionCategory.TeamSize => op == "以上"
                ? new() { "敌2体", "敌3体", "敌4体", "敌5体", "友2体", "友3体", "友4体", "友5体" }
                : new() { "敌1体", "敌2体", "敌3体", "敌4体", "友1体", "友2体", "友3体", "友4体" },
            ConditionCategory.SelfState => new() { "自身", "自身以外", "buff", "debuff", "第1次行动", "第2次行动", "第3次行动", "第4次行动", "第5次行动", "蓄力", "气绝", "冻结", "黑暗" },
            ConditionCategory.SelfHp => new() { "25%", "50%", "75%", "100%" },
            ConditionCategory.SelfApPp => new() { "1", "2", "3", "4" },
            ConditionCategory.EnemyClassExists => new() { "步兵", "骑马", "飞行", "重装", "斥候", "弓兵", "术师", "精灵", "兽人", "有翼人" },
            ConditionCategory.AttributeRank => new() { "最大HP", "最大AP", "最大PP", "HP", "物攻", "魔攻", "物防", "魔防", "速度", "命中", "回避", "会心", "格挡" },
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

            if (cat == ConditionCategory.AttributeRank && (op == "最低" || op == "最高"))
                return new Condition { Category = cat, Operator = op == "最低" ? "lowest" : "highest", Value = MapAttributeRank(val) };

            string actualOp = op switch
            {
                "低于" => "less_than",
                "高于" => "greater_than",
                "等于" => "equals",
                "以上" => "greater_or_equal",
                "以下" => "less_or_equal",
                "有" => "equals",
                "无" => "not_equals",
                _ => op
            };

            object actualVal = cat switch
            {
                ConditionCategory.Hp => val == "25%" ? 0.25f : val == "50%" ? 0.5f : val == "75%" ? 0.75f : 1.0f,
                ConditionCategory.SelfHp => val == "25%" ? 0.25f : val == "50%" ? 0.5f : val == "75%" ? 0.75f : 1.0f,
                ConditionCategory.SelfApPp => int.Parse(val),
                ConditionCategory.TeamSize => MapTeamSize(val),
                ConditionCategory.AttributeRank => MapAttributeRank(val),
                ConditionCategory.Position => MapPosition(val),
                ConditionCategory.UnitClass => MapUnitClass(val),
                ConditionCategory.SelfState => MapSelfState(val),
                ConditionCategory.EnemyClassExists => MapUnitClass(val),
                ConditionCategory.Status => MapStatus(val),
                ConditionCategory.AttackAttribute => MapAttackAttribute(val),
                _ => val
            };

            return new Condition { Category = cat, Operator = actualOp, Value = actualVal };
        }

        private static string MapPosition(string label) => label switch
        {
            "前排" => "front",
            "后排" => "back",
            "前后排一列" => "front_and_back",
            "白天" => "daytime",
            "夜晚" => "nighttime",
            _ => label
        };

        private static string MapAttributeRank(string label) => label switch
        {
            "最大HP" => "MaxHp", "最大AP" => "MaxAp", "最大PP" => "MaxPp",
            "HP" => "HP", "物攻" => "Str", "魔攻" => "Mag", "物防" => "Def", "魔防" => "MDef",
            "速度" => "Spd", "命中" => "Hit", "回避" => "Eva", "会心" => "Crit", "格挡" => "Block",
            _ => label
        };

        private static string MapUnitClass(string label) => label switch
        {
            "步兵" => "Infantry",
            "骑兵" => "Cavalry",
            "骑马" => "Cavalry",
            "飞行" => "Flying",
            "重装" => "Heavy",
            "斥候" => "Scout",
            "弓兵" => "Archer",
            "术士" => "Mage",
            "术师" => "Mage",
            "精灵" => "Elf",
            "兽人" => "Beastman",
            "有翼人" => "Winged",
            _ => label
        };

        private static string MapTeamSize(string label) => label switch
        {
            "敌1体" => "enemy:1",
            "敌2体" => "enemy:2",
            "敌3体" => "enemy:3",
            "敌4体" => "enemy:4",
            "敌5体" => "enemy:5",
            "友1体" => "ally:1",
            "友2体" => "ally:2",
            "友3体" => "ally:3",
            "友4体" => "ally:4",
            "友5体" => "ally:5",
            _ => label
        };

        private static string MapStatus(string label) => label switch
        {
            "毒" => "Poison",
            "炎上" => "Burn",
            "冻结" => "Freeze",
            "气绝" => "Stun",
            "黑暗" => "Darkness",
            "被动封印" => "PassiveSeal",
            "格挡封印" => "BlockSeal",
            "异常" => "ailment",
            "无状态" => "none",
            "非毒" => "not:Poison",
            "非炎上" => "not:Burn",
            "非冻结" => "not:Freeze",
            "非气绝" => "not:Stun",
            "非黑暗" => "not:Darkness",
            "非被动封印" => "not:PassiveSeal",
            "非格挡封印" => "not:BlockSeal",
            _ => label
        };

        private static string MapSelfState(string label) => label switch
        {
            "自身" => "self",
            "自身以外" => "not_self",
            "第1次行动" => "action:1",
            "第2次行动" => "action:2",
            "第3次行动" => "action:3",
            "第4次行动" => "action:4",
            "第5次行动" => "action:5",
            "蓄力" => "charging",
            "气绝" => "stunned",
            "冻结" => "frozen",
            "黑暗" => "darkness",
            _ => label
        };

        private static string MapAttackAttribute(string label) => label switch
        {
            "物理" => "physical",
            "魔法" => "magical",
            "近接" => "melee",
            "远程" => "ranged",
            "列" => "column",
            "纵列" => "column",
            "横排" => "row",
            "前后排" => "front_and_back",
            "全体" => "all",
            _ => label
        };
    }
}
