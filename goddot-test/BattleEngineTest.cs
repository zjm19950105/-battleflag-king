using BattleKing.Core;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class BattleEngineTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void StartBattle_AndStepOneAction_ProduceSameFinalState()
        {
            var autoContext = CreateStateMachineParityContext();
            var stepContext = CreateStateMachineParityContext();
            var autoEngine = new BattleEngine(autoContext) { OnLog = _ => { } };
            var stepEngine = new BattleEngine(stepContext) { OnLog = _ => { } };
            var autoBattleEndEvents = new List<BattleEndEvent>();
            var stepBattleEndEvents = new List<BattleEndEvent>();
            autoEngine.EventBus.Subscribe<BattleEndEvent>(autoBattleEndEvents.Add);
            stepEngine.EventBus.Subscribe<BattleEndEvent>(stepBattleEndEvents.Add);

            var autoResult = autoEngine.StartBattle();
            stepEngine.InitBattle();
            var stepResult = RunStepOneActionToEnd(stepEngine);

            ClassicAssert.AreEqual(autoResult, stepResult);
            CollectionAssert.AreEqual(
                autoContext.AllUnits.Select(u => u.CurrentHp).ToList(),
                stepContext.AllUnits.Select(u => u.CurrentHp).ToList());
            CollectionAssert.AreEqual(
                autoContext.AllUnits.Select(u => u.CurrentAp).ToList(),
                stepContext.AllUnits.Select(u => u.CurrentAp).ToList());
            ClassicAssert.AreEqual(1, autoBattleEndEvents.Count);
            ClassicAssert.AreEqual(1, stepBattleEndEvents.Count);
        }

        [Test]
        public void StepOneAction_WhenBattleEnds_PublishesBattleEndEventOnce()
        {
            var context = CreateContextWithDefeatedEnemy();
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            engine.InitBattle();
            var result = engine.StepOneAction();
            var repeatedResult = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, result);
            ClassicAssert.AreEqual(SingleActionResult.PlayerWin, repeatedResult);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
            ClassicAssert.AreSame(context, battleEndEvents[0].Context);
        }

        [Test]
        public void StepOneAction_WhenActorIsStunned_SkipsOnceAndClearsState()
        {
            var context = CreateStateMachineParityContext();
            var player = context.PlayerUnits.Single();
            var enemy = context.EnemyUnits.Single();
            player.State = UnitState.Stunned;
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(UnitState.Normal, player.State);
            ClassicAssert.AreEqual(2, player.CurrentAp);
            ClassicAssert.AreEqual(200, enemy.CurrentHp);
        }

        [Test]
        public void StartBattle_WhenBattleEnds_PublishesBattleEndEventOnce()
        {
            var context = CreateContextWithDefeatedEnemy();
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = engine.StartBattle();

            ClassicAssert.AreEqual(BattleResult.PlayerWin, result);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
            ClassicAssert.AreSame(context, battleEndEvents[0].Context);
        }

        [Test]
        public void StartBattle_ProducesStructuredLogsForActiveAttackPassiveTriggerAndBattleEnd()
        {
            var repository = CreateStructuredLogRepository();
            var player = new BattleUnit(CreateStructuredLogCharacter("player", "act_log_strike", hp: 100, str: 50, def: 0, spd: 20, ap: 1, pp: 0), repository, true)
            {
                Position = 1
            };
            var enemy = new BattleUnit(CreateStructuredLogCharacter("enemy", null, hp: 100, str: 30, def: 0, spd: 10, ap: 0, pp: 1), repository, false)
            {
                Position = 1
            };
            enemy.EquippedPassiveSkillIds.Add("pas_log_counter");
            player.Strategies.Add(new Strategy { SkillId = "act_log_strike" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { player },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var callbackEntries = new List<BattleLogEntry>();
            var engine = new BattleEngine(context)
            {
                OnLog = _ => { },
                OnBattleLogEntry = callbackEntries.Add
            };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StartBattle();

            ClassicAssert.AreEqual(BattleResult.PlayerWin, result);
            ClassicAssert.AreEqual(3, engine.BattleLogEntries.Count);
            CollectionAssert.AreEqual(engine.BattleLogEntries, callbackEntries);

            var active = engine.BattleLogEntries[0];
            CollectionAssert.Contains(active.Flags, "ActiveAttack");
            ClassicAssert.AreEqual("player", active.ActorId);
            ClassicAssert.AreEqual("act_log_strike", active.SkillId);
            CollectionAssert.AreEqual(new[] { "enemy" }, active.TargetIds);
            ClassicAssert.AreEqual(50, active.Damage);

            var passive = engine.BattleLogEntries[1];
            CollectionAssert.Contains(passive.Flags, "PassiveTrigger");
            CollectionAssert.Contains(passive.Flags, "Counter");
            ClassicAssert.AreEqual("enemy", passive.ActorId);
            ClassicAssert.AreEqual("pas_log_counter", passive.SkillId);
            CollectionAssert.AreEqual(new[] { "player" }, passive.TargetIds);
            ClassicAssert.AreEqual(30, passive.Damage);

            var battleEnd = engine.BattleLogEntries[2];
            CollectionAssert.Contains(battleEnd.Flags, "BattleEnd");
            CollectionAssert.Contains(battleEnd.Flags, "PlayerWin");
            ClassicAssert.AreEqual(0, battleEnd.Damage);
        }

        [Test]
        public void InitBattle_RealPassiveJsonQuickStrike_ExecutesSureHitPreemptiveDamageAndDetailedLog()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var passive = repository.PassiveSkills["pas_quick_strike"];
            var swordsman = new BattleUnit(CreateStructuredLogCharacter("swordsman", null, hp: 100, str: 45, def: 10, spd: 30, ap: 0, pp: 1), repository, true)
            {
                Position = 1
            };
            var enemy = new BattleUnit(CreateStructuredLogCharacter("enemy", null, hp: 100, str: 10, def: 10, spd: 10, ap: 0, pp: 0), repository, false)
            {
                Position = 1
            };
            swordsman.EquippedPassiveSkillIds.Add("pas_quick_strike");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { swordsman },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();

            ClassicAssert.AreEqual("PreemptiveAttack", passive.Effects.Single().EffectType);
            ClassicAssert.AreEqual(0, swordsman.CurrentPp);
            ClassicAssert.Less(enemy.CurrentHp, 100);

            var passiveLog = engine.BattleLogEntries.Single(e => e.SkillId == "pas_quick_strike");
            CollectionAssert.Contains(passiveLog.Flags, "PassiveTrigger");
            CollectionAssert.Contains(passiveLog.Flags, "Preemptive");
            CollectionAssert.Contains(passiveLog.Flags, "Hit");
            CollectionAssert.Contains(passiveLog.Flags, "ForceHit");
            CollectionAssert.Contains(passiveLog.Flags, "SureHit");
            CollectionAssert.AreEqual(new[] { "enemy" }, passiveLog.TargetIds);
            ClassicAssert.AreEqual(100 - enemy.CurrentHp, passiveLog.Damage);
            ClassicAssert.Greater(passiveLog.Damage, 0);
            Assert.That(passiveLog.Text, Does.Contain("actor={{P:swordsman}}"));
            Assert.That(passiveLog.Text, Does.Contain(passive.Name));
            Assert.That(passiveLog.Text, Does.Contain("target={{E:enemy}}"));
            Assert.That(passiveLog.Text, Does.Contain("damage="));
            Assert.That(passiveLog.Text, Does.Contain("hit=True"));
            Assert.That(passiveLog.Text, Does.Contain("blocked=False"));
            Assert.That(passiveLog.Text, Does.Contain("critical=False"));
            Assert.That(passiveLog.Text, Does.Contain("ailments=None"));
            Assert.That(passiveLog.Text, Does.Contain("temporary=actor:None;receiver:None"));
            Assert.That(passiveLog.Text, Does.Contain("tags=SureHit"));
            ClassicAssert.IsFalse(logs.Any(line => line.Trim() == "Preemptive queued"));
        }

        [Test]
        public void StepOneAction_RealPassiveJsonWideCounter_ExpandsRowTargetsAndWritesPassiveLogs()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_touch"] = CreateTouchSkill();
            var attacker = new BattleUnit(CreateStructuredLogCharacter("attacker", "act_touch", hp: 300, str: 20, def: 0, spd: 30, ap: 1, pp: 0), repository, true)
            {
                Position = 1
            };
            var sameRowAlly = new BattleUnit(CreateStructuredLogCharacter("same_row", null, hp: 300, str: 1, def: 0, spd: 1, ap: 0, pp: 0), repository, true)
            {
                Position = 2
            };
            var otherRowAlly = new BattleUnit(CreateStructuredLogCharacter("other_row", null, hp: 300, str: 1, def: 0, spd: 1, ap: 0, pp: 0), repository, true)
            {
                Position = 4
            };
            var counterUser = new BattleUnit(CreateStructuredLogCharacter("counter_user", null, hp: 300, str: 60, def: 0, spd: 10, ap: 0, pp: 1), repository, false)
            {
                Position = 1
            };
            counterUser.EquippedPassiveSkillIds.Add("pas_wide_counter");
            attacker.Strategies.Add(new Strategy { SkillId = "act_touch" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, sameRowAlly, otherRowAlly },
                EnemyUnits = new List<BattleUnit> { counterUser }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();
            engine.StepOneAction();

            ClassicAssert.Less(attacker.CurrentHp, 300);
            ClassicAssert.Less(sameRowAlly.CurrentHp, 300);
            ClassicAssert.AreEqual(300, otherRowAlly.CurrentHp);
            var passiveLogs = engine.BattleLogEntries
                .Where(entry => entry.SkillId == "pas_wide_counter")
                .ToList();
            ClassicAssert.AreEqual(2, passiveLogs.Count);
            CollectionAssert.AreEquivalent(
                new[] { "attacker", "same_row" },
                passiveLogs.SelectMany(entry => entry.TargetIds).ToList());
            Assert.That(passiveLogs, Has.All.Matches<BattleLogEntry>(entry =>
                entry.ActorId == "counter_user"
                && entry.Flags.Contains("PassiveTrigger")
                && entry.Flags.Contains("Counter")));
        }

        [Test]
        public void StepOneAction_RealPassiveJsonBlockSeal_IgnoresDefenseOnlyAgainstHeavyTargets()
        {
            var heavyLog = RunBlockSealCounterScenario(new List<UnitClass> { UnitClass.Heavy });
            var infantryLog = RunBlockSealCounterScenario(new List<UnitClass> { UnitClass.Infantry });

            ClassicAssert.AreEqual(150, heavyLog.Damage);
            ClassicAssert.AreEqual(30, infantryLog.Damage);
            CollectionAssert.Contains(heavyLog.Flags, "IgnoreDefense=1");
            CollectionAssert.DoesNotContain(infantryLog.Flags, "IgnoreDefense=1");
        }

        [Test]
        public void StepOneAction_SameSpeedActors_RandomizesFirstActorAcrossBattles()
        {
            int playerFirst = 0;
            int enemyFirst = 0;

            for (int i = 0; i < 120; i++)
            {
                var repository = CreateStructuredLogRepository();
                var player = new BattleUnit(CreateStructuredLogCharacter("player", "act_log_strike", hp: 1000, str: 1, def: 0, spd: 20, ap: 1, pp: 0), repository, true)
                {
                    Position = 1
                };
                var enemy = new BattleUnit(CreateStructuredLogCharacter("enemy", "act_log_strike", hp: 1000, str: 1, def: 0, spd: 20, ap: 1, pp: 0), repository, false)
                {
                    Position = 1
                };
                player.Strategies.Add(new Strategy { SkillId = "act_log_strike" });
                enemy.Strategies.Add(new Strategy { SkillId = "act_log_strike" });
                var context = new BattleContext(repository)
                {
                    PlayerUnits = new List<BattleUnit> { player },
                    EnemyUnits = new List<BattleUnit> { enemy }
                };
                var engine = new BattleEngine(context) { OnLog = _ => { } };

                engine.InitBattle();
                engine.StepOneAction();

                if (player.CurrentAp == 0)
                    playerFirst++;
                if (enemy.CurrentAp == 0)
                    enemyFirst++;
            }

            ClassicAssert.Greater(playerFirst, 0);
            ClassicAssert.Greater(enemyFirst, 0);
        }

        [Test]
        public void StepOneAction_WhenApExhausted_InitialResultUsesHpRatio()
        {
            var context = CreateApExhaustionContext();
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = RunStepOneActionToEnd(engine);

            ClassicAssert.AreEqual(BattleResult.PlayerWin, result);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
        }

        [Test]
        public void StepOneAction_WhenBattleEndPassiveHealsEnemy_RecalculatesFinalResultFromPostBattleEndHp()
        {
            var context = CreateApExhaustionContext(enemyPassiveId: "pas_end_heal_enemy");
            var enemy = context.EnemyUnits.Single();
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, context.GameData, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = RunStepOneActionToEnd(engine);

            ClassicAssert.AreEqual(BattleResult.EnemyWin, result);
            ClassicAssert.AreEqual(90, enemy.CurrentHp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
        }

        [Test]
        public void StepOneAction_WhenBattleEndPassiveQueuesDamage_RecalculatesFinalResultAfterPendingActions()
        {
            var context = CreateApExhaustionContext(enemyPassiveId: "pas_end_attack_player");
            var player = context.PlayerUnits.Single();
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, context.GameData, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();
            var battleEndEvents = new List<BattleEndEvent>();
            engine.EventBus.Subscribe<BattleEndEvent>(battleEndEvents.Add);

            var result = RunStepOneActionToEnd(engine);
            var repeatedResult = engine.StepOneAction();

            ClassicAssert.AreEqual(BattleResult.EnemyWin, result);
            ClassicAssert.AreEqual(SingleActionResult.EnemyWin, repeatedResult);
            ClassicAssert.AreEqual(30, player.CurrentHp);
            ClassicAssert.AreEqual(1, battleEndEvents.Count);
            ClassicAssert.AreEqual(BattleResult.PlayerWin, battleEndEvents[0].Result);
            ClassicAssert.AreEqual(1, engine.BattleLogEntries.Count(e => e.Flags.Contains("BattleEnd")));
        }

        private static BattleContext CreateContextWithDefeatedEnemy()
        {
            var repository = new GameDataRepository();
            var player = TestDataFactory.CreateUnit(hp: 100, spd: 10, isPlayer: true);
            var enemy = TestDataFactory.CreateUnit(hp: 100, spd: 1, isPlayer: false);
            enemy.CurrentHp = 0;

            return new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { player },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
        }

        private static GameDataRepository CreateStructuredLogRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_log_strike"] = new ActiveSkillData
            {
                Id = "act_log_strike",
                Name = "Log Strike",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 1000,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            repository.PassiveSkills["pas_log_counter"] = new PassiveSkillData
            {
                Id = "pas_log_counter",
                Name = "Log Counter",
                PpCost = 1,
                TriggerTiming = PassiveTriggerTiming.OnBeingHit,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "CounterAttack",
                        Parameters = new Dictionary<string, object>
                        {
                            { "power", 100 },
                            { "hitRate", 1000 }
                        }
                    }
                }
            };
            return repository;
        }

        private static BattleContext CreateApExhaustionContext(string? enemyPassiveId = null)
        {
            var repository = CreateBattleEndResultRepository();
            var player = new BattleUnit(CreateBattleEndCharacter("player", "act_noop", hp: 100, str: 20, def: 0, spd: 20, ap: 0, pp: 0), repository, true)
            {
                Position = 1,
                CurrentHp = 80
            };
            var enemy = new BattleUnit(CreateBattleEndCharacter("enemy", "act_noop", hp: 100, str: 50, def: 0, spd: 10, ap: 0, pp: 1), repository, false)
            {
                Position = 1,
                CurrentHp = 60
            };

            player.Strategies.Add(new Strategy { SkillId = "act_noop" });
            enemy.Strategies.Add(new Strategy { SkillId = "act_noop" });
            if (enemyPassiveId != null)
                enemy.EquippedPassiveSkillIds.Add(enemyPassiveId);

            return new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { player },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
        }

        private static GameDataRepository CreateBattleEndResultRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_noop"] = new ActiveSkillData
            {
                Id = "act_noop",
                Name = "Noop Strike",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 1,
                HitRate = 1000,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            repository.PassiveSkills["pas_end_heal_enemy"] = new PassiveSkillData
            {
                Id = "pas_end_heal_enemy",
                Name = "Battle End Heal Enemy",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.BattleEnd,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "RecoverHp",
                        Parameters = new Dictionary<string, object>
                        {
                            { "target", "Self" },
                            { "amount", 30 }
                        }
                    }
                }
            };
            repository.PassiveSkills["pas_end_attack_player"] = new PassiveSkillData
            {
                Id = "pas_end_attack_player",
                Name = "Battle End Attack Player",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.BattleEnd,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "PreemptiveAttack",
                        Parameters = new Dictionary<string, object>
                        {
                            { "target", "AllEnemies" },
                            { "power", 100 },
                            { "hitRate", 1000 },
                            { "damageType", "Physical" },
                            { "attackType", "Melee" }
                        }
                    }
                }
            };
            return repository;
        }

        private static BattleContext CreateStateMachineParityContext()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_guarded_strike"] = new ActiveSkillData
            {
                Id = "act_guarded_strike",
                Name = "Guarded Strike",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "AddBuff",
                        Parameters = new()
                        {
                            { "target", "Self" },
                            { "stat", "Def" },
                            { "amount", 100 },
                            { "turns", 1 },
                            { "oneTime", true }
                        }
                    },
                    new()
                    {
                        EffectType = "ModifyDamageCalc",
                        Parameters = new()
                        {
                            { "ForceHit", true },
                            { "CannotBeBlocked", true }
                        }
                    }
                }
            };

            var player = new BattleUnit(CreateCharacter("player", hp: 200, str: 50, def: 0, spd: 20, ap: 2), repository, true)
            {
                Position = 1
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", hp: 200, str: 50, def: 0, spd: 10, ap: 2), repository, false)
            {
                Position = 1
            };
            player.Strategies.Add(new Strategy { SkillId = "act_guarded_strike" });
            enemy.Strategies.Add(new Strategy { SkillId = "act_guarded_strike" });

            return new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { player },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
        }

        private static BattleResult RunStepOneActionToEnd(BattleEngine engine)
        {
            while (true)
            {
                var result = engine.StepOneAction();
                if (result == SingleActionResult.PlayerWin)
                    return BattleResult.PlayerWin;
                if (result == SingleActionResult.EnemyWin)
                    return BattleResult.EnemyWin;
                if (result == SingleActionResult.Draw)
                    return BattleResult.Draw;
            }
        }

        private static CharacterData CreateCharacter(string id, int hp, int str, int def, int spd, int ap)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = new List<string> { "act_guarded_strike" },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", 100 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", 0 }
                }
            };
        }

        private static BattleLogEntry RunBlockSealCounterScenario(List<UnitClass> attackerClasses)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_touch"] = CreateTouchSkill();
            var attacker = new BattleUnit(CreateStructuredLogCharacter("attacker", "act_touch", hp: 300, str: 10, def: 80, spd: 30, ap: 1, pp: 0, attackerClasses), repository, true)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreateStructuredLogCharacter("block_user", null, hp: 300, str: 100, def: 0, spd: 10, ap: 0, pp: 1), repository, false)
            {
                Position = 1
            };
            defender.EquippedPassiveSkillIds.Add("pas_block_seal");
            attacker.Strategies.Add(new Strategy { SkillId = "act_touch" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();
            engine.StepOneAction();

            return engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_block_seal");
        }

        private static ActiveSkillData CreateTouchSkill()
        {
            return new ActiveSkillData
            {
                Id = "act_touch",
                Name = "Touch",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 1000,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
        }

        private static CharacterData CreateStructuredLogCharacter(
            string id,
            string? skillId,
            int hp,
            int str,
            int def,
            int spd,
            int ap,
            int pp,
            List<UnitClass>? classes = null)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = classes ?? new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = skillId == null ? new List<string>() : new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", 1000 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", pp }
                }
            };
        }

        private static CharacterData CreateBattleEndCharacter(string id, string skillId, int hp, int str, int def, int spd, int ap, int pp)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", 1000 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", pp }
                }
            };
        }
    }
}
