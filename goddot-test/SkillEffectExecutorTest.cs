using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class SkillEffectExecutorTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void ModifyDamageCalc_主动效果_修改当前伤害上下文()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var attacker = TestDataFactory.CreateUnit();
            var defender = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ModifyDamageCalc",
                    Parameters = new()
                    {
                        { "ForceHit", true },
                        { "ForceBlock", true },
                        { "HitCount", 9 },
                        { "CannotBeBlocked", true },
                        { "DamageMultiplier", 1.5 },
                        { "NullifyPhysicalDamage", true },
                        { "IgnoreDefenseRatio", 0.5 }
                    }
                }
            });
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            executor.ExecuteCalculationEffects(
                context, attacker, new List<BattleUnit> { defender }, skill.Data.Effects,
                skill.Data.Id, calc, new SkillEffectExecutionState());

            ClassicAssert.IsTrue(calc.ForceHit);
            ClassicAssert.AreEqual(true, calc.ForceBlock);
            ClassicAssert.IsTrue(calc.CannotBeBlocked);
            ClassicAssert.IsTrue(calc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(9, calc.HitCount);
            ClassicAssert.AreEqual(0.5f, calc.IgnoreDefenseRatio);
            ClassicAssert.AreEqual(1.5f, calc.DamageMultiplier);
        }

        [Test]
        public void AddBuff_主动效果_按JSON参数修改目标面板()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit(str: 100);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AddBuff",
                    Parameters = new()
                    {
                        { "target", "Self" },
                        { "stat", "Str" },
                        { "ratio", 0.2 },
                        { "turns", -1 }
                    }
                }
            });

            executor.ExecuteActionEffects(context, caster, Array.Empty<BattleUnit>(), skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(120, caster.GetCurrentStat("Str"));
        }

        [Test]
        public void CounterEffects_主动效果_一次消耗并作用到同次技能所有目标()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            caster.ModifyCounter("Sprite", 2);
            var first = TestDataFactory.CreateUnit(isPlayer: false);
            var second = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ConsumeCounter",
                    Parameters = new()
                    {
                        { "key", "Sprite" },
                        { "powerPerCounter", 30 }
                    }
                }
            });
            var state = new SkillEffectExecutionState();
            var firstCalc = TestDataFactory.CreateCalc(caster, first, skill);
            var secondCalc = TestDataFactory.CreateCalc(caster, second, skill);

            executor.ExecuteCalculationEffects(context, caster, new List<BattleUnit> { first }, skill.Data.Effects, skill.Data.Id, firstCalc, state);
            executor.ExecuteCalculationEffects(context, caster, new List<BattleUnit> { second }, skill.Data.Effects, skill.Data.Id, secondCalc, state);

            ClassicAssert.AreEqual(0, caster.GetCounter("Sprite"));
            ClassicAssert.AreEqual(60, firstCalc.CounterPowerBonus);
            ClassicAssert.AreEqual(60, secondCalc.CounterPowerBonus);
        }

        [Test]
        public void ResourceAndHealEffects_ApplyApPpDamageAndHealRatio()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var target = TestDataFactory.CreateUnit(hp: 200, ap: 3, pp: 2, isPlayer: false);
            target.CurrentHp = 80;
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ApDamage",
                    Parameters = new() { { "target", "Target" }, { "amount", 2 } }
                },
                new SkillEffectData
                {
                    EffectType = "PpDamage",
                    Parameters = new() { { "target", "Target" }, { "amount", 1 } }
                },
                new SkillEffectData
                {
                    EffectType = "HealRatio",
                    Parameters = new() { { "target", "Target" }, { "ratio", 0.25 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(1, target.CurrentAp);
            ClassicAssert.AreEqual(1, target.CurrentPp);
            ClassicAssert.AreEqual(130, target.CurrentHp);
        }

        [Test]
        public void AddBuff_TargetAllAllies_BuffsOnlyCasterSideAliveUnits()
        {
            var executor = new SkillEffectExecutor();
            var caster = TestDataFactory.CreateUnit(str: 100, isPlayer: true);
            var ally = TestDataFactory.CreateUnit(str: 80, isPlayer: true);
            var defeatedAlly = TestDataFactory.CreateUnit(str: 60, isPlayer: true);
            defeatedAlly.CurrentHp = 0;
            var enemy = TestDataFactory.CreateUnit(str: 100, isPlayer: false);
            var context = new BattleContext(new GameDataRepository())
            {
                PlayerUnits = new List<BattleUnit> { caster, ally, defeatedAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AddBuff",
                    Parameters = new()
                    {
                        { "target", "AllAllies" },
                        { "stat", "Str" },
                        { "ratio", 0.25 },
                        { "turns", -1 }
                    }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { enemy }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(125, caster.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(100, ally.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(60, defeatedAlly.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(100, enemy.GetCurrentStat("Str"));
        }

        [Test]
        public void ApDamage_TargetAllEnemies_DamagesOnlyEnemySideAliveUnits()
        {
            var executor = new SkillEffectExecutor();
            var caster = TestDataFactory.CreateUnit(ap: 3, isPlayer: true);
            var ally = TestDataFactory.CreateUnit(ap: 3, isPlayer: true);
            var firstEnemy = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            var secondEnemy = TestDataFactory.CreateUnit(ap: 2, isPlayer: false);
            var defeatedEnemy = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            defeatedEnemy.CurrentHp = 0;
            var context = new BattleContext(new GameDataRepository())
            {
                PlayerUnits = new List<BattleUnit> { caster, ally },
                EnemyUnits = new List<BattleUnit> { firstEnemy, secondEnemy, defeatedEnemy }
            };
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ApDamage",
                    Parameters = new() { { "target", "AllEnemies" }, { "amount", 1 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { firstEnemy }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(3, caster.CurrentAp);
            ClassicAssert.AreEqual(3, ally.CurrentAp);
            ClassicAssert.AreEqual(2, firstEnemy.CurrentAp);
            ClassicAssert.AreEqual(1, secondEnemy.CurrentAp);
            ClassicAssert.AreEqual(3, defeatedEnemy.CurrentAp);
        }

        [Test]
        public void ApDamage_TargetTarget_DamagesOnlyExplicitTarget()
        {
            var executor = new SkillEffectExecutor();
            var caster = TestDataFactory.CreateUnit(ap: 3, isPlayer: true);
            var firstEnemy = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            var secondEnemy = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            var context = new BattleContext(new GameDataRepository())
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { firstEnemy, secondEnemy }
            };
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ApDamage",
                    Parameters = new() { { "target", "Target" }, { "amount", 2 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { firstEnemy }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(3, caster.CurrentAp);
            ClassicAssert.AreEqual(1, firstEnemy.CurrentAp);
            ClassicAssert.AreEqual(3, secondEnemy.CurrentAp);
        }

        [Test]
        public void HealRatio_TargetLowestHpAlly_HealsOnlyLowestCurrentHpAlly()
        {
            var executor = new SkillEffectExecutor();
            var caster = TestDataFactory.CreateUnit(hp: 200, isPlayer: true);
            caster.CurrentHp = 120;
            var lowestAlly = TestDataFactory.CreateUnit(hp: 200, isPlayer: true);
            lowestAlly.CurrentHp = 40;
            var otherAlly = TestDataFactory.CreateUnit(hp: 200, isPlayer: true);
            otherAlly.CurrentHp = 80;
            var enemy = TestDataFactory.CreateUnit(hp: 200, isPlayer: false);
            enemy.CurrentHp = 10;
            var context = new BattleContext(new GameDataRepository())
            {
                PlayerUnits = new List<BattleUnit> { caster, lowestAlly, otherAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "HealRatio",
                    Parameters = new() { { "target", "LowestHpAlly" }, { "ratio", 0.25 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { enemy }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(120, caster.CurrentHp);
            ClassicAssert.AreEqual(90, lowestAlly.CurrentHp);
            ClassicAssert.AreEqual(80, otherAlly.CurrentHp);
            ClassicAssert.AreEqual(10, enemy.CurrentHp);
        }

        [Test]
        public void UnknownTarget_FallsBackToExplicitTargets()
        {
            var executor = new SkillEffectExecutor();
            var caster = TestDataFactory.CreateUnit(ap: 3, isPlayer: true);
            var explicitTarget = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            var otherEnemy = TestDataFactory.CreateUnit(ap: 3, isPlayer: false);
            var context = new BattleContext(new GameDataRepository())
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { explicitTarget, otherEnemy }
            };
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ApDamage",
                    Parameters = new() { { "target", "NotARealTarget" }, { "amount", 1 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { explicitTarget }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(3, caster.CurrentAp);
            ClassicAssert.AreEqual(2, explicitTarget.CurrentAp);
            ClassicAssert.AreEqual(3, otherEnemy.CurrentAp);
        }

        [Test]
        public void GrantSkill_AddsTemporarySkillToAvailablePool()
        {
            var executor = new SkillEffectExecutor();
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var context = new BattleContext(repository);
            var caster = new BattleUnit(CreateCharacter("caster", null, hp: 100, str: 10, def: 10, spd: 10), repository, true);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "GrantSkill",
                    Parameters = new()
                    {
                        { "target", "Self" },
                        { "skillId", "act_meteor_slash" },
                        { "skillType", "Active" }
                    }
                }
            });

            executor.ExecuteActionEffects(context, caster, Array.Empty<BattleUnit>(), skill.Data.Effects, skill.Data.Id);

            CollectionAssert.Contains(caster.GetAvailableActiveSkillIds(), "act_meteor_slash");
        }

        [Test]
        public void RemoveBuff_CanDispelBuffAndCleanseDebuff()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var target = TestDataFactory.CreateUnit(isPlayer: false);
            target.Buffs.Add(new Buff { SkillId = "buff", TargetStat = "Str", Ratio = 0.2f, RemainingTurns = -1 });
            target.Buffs.Add(new Buff { SkillId = "debuff", TargetStat = "Def", Ratio = -0.3f, RemainingTurns = -1 });
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "RemoveBuff",
                    Parameters = new() { { "target", "Target" }, { "kind", "Buff" } }
                },
                new SkillEffectData
                {
                    EffectType = "CleanseDebuff",
                    Parameters = new() { { "target", "Target" } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(0, target.Buffs.Count);
        }

        [Test]
        public void AddDebuff_ConsumesDebuffNullifyTemporalMark()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var target = TestDataFactory.CreateUnit(def: 100, isPlayer: false);
            target.AddTemporal("DebuffNullify", 1, -1, "pas_spirit_guard");
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AddDebuff",
                    Parameters = new() { { "target", "Target" }, { "stat", "Def" }, { "ratio", 0.3 }, { "turns", -1 } }
                }
            });

            var logs = executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(100, target.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(0, target.Buffs.Count);
            ClassicAssert.IsFalse(target.TemporalStates.Any(s => s.Key == "DebuffNullify"));
            CollectionAssert.Contains(logs, $"{target.Data.Name}.DebuffNullified");
        }

        [Test]
        public void AddDebuffAndModifyCounter_ApplyToSelectedTarget()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var target = TestDataFactory.CreateUnit(def: 100, isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AddDebuff",
                    Parameters = new() { { "target", "Target" }, { "stat", "Def" }, { "ratio", 0.3 }, { "turns", -1 } }
                },
                new SkillEffectData
                {
                    EffectType = "ModifyCounter",
                    Parameters = new() { { "target", "Target" }, { "key", "Rage" }, { "delta", 2 } }
                }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(70, target.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(2, target.GetCounter("Rage"));
            ClassicAssert.AreEqual(0, caster.GetCounter("Rage"));
        }

        [Test]
        public void TemporalAndCoverEffects_AddMarkAndRedirectCalculation()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var defender = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "TemporalMark",
                    Parameters = new()
                    {
                        { "target", "Self" },
                        { "key", "DeathResist" },
                        { "count", 1 }
                    }
                },
                new SkillEffectData
                {
                    EffectType = "CoverAlly",
                    Parameters = new()
                }
            });
            var calc = TestDataFactory.CreateCalc(caster, defender, skill);

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { defender }, skill.Data.Effects, skill.Data.Id);
            executor.ExecuteCalculationEffects(context, caster, new List<BattleUnit> { defender }, skill.Data.Effects, skill.Data.Id, calc, new SkillEffectExecutionState());

            ClassicAssert.IsTrue(caster.TemporalStates.Any(s => s.Key == "DeathResist"));
            ClassicAssert.AreSame(caster, calc.CoverTarget);
        }

        [Test]
        public void PendingActionEffects_QueueCounterPursuitAndPreemptiveActions()
        {
            var queued = new List<PendingAction>();
            var executor = new SkillEffectExecutor(queued.Add);
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var target = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData { EffectType = "CounterAttack", Parameters = new() { { "power", 70 } } },
                new SkillEffectData { EffectType = "PursuitAttack", Parameters = new() { { "power", 80 } } },
                new SkillEffectData { EffectType = "PreemptiveAttack", Parameters = new() { { "power", 90 } } }
            });

            executor.ExecuteActionEffects(context, caster, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(3, queued.Count);
            ClassicAssert.AreEqual(PendingActionType.Counter, queued[0].Type);
            ClassicAssert.AreEqual(PendingActionType.Pursuit, queued[1].Type);
            ClassicAssert.AreEqual(PendingActionType.Preemptive, queued[2].Type);
            ClassicAssert.AreEqual(90, queued[2].Power);
        }

        [Test]
        public void RealPassiveJson_QuickStrike_QueuesSingleSureHitPreemptiveAttackAtBattleStart()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var queued = new List<PendingAction>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { }, queued.Add);
            processor.SubscribeAll();
            var swordsman = new BattleUnit(CreateCharacter("swordsman", null, hp: 100, str: 45, def: 10, spd: 30), repository, true);
            swordsman.CurrentPp = 1;
            swordsman.EquippedPassiveSkillIds.Add("pas_quick_strike");
            var firstEnemy = new BattleUnit(CreateCharacter("enemy1", null, hp: 100, str: 10, def: 10, spd: 10), repository, false);
            var secondEnemy = new BattleUnit(CreateCharacter("enemy2", null, hp: 100, str: 10, def: 10, spd: 9), repository, false);
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { swordsman },
                EnemyUnits = new List<BattleUnit> { firstEnemy, secondEnemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            ClassicAssert.AreEqual(1, queued.Count);
            var action = queued.Single();
            ClassicAssert.AreEqual(PendingActionType.Preemptive, action.Type);
            ClassicAssert.AreSame(swordsman, action.Actor);
            ClassicAssert.AreEqual(150, action.Power);
            ClassicAssert.AreEqual(100, action.HitRate);
            ClassicAssert.AreEqual(SkillType.Physical, action.DamageType);
            ClassicAssert.AreEqual(AttackType.Melee, action.AttackType);
            CollectionAssert.AreEqual(new[] { firstEnemy }, action.Targets);
            CollectionAssert.Contains(action.Tags, "SureHit");
            ClassicAssert.AreEqual(0, swordsman.CurrentPp);
        }

        [Test]
        public void BattleEndPasRampage_StructuredEffect_QueuesBattleEndAttackAndLogsDamage()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var player = new BattleUnit(CreateCharacter("player", null, hp: 200, str: 10, def: 40, spd: 20, hit: 1000), repository, true)
            {
                Position = 1,
                CurrentAp = 0
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", null, hp: 200, str: 100, def: 0, spd: 10, hit: 1000), repository, false)
            {
                Position = 1,
                CurrentAp = 0,
                CurrentPp = 2
            };
            enemy.EquippedPassiveSkillIds.Add("pas_rampage");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { player },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StartBattle();

            ClassicAssert.AreEqual(BattleResult.EnemyWin, result);
            ClassicAssert.Less(player.CurrentHp, 200);
            var passiveLog = engine.BattleLogEntries.Single(e => e.SkillId == "pas_rampage");
            CollectionAssert.Contains(passiveLog.Flags, "PassiveTrigger");
            CollectionAssert.Contains(passiveLog.Flags, "BattleEnd");
            CollectionAssert.AreEqual(new[] { "player" }, passiveLog.TargetIds);
            ClassicAssert.Greater(passiveLog.Damage, 0);
            CollectionAssert.Contains(engine.BattleLogEntries.Last().Flags, "BattleEnd");
        }

        [Test]
        public void BattleEndPasGiveAp_StructuredEffect_RecoversLowestHpAllyWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var passive = repository.PassiveSkills["pas_give_ap"];
            var medic = new BattleUnit(CreateCharacter("medic", null, hp: 200, str: 10, def: 10, spd: 20), repository, true)
            {
                Position = 1,
                CurrentPp = 2
            };
            var wounded = new BattleUnit(CreateCharacter("wounded", null, hp: 200, str: 10, def: 10, spd: 10), repository, true)
            {
                Position = 2,
                CurrentHp = 50
            };
            medic.EquippedPassiveSkillIds.Add("pas_give_ap");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { medic, wounded }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            eventBus.Publish(new BattleEndEvent { Context = context, Result = BattleResult.Draw });

            ClassicAssert.IsNotEmpty(passive.Effects);
            CollectionAssert.IsEmpty(passive.Tags);
            ClassicAssert.AreEqual(100, wounded.CurrentHp);
            ClassicAssert.AreEqual(200, medic.CurrentHp);
        }

        [Test]
        public void BattleEndPasBattleHorn_StructuredEffect_HealsCasterRowWithLowHpBonusWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var passive = repository.PassiveSkills["pas_battle_horn"];
            var archer = new BattleUnit(CreateCharacter("archer", null, hp: 200, str: 10, def: 10, spd: 20), repository, true)
            {
                Position = 4,
                CurrentPp = 2
            };
            var backRowAlly = new BattleUnit(CreateCharacter("backRowAlly", null, hp: 200, str: 10, def: 10, spd: 10), repository, true)
            {
                Position = 5,
                CurrentHp = 40
            };
            var frontRowAlly = new BattleUnit(CreateCharacter("frontRowAlly", null, hp: 200, str: 10, def: 10, spd: 10), repository, true)
            {
                Position = 1,
                CurrentHp = 40
            };
            archer.EquippedPassiveSkillIds.Add("pas_battle_horn");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { archer, backRowAlly, frontRowAlly }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            eventBus.Publish(new BattleEndEvent { Context = context, Result = BattleResult.Draw });

            ClassicAssert.IsNotEmpty(passive.Effects);
            CollectionAssert.IsEmpty(passive.Tags);
            ClassicAssert.AreEqual(100, backRowAlly.CurrentHp);
            ClassicAssert.AreEqual(40, frontRowAlly.CurrentHp);
        }

        [Test]
        public void BattleEngine_主动技能effects_HitCount进入真实战斗路径()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_meteor_slash", hp: 1000, str: 100, def: 0, spd: 100), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 50, spd: 1), repository, false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_meteor_slash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(910, defender.CurrentHp);
        }

        [Test]
        public void BattleEngine_锐利斩击_JSONEffects贯通必中与会心加成()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_sharp_slash", hp: 1000, str: 100, def: 0, spd: 100, hit: 0, crit: 50), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 50, spd: 1, eva: 1000), repository, false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_sharp_slash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(925, defender.CurrentHp);
            ClassicAssert.IsTrue(logs.Any(l => l.Contains("attacker.Crit 50->100")));
            ClassicAssert.IsFalse(attacker.Buffs.Any(b => b.SkillId == "act_sharp_slash"));
        }

        [Test]
        public void BattleEngine_粉碎_JSONEffects贯通物防Debuff()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_smash", hp: 1000, str: 100, def: 0, spd: 100, hit: 0), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 100, spd: 1), repository, false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_smash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(80, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(980, defender.CurrentHp);
        }

        [Test]
        public void BattleEngine_列治愈_JSONEffects贯通友方RowHealRatio()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var caster = new BattleUnit(CreateCharacter("caster", "act_row_heal", hp: 200, str: 10, def: 0, spd: 100), repository, true)
            {
                Position = 4,
                CurrentLevel = 20
            };
            var frontAlly = new BattleUnit(CreateCharacter("frontAlly", null, hp: 200, str: 10, def: 0, spd: 1), repository, true)
            {
                Position = 1
            };
            frontAlly.CurrentHp = 80;
            var sameRowAlly = new BattleUnit(CreateCharacter("sameRowAlly", null, hp: 200, str: 10, def: 0, spd: 1), repository, true)
            {
                Position = 2
            };
            sameRowAlly.CurrentHp = 130;
            var enemy = new BattleUnit(CreateCharacter("enemy", null, hp: 200, str: 10, def: 0, spd: 1), repository, false)
            {
                Position = 1
            };
            caster.Strategies.Add(new Strategy { SkillId = "act_row_heal" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, frontAlly, sameRowAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(180, frontAlly.CurrentHp);
            ClassicAssert.AreEqual(200, sameRowAlly.CurrentHp);
            ClassicAssert.AreEqual(200, enemy.CurrentHp);
        }

        [Test]
        public void BattleEngine_CoverAlly_DamagesCoverTargetAndReportsActualDefender()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_test_cover",
                Name = "Test Cover",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.AllyBeforeHit,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "CoverAlly",
                        Parameters = new()
                    }
                },
                HasSimultaneousLimit = false
            });
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_smash", hp: 1000, str: 200, def: 0, spd: 100, hit: 1000), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var coveredTarget = new BattleUnit(CreateCharacter("coveredTarget", null, hp: 1000, str: 10, def: 0, spd: 1), repository, false)
            {
                Position = 1
            };
            var coverTarget = new BattleUnit(CreateCharacter("coverTarget", null, hp: 1000, str: 10, def: 0, spd: 2), repository, false)
            {
                Position = 2
            };
            coverTarget.EquippedPassiveSkillIds.Add("pas_test_cover");
            attacker.Strategies.Add(new Strategy { SkillId = "act_smash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { coveredTarget, coverTarget }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { });
            processor.SubscribeAll();
            var afterHitEvents = new List<AfterHitEvent>();
            var knockdownEvents = new List<OnKnockdownEvent>();
            engine.EventBus.Subscribe<AfterHitEvent>(afterHitEvents.Add);
            engine.EventBus.Subscribe<OnKnockdownEvent>(knockdownEvents.Add);

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(1000, coveredTarget.CurrentHp);
            ClassicAssert.AreEqual(800, coverTarget.CurrentHp);
            ClassicAssert.AreEqual(1, afterHitEvents.Count);
            ClassicAssert.AreSame(coverTarget, afterHitEvents[0].Defender);
            ClassicAssert.AreEqual(200, afterHitEvents[0].DamageDealt);
            ClassicAssert.AreEqual(0, knockdownEvents.Count);
        }

        [Test]
        public void BattleEngine_CoverAlly_KnockdownVictimIsActualDefender()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_test_cover",
                Name = "Test Cover",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.AllyBeforeHit,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "CoverAlly",
                        Parameters = new()
                    }
                },
                HasSimultaneousLimit = false
            });
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_smash", hp: 1000, str: 200, def: 0, spd: 100, hit: 1000), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var coveredTarget = new BattleUnit(CreateCharacter("coveredTarget", null, hp: 1000, str: 10, def: 0, spd: 1), repository, false)
            {
                Position = 1
            };
            var coverTarget = new BattleUnit(CreateCharacter("coverTarget", null, hp: 100, str: 10, def: 0, spd: 2), repository, false)
            {
                Position = 2
            };
            coverTarget.EquippedPassiveSkillIds.Add("pas_test_cover");
            attacker.Strategies.Add(new Strategy { SkillId = "act_smash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { coveredTarget, coverTarget }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { });
            processor.SubscribeAll();
            var knockdownEvents = new List<OnKnockdownEvent>();
            engine.EventBus.Subscribe<OnKnockdownEvent>(knockdownEvents.Add);

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.IsTrue(coveredTarget.IsAlive);
            ClassicAssert.IsFalse(coverTarget.IsAlive);
            ClassicAssert.AreEqual(1, knockdownEvents.Count);
            ClassicAssert.AreSame(coverTarget, knockdownEvents[0].Victim);
            ClassicAssert.AreSame(attacker, knockdownEvents[0].Killer);
        }

        [Test]
        public void PassiveSkillProcessor_RangedCover_OnlyTriggersForRangedPhysicalHit()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_test_ranged_cover",
                Name = "Test Ranged Cover",
                PpCost = 1,
                TriggerTiming = PassiveTriggerTiming.AllyBeforeHit,
                Tags = new List<string> { "RangedCover", "NullifyDamage" },
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "CoverAlly",
                        Parameters = new()
                    },
                    new()
                    {
                        EffectType = "ModifyDamageCalc",
                        Parameters = new()
                        {
                            { "NullifyPhysicalDamage", true }
                        }
                    }
                },
                HasSimultaneousLimit = true
            });
            var attacker = new BattleUnit(CreateCharacter("attacker", null, hp: 100, str: 50, def: 0, spd: 20), repository, true);
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 100, str: 10, def: 0, spd: 10), repository, false);
            var cover = new BattleUnit(CreateCharacter("cover", null, hp: 100, str: 10, def: 0, spd: 10), repository, false);
            cover.CurrentPp = 1;
            cover.EquippedPassiveSkillIds.Add("pas_test_ranged_cover");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender, cover }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            var meleeSkill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Melee);
            var meleeCalc = TestDataFactory.CreateCalc(attacker, defender, meleeSkill);
            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = meleeSkill,
                Context = context,
                Calc = meleeCalc
            });

            ClassicAssert.IsNull(meleeCalc.CoverTarget);
            ClassicAssert.IsFalse(meleeCalc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(1, cover.CurrentPp);

            var rangedSkill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);
            var rangedCalc = TestDataFactory.CreateCalc(attacker, defender, rangedSkill);
            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = rangedSkill,
                Context = context,
                Calc = rangedCalc
            });

            ClassicAssert.AreSame(cover, rangedCalc.CoverTarget);
            ClassicAssert.IsTrue(rangedCalc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(0, cover.CurrentPp);
        }

        [Test]
        public void PassiveSkillProcessor_SharedExecutor_ExecutesBuffAndHealEffects()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_shared_buff_heal",
                Name = "Shared Buff Heal",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.BattleStart,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "AddBuff",
                        Parameters = new()
                        {
                            { "target", "Self" },
                            { "stat", "Str" },
                            { "ratio", 0.2 },
                            { "turns", -1 }
                        }
                    },
                    new()
                    {
                        EffectType = "RecoverHp",
                        Parameters = new()
                        {
                            { "target", "Self" },
                            { "amount", 25 }
                        }
                    }
                }
            });
            var unit = new BattleUnit(CreateCharacter("unit", null, hp: 100, str: 50, def: 10, spd: 10), repository, true);
            unit.CurrentHp = 50;
            unit.EquippedPassiveSkillIds.Add("pas_shared_buff_heal");
            var context = new BattleContext(repository) { PlayerUnits = new List<BattleUnit> { unit } };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            eventBus.Publish(new BattleStartEvent { Context = context });

            ClassicAssert.AreEqual(60, unit.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(75, unit.CurrentHp);
        }

        [Test]
        public void PassiveSkillProcessor_SharedExecutor_ModifiesBeforeHitCalculation()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_shared_calc",
                Name = "Shared Calc",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.SelfBeforeHit,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "ModifyDamageCalc",
                        Parameters = new()
                        {
                            { "ForceEvasion", true },
                            { "CannotBeBlocked", true },
                            { "IgnoreDefenseRatio", 0.5 }
                        }
                    }
                }
            });
            var attacker = new BattleUnit(CreateCharacter("attacker", null, hp: 100, str: 50, def: 10, spd: 20), repository, true);
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 100, str: 30, def: 10, spd: 10), repository, false);
            defender.EquippedPassiveSkillIds.Add("pas_shared_calc");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();
            var skill = TestDataFactory.CreateSkill();
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });

            ClassicAssert.IsTrue(calc.ForceEvasion);
            ClassicAssert.IsTrue(calc.CannotBeBlocked);
            ClassicAssert.AreEqual(0.5f, calc.IgnoreDefenseRatio);
        }

        [Test]
        public void PassiveSkillProcessor_PendingActions_ReadJsonElementParametersFromRealPassiveJson()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var queued = new List<PendingAction>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { }, queued.Add);
            processor.SubscribeAll();
            var preemptiveUser = new BattleUnit(CreateCharacter("preemptiveUser", null, hp: 100, str: 10, def: 10, spd: 20), repository, true);
            preemptiveUser.CurrentPp = 1;
            preemptiveUser.EquippedPassiveSkillIds.Add("pas_stealth_blade");
            var counterUser = new BattleUnit(CreateCharacter("counterUser", null, hp: 100, str: 10, def: 10, spd: 10), repository, false);
            counterUser.CurrentPp = 1;
            counterUser.EquippedPassiveSkillIds.Add("pas_wide_counter");
            var attacker = new BattleUnit(CreateCharacter("attacker", null, hp: 100, str: 10, def: 10, spd: 30), repository, true);
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { preemptiveUser, attacker },
                EnemyUnits = new List<BattleUnit> { counterUser }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });
            eventBus.Publish(new AfterHitEvent
            {
                Attacker = attacker,
                Defender = counterUser,
                Context = context,
                DamageDealt = 1
            });

            var preemptive = queued.Single(a => a.SourcePassiveId == "pas_stealth_blade");
            ClassicAssert.AreEqual(PendingActionType.Preemptive, preemptive.Type);
            ClassicAssert.AreEqual(50, preemptive.Power);
            ClassicAssert.AreEqual(100, preemptive.HitRate);
            CollectionAssert.AreEquivalent(
                new[] { "SureHit", "CannotBeBlocked", "CannotBeCovered" },
                preemptive.Tags);

            var counter = queued.Single(a => a.SourcePassiveId == "pas_wide_counter");
            ClassicAssert.AreEqual(PendingActionType.Counter, counter.Type);
            ClassicAssert.AreEqual(100, counter.Power);
            ClassicAssert.AreEqual(75, counter.HitRate);
            ClassicAssert.AreEqual(TargetType.Row, counter.TargetType);
        }

        [Test]
        public void PassiveSkillProcessor_SimultaneousLimit_IgnoresFailedConditionBeforeClaimingLimit()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_limited_recover_ap",
                Name = "Limited Recover AP",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.BattleStart,
                HasSimultaneousLimit = true,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "RecoverAp",
                        Parameters = new()
                        {
                            { "target", "Self" },
                            { "amount", 1 }
                        }
                    }
                }
            });
            var fastConditionFails = new BattleUnit(CreateCharacter("fast", null, hp: 100, str: 10, def: 10, spd: 30), repository, true);
            var slowConditionPasses = new BattleUnit(CreateCharacter("slow", null, hp: 100, str: 10, def: 10, spd: 10), repository, true);
            fastConditionFails.CurrentAp = 0;
            slowConditionPasses.CurrentAp = 0;
            slowConditionPasses.CurrentHp = 40;
            fastConditionFails.EquippedPassiveSkillIds.Add("pas_limited_recover_ap");
            slowConditionPasses.EquippedPassiveSkillIds.Add("pas_limited_recover_ap");
            fastConditionFails.PassiveConditions["pas_limited_recover_ap"] = LowHpCondition();
            slowConditionPasses.PassiveConditions["pas_limited_recover_ap"] = LowHpCondition();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { fastConditionFails, slowConditionPasses }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            eventBus.Publish(new BattleStartEvent { Context = context });

            ClassicAssert.AreEqual(0, fastConditionFails.CurrentAp);
            ClassicAssert.AreEqual(1, slowConditionPasses.CurrentAp);
        }

        [Test]
        public void PassiveSkillProcessor_SimultaneousLimit_AllowsOnlyOnePassingUnitPerSideAndTiming()
        {
            var repository = LoadRepositoryWithPassive(new PassiveSkillData
            {
                Id = "pas_limited_recover_ap",
                Name = "Limited Recover AP",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.BattleStart,
                HasSimultaneousLimit = true,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "RecoverAp",
                        Parameters = new()
                        {
                            { "target", "Self" },
                            { "amount", 1 }
                        }
                    }
                }
            });
            var fast = new BattleUnit(CreateCharacter("fast", null, hp: 100, str: 10, def: 10, spd: 30), repository, true);
            var slow = new BattleUnit(CreateCharacter("slow", null, hp: 100, str: 10, def: 10, spd: 10), repository, true);
            fast.CurrentAp = 0;
            slow.CurrentAp = 0;
            fast.CurrentHp = 40;
            slow.CurrentHp = 40;
            fast.EquippedPassiveSkillIds.Add("pas_limited_recover_ap");
            slow.EquippedPassiveSkillIds.Add("pas_limited_recover_ap");
            fast.PassiveConditions["pas_limited_recover_ap"] = LowHpCondition();
            slow.PassiveConditions["pas_limited_recover_ap"] = LowHpCondition();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { fast, slow }
            };
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();

            eventBus.Publish(new BattleStartEvent { Context = context });

            ClassicAssert.AreEqual(1, fast.CurrentAp);
            ClassicAssert.AreEqual(0, slow.CurrentAp);
        }

        private static CharacterData CreateCharacter(
            string id,
            string skillId,
            int hp,
            int str,
            int def,
            int spd,
            int hit = 1000,
            int eva = 0,
            int crit = 0,
            int block = 0)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = skillId == null ? new List<string>() : new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", hit },
                    { "Eva", eva },
                    { "Crit", crit },
                    { "Block", block },
                    { "Spd", spd },
                    { "AP", 3 },
                    { "PP", 0 }
                }
            };
        }

        private static GameDataRepository LoadRepositoryWithPassive(PassiveSkillData passive)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.PassiveSkills[passive.Id] = passive;
            return repository;
        }

        private static Condition LowHpCondition()
        {
            return new Condition
            {
                Category = ConditionCategory.SelfHp,
                Operator = "less_than",
                Value = 0.5
            };
        }
    }
}
