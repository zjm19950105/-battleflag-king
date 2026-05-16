using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;

namespace BattleKing.Ai
{
    public enum StrategyConditionCatalogCategoryId
    {
        QueueStatus,
        UnitClass,
        Hp,
        ApPp,
        Status,
        AttackAttribute,
        TeamSize,
        SelfState,
        SelfHp,
        SelfApPp,
        EnemyClassExists,
        HighestAttribute,
        LowestAttribute
    }

    public enum StrategyConditionKind
    {
        Priority,
        Only
    }

    public enum StrategyConditionTargetSide
    {
        Enemy,
        Ally,
        Self,
        SkillDependent,
        None
    }

    public enum StrategyConditionArrow
    {
        None,
        Up,
        Down
    }

    public enum StrategyConditionTextColor
    {
        EnemyRed,
        AllyCyanGreen,
        NeutralWhite,
        NeutralGold
    }

    public sealed class StrategyConditionCatalogCategory
    {
        public StrategyConditionCatalogCategoryId Id { get; }
        public string Label { get; }

        public StrategyConditionCatalogCategory(StrategyConditionCatalogCategoryId id, string label)
        {
            Id = id;
            Label = label;
        }
    }

    public sealed class StrategyConditionCatalogItem
    {
        public string Id { get; }
        public StrategyConditionCatalogCategoryId Category { get; }
        public string DisplayTemplate { get; }
        public StrategyConditionKind Kind { get; }
        public StrategyConditionTargetSide TargetSide { get; }
        public StrategyConditionArrow Arrow { get; }
        public StrategyConditionTextColor TextColor { get; }
        public bool IsImplemented { get; }
        public string NotImplementedReason { get; }
        public ConditionCategory? ConditionCategory { get; }
        public string Operator { get; }
        public object Value { get; }

        public StrategyConditionCatalogItem(
            string id,
            StrategyConditionCatalogCategoryId category,
            string displayTemplate,
            StrategyConditionKind kind,
            StrategyConditionTargetSide targetSide,
            StrategyConditionArrow arrow,
            StrategyConditionTextColor textColor,
            bool isImplemented,
            string notImplementedReason,
            ConditionCategory? conditionCategory,
            string op,
            object value)
        {
            Id = id;
            Category = category;
            DisplayTemplate = displayTemplate;
            Kind = kind;
            TargetSide = targetSide;
            Arrow = arrow;
            TextColor = textColor;
            IsImplemented = isImplemented;
            NotImplementedReason = notImplementedReason;
            ConditionCategory = conditionCategory;
            Operator = op;
            Value = value;
        }

        public Condition BuildCondition()
        {
            if (!IsImplemented || !ConditionCategory.HasValue)
                throw new InvalidOperationException($"Condition catalog item '{Id}' is not implemented: {NotImplementedReason}");

            return new Condition
            {
                Category = ConditionCategory.Value,
                Operator = Operator,
                Value = Value
            };
        }

        public string RenderLabel(ActiveSkillData skill = null)
        {
            string targetText = StrategyConditionCatalog.TargetSideLabel(ResolveTargetSide(skill));
            return DisplayTemplate.Replace("{target}", targetText);
        }

        public StrategyConditionTargetSide ResolveTargetSide(ActiveSkillData skill = null)
        {
            if (TargetSide != StrategyConditionTargetSide.SkillDependent)
                return TargetSide;

            return StrategyConditionCatalog.InferTargetSide(skill);
        }

        public StrategyConditionTextColor ResolveTextColor(ActiveSkillData skill = null)
        {
            return ResolveTargetSide(skill) switch
            {
                StrategyConditionTargetSide.Enemy => StrategyConditionTextColor.EnemyRed,
                StrategyConditionTargetSide.Ally => StrategyConditionTextColor.AllyCyanGreen,
                StrategyConditionTargetSide.Self => StrategyConditionTextColor.NeutralGold,
                _ => TextColor
            };
        }
    }

