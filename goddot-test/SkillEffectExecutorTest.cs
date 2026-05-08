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
    }
}
