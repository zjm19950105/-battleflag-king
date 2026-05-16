using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
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
                AddEnemy(pos, hp);
        }

        private BattleUnit AddEnemy(int pos, int hp = 100, List<UnitClass>? classes = null)
        {
            var unit = classes == null
                ? TestDataFactory.CreateUnit(hp: hp, isPlayer: false)
                : TestDataFactory.CreateUnit(hp: hp, isPlayer: false, classes: classes);
            unit.CurrentHp = hp;
            unit.Position = pos;
            _ctx.EnemyUnits.Add(unit);
            return unit;
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
            ClassicAssert.IsTrue(targets[0].Position == 1 || targets[0].Position == 5);
        }

        [Test]
        public void Default_NoConditions_RandomizesAmongMultipleLegalFrontRowTargets()
        {
            AddEnemies((1, 80), (2, 60), (3, 40), (4, 20));
            var selector = new TargetSelector(_ctx, new Random(1));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Melee, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy { SkillId = "test_skill" };
            var selectedPositions = new HashSet<int>();

            for (int i = 0; i < 20; i++)
            {
                var targets = selector.SelectTargets(caster, strategy, skill.Data);

                ClassicAssert.AreEqual(1, targets.Count);
                ClassicAssert.IsTrue(targets[0].IsFrontRow);
                selectedPositions.Add(targets[0].Position);
            }

            ClassicAssert.Greater(selectedPositions.Count, 1);
            ClassicAssert.IsTrue(selectedPositions.All(position => position >= 1 && position <= 3));
        }

        [Test]
        public void Conditions_OnlyAndPriorityKeepTheirFilteringAndOrdering()
        {
            AddEnemy(1, hp: 80, classes: new() { UnitClass.Infantry });
            var cavalry = AddEnemy(2, hp: 20, classes: new() { UnitClass.Cavalry });
            AddEnemy(3, hp: 60, classes: new() { UnitClass.Cavalry });
            var selector = new TargetSelector(_ctx, new Random(1));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Cavalry" },
                Mode1 = ConditionMode.Only,
                Condition2 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode2 = ConditionMode.Priority
            };

            for (int i = 0; i < 10; i++)
            {
                var targets = selector.SelectTargets(caster, strategy, skill.Data);

                ClassicAssert.AreEqual(1, targets.Count);
                ClassicAssert.AreSame(cavalry, targets[0]);
            }
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
            ClassicAssert.GreaterOrEqual(targets.Count, 1);
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

        [Test]
        public void Priority_NoMatchingTarget_FallsBackToDefaultTarget()
        {
            var defaultTarget = AddEnemy(1, classes: new() { UnitClass.Infantry });
            AddEnemy(2, classes: new() { UnitClass.Infantry });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Cavalry" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(defaultTarget, targets[0]);
        }

        [Test]
        public void Only_InCondition2_NoMatchingTarget_SkipsSkill()
        {
            AddEnemy(1, classes: new() { UnitClass.Infantry });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition2 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Cavalry" },
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.IsNull(targets);
        }

        [Test]
        public void TwoConditions_SelectsIntersection()
        {
            AddEnemy(1, classes: new() { UnitClass.Scout });
            var backScout = AddEnemy(4, classes: new() { UnitClass.Scout });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Priority,
                Condition2 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Scout" },
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(backScout, targets[0]);
        }

        [Test]
        public void PriorityAndOnly_WhenIntersectionEmpty_FallsBackToOnlyCandidates()
        {
            var frontScout = AddEnemy(1, classes: new() { UnitClass.Scout });
            AddEnemy(4, classes: new() { UnitClass.Infantry });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Priority,
                Condition2 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Scout" },
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(frontScout, targets[0]);
        }

        [Test]
        public void OnlyAndOnly_UsesIntersectionOfBothConditions()
        {
            AddEnemy(1, classes: new() { UnitClass.Scout });
            var scoutInfantry = AddEnemy(2, classes: new() { UnitClass.Scout, UnitClass.Infantry });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Scout" },
                Mode1 = ConditionMode.Only,
                Condition2 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Infantry" },
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(scoutInfantry, targets[0]);
        }

        [Test]
        public void OnlyAndOnly_WhenIntersectionEmpty_SkipsSkill()
        {
            AddEnemy(1, classes: new() { UnitClass.Scout });
            AddEnemy(2, classes: new() { UnitClass.Infantry });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Scout" },
                Mode1 = ConditionMode.Only,
                Condition2 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Infantry" },
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.IsNull(targets);
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

        [Test]
        public void FrontAndBack_MeleeBackRowPriority_DoesNotBypassFrontRowAnchor()
        {
            AddEnemies((1, 80), (4, 50));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Melee, targetType: TargetType.FrontAndBack);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            CollectionAssert.AreEqual(new[] { 1, 4 }, targets.Select(target => target.Position).ToArray());
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
            ClassicAssert.GreaterOrEqual(targets.Count, 1);
            // All targets should be in the same row as the first target
            bool isFront = targets[0].IsFrontRow;
            ClassicAssert.IsTrue(targets.All(t => t.IsFrontRow == isFront));
        }

        [Test]
        public void Row_ExpandsSelectedBackRowAnchorToAllAliveEnemiesInThatRow()
        {
            AddEnemies((1, 80), (4, 50), (5, 40));
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.Row);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            CollectionAssert.AreEqual(new[] { 4, 5 }, targets.Select(target => target.Position).ToArray());
        }

        [Test]
        public void Row_只攻击敌方同排_不误伤友方()
        {
            AddEnemies((1, 80), (2, 60), (4, 50));
            var ally = TestDataFactory.CreateUnit(isPlayer: true);
            ally.Position = 3;
            _ctx.PlayerUnits.Add(ally);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.Row);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(2, targets.Count);
            ClassicAssert.IsTrue(targets.All(t => !t.IsPlayer));
        }

        [Test]
        public void Column_只攻击敌方同列_不误伤友方()
        {
            AddEnemies((1, 80), (4, 50), (2, 60));
            var ally = TestDataFactory.CreateUnit(isPlayer: true);
            ally.Position = 1;
            _ctx.PlayerUnits.Add(ally);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(targetType: TargetType.Column);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(2, targets.Count);
            ClassicAssert.IsTrue(targets.All(t => !t.IsPlayer));
            ClassicAssert.IsTrue(targets.All(t => t.Position == 1 || t.Position == 4));
        }

        // ────────── 双优先条件 ──────────

        [Test]
        public void PriorityAndPriority_Condition2WinsWhenIntersectionIsEmpty()
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
            ClassicAssert.AreEqual(20, targets[0].CurrentHp);
        }

        [Test]
        public void TwoConditions_CanCombineClassAndRank()
        {
            AddEnemy(1, 80, new() { UnitClass.Heavy });
            AddEnemy(2, 20, new() { UnitClass.Infantry });
            var heavyLowestHp = AddEnemy(3, 10, new() { UnitClass.Heavy });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Heavy" },
                Mode1 = ConditionMode.Priority,
                Condition2 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode2 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(heavyLowestHp, targets[0]);
        }

        [Test]
        public void TwoConditions_AreAndRegardlessOfSlotOrder()
        {
            AddEnemy(1, classes: new() { UnitClass.Heavy });
            var backHeavy = AddEnemy(4, classes: new() { UnitClass.Heavy });
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var backPriority = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" };
            var heavyPriority = new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Heavy" };
            var positionInCondition1 = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = backPriority,
                Mode1 = ConditionMode.Priority,
                Condition2 = heavyPriority,
                Mode2 = ConditionMode.Priority
            };
            var positionInCondition2 = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = heavyPriority,
                Mode1 = ConditionMode.Priority,
                Condition2 = backPriority,
                Mode2 = ConditionMode.Priority
            };

            var firstTargets = _selector.SelectTargets(caster, positionInCondition1, skill.Data);
            var secondTargets = _selector.SelectTargets(caster, positionInCondition2, skill.Data);

            ClassicAssert.AreSame(backHeavy, firstTargets[0]);
            ClassicAssert.AreSame(backHeavy, secondTargets[0]);
        }

        [Test]
        public void GroundMelee_BackRowPriority_DoesNotBypassFrontRow()
        {
            var frontEnemy = AddEnemy(1);
            AddEnemy(4);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Melee, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(frontEnemy, targets[0]);
        }

        [Test]
        public void GroundMelee_BackRowOnly_WithFrontRowBlocking_SkipsSkill()
        {
            AddEnemy(1);
            AddEnemy(4);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Melee, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "back" },
                Mode1 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.IsNull(targets);
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

        [Test]
        public void Priority_AttributeRankHighest_UsesCurrentStatAfterEquipment()
        {
            var lowBaseBoosted = TestDataFactory.CreateUnit(str: 50, isPlayer: false);
            lowBaseBoosted.Position = 1;
            lowBaseBoosted.Equipment.Equip(TestDataFactory.CreateEquipment("eq_str_ring", "Str Ring", EquipmentCategory.Accessory,
                new() { { "Str", 80 } }));
            var highBase = TestDataFactory.CreateUnit(str: 100, isPlayer: false);
            highBase.Position = 2;
            _ctx.EnemyUnits.Add(lowBaseBoosted);
            _ctx.EnemyUnits.Add(highBase);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.AttributeRank, Operator = "highest", Value = "Str" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(lowBaseBoosted, targets[0]);
        }

        [Test]
        public void Priority_ApPpHighest_UsesPpWhenConditionValueIsPp()
        {
            var highApLowPp = TestDataFactory.CreateUnit(isPlayer: false);
            highApLowPp.Position = 1;
            highApLowPp.CurrentAp = 4;
            highApLowPp.CurrentPp = 0;
            var lowApHighPp = TestDataFactory.CreateUnit(isPlayer: false);
            lowApHighPp.Position = 2;
            lowApHighPp.CurrentAp = 0;
            lowApHighPp.CurrentPp = 3;
            _ctx.EnemyUnits.Add(highApLowPp);
            _ctx.EnemyUnits.Add(lowApHighPp);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.ApPp, Operator = "highest", Value = "PP" },
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(lowApHighPp, targets[0]);
        }

        [Test]
        public void Priority_HpRatioLowest_UsesHpRatioInsteadOfCurrentHp()
        {
            var higherCurrentLowerRatio = AddEnemy(1, hp: 200);
            higherCurrentLowerRatio.CurrentHp = 60;
            var lowerCurrentHigherRatio = AddEnemy(2, hp: 100);
            lowerCurrentHigherRatio.CurrentHp = 50;
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = StrategyConditionCatalog.BuildCondition("hp-ratio-priority-lowest"),
                Mode1 = ConditionMode.Priority
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(higherCurrentLowerRatio, targets[0]);
        }

        [Test]
        public void Only_RowAtLeastTwo_FiltersTargetsByRowPopulation()
        {
            var frontRowMember = AddEnemy(1);
            var frontRowOther = AddEnemy(2);
            AddEnemy(4);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.AllEnemies);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = StrategyConditionCatalog.BuildCondition("queue-only-column-at-least-2"),
                Mode1 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(caster, strategy, skill.Data);

            CollectionAssert.AreEquivalent(new[] { frontRowMember, frontRowOther }, targets);
        }

        [Test]
        public void Only_AttackAttribute_UsesCurrentSkillWhenSelectingTargets()
        {
            AddEnemy(1);
            AddEnemy(2);
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var rowSkill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.Row);
            var singleSkill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = StrategyConditionCatalog.BuildCondition("attack-row"),
                Mode1 = ConditionMode.Only
            };

            var rowTargets = _selector.SelectTargets(caster, strategy, rowSkill.Data);
            var singleTargets = _selector.SelectTargets(caster, strategy, singleSkill.Data);

            ClassicAssert.IsNotNull(rowTargets);
            ClassicAssert.IsNull(singleTargets);
        }
    }
}