    public static class StrategyConditionCatalog
    {
        public static readonly IReadOnlyList<StrategyConditionCatalogCategory> Categories = new List<StrategyConditionCatalogCategory>
        {
            new(StrategyConditionCatalogCategoryId.QueueStatus, "队列・状况"),
            new(StrategyConditionCatalogCategoryId.UnitClass, "兵种"),
            new(StrategyConditionCatalogCategoryId.Hp, "HP"),
            new(StrategyConditionCatalogCategoryId.ApPp, "AP・PP"),
            new(StrategyConditionCatalogCategoryId.Status, "状态"),
            new(StrategyConditionCatalogCategoryId.AttackAttribute, "攻击属性"),
            new(StrategyConditionCatalogCategoryId.TeamSize, "编成人数"),
            new(StrategyConditionCatalogCategoryId.SelfState, "自身状态"),
            new(StrategyConditionCatalogCategoryId.SelfHp, "自身 HP"),
            new(StrategyConditionCatalogCategoryId.SelfApPp, "自身 AP・PP"),
            new(StrategyConditionCatalogCategoryId.EnemyClassExists, "敌方兵种有无"),
            new(StrategyConditionCatalogCategoryId.HighestAttribute, "最高能力值"),
            new(StrategyConditionCatalogCategoryId.LowestAttribute, "最低能力值"),
        };

        public static readonly IReadOnlyList<StrategyConditionCatalogItem> AllItems = BuildItems();

        public static IReadOnlyList<StrategyConditionCatalogItem> GetItems(
            StrategyConditionCatalogCategoryId? category = null,
            bool includeNotImplemented = false)
        {
            return AllItems
                .Where(item => (!category.HasValue || item.Category == category.Value)
                    && (includeNotImplemented || item.IsImplemented))
                .ToList();
        }

