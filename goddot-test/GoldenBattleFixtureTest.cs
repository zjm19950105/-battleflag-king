using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class GoldenBattleFixtureTest
    {
        [Test]
        public void FrontRowBlocking_GroundMeleeCannotSelectBackRowWhileFrontLives()
        {
            var fixture = GoldenBattleFixture.Create();
            var front = fixture.Enemy("enemy_front", position: 1);
            var back = fixture.Enemy("enemy_back", position: 4);
            var context = fixture.Context(players: new[] { fixture.Melee }, enemies: new[] { front, back });
            var selector = new TargetSelector(context);

            var targets = selector.SelectTargets(
                fixture.Melee,
                new Strategy { SkillId = GoldenBattleFixture.MeleeStrikeId },
                fixture.Repository.ActiveSkills[GoldenBattleFixture.MeleeStrikeId]);

            CollectionAssert.AreEqual(new[] { front }, targets);
        }

        [Test]
        public void RangedAttack_CanSelectBackRowThroughLivingFrontRow()
        {
            var fixture = GoldenBattleFixture.Create();
            var front = fixture.Enemy("enemy_front", position: 1);
            var back = fixture.Enemy("enemy_back", position: 4);
            back.CurrentHp = 40;
            var context = fixture.Context(players: new[] { fixture.Archer }, enemies: new[] { front, back });
            var selector = new TargetSelector(context);

            var targets = selector.SelectTargets(
                fixture.Archer,
                new Strategy
                {
                    SkillId = GoldenBattleFixture.ArrowShotId,
                    Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                    Mode1 = ConditionMode.Priority
                },
                fixture.Repository.ActiveSkills[GoldenBattleFixture.ArrowShotId]);

            CollectionAssert.AreEqual(new[] { back }, targets);
        }

        [Test]
        public void FlyingDefense_GroundedMeleeHitRateCanBeHalvedToZero()
        {
            var fixture = GoldenBattleFixture.Create();
            var attacker = TestDataFactory.CreateUnit(hit: 0, crit: 0, block: 0, classes: new() { UnitClass.Infantry });
            var flyingDefender = TestDataFactory.CreateUnit(eva: 0, block: 0, classes: new() { UnitClass.Flying });
            var skill = TestDataFactory.CreateSkill(
                type: SkillType.Physical,
                attackType: AttackType.Melee,
                hitRate: 1);
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = flyingDefender,
                Skill = skill
            };

            var result = new DamageCalculator().Calculate(calc);

            ClassicAssert.IsFalse(result.IsHit);
            ClassicAssert.AreEqual(0, result.TotalDamage);
        }

        [Test]
        public void RowAndColumn_TargetShapesStayOnAnchorSide()
        {
            var fixture = GoldenBattleFixture.Create();
            var enemyFrontLeft = fixture.Enemy("enemy_front_left", position: 1);
            var enemyFrontRight = fixture.Enemy("enemy_front_right", position: 3);
            var enemyBackLeft = fixture.Enemy("enemy_back_left", position: 4);
            var allyFrontLeft = fixture.Heavy;
            allyFrontLeft.Position = 1;
            var context = fixture.Context(
                players: new[] { fixture.Mage, allyFrontLeft },
                enemies: new[] { enemyFrontLeft, enemyFrontRight, enemyBackLeft });
            var selector = new TargetSelector(context);

            var rowTargets = selector.SelectTargets(
                fixture.Mage,
                new Strategy { SkillId = GoldenBattleFixture.RowSpellId },
                fixture.Repository.ActiveSkills[GoldenBattleFixture.RowSpellId]);
            var columnTargets = selector.SelectTargets(
                fixture.Mage,
                new Strategy { SkillId = GoldenBattleFixture.ColumnSpellId },
                fixture.Repository.ActiveSkills[GoldenBattleFixture.ColumnSpellId]);

            CollectionAssert.AreEqual(new[] { enemyFrontLeft, enemyFrontRight }, rowTargets);
            CollectionAssert.AreEqual(new[] { enemyFrontLeft, enemyBackLeft }, columnTargets);
            CollectionAssert.DoesNotContain(rowTargets, allyFrontLeft);
            CollectionAssert.DoesNotContain(columnTargets, allyFrontLeft);
        }

        [Test]
        public void HealingSkill_HealsSelectedAllyAndDoesNotHealEnemy()
        {
            var fixture = GoldenBattleFixture.Create();
            fixture.Heavy.CurrentHp = 120;
            var enemy = fixture.Enemy("enemy", position: 1, hp: 300);
            enemy.CurrentHp = 50;
            var context = fixture.Context(players: new[] { fixture.Healer, fixture.Heavy }, enemies: new[] { enemy });
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(200, fixture.Heavy.CurrentHp);
            ClassicAssert.AreEqual(50, enemy.CurrentHp);
            ClassicAssert.AreEqual(2, fixture.Healer.CurrentAp);
        }

        [Test]
        public void CounterPassive_QueuesCounterAttackAfterBeingHit()
        {
            var fixture = GoldenBattleFixture.Create();
            var attacker = fixture.Melee;
            var defender = fixture.CounterGuard;
            defender.EquippedPassiveSkillIds.Add(GoldenBattleFixture.CounterPassiveId);
            var context = fixture.Context(players: new[] { attacker }, enemies: new[] { defender });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var attackerHpBefore = attacker.CurrentHp;

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.Less(attacker.CurrentHp, attackerHpBefore);
            ClassicAssert.Less(defender.CurrentHp, defender.Data.BaseStats["HP"]);
        }

        [Test]
        public void CoverPassive_RedirectsDamageToCoverUnit()
        {
            var fixture = GoldenBattleFixture.Create();
            var attacker = fixture.Melee;
            var covered = fixture.Enemy("covered", position: 1, hp: 220);
            var cover = fixture.CoverGuard;
            cover.EquippedPassiveSkillIds.Add(GoldenBattleFixture.CoverPassiveId);
            var context = fixture.Context(players: new[] { attacker }, enemies: new[] { covered, cover });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var afterHitEvents = new List<AfterHitEvent>();
            engine.EventBus.Subscribe<AfterHitEvent>(afterHitEvents.Add);

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(220, covered.CurrentHp);
            ClassicAssert.Less(cover.CurrentHp, cover.Data.BaseStats["HP"]);
            ClassicAssert.AreSame(cover, afterHitEvents.Single().Defender);
        }

        [Test]
        public void BattleEndPassive_RunsWhenStepOneActionReachesTerminalResult()
        {
            var fixture = GoldenBattleFixture.Create();
            var survivor = fixture.Melee;
            survivor.CurrentAp = 0;
            survivor.EquippedPassiveSkillIds.Add(GoldenBattleFixture.BattleEndPassiveId);
            var defeatedEnemy = fixture.Enemy("defeated", position: 1);
            defeatedEnemy.CurrentHp = 0;
            var context = fixture.Context(players: new[] { survivor }, enemies: new[] { defeatedEnemy });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, result);
            ClassicAssert.AreEqual(1, survivor.CurrentAp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
        }

        [Test]
        public void BattleEndAttack_KnocksDownTargetAndRejudgesFinalResult()
        {
            var fixture = GoldenBattleFixture.Create();
            var finisher = fixture.BattleEndAttacker;
            finisher.CurrentHp = 40;
            finisher.CurrentAp = 0;
            finisher.EquippedPassiveSkillIds.Add(GoldenBattleFixture.BattleEndAttackPassiveId);
            var enemy = fixture.Enemy("battle_end_attack_target", position: 1, hp: 100, def: 0);
            enemy.CurrentHp = 60;
            enemy.CurrentAp = 0;
            var context = fixture.Context(players: new[] { finisher }, enemies: new[] { enemy });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = RunStepOneActionToEnd(engine);
            var repeatedResult = engine.StepOneAction();

            ClassicAssert.AreEqual(BattleResult.PlayerWin, result);
            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, repeatedResult);
            ClassicAssert.AreEqual(0, enemy.CurrentHp);
            ClassicAssert.AreEqual(40, finisher.CurrentHp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.EnemyWin, battleEndEvents[0].Result);
            var passiveLog = engine.BattleLogEntries.Single(e => e.SkillId == GoldenBattleFixture.BattleEndAttackPassiveId);
            CollectionAssert.Contains(passiveLog.Flags, "PassiveTrigger");
            CollectionAssert.Contains(passiveLog.Flags, "BattleEnd");
            CollectionAssert.Contains(passiveLog.Flags, "Knockdown");
        }

        [Test]
        public void BattleEndHeal_ChangesHpRatioWinner()
        {
            var fixture = GoldenBattleFixture.Create();
            var healer = fixture.BattleEndHealer;
            healer.CurrentHp = 40;
            healer.CurrentAp = 0;
            healer.EquippedPassiveSkillIds.Add(GoldenBattleFixture.BattleEndHealPassiveId);
            var enemy = fixture.Enemy("battle_end_heal_enemy", position: 1, hp: 100, def: 0);
            enemy.CurrentHp = 60;
            enemy.CurrentAp = 0;
            var context = fixture.Context(players: new[] { healer }, enemies: new[] { enemy });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = RunStepOneActionToEnd(engine);

            ClassicAssert.AreEqual(BattleResult.PlayerWin, result);
            ClassicAssert.AreEqual(70, healer.CurrentHp);
            ClassicAssert.AreEqual(60, enemy.CurrentHp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.EnemyWin, battleEndEvents[0].Result);
        }

        [Test]
        public void BattleEndPassive_TriggersOnlyOnceAcrossRepeatedStepCalls()
        {
            var fixture = GoldenBattleFixture.Create();
            var survivor = fixture.Melee;
            survivor.CurrentAp = 0;
            survivor.EquippedPassiveSkillIds.Add(GoldenBattleFixture.BattleEndApPassiveId);
            var defeatedEnemy = fixture.Enemy("defeated_once", position: 1);
            defeatedEnemy.CurrentHp = 0;
            var context = fixture.Context(players: new[] { survivor }, enemies: new[] { defeatedEnemy });
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, fixture.Repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            engine.InitBattle();
            var result = engine.StepOneAction();
            var repeatedResult = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, result);
            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, repeatedResult);
            ClassicAssert.AreEqual(1, survivor.CurrentAp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(1, engine.BattleLogEntries.Count(e => e.Flags.Contains("BattleEnd")));
        }

        private static BattleResult RunStepOneActionToEnd(BattleEngine engine)
        {
            for (int i = 0; i < 20; i++)
            {
                var result = engine.StepOneAction();
                if (result == SingleActionResult.PlayerWin)
                    return BattleResult.PlayerWin;
                if (result == SingleActionResult.EnemyWin)
                    return BattleResult.EnemyWin;
                if (result == SingleActionResult.Draw)
                    return BattleResult.Draw;
            }

            Assert.Fail("Battle did not end within the golden fixture step limit.");
            return BattleResult.Draw;
        }
    }
}
