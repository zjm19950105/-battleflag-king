using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class StrategyConditionUiMapperTest
    {
        [Test]
        public void SaveSelection_PreservesPriorityAndOnlyModesPerSlot()
        {
            var strategy = new Strategy();
            int unitClassIndex = ConditionMeta.AllCategories.IndexOf(ConditionCategory.UnitClass) + 1;
            int positionIndex = ConditionMeta.AllCategories.IndexOf(ConditionCategory.Position) + 1;

            StrategyConditionUiMapper.SaveSelection(
                strategy,
                true,
                unitClassIndex,
                "等于",
                "骑兵",
                ConditionMode.Priority);
            StrategyConditionUiMapper.SaveSelection(
                strategy,
                false,
                positionIndex,
                "等于",
                "后排",
                ConditionMode.Only);

            ClassicAssert.AreEqual(ConditionMode.Priority, strategy.Mode1);
            ClassicAssert.AreEqual(ConditionCategory.UnitClass, strategy.Condition1.Category);
            ClassicAssert.AreEqual("Cavalry", strategy.Condition1.Value);
            ClassicAssert.AreEqual(ConditionMode.Only, strategy.Mode2);
            ClassicAssert.AreEqual(ConditionCategory.Position, strategy.Condition2.Category);
            ClassicAssert.AreEqual("back", strategy.Condition2.Value);
        }

        [Test]
        public void FindSelection_MapsCanonicalConditionBackToChineseUiChoices()
        {
            var condition = new Condition
            {
                Category = ConditionCategory.Status,
                Operator = "equals",
                Value = "not:Poison"
            };

            var selection = StrategyConditionUiMapper.FindSelection(condition);
            var category = ConditionMeta.AllCategories[selection.CategoryIndex - 1];
            var op = ConditionMeta.GetOperators(category)[selection.OperatorIndex];
            var value = ConditionMeta.GetValues(category, op)[selection.ValueIndex];

            ClassicAssert.AreEqual(ConditionCategory.Status, category);
            ClassicAssert.AreEqual("等于", op);
            ClassicAssert.AreEqual("非毒", value);
        }
    }
}