        public static StrategyConditionCatalogItem FindById(string id)
        {
            return AllItems.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static Condition BuildCondition(string id)
        {
            var item = FindById(id);
            if (item == null)
                throw new ArgumentException($"Unknown strategy condition catalog item id: {id}", nameof(id));

            return item.BuildCondition();
        }

        public static StrategyConditionTargetSide InferTargetSide(ActiveSkillData skill)
        {
            if (skill == null)
                return StrategyConditionTargetSide.Enemy;

            return skill.TargetType switch
            {
                TargetType.Self => StrategyConditionTargetSide.Self,
                TargetType.SingleAlly => StrategyConditionTargetSide.Ally,
                TargetType.AllAllies => StrategyConditionTargetSide.Ally,
                _ => skill.Type == SkillType.Heal || skill.Type == SkillType.Assist
                    ? StrategyConditionTargetSide.Ally
                    : StrategyConditionTargetSide.Enemy
            };
        }

        public static string TargetSideLabel(StrategyConditionTargetSide targetSide)
        {
            return targetSide switch
            {
                StrategyConditionTargetSide.Enemy => "敌方",
                StrategyConditionTargetSide.Ally => "友方",
                StrategyConditionTargetSide.Self => "自身",
                _ => ""
            };
        }

        private static IReadOnlyList<StrategyConditionCatalogItem> BuildItems()
        {
            var items = new List<StrategyConditionCatalogItem>();

            AddTargetPair(items, "queue-front", StrategyConditionCatalogCategoryId.QueueStatus, "前排",
                ConditionCategory.Position, "equals", "front", StrategyConditionArrow.None);
            AddTargetPair(items, "queue-back", StrategyConditionCatalogCategoryId.QueueStatus, "后排",
                ConditionCategory.Position, "equals", "back", StrategyConditionArrow.None);
            AddTargetPair(items, "queue-front-and-back", StrategyConditionCatalogCategoryId.QueueStatus, "前后排一列",
                ConditionCategory.Position, "equals", "front_and_back", StrategyConditionArrow.None);
            // Historical note: these IDs kept the old "column" wording after a
            // JP->CN terminology correction. In game terms they mean front/back
            // row population, not vertical 1-4 / 2-5 / 3-6 column population.
            AddTargetPair(items, "queue-most-column", StrategyConditionCatalogCategoryId.QueueStatus, "人数最多一排",
                ConditionCategory.Position, "highest", "row_unit_count", StrategyConditionArrow.Up);
            AddTargetPair(items, "queue-fewest-column", StrategyConditionCatalogCategoryId.QueueStatus, "人数最少一排",
                ConditionCategory.Position, "lowest", "row_unit_count", StrategyConditionArrow.Down);
            items.Add(Item("queue-only-column-at-least-2", StrategyConditionCatalogCategoryId.QueueStatus,
                "仅{target}2体以上一排", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Position, "greater_or_equal", "row_units:2"));
            items.Add(Item("queue-only-column-at-least-3", StrategyConditionCatalogCategoryId.QueueStatus,
                "仅{target}3体以上一排", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Position, "greater_or_equal", "row_units:3"));
            items.Add(Item("queue-only-daytime", StrategyConditionCatalogCategoryId.QueueStatus,
                "仅白天", StrategyConditionKind.Only, StrategyConditionTargetSide.None,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.Position, "equals", "daytime"));
            items.Add(Item("queue-only-nighttime", StrategyConditionCatalogCategoryId.QueueStatus,
                "仅夜晚", StrategyConditionKind.Only, StrategyConditionTargetSide.None,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.Position, "equals", "nighttime"));

            foreach (var unitClass in UnitClasses())
            {
                AddTargetPair(items, $"class-{unitClass.Id}", StrategyConditionCatalogCategoryId.UnitClass, unitClass.Label,
                    ConditionCategory.UnitClass, "equals", unitClass.Value.ToString(), StrategyConditionArrow.None);
            }

            items.Add(Item("hp-priority-lowest", StrategyConditionCatalogCategoryId.Hp,
                "优先{target}HP最低", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "lowest", null));
            items.Add(Item("hp-priority-highest", StrategyConditionCatalogCategoryId.Hp,
                "优先{target}HP最高", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "highest", null));
            items.Add(Item("hp-ratio-priority-lowest", StrategyConditionCatalogCategoryId.Hp,
                "优先{target}HP比例最低", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "lowest", "ratio"));
            items.Add(Item("hp-ratio-priority-highest", StrategyConditionCatalogCategoryId.Hp,
                "优先{target}HP比例最高", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "highest", "ratio"));
            foreach (var threshold in HpThresholds())
            {
                items.Add(Item($"hp-only-le-{threshold.Id}", StrategyConditionCatalogCategoryId.Hp,
                    $"仅{{target}}HP {threshold.Label}以下", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.Hp, "less_or_equal", threshold.Value));
                items.Add(Item($"hp-only-ge-{threshold.Id}", StrategyConditionCatalogCategoryId.Hp,
                    $"仅{{target}}HP {threshold.Label}以上", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.Hp, "greater_or_equal", threshold.Value));
            }
            items.Add(Item("hp-only-below-average", StrategyConditionCatalogCategoryId.Hp,
                "仅{target}HP低于平均", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "less_than_average", null));
            items.Add(Item("hp-only-above-average", StrategyConditionCatalogCategoryId.Hp,
                "仅{target}HP高于平均", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Hp, "greater_than_average", null));

            AddResourceCatalogItems(items, StrategyConditionCatalogCategoryId.ApPp, ConditionCategory.ApPp, "{target}", isSelf: false);

            AddStatusPair(items, "status-buff", "强化", "buff");
            AddStatusPair(items, "status-debuff", "弱化", "debuff");
            AddStatusPair(items, "status-ailment", "异常", "ailment");
            items.Add(Item("status-only-not-buff", StrategyConditionCatalogCategoryId.Status,
                "仅无强化的{target}", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", "not:buff"));
            items.Add(Item("status-only-not-debuff", StrategyConditionCatalogCategoryId.Status,
                "仅无弱化的{target}", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", "not:debuff"));
            items.Add(Item("status-only-not-ailment", StrategyConditionCatalogCategoryId.Status,
                "仅无异常的{target}", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", "not:ailment"));
            items.Add(Item("status-only-none", StrategyConditionCatalogCategoryId.Status,
                "仅无状态的{target}", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", "none"));

            AddAttackAttribute(items, "physical", "物理攻击", "physical");
            AddAttackAttribute(items, "magical", "魔法攻击", "magical");
            AddAttackAttribute(items, "row", "横排攻击", "row");
            AddAttackAttribute(items, "column", "纵列攻击", "column");
            AddAttackAttribute(items, "front-and-back", "前后排攻击", "front_and_back");
            AddAttackAttribute(items, "all", "全体攻击", "all");
            foreach (var unitClass in UnitClasses())
            {
                items.Add(NotImplemented($"attack-vs-{unitClass.Id}", StrategyConditionCatalogCategoryId.AttackAttribute,
                    $"{unitClass.Label}攻击", StrategyConditionKind.Only, StrategyConditionTargetSide.None,
                    StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                    "ActiveSkillData 目前没有结构化的特攻/目标兵种字段，只有零散 tags，暂不暴露。"));
            }

            for (int count = 1; count <= 5; count++)
            {
                items.Add(Item($"team-enemy-ge-{count}", StrategyConditionCatalogCategoryId.TeamSize,
                    $"敌方数量不少于{count}", StrategyConditionKind.Only, StrategyConditionTargetSide.Enemy,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.EnemyRed,
                    ConditionCategory.TeamSize, "greater_or_equal", $"enemy:{count}"));
                items.Add(Item($"team-enemy-le-{count}", StrategyConditionCatalogCategoryId.TeamSize,
                    $"敌方数量不多于{count}", StrategyConditionKind.Only, StrategyConditionTargetSide.Enemy,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.EnemyRed,
                    ConditionCategory.TeamSize, "less_or_equal", $"enemy:{count}"));
                items.Add(Item($"team-ally-ge-{count}", StrategyConditionCatalogCategoryId.TeamSize,
                    $"友方数量不少于{count}", StrategyConditionKind.Only, StrategyConditionTargetSide.Ally,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.AllyCyanGreen,
                    ConditionCategory.TeamSize, "greater_or_equal", $"ally:{count}"));
                items.Add(Item($"team-ally-le-{count}", StrategyConditionCatalogCategoryId.TeamSize,
                    $"友方数量不多于{count}", StrategyConditionKind.Only, StrategyConditionTargetSide.Ally,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.AllyCyanGreen,
                    ConditionCategory.TeamSize, "less_or_equal", $"ally:{count}"));
            }

            items.Add(Item("self-only-self", StrategyConditionCatalogCategoryId.SelfState,
                "仅自身", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.SelfState, "equals", "self"));
            items.Add(Item("self-only-not-self", StrategyConditionCatalogCategoryId.SelfState,
                "仅自身以外", StrategyConditionKind.Only, StrategyConditionTargetSide.Ally,
                StrategyConditionArrow.None, StrategyConditionTextColor.AllyCyanGreen,
                ConditionCategory.SelfState, "equals", "not_self"));
            items.Add(Item("self-only-buff", StrategyConditionCatalogCategoryId.SelfState,
                "仅自身强化时", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.SelfState, "equals", "buff"));
            items.Add(Item("self-only-debuff", StrategyConditionCatalogCategoryId.SelfState,
                "仅自身弱化时", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.SelfState, "equals", "debuff"));
            for (int action = 1; action <= 5; action++)
            {
                items.Add(Item($"self-only-action-{action}", StrategyConditionCatalogCategoryId.SelfState,
                    $"仅第{action}次行动", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                    StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                    ConditionCategory.SelfState, "equals", $"action:{action}"));
            }

            foreach (var threshold in HpThresholds())
            {
                items.Add(Item($"self-hp-only-le-{threshold.Id}", StrategyConditionCatalogCategoryId.SelfHp,
                    $"仅自身HP {threshold.Label}以下", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralGold,
                    ConditionCategory.SelfHp, "less_or_equal", threshold.Value));
                items.Add(Item($"self-hp-only-ge-{threshold.Id}", StrategyConditionCatalogCategoryId.SelfHp,
                    $"仅自身HP {threshold.Label}以上", StrategyConditionKind.Only, StrategyConditionTargetSide.Self,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralGold,
                    ConditionCategory.SelfHp, "greater_or_equal", threshold.Value));
            }

            AddResourceCatalogItems(items, StrategyConditionCatalogCategoryId.SelfApPp, ConditionCategory.SelfApPp, "自身", isSelf: true);

            foreach (var unitClass in UnitClasses())
            {
                items.Add(Item($"enemy-class-exists-{unitClass.Id}", StrategyConditionCatalogCategoryId.EnemyClassExists,
                    $"敌方有{unitClass.Label}", StrategyConditionKind.Only, StrategyConditionTargetSide.Enemy,
                    StrategyConditionArrow.None, StrategyConditionTextColor.EnemyRed,
                    ConditionCategory.EnemyClassExists, "equals", unitClass.Value.ToString()));
                items.Add(Item($"enemy-class-missing-{unitClass.Id}", StrategyConditionCatalogCategoryId.EnemyClassExists,
                    $"敌方无{unitClass.Label}", StrategyConditionKind.Only, StrategyConditionTargetSide.Enemy,
                    StrategyConditionArrow.None, StrategyConditionTextColor.EnemyRed,
                    ConditionCategory.EnemyClassExists, "not_equals", unitClass.Value.ToString()));
            }

            foreach (var stat in AttributeStats())
            {
                items.Add(Item($"attribute-highest-{stat.Id}", StrategyConditionCatalogCategoryId.HighestAttribute,
                    $"优先{{target}}{stat.Label}最高", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.AttributeRank, "highest", stat.Value));
                items.Add(Item($"attribute-only-highest-{stat.Id}", StrategyConditionCatalogCategoryId.HighestAttribute,
                    $"仅{{target}}{stat.Label}最高", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Up, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.AttributeRank, "highest", stat.Value));
                items.Add(Item($"attribute-lowest-{stat.Id}", StrategyConditionCatalogCategoryId.LowestAttribute,
                    $"优先{{target}}{stat.Label}最低", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.AttributeRank, "lowest", stat.Value));
                items.Add(Item($"attribute-only-lowest-{stat.Id}", StrategyConditionCatalogCategoryId.LowestAttribute,
                    $"仅{{target}}{stat.Label}最低", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                    StrategyConditionArrow.Down, StrategyConditionTextColor.NeutralWhite,
                    ConditionCategory.AttributeRank, "lowest", stat.Value));
            }

            return items;
        }

        private static void AddTargetPair(
            List<StrategyConditionCatalogItem> items,
            string idBase,
            StrategyConditionCatalogCategoryId category,
            string label,
            ConditionCategory conditionCategory,
            string op,
            object value,
            StrategyConditionArrow arrow)
        {
            items.Add(Item($"{idBase}-priority", category, $"优先{{target}}{label}",
                StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent, arrow,
                StrategyConditionTextColor.NeutralWhite, conditionCategory, op, value));
            items.Add(Item($"{idBase}-only", category, $"仅{{target}}{label}",
                StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent, arrow,
                StrategyConditionTextColor.NeutralWhite, conditionCategory, op, value));
        }

        private static void AddResourceCatalogItems(
            List<StrategyConditionCatalogItem> items,
            StrategyConditionCatalogCategoryId category,
            ConditionCategory conditionCategory,
            string targetTemplate,
            bool isSelf)
        {
            var side = isSelf ? StrategyConditionTargetSide.Self : StrategyConditionTargetSide.SkillDependent;
            var color = isSelf ? StrategyConditionTextColor.NeutralGold : StrategyConditionTextColor.NeutralWhite;
            foreach (string resource in new[] { "AP", "PP" })
            {
                items.Add(Item($"{CategoryPrefix(category)}-{resource.ToLowerInvariant()}-priority-lowest", category,
                    $"优先{targetTemplate}{resource}最低", StrategyConditionKind.Priority, side,
                    StrategyConditionArrow.Down, color, conditionCategory, "lowest", resource));
                items.Add(Item($"{CategoryPrefix(category)}-{resource.ToLowerInvariant()}-priority-highest", category,
                    $"优先{targetTemplate}{resource}最高", StrategyConditionKind.Priority, side,
                    StrategyConditionArrow.Up, color, conditionCategory, "highest", resource));
                items.Add(Item($"{CategoryPrefix(category)}-{resource.ToLowerInvariant()}-only-eq-0", category,
                    $"仅{targetTemplate}{resource}为0", StrategyConditionKind.Only, side,
                    StrategyConditionArrow.Down, color, conditionCategory, "equals", $"{resource}:0"));
                for (int threshold = 1; threshold <= 3; threshold++)
                {
                    items.Add(Item($"{CategoryPrefix(category)}-{resource.ToLowerInvariant()}-only-le-{threshold}", category,
                        $"仅{targetTemplate}{resource}{threshold}以下", StrategyConditionKind.Only, side,
                        StrategyConditionArrow.Down, color, conditionCategory, "less_or_equal", $"{resource}:{threshold}"));
                }
                for (int threshold = 1; threshold <= 4; threshold++)
                {
                    items.Add(Item($"{CategoryPrefix(category)}-{resource.ToLowerInvariant()}-only-ge-{threshold}", category,
                        $"仅{targetTemplate}{resource}{threshold}以上", StrategyConditionKind.Only, side,
                        StrategyConditionArrow.Up, color, conditionCategory, "greater_or_equal", $"{resource}:{threshold}"));
                }
            }
        }

        private static void AddStatusPair(List<StrategyConditionCatalogItem> items, string idBase, string label, string value)
        {
            items.Add(Item($"{idBase}-priority", StrategyConditionCatalogCategoryId.Status,
                $"优先{label}的{{target}}", StrategyConditionKind.Priority, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", value));
            items.Add(Item($"{idBase}-only", StrategyConditionCatalogCategoryId.Status,
                $"仅{label}的{{target}}", StrategyConditionKind.Only, StrategyConditionTargetSide.SkillDependent,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralWhite,
                ConditionCategory.Status, "equals", value));
        }

        private static void AddAttackAttribute(List<StrategyConditionCatalogItem> items, string id, string label, string value)
        {
            items.Add(Item($"attack-{id}", StrategyConditionCatalogCategoryId.AttackAttribute,
                $"仅{label}", StrategyConditionKind.Only, StrategyConditionTargetSide.None,
                StrategyConditionArrow.None, StrategyConditionTextColor.NeutralGold,
                ConditionCategory.AttackAttribute, "equals", value));
        }

        private static StrategyConditionCatalogItem Item(
            string id,
            StrategyConditionCatalogCategoryId category,
            string template,
            StrategyConditionKind kind,
            StrategyConditionTargetSide targetSide,
            StrategyConditionArrow arrow,
            StrategyConditionTextColor color,
            ConditionCategory conditionCategory,
            string op,
            object value)
        {
            return new StrategyConditionCatalogItem(id, category, template, kind, targetSide, arrow, color,
                true, "", conditionCategory, op, value);
        }

        private static StrategyConditionCatalogItem NotImplemented(
            string id,
            StrategyConditionCatalogCategoryId category,
            string template,
            StrategyConditionKind kind,
            StrategyConditionTargetSide targetSide,
            StrategyConditionArrow arrow,
            StrategyConditionTextColor color,
            string reason)
        {
            return new StrategyConditionCatalogItem(id, category, template, kind, targetSide, arrow, color,
                false, reason, null, null, null);
        }

        private static string CategoryPrefix(StrategyConditionCatalogCategoryId category)
        {
            return category == StrategyConditionCatalogCategoryId.SelfApPp ? "self-appp" : "appp";
        }

        private static IEnumerable<(UnitClass Value, string Label, string Id)> UnitClasses()
        {
            yield return (UnitClass.Infantry, "步兵", "infantry");
            yield return (UnitClass.Cavalry, "骑马", "cavalry");
            yield return (UnitClass.Flying, "飞行", "flying");
            yield return (UnitClass.Heavy, "重装", "heavy");
            yield return (UnitClass.Scout, "斥候", "scout");
            yield return (UnitClass.Archer, "弓兵", "archer");
            yield return (UnitClass.Mage, "术师", "mage");
            yield return (UnitClass.Elf, "精灵", "elf");
            yield return (UnitClass.Beastman, "兽人", "beastman");
            yield return (UnitClass.Winged, "有翼人", "winged");
            yield return (UnitClass.Undead, "不死系", "undead");
        }

        private static IEnumerable<(string Id, string Label, float Value)> HpThresholds()
        {
            yield return ("25", "25%", 0.25f);
            yield return ("50", "50%", 0.5f);
            yield return ("75", "75%", 0.75f);
            yield return ("100", "100%", 1.0f);
        }

        private static IEnumerable<(string Id, string Label, string Value)> AttributeStats()
        {
            yield return ("max-hp", "最大HP", "MaxHp");
            yield return ("max-ap", "最大AP", "MaxAp");
            yield return ("max-pp", "最大PP", "MaxPp");
            yield return ("str", "物攻", "Str");
            yield return ("mag", "魔攻", "Mag");
            yield return ("def", "物防", "Def");
            yield return ("mdef", "魔防", "MDef");
            yield return ("spd", "速度", "Spd");
            yield return ("hit", "命中", "Hit");
            yield return ("eva", "回避", "Eva");
            yield return ("crit", "会心", "Crit");
            yield return ("block", "格挡", "Block");
        }
    }
}
