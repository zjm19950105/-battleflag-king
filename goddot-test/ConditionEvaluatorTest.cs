using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Collections.Generic;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Tests
{
    [TestFixture]
    public class ConditionEvaluatorTest
    {
        private ConditionEvaluator _eval;
        private BattleContext _ctx;
        private BattleUnit _caster;

        [SetUp]
        public void SetUp()
        {
            _caster = TestDataFactory.CreateUnit(isPlayer: true);
            _ctx = new BattleContext(null!);
            _eval = new ConditionEvaluator(_ctx);
        }

        // ────────── HP条件 ──────────

        [Test]
        public void HP_低于50percent_HP30per_通过()
        {
            var target = TestDataFactory.CreateUnit(hp: 100);
            target.CurrentHp = 30;
            var cond = new Condition { Category = ConditionCategory.Hp, Operator = "less_than", Value = 0.5f };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void HP_高于50percent_HP80per_通过()
        {
            var target = TestDataFactory.CreateUnit(hp: 100);
            target.CurrentHp = 80;
            var cond = new Condition { Category = ConditionCategory.Hp, Operator = "greater_than", Value = 0.5f };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void HP_低于25percent_HP80per_不通过()
        {
            var target = TestDataFactory.CreateUnit(hp: 100);
            target.CurrentHp = 80;
            var cond = new Condition { Category = ConditionCategory.Hp, Operator = "less_than", Value = 0.25f };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        // ────────── 自身HP ──────────

        [Test]
        public void 自身HP_高于50per_HP60_通过()
        {
            var self = TestDataFactory.CreateUnit(hp: 100);
            self.CurrentHp = 60;
            var cond = new Condition { Category = ConditionCategory.SelfHp, Operator = "greater_than", Value = 0.5f };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, self));
        }

        [Test]
        public void 自身HP_低于25per_HP20_通过()
        {
            var self = TestDataFactory.CreateUnit(hp: 100);
            self.CurrentHp = 20;
            var cond = new Condition { Category = ConditionCategory.SelfHp, Operator = "less_than", Value = 0.25f };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, self));
        }

        // ────────── 兵种条件 ──────────

        [Test]
        public void 兵种_等于飞行_目标飞行_通过()
        {
            var target = TestDataFactory.CreateUnit(classes: new() { UnitClass.Flying });
            var cond = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Flying" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void 兵种_等于骑兵_目标步兵_不通过()
        {
            var target = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry });
            var cond = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Cavalry" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void UnitClass_UsesEffectiveClassesAfterCc()
        {
            var target = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry });
            target.Data.CcClasses = new() { UnitClass.Cavalry };
            target.SetCcState(true);
            var cond = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Cavalry" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void UnitClass_ChineseValue_DoesNotSilentlyPass()
        {
            var target = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry });
            var cond = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "骑兵" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void BuildCondition_ChineseUnitClass_StoresCanonicalValue()
        {
            var cond = ConditionMeta.BuildCondition(ConditionCategory.UnitClass, "等于", "骑兵", true);
            ClassicAssert.AreEqual("equals", cond.Operator);
            ClassicAssert.AreEqual("Cavalry", cond.Value);
        }

        // ────────── 位置条件 ──────────

        [Test]
        public void 位置_前排_pos1_通过()
        {
            var target = TestDataFactory.CreateUnit();
            target.Position = 1;
            var cond = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void 位置_后排_pos5_不通过前排()
        {
            var target = TestDataFactory.CreateUnit();
            target.Position = 5;
            var cond = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void Position_FrontAndBackColumn_PassesOnlyWhenOppositeRowSameColumnExists()
        {
            var front = TestDataFactory.CreateUnit(isPlayer: false);
            front.Position = 1;
            var sameColumnBack = TestDataFactory.CreateUnit(isPlayer: false);
            sameColumnBack.Position = 4;
            var otherColumnBack = TestDataFactory.CreateUnit(isPlayer: false);
            otherColumnBack.Position = 5;
            _ctx.EnemyUnits.Add(front);
            _ctx.EnemyUnits.Add(sameColumnBack);
            _ctx.EnemyUnits.Add(otherColumnBack);
            var cond = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front_and_back" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, front));
            _ctx.EnemyUnits.Remove(front);
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, otherColumnBack));
        }

        [Test]
        public void Position_RowUnitCount_PassesOnlyWhenTargetRowHasEnoughAliveUnits()
        {
            var front = TestDataFactory.CreateUnit(isPlayer: false);
            front.Position = 1;
            var sameRow = TestDataFactory.CreateUnit(isPlayer: false);
            sameRow.Position = 2;
            var backRow = TestDataFactory.CreateUnit(isPlayer: false);
            backRow.Position = 4;
            _ctx.EnemyUnits.Add(front);
            _ctx.EnemyUnits.Add(sameRow);
            _ctx.EnemyUnits.Add(backRow);
            var cond = StrategyConditionCatalog.BuildCondition("queue-only-column-at-least-2");

            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, front));
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, sameRow));
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, backRow));
        }

        [Test]
        public void Position_DayNight_UsesBattleContextTime()
        {
            _ctx.IsDaytime = false;
            var night = StrategyConditionCatalog.BuildCondition("queue-only-nighttime");
            var day = StrategyConditionCatalog.BuildCondition("queue-only-daytime");

            ClassicAssert.IsTrue(_eval.Evaluate(night, _caster));
            ClassicAssert.IsFalse(_eval.Evaluate(day, _caster));
        }

        [Test]
        public void BuildCondition_ChinesePosition_StoresCanonicalValue()
        {
            var back = ConditionMeta.BuildCondition(ConditionCategory.Position, "等于", "后排", false);
            ClassicAssert.AreEqual("equals", back.Operator);
            ClassicAssert.AreEqual("back", back.Value);

            var columnPair = ConditionMeta.BuildCondition(ConditionCategory.Position, "等于", "前后排一列", true);
            ClassicAssert.AreEqual("front_and_back", columnPair.Value);
        }

        // ────────── 状态条件 ──────────

        [Test]
        public void 状态_buff_目标有buff_通过()
        {
            var target = TestDataFactory.CreateUnit();
            target.Buffs.Add(new BattleKing.Equipment.Buff { Ratio = 0.2f }); // positive buff
            var cond = new Condition { Category = ConditionCategory.Status, Operator = "equals", Value = "buff" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void 状态_毒_目标中毒_通过()
        {
            var target = TestDataFactory.CreateUnit();
            target.Ailments.Add(StatusAilment.Poison);
            var cond = new Condition { Category = ConditionCategory.Status, Operator = "equals", Value = "Poison" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void Status_NotCanonicalAilment_PassesOnlyWhenAilmentAbsent()
        {
            var target = TestDataFactory.CreateUnit();
            var cond = new Condition { Category = ConditionCategory.Status, Operator = "equals", Value = "not:Poison" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));

            target.Ailments.Add(StatusAilment.Poison);
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void Status_ChineseValue_DoesNotSilentlyPass()
        {
            var target = TestDataFactory.CreateUnit();
            var cond = new Condition { Category = ConditionCategory.Status, Operator = "equals", Value = "非毒" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void BuildCondition_ChineseNegativeStatus_StoresCanonicalValue()
        {
            var cond = ConditionMeta.BuildCondition(ConditionCategory.Status, "等于", "非毒", true);
            ClassicAssert.AreEqual("equals", cond.Operator);
            ClassicAssert.AreEqual("not:Poison", cond.Value);
        }

        // ────────── 敌兵种存在 ──────────

        [Test]
        public void 敌兵种存在_有飞行_通过()
        {
            var enemy = TestDataFactory.CreateUnit(classes: new() { UnitClass.Flying }, isPlayer: false);
            _ctx.EnemyUnits.Add(enemy);
            var cond = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "equals", Value = "Flying" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void 敌兵种存在_无骑兵_不通过()
        {
            var enemy = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry }, isPlayer: false);
            _ctx.EnemyUnits.Add(enemy);
            var cond = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "equals", Value = "Cavalry" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void EnemyClassExists_PlayerSubject_ScansEnemyUnits()
        {
            var player = TestDataFactory.CreateUnit(isPlayer: true);
            var enemy = TestDataFactory.CreateUnit(classes: new() { UnitClass.Flying }, isPlayer: false);
            _ctx.PlayerUnits.Add(player);
            _ctx.EnemyUnits.Add(enemy);

            var cond = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "equals", Value = "Flying" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, player));
        }

        [Test]
        public void EnemyClassExists_EnemySubject_ScansPlayerUnits()
        {
            var player = TestDataFactory.CreateUnit(classes: new() { UnitClass.Flying }, isPlayer: true);
            var enemy = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry }, isPlayer: false);
            _ctx.PlayerUnits.Add(player);
            _ctx.EnemyUnits.Add(enemy);

            var cond = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "equals", Value = "Flying" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, enemy));
        }

        [Test]
        public void EnemyClassExists_UsesEffectiveClassesAfterCc()
        {
            var player = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry }, isPlayer: true);
            player.Data.CcClasses = new() { UnitClass.Flying };
            player.SetCcState(true);
            var enemy = TestDataFactory.CreateUnit(isPlayer: false);
            _ctx.PlayerUnits.Add(player);
            _ctx.EnemyUnits.Add(enemy);

            var cond = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "equals", Value = "Flying" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, enemy));
        }

        [Test]
        public void EnemyClassExists_NotEquals_PassesOnlyWhenClassAbsent()
        {
            var enemy = TestDataFactory.CreateUnit(classes: new() { UnitClass.Infantry }, isPlayer: false);
            _ctx.EnemyUnits.Add(enemy);

            var noCavalry = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "not_equals", Value = "Cavalry" };
            var noInfantry = new Condition { Category = ConditionCategory.EnemyClassExists, Operator = "not_equals", Value = "Infantry" };

            ClassicAssert.IsTrue(_eval.Evaluate(noCavalry, _caster));
            ClassicAssert.IsFalse(_eval.Evaluate(noInfantry, _caster));
        }

        [Test]
        public void BuildCondition_EnemyClassExistsNone_StoresNegatedOperator()
        {
            var cond = ConditionMeta.BuildCondition(ConditionCategory.EnemyClassExists, "无", "骑兵", true);
            ClassicAssert.AreEqual("not_equals", cond.Operator);
            ClassicAssert.AreEqual("Cavalry", cond.Value);
        }

        // ────────── 自身AP·PP ──────────

        [Test]
        public void 自身AP_大于2_AP3_通过()
        {
            var self = TestDataFactory.CreateUnit();
            self.CurrentAp = 3;
            var cond = new Condition { Category = ConditionCategory.SelfApPp, Operator = "greater_than", Value = 2 };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, self));
        }

        [Test]
        public void 自身AP_大于2_AP1_不通过()
        {
            var self = TestDataFactory.CreateUnit();
            self.CurrentAp = 1;
            var cond = new Condition { Category = ConditionCategory.SelfApPp, Operator = "greater_than", Value = 2 };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, self));
        }

        [Test]
        public void ApPp_ResourceThreshold_UsesCanonicalResourcePrefix()
        {
            var target = TestDataFactory.CreateUnit(isPlayer: false);
            target.CurrentAp = 3;
            target.CurrentPp = 1;
            var ppAtMostOne = StrategyConditionCatalog.BuildCondition("appp-pp-only-le-1");
            var apAtMostOne = StrategyConditionCatalog.BuildCondition("appp-ap-only-le-1");

            ClassicAssert.IsTrue(_eval.Evaluate(ppAtMostOne, _caster, target));
            ClassicAssert.IsFalse(_eval.Evaluate(apAtMostOne, _caster, target));
        }

        [Test]
        public void SelfState_ActionCount_UsesNextActionNumber()
        {
            var self = TestDataFactory.CreateUnit();
            self.ActionCount = 1;
            var secondAction = StrategyConditionCatalog.BuildCondition("self-only-action-2");
            var firstAction = StrategyConditionCatalog.BuildCondition("self-only-action-1");

            ClassicAssert.IsTrue(_eval.Evaluate(secondAction, self));
            ClassicAssert.IsFalse(_eval.Evaluate(firstAction, self));
        }

        [Test]
        public void Status_AilmentAndNone_UseCanonicalValues()
        {
            var target = TestDataFactory.CreateUnit();
            var ailment = StrategyConditionCatalog.BuildCondition("status-ailment-only");
            var none = StrategyConditionCatalog.BuildCondition("status-only-none");

            ClassicAssert.IsTrue(_eval.Evaluate(none, _caster, target));
            ClassicAssert.IsFalse(_eval.Evaluate(ailment, _caster, target));

            target.Ailments.Add(StatusAilment.Burn);
            ClassicAssert.IsTrue(_eval.Evaluate(ailment, _caster, target));
            ClassicAssert.IsFalse(_eval.Evaluate(none, _caster, target));
        }

        // ────────── 编成人数 ──────────

        [Test]
        public void 编成人数_敌大于等于2_2敌_通过()
        {
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_or_equal", Value = "enemy:2" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void 编成人数_敌大于等于3_2敌_不通过()
        {
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_or_equal", Value = "enemy:3" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void TeamSize_EnemySubject_EnemyMeansPlayerSide()
        {
            var enemySubject = TestDataFactory.CreateUnit(isPlayer: false);
            _ctx.EnemyUnits.Add(enemySubject);
            _ctx.PlayerUnits.Add(TestDataFactory.CreateUnit(isPlayer: true));
            _ctx.PlayerUnits.Add(TestDataFactory.CreateUnit(isPlayer: true));

            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_or_equal", Value = "enemy:2" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, enemySubject));
        }

        [Test]
        public void TeamSize_BadValue_DoesNotSilentlyPass()
        {
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_or_equal", Value = "敌2体" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void BuildCondition_ChineseTeamSize_StoresCanonicalValue()
        {
            var cond = ConditionMeta.BuildCondition(ConditionCategory.TeamSize, "以上", "敌2体", true);
            ClassicAssert.AreEqual("greater_or_equal", cond.Operator);
            ClassicAssert.AreEqual("enemy:2", cond.Value);
        }

        [Test]
        public void AttackAttribute_BadValue_DoesNotSilentlyPass()
        {
            var target = TestDataFactory.CreateUnit();
            _ctx.CurrentCalc = TestDataFactory.CreateCalc(
                _caster,
                target,
                TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Melee));
            var cond = new Condition { Category = ConditionCategory.AttackAttribute, Operator = "equals", Value = "物理" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void BuildCondition_StrategyEditorSelections_StoreCanonicalValues()
        {
            var attackAttribute = ConditionMeta.BuildCondition(ConditionCategory.AttackAttribute, "等于", "物理", true);
            ClassicAssert.AreEqual("equals", attackAttribute.Operator);
            ClassicAssert.AreEqual("physical", attackAttribute.Value);

            var attributeRank = ConditionMeta.BuildCondition(ConditionCategory.AttributeRank, "最高", "物攻", false);
            ClassicAssert.AreEqual("highest", attributeRank.Operator);
            ClassicAssert.AreEqual("Str", attributeRank.Value);

            var apPpRank = ConditionMeta.BuildCondition(ConditionCategory.ApPp, "最高", "PP", false);
            ClassicAssert.AreEqual("highest", apPpRank.Operator);
            ClassicAssert.AreEqual("PP", apPpRank.Value);
        }

        [Test]
        public void UnknownOperator_DoesNotSilentlyPass()
        {
            var target = TestDataFactory.CreateUnit(classes: new() { UnitClass.Cavalry });
            var cond = new Condition { Category = ConditionCategory.UnitClass, Operator = "maybe", Value = "Cavalry" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, target));
        }

        // ────────── 属性排名 ──────────

        [Test]
        public void 属性排名_最高HP_HP最高者_通过()
        {
            var target = TestDataFactory.CreateUnit(hp: 100);
            target.CurrentHp = 100;
            var otherEnemy = TestDataFactory.CreateUnit(hp: 50, isPlayer: false);
            otherEnemy.CurrentHp = 50;
            _ctx.EnemyUnits.Add(target);
            _ctx.EnemyUnits.Add(otherEnemy);
            var cond = new Condition { Category = ConditionCategory.AttributeRank, Operator = "highest", Value = "HP" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
        }

        [Test]
        public void AttributeRank_UsesCurrentStatAfterBuff()
        {
            var target = TestDataFactory.CreateUnit(str: 50, isPlayer: false);
            target.Buffs.Add(new BattleKing.Equipment.Buff { TargetStat = "Str", FlatAmount = 60 });
            var otherEnemy = TestDataFactory.CreateUnit(str: 100, isPlayer: false);
            _ctx.EnemyUnits.Add(target);
            _ctx.EnemyUnits.Add(otherEnemy);
            var cond = new Condition { Category = ConditionCategory.AttributeRank, Operator = "highest", Value = "Str" };

            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster, target));
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster, otherEnemy));
        }

        // ────────── 空条件 ──────────

        [Test]
        public void 空条件_始终通过()
        {
            ClassicAssert.IsTrue(_eval.Evaluate(null, _caster));
            ClassicAssert.IsTrue(_eval.Evaluate(null, _caster, TestDataFactory.CreateUnit()));
        }
    }
}
