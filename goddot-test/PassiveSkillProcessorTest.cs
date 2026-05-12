using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;
using BattleKing.Equipment;
using BattleKing.Events;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class PassiveSkillProcessorTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void RealPassiveJson_ElfSibylHealingTouch_HealsAndCleansesAttackedAlly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var elfSibyl = repository.Characters["elf_sibyl"];
            var healingTouch = repository.PassiveSkills["pas_healing_touch"];
            var spiritGuard = repository.PassiveSkills["pas_spirit_guard"];
            var elementalAction = repository.PassiveSkills["pas_elemental_action"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var sibyl = new BattleUnit(CreateCharacter("sibyl", hp: 100, pp: 1), repository, true);
            var ally = new BattleUnit(CreateCharacter("ally", hp: 100, pp: 0), repository, true);
            var enemy = new BattleUnit(CreateCharacter("enemy", hp: 100, pp: 0), repository, false);
            sibyl.EquippedPassiveSkillIds.Add("pas_healing_touch");
            ally.CurrentHp = 40;
            ally.Buffs.Add(new Buff
            {
                TargetStat = "Def",
                Ratio = -0.25f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { sibyl, ally },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = enemy,
                Defender = ally,
                Context = context,
                DamageDealt = 10,
                IsHit = true
            });

            CollectionAssert.Contains(elfSibyl.InnatePassiveSkillIds, "pas_healing_touch");
            CollectionAssert.Contains(elfSibyl.InnatePassiveSkillIds, "pas_spirit_guard");
            CollectionAssert.Contains(elfSibyl.InnatePassiveSkillIds, "pas_elemental_action");
            CollectionAssert.Contains(healingTouch.Tags, "Heal");
            CollectionAssert.Contains(healingTouch.Tags, "CleanseDebuff");
            CollectionAssert.AreEqual(
                new[] { "HealRatio", "CleanseDebuff" },
                healingTouch.Effects.Select(e => e.EffectType).ToList());
            CollectionAssert.Contains(spiritGuard.Tags, "HealRow");
            CollectionAssert.Contains(spiritGuard.Tags, "DebuffNullify");
            CollectionAssert.AreEqual(
                new[] { "HealRatio", "TemporalMark", "ModifyCounter" },
                spiritGuard.Effects.Select(e => e.EffectType).ToList());
            CollectionAssert.AreEqual(
                new[] { "RecoverAp", "ModifyCounter" },
                elementalAction.Effects.Select(e => e.EffectType).ToList());
            ClassicAssert.AreEqual(0, sibyl.CurrentPp);
            ClassicAssert.AreEqual(65, ally.CurrentHp);
            ClassicAssert.AreEqual(0, ally.Buffs.Count(b => b.Ratio < 0));
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_ElfSibylSpiritGuard_HealsBackRowGrantsDebuffNullifyAndSprite()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var spiritGuard = repository.PassiveSkills["pas_spirit_guard"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var sibyl = new BattleUnit(CreateCharacter("sibyl", hp: 100, pp: 2), repository, true)
            {
                Position = 4
            };
            var backRowAlly = new BattleUnit(CreateCharacter("backRowAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 5,
                CurrentHp = 40
            };
            var frontRowAlly = new BattleUnit(CreateCharacter("frontRowAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 1,
                CurrentHp = 40
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", hp: 100, pp: 0), repository, false)
            {
                Position = 1
            };
            sibyl.EquippedPassiveSkillIds.Add("pas_spirit_guard");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { sibyl, backRowAlly, frontRowAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            CollectionAssert.AreEqual(
                new[] { "HealRatio", "TemporalMark", "ModifyCounter" },
                spiritGuard.Effects.Select(e => e.EffectType).ToList());
            ClassicAssert.AreEqual(0, sibyl.CurrentPp);
            ClassicAssert.AreEqual(90, backRowAlly.CurrentHp);
            ClassicAssert.AreEqual(40, frontRowAlly.CurrentHp);
            ClassicAssert.AreEqual(1, sibyl.GetCounter("Sprite"));
            ClassicAssert.IsTrue(sibyl.TemporalStates.Any(s => s.Key == "DebuffNullify"));
            ClassicAssert.IsTrue(backRowAlly.TemporalStates.Any(s => s.Key == "DebuffNullify"));
            ClassicAssert.IsFalse(frontRowAlly.TemporalStates.Any(s => s.Key == "DebuffNullify"));
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_QuickCurse_DebuffsAndCritSealsAttackerWithoutForceHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var quickCurse = repository.PassiveSkills["pas_quick_curse"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, true);
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            defender.EquippedPassiveSkillIds.Add("pas_quick_curse");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
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

            CollectionAssert.AreEqual(
                new[] { "AddDebuff", "StatusAilment" },
                quickCurse.Effects.Select(e => e.EffectType).ToList());
            CollectionAssert.DoesNotContain(quickCurse.Tags, "SureHit");
            ClassicAssert.IsFalse(calc.ForceHit);
            ClassicAssert.AreEqual(8, attacker.GetCurrentStat("Str"));
            ClassicAssert.IsTrue(attacker.Buffs.Any(b =>
                b.TargetStat == "Str" && b.Ratio < 0f && b.IsPureBuffOrDebuff));
            CollectionAssert.Contains(attacker.Ailments, StatusAilment.CritSeal);
            ClassicAssert.AreEqual(10, defender.GetCurrentStat("Str"));
            ClassicAssert.IsFalse(defender.Buffs.Any(b => b.TargetStat == "Str" && b.Ratio < 0f));
            CollectionAssert.DoesNotContain(defender.Ailments, StatusAilment.CritSeal);
            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_CounterMagic_QueuesMagicalMagicCounter()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var queued = new List<PendingAction>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { }, queued.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, false);
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, true);
            defender.EquippedPassiveSkillIds.Add("pas_counter_magic");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Context = context,
                DamageDealt = 1,
                IsHit = true
            });

            var action = queued.Single();
            ClassicAssert.AreEqual(PendingActionType.Counter, action.Type);
            ClassicAssert.AreSame(defender, action.Actor);
            CollectionAssert.AreEqual(new[] { attacker }, action.Targets);
            ClassicAssert.AreEqual(SkillType.Magical, action.DamageType);
            ClassicAssert.AreEqual(AttackType.Magic, action.AttackType);
        }

        [Test]
        public void RealPassiveJson_BlockSeal_QueuesCounterWithoutModifyingIncomingCannotBeBlocked()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var blockSeal = repository.PassiveSkills["pas_block_seal"];
            var queued = new List<PendingAction>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { }, queued.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, false);
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, true);
            defender.EquippedPassiveSkillIds.Add("pas_block_seal");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
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

            ClassicAssert.AreEqual("CounterAttack", blockSeal.Effects.Single().EffectType);
            ClassicAssert.IsFalse(calc.CannotBeBlocked);
            var action = queued.Single();
            ClassicAssert.AreEqual(PendingActionType.Counter, action.Type);
            ClassicAssert.AreSame(defender, action.Actor);
            CollectionAssert.AreEqual(new[] { attacker }, action.Targets);
            ClassicAssert.AreEqual(1f, action.IgnoreDefenseRatio);
            ClassicAssert.AreEqual(UnitClass.Heavy, action.IgnoreDefenseTargetClass);
            ClassicAssert.AreEqual(0, defender.CurrentPp);
        }

        [Test]
        public void BattleStart_UsesPassiveStrategyRowOrderBeforeLegacyEquippedOrder()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var unit = new BattleUnit(CreateCharacter("unit", hp: 100, pp: 2), repository, true)
            {
                Position = 1
            };
            var enemy = new BattleUnit(CreateCharacter("enemy", hp: 100, pp: 0), repository, false)
            {
                Position = 1
            };
            unit.EquippedPassiveSkillIds.Add("pas_quick_strike");
            unit.EquippedPassiveSkillIds.Add("pas_rapid_order");
            unit.PassiveStrategies.Add(new PassiveStrategy { SkillId = "pas_rapid_order" });
            unit.PassiveStrategies.Add(new PassiveStrategy { SkillId = "pas_quick_strike" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { unit },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            StringAssert.Contains("快速命令", logs.First(log => log.Contains("[被动]")));
            ClassicAssert.AreEqual(1, unit.CurrentPp);
        }

        private static CharacterData CreateCharacter(string id, int hp, int pp)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", 10 },
                    { "Def", 10 },
                    { "Mag", 10 },
                    { "MDef", 10 },
                    { "Hit", 100 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", 10 },
                    { "AP", 0 },
                    { "PP", pp }
                }
            };
        }
    }
}
