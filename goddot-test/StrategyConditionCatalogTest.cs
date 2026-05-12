using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class StrategyConditionCatalogTest
    {
        [Test]
        public void Catalog_LeftCategories_FollowOriginalStyleOrder()
        {
            var labels = StrategyConditionCatalog.Categories.Select(category => category.Label).ToList();

            CollectionAssert.AreEqual(new[]
            {
                "队列・状况",
                "兵种",
                "HP",
                "AP・PP",
                "状态",
                "攻击属性",
                "编成人数",
                "自身状态",
                "自身 HP",
                "自身 AP・PP",
                "敌方兵种有无",
                "最高能力值",
                "最低能力值",
            }, labels);
        }

        [Test]
        public void Catalog_RepresentativeItems_BuildCanonicalConditions()
        {
            var cavalryOnly = StrategyConditionCatalog.BuildCondition("class-cavalry-only");
            ClassicAssert.AreEqual(ConditionCategory.UnitClass, cavalryOnly.Category);
            ClassicAssert.AreEqual("equals", cavalryOnly.Operator);
            ClassicAssert.AreEqual("Cavalry", cavalryOnly.Value);

            var columnAtLeastTwo = StrategyConditionCatalog.BuildCondition("queue-only-column-at-least-2");
            ClassicAssert.AreEqual(ConditionCategory.Position, columnAtLeastTwo.Category);
            ClassicAssert.AreEqual("greater_or_equal", columnAtLeastTwo.Operator);
            ClassicAssert.AreEqual("row_units:2", columnAtLeastTwo.Value);

            var thirdAction = StrategyConditionCatalog.BuildCondition("self-only-action-3");
            ClassicAssert.AreEqual(ConditionCategory.SelfState, thirdAction.Category);
            ClassicAssert.AreEqual("equals", thirdAction.Operator);
            ClassicAssert.AreEqual("action:3", thirdAction.Value);

            var highestMaxAp = StrategyConditionCatalog.BuildCondition("attribute-highest-max-ap");
            ClassicAssert.AreEqual(ConditionCategory.AttributeRank, highestMaxAp.Category);
            ClassicAssert.AreEqual("highest", highestMaxAp.Operator);
            ClassicAssert.AreEqual("MaxAp", highestMaxAp.Value);
        }

        [Test]
        public void Catalog_RenderLabelAndColor_ResolveEnemyOrAllyFromSkillTarget()
        {
            var item = StrategyConditionCatalog.FindById("queue-back-priority");
            var attack = TestDataFactory.CreateSkill(type: SkillType.Physical, targetType: TargetType.SingleEnemy).Data;
            var heal = TestDataFactory.CreateSkill(type: SkillType.Heal, targetType: TargetType.SingleAlly).Data;

            ClassicAssert.AreEqual("优先敌方后排", item.RenderLabel(attack));
            ClassicAssert.AreEqual(StrategyConditionTextColor.EnemyRed, item.ResolveTextColor(attack));
            ClassicAssert.AreEqual("优先友方后排", item.RenderLabel(heal));
            ClassicAssert.AreEqual(StrategyConditionTextColor.AllyCyanGreen, item.ResolveTextColor(heal));
        }

        [Test]
        public void Catalog_ArrowDirection_MatchesPriorityMeaning()
        {
            ClassicAssert.AreEqual(StrategyConditionArrow.Down, StrategyConditionCatalog.FindById("hp-priority-lowest").Arrow);
            ClassicAssert.AreEqual(StrategyConditionArrow.Up, StrategyConditionCatalog.FindById("hp-priority-highest").Arrow);
            ClassicAssert.AreEqual(StrategyConditionArrow.None, StrategyConditionCatalog.FindById("class-cavalry-only").Arrow);
        }

        [Test]
        public void Catalog_NotImplementedItems_AreHiddenByDefault()
        {
            var visibleAttackItems = StrategyConditionCatalog.GetItems(StrategyConditionCatalogCategoryId.AttackAttribute);
            var allAttackItems = StrategyConditionCatalog.GetItems(StrategyConditionCatalogCategoryId.AttackAttribute, includeNotImplemented: true);

            ClassicAssert.IsFalse(visibleAttackItems.Any(item => item.Id == "attack-vs-cavalry"));
            ClassicAssert.IsTrue(allAttackItems.Any(item => item.Id == "attack-vs-cavalry" && !item.IsImplemented));
        }

        [Test]
        public void UiMapper_SaveCatalogSelection_SetsModeFromCatalogItem()
        {
            var strategy = new Strategy();

            StrategyConditionUiMapper.SaveCatalogSelection(strategy, isCondition1: true, "hp-ratio-priority-lowest");
            StrategyConditionUiMapper.SaveCatalogSelection(strategy, isCondition1: false, "self-only-action-2");

            ClassicAssert.AreEqual(ConditionMode.Priority, strategy.Mode1);
            ClassicAssert.AreEqual("ratio", strategy.Condition1.Value);
            ClassicAssert.AreEqual(ConditionMode.Only, strategy.Mode2);
            ClassicAssert.AreEqual("action:2", strategy.Condition2.Value);
        }
    }
}
