using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Text.Json;

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
        public void AmplifyDebuffs_ActionEffect_MultipliesOnlyExistingPureNegativeBuffs()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            var affected = TestDataFactory.CreateUnit(str: 100, def: 100, mag: 100, spd: 100, isPlayer: false);
            var noDebuffTarget = TestDataFactory.CreateUnit(str: 100, def: 100, mag: 100, spd: 100, isPlayer: false);
            affected.Buffs.Add(new Buff
            {
                SkillId = "shared_debuff",
                TargetStat = "Str",
                Ratio = -0.2f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            affected.Buffs.Add(new Buff
            {
                SkillId = "shared_debuff",
                TargetStat = "Def",
                FlatAmount = -10,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            affected.Buffs.Add(new Buff
            {
                SkillId = "positive_buff",
                TargetStat = "Mag",
                Ratio = 0.2f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            affected.Buffs.Add(new Buff
            {
                SkillId = "mixed_runtime_modifier",
                TargetStat = "Spd",
                Ratio = -0.2f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = false
            });
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AmplifyDebuffs",
                    Parameters = new()
                    {
                        { "target", "Target" },
                        { "multiplier", 1.5 }
                    }
                }
            });

            var logs = executor.ExecuteActionEffects(
                context,
                caster,
                new List<BattleUnit> { affected, noDebuffTarget },
                skill.Data.Effects,
                skill.Data.Id);

            ClassicAssert.AreEqual(-0.3f, affected.Buffs.Single(buff => buff.TargetStat == "Str").Ratio, 0.001f);
            ClassicAssert.AreEqual(-15, affected.Buffs.Single(buff => buff.TargetStat == "Def").FlatAmount);
            ClassicAssert.AreEqual(0.2f, affected.Buffs.Single(buff => buff.TargetStat == "Mag").Ratio, 0.001f);
            ClassicAssert.AreEqual(-0.2f, affected.Buffs.Single(buff => buff.TargetStat == "Spd").Ratio, 0.001f);
            ClassicAssert.AreEqual(2, affected.Buffs.Count(buff => buff.SkillId == "shared_debuff"));
            ClassicAssert.IsEmpty(noDebuffTarget.Buffs);
            ClassicAssert.AreEqual(70, affected.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(85, affected.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(120, affected.GetCurrentStat("Mag"));
            ClassicAssert.AreEqual(80, affected.GetCurrentStat("Spd"));
            Assert.That(logs, Has.Some.Contains("Str"));
            Assert.That(logs, Has.Some.Contains("Def"));
            Assert.That(logs, Has.None.Contains("Mag"));
            Assert.That(logs, Has.None.Contains("Spd"));
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
            ClassicAssert.AreEqual(1, action.SourcePpCost);
            ClassicAssert.AreEqual(1, swordsman.CurrentPp);
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
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_smash", hp: 1000, str: 120, def: 0, spd: 100, hit: 1000), repository, true)
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
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(80, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(980, defender.CurrentHp);
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("defender.Def"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonSmash_DoesNotDebuffOrPolluteDamageOnMiss()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_smash", hp: 1000, str: 120, def: 0, spd: 100, hit: 0), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 100, spd: 1, eva: 1000), repository, false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_smash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(100, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(1000, defender.CurrentHp);
            Assert.That(logs, Has.None.Contains("post effects:"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonFireball_BurnsOnlyAfterHit()
        {
            var hitScenario = RunFireballScenario(attackerHit: 1000, defenderEva: 0);
            var missScenario = RunFireballScenario(attackerHit: 0, defenderEva: 1000);

            CollectionAssert.Contains(hitScenario.Defender.Ailments, StatusAilment.Burn);
            Assert.That(hitScenario.Logs, Has.Some.Contains("post effects:").And.Contains("Burn"));
            CollectionAssert.DoesNotContain(missScenario.Defender.Ailments, StatusAilment.Burn);
            ClassicAssert.AreEqual(1000, missScenario.Defender.CurrentHp);
            Assert.That(missScenario.Logs, Has.None.Contains("post effects:"));
        }

        [TestCase("act_poison_bolt", StatusAilment.Poison)]
        [TestCase("act_poison_throw", StatusAilment.Poison)]
        [TestCase("act_ice_arrow", StatusAilment.Freeze)]
        public void BattleEngine_RealActiveJsonG2SingleAilments_ApplyOnlyAfterHit(string skillId, StatusAilment ailment)
        {
            var hitScenario = RunSingleTargetSkillScenario(skillId, attackerHit: 1000, defenderEva: 0);
            var missScenario = RunSingleTargetSkillScenario(skillId, attackerHit: 0, defenderEva: 1000);

            CollectionAssert.Contains(hitScenario.Defender.Ailments, ailment);
            Assert.That(hitScenario.Logs, Has.Some.Contains("post effects:").And.Contains(ailment.ToString()));
            CollectionAssert.DoesNotContain(missScenario.Defender.Ailments, ailment);
            Assert.That(missScenario.Logs, Has.None.Contains("post effects:"));
        }

        [TestCase("act_thunderstorm", StatusAilment.Stun)]
        [TestCase("act_volcano", StatusAilment.Burn)]
        [TestCase("act_ice_coffin", StatusAilment.Freeze)]
        public void BattleEngine_RealActiveJsonG2RowAilments_ApplyOnlyAfterHit(string skillId, StatusAilment ailment)
        {
            var hitScenario = RunRowSkillScenario(skillId, attackerHit: 1000, defenderEva: 0);
            var missScenario = RunRowSkillScenario(skillId, attackerHit: 0, defenderEva: 1000);

            CollectionAssert.Contains(hitScenario.FrontA.Ailments, ailment);
            CollectionAssert.Contains(hitScenario.FrontB.Ailments, ailment);
            CollectionAssert.DoesNotContain(hitScenario.Back.Ailments, ailment);
            if (ailment == StatusAilment.Stun)
            {
                ClassicAssert.AreEqual(UnitState.Stunned, hitScenario.FrontA.State);
                ClassicAssert.AreEqual(UnitState.Stunned, hitScenario.FrontB.State);
            }
            Assert.That(hitScenario.Logs, Has.Some.Contains("post effects:").And.Contains(ailment.ToString()));

            CollectionAssert.DoesNotContain(missScenario.FrontA.Ailments, ailment);
            CollectionAssert.DoesNotContain(missScenario.FrontB.Ailments, ailment);
            CollectionAssert.DoesNotContain(missScenario.Back.Ailments, ailment);
            Assert.That(missScenario.Logs, Has.None.Contains("post effects:"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonShadowBite_DarknessAndDebuffsOnlyAfterHit()
        {
            var hitScenario = RunRowSkillScenario("act_shadow_bite", attackerHit: 1000, defenderEva: 100);
            var missScenario = RunRowSkillScenario("act_shadow_bite", attackerHit: 0, defenderEva: 1000);

            CollectionAssert.Contains(hitScenario.FrontA.Ailments, StatusAilment.Darkness);
            CollectionAssert.Contains(hitScenario.FrontB.Ailments, StatusAilment.Darkness);
            ClassicAssert.AreEqual(80, hitScenario.FrontA.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(80, hitScenario.FrontA.GetCurrentStat("Eva"));
            ClassicAssert.AreEqual(80, hitScenario.FrontB.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(80, hitScenario.FrontB.GetCurrentStat("Eva"));
            CollectionAssert.DoesNotContain(hitScenario.Back.Ailments, StatusAilment.Darkness);
            ClassicAssert.AreEqual(100, hitScenario.Back.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(100, hitScenario.Back.GetCurrentStat("Eva"));
            Assert.That(hitScenario.Logs, Has.Some.Contains("post effects:").And.Contains("Darkness"));

            CollectionAssert.DoesNotContain(missScenario.FrontA.Ailments, StatusAilment.Darkness);
            CollectionAssert.DoesNotContain(missScenario.FrontB.Ailments, StatusAilment.Darkness);
            ClassicAssert.AreEqual(100, missScenario.FrontA.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(1000, missScenario.FrontA.GetCurrentStat("Eva"));
            ClassicAssert.AreEqual(100, missScenario.FrontB.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(1000, missScenario.FrontB.GetCurrentStat("Eva"));
            Assert.That(missScenario.Logs, Has.None.Contains("post effects:"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonPassiveCurse_DirectRowEffectsIgnoreHitCheck()
        {
            var scenario = RunRowSkillScenario(
                "act_passive_curse",
                attackerHit: 0,
                defenderEva: 1000,
                defenderPp: 2);

            ClassicAssert.AreEqual(1, scenario.FrontA.CurrentPp);
            ClassicAssert.AreEqual(1, scenario.FrontB.CurrentPp);
            ClassicAssert.AreEqual(2, scenario.Back.CurrentPp);
            ClassicAssert.AreEqual(90, scenario.FrontA.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(90, scenario.FrontB.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(100, scenario.Back.GetCurrentStat("Spd"));
            Assert.That(scenario.Logs, Has.Some.Contains("effects:").And.Contains("PP").And.Contains("Spd"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonAttackCurse_AppliesBothAttackDebuffsToRow()
        {
            var scenario = RunRowSkillScenario(
                "act_attack_curse",
                attackerHit: 0,
                defenderEva: 1000,
                defenderStr: 100,
                defenderMag: 120);

            ClassicAssert.AreEqual(50, scenario.FrontA.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(60, scenario.FrontA.GetCurrentStat("Mag"));
            ClassicAssert.AreEqual(50, scenario.FrontB.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(60, scenario.FrontB.GetCurrentStat("Mag"));
            ClassicAssert.AreEqual(100, scenario.Back.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(120, scenario.Back.GetCurrentStat("Mag"));
            ClassicAssert.AreEqual(2, scenario.FrontA.Buffs.Count(buff =>
                buff.SkillId == "act_attack_curse"
                && (buff.TargetStat == "Str" || buff.TargetStat == "Mag")
                && buff.Ratio == -0.5f
                && buff.IsPureBuffOrDebuff));
            Assert.That(scenario.Logs, Has.Some.Contains("effects:").And.Contains("Str").And.Contains("Mag"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonDefenseCurse_AppliesDefenseDebuffsAndBlockSealToRow()
        {
            var scenario = RunRowSkillScenario(
                "act_defense_curse",
                attackerHit: 0,
                defenderEva: 1000,
                defenderDef: 100,
                defenderMDef: 80);

            ClassicAssert.AreEqual(50, scenario.FrontA.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(40, scenario.FrontA.GetCurrentStat("MDef"));
            ClassicAssert.AreEqual(50, scenario.FrontB.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(40, scenario.FrontB.GetCurrentStat("MDef"));
            ClassicAssert.AreEqual(100, scenario.Back.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(80, scenario.Back.GetCurrentStat("MDef"));
            CollectionAssert.Contains(scenario.FrontA.Ailments, StatusAilment.BlockSeal);
            CollectionAssert.Contains(scenario.FrontB.Ailments, StatusAilment.BlockSeal);
            CollectionAssert.DoesNotContain(scenario.Back.Ailments, StatusAilment.BlockSeal);
            ClassicAssert.AreEqual(2, scenario.FrontA.Buffs.Count(buff =>
                buff.SkillId == "act_defense_curse"
                && (buff.TargetStat == "Def" || buff.TargetStat == "MDef")
                && buff.Ratio == -0.5f
                && buff.IsPureBuffOrDebuff));
            Assert.That(scenario.Logs, Has.Some.Contains("effects:").And.Contains("BlockSeal"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonCavalryBane_PostHitEffectsOnlyApplyToCavalryTargets()
        {
            var cavalryScenario = RunSingleTargetSkillScenario(
                "act_cavalry_bane",
                attackerHit: 1000,
                defenderEva: 0,
                defenderClasses: new List<UnitClass> { UnitClass.Cavalry },
                defenderAp: 2,
                defenderPp: 2);
            var infantryScenario = RunSingleTargetSkillScenario(
                "act_cavalry_bane",
                attackerHit: 1000,
                defenderEva: 0,
                defenderClasses: new List<UnitClass> { UnitClass.Infantry },
                defenderAp: 2,
                defenderPp: 2);

            ClassicAssert.AreEqual(1, cavalryScenario.Defender.CurrentAp);
            ClassicAssert.AreEqual(1, cavalryScenario.Defender.CurrentPp);
            CollectionAssert.Contains(cavalryScenario.Defender.Ailments, StatusAilment.BlockSeal);
            Assert.That(cavalryScenario.Logs, Has.Some.Contains("post effects:").And.Contains("BlockSeal"));
            ClassicAssert.AreEqual(2, infantryScenario.Defender.CurrentAp);
            ClassicAssert.AreEqual(2, infantryScenario.Defender.CurrentPp);
            CollectionAssert.DoesNotContain(infantryScenario.Defender.Ailments, StatusAilment.BlockSeal);
            Assert.That(infantryScenario.Logs, Has.None.Contains("post effects:"));
        }

        [Test]
        public void RealActiveJsonShieldBash_UsesChanceGatedOnHitStun()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var shieldBash = repository.ActiveSkills["act_shield_bash"];
            var onHit = shieldBash.Effects.Single();
            var effectsElement = (JsonElement)onHit.Parameters["effects"];
            var nested = JsonSerializer.Deserialize<List<SkillEffectData>>(
                effectsElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            ClassicAssert.AreEqual("OnHitEffect", onHit.EffectType);
            ClassicAssert.AreEqual(25, ((JsonElement)onHit.Parameters["chance"]).GetInt32());
            ClassicAssert.AreEqual("StatusAilment", nested.Single().EffectType);
            ClassicAssert.AreEqual("Stun", ((JsonElement)nested.Single().Parameters["ailment"]).GetString());
        }

        [Test]
        public void OnHitEffect_ChanceGate_ControlsNestedEffectsAndStunSetsUnitState()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit(isPlayer: true);
            var target = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill();
            var calc = TestDataFactory.CreateCalc(caster, target, skill);
            calc.ResolvedDefender = target;
            var hit = new DamageResult(1, 0, true, false, false, false, new List<StatusAilment>(), target);
            var effects = new List<SkillEffectData>
            {
                new()
                {
                    EffectType = "OnHitEffect",
                    Parameters = new()
                    {
                        { "chance", 0 },
                        { "effects", new List<SkillEffectData>
                            {
                                new()
                                {
                                    EffectType = "StatusAilment",
                                    Parameters = new() { { "target", "Target" }, { "ailment", "Stun" } }
                                }
                            }
                        }
                    }
                }
            };

            var missByChanceLogs = executor.ExecutePostDamageEffects(
                context, caster, target, effects, skill.Data.Id, calc, hit, killed: false);
            effects.Single().Parameters["chance"] = 100;
            var applyLogs = executor.ExecutePostDamageEffects(
                context, caster, target, effects, skill.Data.Id, calc, hit, killed: false);

            ClassicAssert.IsEmpty(missByChanceLogs);
            CollectionAssert.Contains(target.Ailments, StatusAilment.Stun);
            ClassicAssert.AreEqual(UnitState.Stunned, target.State);
            Assert.That(applyLogs, Has.Some.Contains("Stun"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonBreakFormation_BonusesOnlyFromPreexistingDebuffAndDebuffsAfterHit()
        {
            var clean = RunSingleTargetDamageScenario("act_break_formation");
            var preDebuffed = RunSingleTargetDamageScenario("act_break_formation", defenderHasDebuff: true);

            ClassicAssert.AreEqual(20, clean.Damage);
            ClassicAssert.AreEqual(30, preDebuffed.Damage);
            ClassicAssert.AreEqual(70, clean.Defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(70, preDebuffed.Defender.GetCurrentStat("Def"));
            Assert.That(preDebuffed.Logs, Has.Some.Contains("PowerMultiplier=1.5"));
            Assert.That(preDebuffed.Logs, Has.Some.Contains("post effects:").And.Contains("Def"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonSpike_BonusesOnlyWhenCasterHpIsHalfOrBelow()
        {
            var healthy = RunSingleTargetDamageScenario("act_spike", attackerCurrentHp: 600);
            var lowHp = RunSingleTargetDamageScenario("act_spike", attackerCurrentHp: 500);

            ClassicAssert.AreEqual(20, healthy.Damage);
            ClassicAssert.AreEqual(30, lowHp.Damage);
            Assert.That(lowHp.Logs, Has.Some.Contains("PowerMultiplier=1.5"));
        }

        [TestCase("act_accumulate", 20, 30)]
        [TestCase("act_full_assault", 30, 45)]
        public void BattleEngine_RealActiveJsonFullHpDamageSkills_BonusOnlyAtFullHp(
            string skillId,
            int normalDamage,
            int fullHpDamage)
        {
            var wounded = RunSingleTargetDamageScenario(skillId, attackerCurrentHp: 999);
            var fullHp = RunSingleTargetDamageScenario(skillId, attackerCurrentHp: 1000);

            ClassicAssert.AreEqual(normalDamage, wounded.Damage);
            ClassicAssert.AreEqual(fullHpDamage, fullHp.Damage);
            Assert.That(fullHp.Logs, Has.Some.Contains("PowerMultiplier=1.5"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonHeavySlayer_IgnoresHeavyDefenseAndCannotBeBlocked()
        {
            var heavy = RunSingleTargetDamageScenario(
                "act_heavy_slayer",
                defenderClasses: new List<UnitClass> { UnitClass.Heavy },
                defenderBlock: 100);
            var infantry = RunSingleTargetDamageScenario(
                "act_heavy_slayer",
                defenderClasses: new List<UnitClass> { UnitClass.Infantry },
                defenderBlock: 100);

            ClassicAssert.AreEqual(120, heavy.Damage);
            ClassicAssert.AreEqual(20, infantry.Damage);
            Assert.That(heavy.Logs, Has.Some.Contains("CannotBeBlocked"));
            Assert.That(heavy.Logs, Has.Some.Contains("IgnoreDefense=1"));
            Assert.That(infantry.Logs, Has.Some.Contains("CannotBeBlocked"));
            Assert.That(infantry.Logs, Has.None.Contains("IgnoreDefense=1"));
        }

        [TestCase("act_enhanced_spear", UnitClass.Cavalry, 30, true)]
        [TestCase("act_spear_pierce", UnitClass.Flying, 30, false)]
        [TestCase("act_dive_strike", UnitClass.Cavalry, 30, true)]
        [TestCase("act_wing_gust", UnitClass.Cavalry, 20, true)]
        public void BattleEngine_RealActiveJsonClassBonusSkills_ApplyOnlyToMatchingClass(
            string skillId,
            UnitClass matchingClass,
            int matchingDamage,
            bool matchingCannotBeBlocked)
        {
            var matching = RunSingleTargetDamageScenario(
                skillId,
                defenderClasses: new List<UnitClass> { matchingClass },
                defenderBlock: matchingCannotBeBlocked ? 100 : 0);
            var infantry = RunSingleTargetDamageScenario(
                skillId,
                defenderClasses: new List<UnitClass> { UnitClass.Infantry },
                defenderBlock: matchingCannotBeBlocked ? 100 : 0);

            ClassicAssert.AreEqual(matchingDamage, matching.Damage);
            ClassicAssert.AreEqual(matchingCannotBeBlocked ? 15 : 20, infantry.Damage);
            if (matchingCannotBeBlocked)
                Assert.That(matching.Logs, Has.Some.Contains("CannotBeBlocked"));
            else
                Assert.That(matching.Logs, Has.None.Contains("CannotBeBlocked"));
            Assert.That(infantry.Logs, Has.None.Contains("PowerMultiplier=1.5"));
            Assert.That(infantry.Logs, Has.None.Contains("CannotBeBlocked"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonLineDestruction_IgnoresOnlyHeavyDefenseButCannotBeBlockedGlobally()
        {
            var heavy = RunSingleTargetDamageScenario(
                "act_line_destruction",
                defenderClasses: new List<UnitClass> { UnitClass.Heavy },
                defenderBlock: 100);
            var infantry = RunSingleTargetDamageScenario(
                "act_line_destruction",
                defenderClasses: new List<UnitClass> { UnitClass.Infantry },
                defenderBlock: 100);

            ClassicAssert.AreEqual(120, heavy.Damage);
            ClassicAssert.AreEqual(20, infantry.Damage);
            Assert.That(heavy.Logs, Has.Some.Contains("CannotBeBlocked"));
            Assert.That(heavy.Logs, Has.Some.Contains("CannotBeCovered"));
            Assert.That(heavy.Logs, Has.Some.Contains("IgnoreDefense=1"));
            Assert.That(infantry.Logs, Has.Some.Contains("CannotBeBlocked"));
            Assert.That(infantry.Logs, Has.Some.Contains("CannotBeCovered"));
            Assert.That(infantry.Logs, Has.None.Contains("IgnoreDefense=1"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonThrowingSpear_BuffsOnlyWhenCasterAlreadyBuffedAndSealsCavalryBlock()
        {
            var unbuffedInfantry = RunSingleTargetDamageScenario(
                "act_throwing_spear",
                defenderClasses: new List<UnitClass> { UnitClass.Infantry });
            var buffedInfantry = RunSingleTargetDamageScenario(
                "act_throwing_spear",
                casterHasBuff: true,
                defenderClasses: new List<UnitClass> { UnitClass.Infantry });
            var cavalry = RunSingleTargetDamageScenario(
                "act_throwing_spear",
                defenderClasses: new List<UnitClass> { UnitClass.Cavalry },
                defenderBlock: 100);

            ClassicAssert.AreEqual(26, unbuffedInfantry.Damage);
            ClassicAssert.AreEqual(39, buffedInfantry.Damage);
            ClassicAssert.AreEqual(26, cavalry.Damage);
            Assert.That(buffedInfantry.Logs, Has.Some.Contains("PowerMultiplier=1.5"));
            Assert.That(cavalry.Logs, Has.Some.Contains("CannotBeBlocked"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonChargeStrike_CannotBeBlockedAndRecoversApOnKill()
        {
            var scenario = RunSingleTargetDamageScenario(
                "act_charge_strike",
                attackerCurrentAp: 1,
                defenderHp: 10,
                defenderBlock: 100);

            ClassicAssert.AreEqual(1, scenario.Attacker.CurrentAp);
            ClassicAssert.IsFalse(scenario.Defender.IsAlive);
            Assert.That(scenario.Logs, Has.Some.Contains("CannotBeBlocked"));
            Assert.That(scenario.Logs, Has.Some.Contains("post effects:").And.Contains(".AP 0->1"));
        }

        [Test]
        public void BattleEngine_RealActiveJsonGreatShield_BuffsSelfAndRecoversPpWithoutEnemyDamage()
        {
            var scenario = RunSelfAssistSkillScenario(
                "act_great_shield",
                casterDef: 100,
                casterAp: 3,
                casterPp: 4,
                casterCurrentPp: 1);

            ClassicAssert.AreEqual(150, scenario.Caster.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(3, scenario.Caster.CurrentPp);
            ClassicAssert.AreEqual(2, scenario.Caster.CurrentAp);
            ClassicAssert.AreEqual(1000, scenario.Caster.CurrentHp);
            ClassicAssert.AreEqual(1000, scenario.Enemy.CurrentHp);
            ClassicAssert.AreEqual(3, scenario.Enemy.CurrentAp);
            ClassicAssert.AreEqual(2, scenario.Enemy.CurrentPp);
            ClassicAssert.IsEmpty(scenario.Enemy.Buffs);
            ClassicAssert.IsEmpty(scenario.Enemy.Ailments);
            ClassicAssert.AreEqual(1, scenario.Caster.Buffs.Count(buff =>
                buff.SkillId == "act_great_shield"
                && buff.TargetStat == "Def"
                && buff.Ratio == 0.5f
                && buff.IsPureBuffOrDebuff));
            Assert.That(scenario.Logs, Has.Some.Contains("effects:").And.Contains("Def").And.Contains("PP"));
            Assert.That(scenario.Logs, Has.None.Contains("post effects:"));

            var capped = RunSelfAssistSkillScenario(
                "act_great_shield",
                casterDef: 100,
                casterAp: 3,
                casterPp: 4,
                casterCurrentPp: 3);

            ClassicAssert.AreEqual(4, capped.Caster.CurrentPp);
        }

        [Test]
        public void BattleEngine_RealActiveJsonFormationBreaker_HealsAndBuffsSelfWithoutEnemyDamage()
        {
            var scenario = RunSelfAssistSkillScenario(
                "act_formation_breaker",
                casterHp: 200,
                casterCurrentHp: 100,
                casterStr: 100,
                casterAp: 3);

            ClassicAssert.AreEqual(160, scenario.Caster.CurrentHp);
            ClassicAssert.AreEqual(130, scenario.Caster.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(1, scenario.Caster.CurrentAp);
            ClassicAssert.AreEqual(1000, scenario.Enemy.CurrentHp);
            ClassicAssert.AreEqual(3, scenario.Enemy.CurrentAp);
            ClassicAssert.AreEqual(2, scenario.Enemy.CurrentPp);
            ClassicAssert.IsEmpty(scenario.Enemy.Buffs);
            ClassicAssert.IsEmpty(scenario.Enemy.Ailments);
            ClassicAssert.AreEqual(1, scenario.Caster.Buffs.Count(buff =>
                buff.SkillId == "act_formation_breaker"
                && buff.TargetStat == "Str"
                && buff.Ratio == 0.3f
                && buff.IsPureBuffOrDebuff));
            Assert.That(scenario.Logs, Has.Some.Contains("effects:").And.Contains("HP").And.Contains("Str"));
            Assert.That(scenario.Logs, Has.None.Contains("post effects:"));

            var capped = RunSelfAssistSkillScenario(
                "act_formation_breaker",
                casterHp: 200,
                casterCurrentHp: 180,
                casterStr: 100,
                casterAp: 3);

            ClassicAssert.AreEqual(200, capped.Caster.CurrentHp);
        }

        [Test]
        public void BattleEngine_RealActiveJsonLineDefense_BuffsSelectedAllyColumnNotCasterColumn()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var skill = repository.ActiveSkills["act_line_defense"];
            var effect = skill.Effects.Single();
            var caster = new BattleUnit(CreateCharacter("caster", "act_line_defense", hp: 1000, str: 10, def: 100, spd: 100), repository, true)
            {
                Position = 3,
                CurrentLevel = 30
            };
            var selectedAlly = new BattleUnit(CreateCharacter("selectedAlly", null, hp: 1000, str: 10, def: 100, spd: 1), repository, true)
            {
                Position = 1
            };
            var otherFrontColumn = new BattleUnit(CreateCharacter("otherFrontColumn", null, hp: 1000, str: 10, def: 100, spd: 1), repository, true)
            {
                Position = 2
            };
            var sameTargetColumn = new BattleUnit(CreateCharacter("sameTargetColumn", null, hp: 1000, str: 10, def: 100, spd: 1), repository, true)
            {
                Position = 4
            };
            var otherBackColumn = new BattleUnit(CreateCharacter("otherBackColumn", null, hp: 1000, str: 10, def: 100, spd: 1), repository, true)
            {
                Position = 5
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", null, hp: 1000, str: 10, def: 100, spd: 1), repository, false)
            {
                Position = 1
            };
            caster.Strategies.Add(new Strategy { SkillId = "act_line_defense" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, selectedAlly, otherFrontColumn, sameTargetColumn, otherBackColumn },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AddBuff" }, skill.Effects.Select(e => e.EffectType).ToList());
            ClassicAssert.AreEqual("ColumnAlliesOfTarget", ((JsonElement)effect.Parameters["target"]).GetString());
            ClassicAssert.AreEqual("Def", ((JsonElement)effect.Parameters["stat"]).GetString());
            ClassicAssert.AreEqual(0.5, ((JsonElement)effect.Parameters["ratio"]).GetDouble());
            ClassicAssert.AreEqual(-1, ((JsonElement)effect.Parameters["turns"]).GetInt32());
            ClassicAssert.AreEqual(150, selectedAlly.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(150, sameTargetColumn.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherFrontColumn.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherBackColumn.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, caster.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(2, context.PlayerUnits.Sum(unit => unit.Buffs.Count(buff =>
                buff.SkillId == "act_line_defense"
                && buff.TargetStat == "Def"
                && buff.Ratio == 0.5f
                && buff.IsPureBuffOrDebuff)));
            Assert.That(logs, Has.Some.Contains("effects:").And.Contains("selectedAlly.Def").And.Contains("sameTargetColumn.Def"));
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
        public void BattleEngine_RealPassiveJsonStealthBlade_AppliesBlockSealAfterPendingHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var stealthBlade = repository.PassiveSkills["pas_stealth_blade"];
            var preemptiveUser = new BattleUnit(CreateCharacter("preemptiveUser", null, hp: 200, str: 100, def: 10, spd: 20, hit: 1000), repository, true)
            {
                Position = 1,
                CurrentPp = 1
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", null, hp: 200, str: 10, def: 0, spd: 10), repository, false)
            {
                Position = 1
            };
            preemptiveUser.EquippedPassiveSkillIds.Add("pas_stealth_blade");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { preemptiveUser },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();

            CollectionAssert.AreEqual(
                new[] { "PreemptiveAttack", "OnHitEffect" },
                stealthBlade.Effects.Select(effect => effect.EffectType).ToList());
            CollectionAssert.Contains(enemy.Ailments, StatusAilment.BlockSeal);
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("BlockSeal"));
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

        private static (BattleUnit Defender, List<string> Logs) RunFireballScenario(int attackerHit, int defenderEva)
        {
            return RunSingleTargetSkillScenario("act_fireball", attackerHit, defenderEva);
        }

        private static (BattleUnit Defender, List<string> Logs) RunSingleTargetSkillScenario(
            string skillId,
            int attackerHit,
            int defenderEva,
            List<UnitClass> defenderClasses = null,
            int defenderAp = 3,
            int defenderPp = 2)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", skillId, hp: 1000, str: 120, def: 0, spd: 200, hit: attackerHit), repository, true)
            {
                Position = 1,
                CurrentLevel = 30
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 100, spd: 1, eva: defenderEva, classes: defenderClasses), repository, false)
            {
                Position = 1,
                CurrentAp = defenderAp,
                CurrentPp = defenderPp
            };
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            return (defender, logs);
        }

        private static (BattleUnit FrontA, BattleUnit FrontB, BattleUnit Back, List<string> Logs) RunRowSkillScenario(
            string skillId,
            int attackerHit,
            int defenderEva,
            int defenderPp = 0,
            int defenderStr = 10,
            int defenderDef = 100,
            int defenderSpd = 100,
            int defenderMag = 0,
            int defenderMDef = 0)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", skillId, hp: 1000, str: 120, def: 0, spd: 200, hit: attackerHit), repository, true)
            {
                Position = 1,
                CurrentLevel = 30
            };
            var frontA = new BattleUnit(CreateCharacter(
                "frontA",
                null,
                hp: 1000,
                str: defenderStr,
                def: defenderDef,
                spd: defenderSpd,
                eva: defenderEva,
                mag: defenderMag,
                mdef: defenderMDef,
                pp: defenderPp), repository, false)
            {
                Position = 1
            };
            var frontB = new BattleUnit(CreateCharacter(
                "frontB",
                null,
                hp: 1000,
                str: defenderStr,
                def: defenderDef,
                spd: defenderSpd,
                eva: defenderEva,
                mag: defenderMag,
                mdef: defenderMDef,
                pp: defenderPp), repository, false)
            {
                Position = 2
            };
            var back = new BattleUnit(CreateCharacter(
                "back",
                null,
                hp: 1000,
                str: defenderStr,
                def: defenderDef,
                spd: defenderSpd,
                eva: defenderEva,
                mag: defenderMag,
                mdef: defenderMDef,
                pp: defenderPp), repository, false)
            {
                Position = 4
            };
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { frontA, frontB, back }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            return (frontA, frontB, back, logs);
        }

        private static (BattleUnit Attacker, BattleUnit Defender, int Damage, List<string> Logs) RunSingleTargetDamageScenario(
            string skillId,
            int attackerCurrentHp = 1000,
            int attackerCurrentAp = 3,
            int defenderHp = 1000,
            bool casterHasBuff = false,
            bool defenderHasDebuff = false,
            List<UnitClass> defenderClasses = null,
            int defenderBlock = 0)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", skillId, hp: 1000, str: 120, def: 0, spd: 100, hit: 1000), repository, true)
            {
                Position = 1,
                CurrentLevel = 30,
                CurrentHp = attackerCurrentHp,
                CurrentAp = attackerCurrentAp
            };
            if (casterHasBuff)
            {
                attacker.Buffs.Add(new Buff
                {
                    SkillId = "preexisting_buff",
                    TargetStat = "Def",
                    Ratio = 0.1f,
                    RemainingTurns = -1,
                    IsPureBuffOrDebuff = true
                });
            }
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: defenderHp, str: 10, def: 100, spd: 1, block: defenderBlock, classes: defenderClasses), repository, false)
            {
                Position = 1
            };
            if (defenderHasDebuff)
            {
                defender.Buffs.Add(new Buff
                {
                    SkillId = "preexisting_debuff",
                    TargetStat = "Str",
                    Ratio = -0.1f,
                    RemainingTurns = -1,
                    IsPureBuffOrDebuff = true
                });
            }
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.IsTrue(
                result == SingleActionResult.ActionDone || result == SingleActionResult.PlayerWin,
                $"Unexpected action result: {result}");
            return (attacker, defender, defenderHp - defender.CurrentHp, logs);
        }

        private static (BattleUnit Caster, BattleUnit Enemy, List<string> Logs) RunSelfAssistSkillScenario(
            string skillId,
            int casterHp = 1000,
            int casterCurrentHp = 1000,
            int casterStr = 100,
            int casterDef = 100,
            int casterAp = 3,
            int casterPp = 2,
            int casterCurrentPp = 0)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var caster = new BattleUnit(
                CreateCharacter(
                    "caster",
                    skillId,
                    hp: casterHp,
                    str: casterStr,
                    def: casterDef,
                    spd: 100,
                    hit: 1000,
                    ap: casterAp,
                    pp: casterPp),
                repository,
                true)
            {
                Position = 1,
                CurrentLevel = 30,
                CurrentHp = casterCurrentHp,
                CurrentPp = casterCurrentPp
            };
            var enemy = new BattleUnit(
                CreateCharacter("enemy", null, hp: 1000, str: 10, def: 100, spd: 1, ap: 3, pp: 2),
                repository,
                false)
            {
                Position = 1
            };
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            return (caster, enemy, logs);
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
            int block = 0,
            List<UnitClass> classes = null,
            int mag = 0,
            int mdef = 0,
            int ap = 3,
            int pp = 0)
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
                    { "Mag", mag },
                    { "MDef", mdef },
                    { "Hit", hit },
                    { "Eva", eva },
                    { "Crit", crit },
                    { "Block", block },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", pp }
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
