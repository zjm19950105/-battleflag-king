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

        // ────────── 编成人数 ──────────

        [Test]
        public void 编成人数_敌大于等于2_2敌_通过()
        {
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_than", Value = "enemy:1" };
            ClassicAssert.IsTrue(_eval.Evaluate(cond, _caster));
        }

        [Test]
        public void 编成人数_敌大于等于3_2敌_不通过()
        {
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            _ctx.EnemyUnits.Add(TestDataFactory.CreateUnit(isPlayer: false));
            var cond = new Condition { Category = ConditionCategory.TeamSize, Operator = "greater_than", Value = "enemy:2" };
            ClassicAssert.IsFalse(_eval.Evaluate(cond, _caster));
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

        // ────────── 空条件 ──────────

        [Test]
        public void 空条件_始终通过()
        {
            ClassicAssert.IsTrue(_eval.Evaluate(null, _caster));
            ClassicAssert.IsTrue(_eval.Evaluate(null, _caster, TestDataFactory.CreateUnit()));
        }
    }
}
