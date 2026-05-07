using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;

namespace BattleKing.Tests
{
    [TestFixture]
    public class TargetSelectorTest
    {
        private BattleContext _ctx;
        private TargetSelector _selector;

        [SetUp]
        public void SetUp()
        {
            _ctx = new BattleContext(null!);
            _selector = new TargetSelector(_ctx);
        }

        private void AddEnemies(params (int pos, int hp)[] units)
        {
            foreach (var (pos, hp) in units)
            {
                var u = TestDataFactory.CreateUnit(hp: hp, isPlayer: false);
                u.CurrentHp = hp;
                u.Position = pos;
                _ctx.EnemyUnits.Add(u);
            }
        }

        // ────────── 默认目标规则 ──────────

        [Test]
        public void 近战_前排有敌人_只打前排()
        {
            AddEnemies((1, 80), (2, 60), (5, 40));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Melee, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy { SkillId = "test_skill" };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.IsTrue(targets[0].IsFrontRow);
        }

        [Test]
        public void 远程_可打后排()
        {
            AddEnemies((1, 80), (5, 40));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy { SkillId = "test_skill" };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.AreEqual(1, targets.Count);
            // Default order: position 1 before position 5
            ClassicAssert.AreEqual(1, targets[0].Position);
        }

        // ────────── 条件过滤 ──────────

        [Test]
        public void 仅_前排_过滤后排()
        {
            AddEnemies((1, 80), (2, 60), (5, 40));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.GreaterOrEqual(0, targets.Count); // Row may return 1 or 2
            ClassicAssert.IsTrue(targets.All(t => t.IsFrontRow));
        }

        // ────────── 优先排序 ──────────

        [Test]
        public void 优先_HP最低_选残血()
        {
            AddEnemies((1, 80), (2, 20), (3, 60));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreEqual(20, targets[0].CurrentHp); // lowest HP
        }

        // ────────── 列攻击 ──────────

        [Test]
        public void FrontAndBack_贯通_打前后列()
        {
            AddEnemies((1, 80), (4, 50)); // same column (pos-1)%3 = 0
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.FrontAndBack);
            var strategy = new Strategy { SkillId = "test_skill" };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.AreEqual(2, targets.Count);
        }

        // ────────── Row攻击 ──────────

        [Test]
        public void Row_同排攻击()
        {
            AddEnemies((1, 80), (2, 60), (4, 50));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.Row);
            var strategy = new Strategy { SkillId = "test_skill" };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.GreaterOrEqual(0, targets.Count); // Row may return 1 or 2
            // All targets should be in the same row as the first target
            bool isFront = targets[0].IsFrontRow;
            ClassicAssert.IsTrue(targets.All(t => t.IsFrontRow == isFront));
        }

        // ────────── 双优先条件 ──────────

        [Test]
        public void 双优先_条件2优先于条件1()
        {
            AddEnemies((1, 80), (2, 20), (3, 60));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "highest" },
                Mode1 = ConditionMode.Priority,
                Condition2 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode2 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.AreEqual(1, targets.Count);
            // Condition2 (lowest) takes priority → should be the 20 HP unit
            ClassicAssert.AreEqual(20, targets[0].CurrentHp);
        }

        // ────────── 无目标 ──────────

        [Test]
        public void 敌军全灭_返回null()
        {
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.SingleEnemy);
            var strategy = new Strategy { SkillId = "test_skill" };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);
            ClassicAssert.IsNull(targets);
        }
    }
}
