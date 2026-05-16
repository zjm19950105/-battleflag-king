using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;
using BattleKing.Equipment;
using BattleKing.Events;
using BattleKing.Pipeline;
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
        public void RealPassiveJson_QuickCurse_DebuffsAndCritSealsEnemyActiveAttackerForAlly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var quickCurse = repository.PassiveSkills["pas_quick_curse"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, true);
            var shaman = new BattleUnit(CreateCharacter("shaman", hp: 100, pp: 1), repository, true);
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            shaman.EquippedPassiveSkillIds.Add("pas_quick_curse");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, shaman },
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
            CollectionAssert.Contains(quickCurse.TriggerTimings, PassiveTriggerTiming.AllyBeforeHit);
            CollectionAssert.Contains(quickCurse.Tags, "SureHit");
            ClassicAssert.IsTrue(quickCurse.Effects.All(effect =>
                ReadString(effect.Parameters, "requiresSourceKind") == "ActiveAttack"));
            ClassicAssert.IsFalse(calc.ForceHit);
            ClassicAssert.AreEqual(8, attacker.GetCurrentStat("Str"));
            ClassicAssert.IsTrue(attacker.Buffs.Any(b =>
                b.TargetStat == "Str" && b.Ratio < 0f && b.IsPureBuffOrDebuff));
            CollectionAssert.Contains(attacker.Ailments, StatusAilment.CritSeal);
            ClassicAssert.AreEqual(10, defender.GetCurrentStat("Str"));
            ClassicAssert.IsFalse(defender.Buffs.Any(b => b.TargetStat == "Str" && b.Ratio < 0f));
            CollectionAssert.DoesNotContain(defender.Ailments, StatusAilment.CritSeal);
            ClassicAssert.AreEqual(0, shaman.CurrentPp);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_Bounce_OnBeingHitHealsOnlyAfterHitAndPaysPp()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var bounce = repository.PassiveSkills["pas_bounce"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 200, pp: 0), repository, true)
            {
                CurrentHp = 40
            };
            var defender = new BattleUnit(CreateCharacter("defender", hp: 200, pp: 1), repository, false)
            {
                CurrentHp = 40
            };
            var ally = new BattleUnit(CreateCharacter("ally", hp: 200, pp: 0), repository, false)
            {
                CurrentHp = 40
            };
            defender.EquippedPassiveSkillIds.Add("pas_bounce");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender, ally }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Melee);
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
                new[] { "HealRatio" },
                bounce.Effects.Select(effect => effect.EffectType).ToList());

            ClassicAssert.AreEqual(PassiveTriggerTiming.OnBeingHit, bounce.TriggerTiming);
            ClassicAssert.AreEqual(1, defender.CurrentPp);
            ClassicAssert.AreEqual(40, defender.CurrentHp);
            ClassicAssert.AreEqual(40, attacker.CurrentHp);
            ClassicAssert.AreEqual(40, ally.CurrentHp);

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                DamageDealt = 1,
                IsHit = true
            });

            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.AreEqual(120, defender.CurrentHp);
            ClassicAssert.AreEqual(40, attacker.CurrentHp);
            ClassicAssert.AreEqual(40, ally.CurrentHp);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_Bounce_HealRatioDoesNotLowerHpAboveCurrentMax()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 200, pp: 0), repository, true);
            var defender = new BattleUnit(CreateCharacter("defender", hp: 148, pp: 1), repository, false)
            {
                CurrentHp = 151
            };
            defender.EquippedPassiveSkillIds.Add("pas_bounce");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Melee);

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                DamageDealt = 1,
                IsHit = true
            });

            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.AreEqual(151, defender.CurrentHp);
            Assert.That(logs, Has.Some.Contains("defender.HP 151->151"));
        }

        [Test]
        public void RealPassiveJson_AidCover_CoversBlocksAndHealsOriginalDefenderOnly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_cover_probe"] = new ActiveSkillData
            {
                Id = "act_cover_probe",
                Name = "Cover Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var aidCover = repository.PassiveSkills["pas_aid_cover"];
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_cover_probe", hp: 200, str: 100, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1,
                CurrentHp = 40
            };
            var cover = new BattleUnit(CreatePassiveBattleCharacter(
                "cover", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                false)
            {
                Position = 4,
                CurrentHp = 100
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_cover_probe" });
            cover.EquippedPassiveSkillIds.Add("pas_aid_cover");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender, cover }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();
            DamageCalculation? capturedCalc = null;
            var afterHitDefenders = new List<BattleUnit>();
            engine.EventBus.Subscribe<BeforeHitEvent>(evt =>
            {
                if (evt.Skill.Data.Id == "act_cover_probe")
                    capturedCalc = evt.Calc;
            });
            engine.EventBus.Subscribe<AfterHitEvent>(evt => afterHitDefenders.Add(evt.Defender));

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "CoverAlly", "ModifyDamageCalc", "HealRatio" },
                aidCover.Effects.Select(effect => effect.EffectType).ToList());
            Assert.That(capturedCalc, Is.Not.Null);
            var calc = capturedCalc!;
            ClassicAssert.AreSame(cover, calc.CoverTarget);
            ClassicAssert.AreSame(cover, calc.ResolvedDefender);
            ClassicAssert.AreEqual(true, calc.ForceBlock);
            ClassicAssert.AreEqual(0.50f, calc.ForcedBlockReduction ?? -1f, 0.001f);
            ClassicAssert.IsTrue(calc.IsBlocked);
            ClassicAssert.AreEqual(0, cover.CurrentPp);
            ClassicAssert.AreEqual(90, defender.CurrentHp);
            ClassicAssert.AreEqual(50, cover.CurrentHp);
            CollectionAssert.AreEqual(new[] { cover }, afterHitDefenders);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_ArrowCover_CanTriggerAgainOnLaterRangedPhysicalAttack()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var arrowCover = repository.PassiveSkills["pas_cheer_shout"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", null, hp: 200, str: 50, def: 0, spd: 10, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var fastCover = new BattleUnit(CreatePassiveBattleCharacter(
                "fastCover", null, hp: 200, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 4
            };
            var slowCover = new BattleUnit(CreatePassiveBattleCharacter(
                "slowCover", null, hp: 200, str: 1, def: 0, spd: 20, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 5
            };
            fastCover.EquippedPassiveSkillIds.Add("pas_cheer_shout");
            slowCover.EquippedPassiveSkillIds.Add("pas_cheer_shout");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, fastCover, slowCover },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);

            eventBus.Publish(new BattleStartEvent { Context = context });
            var firstCalc = TestDataFactory.CreateCalc(attacker, defender, skill);
            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = firstCalc
            });

            var secondCalc = TestDataFactory.CreateCalc(attacker, defender, skill);
            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = secondCalc
            });

            CollectionAssert.Contains(arrowCover.Tags, "RangedCover");
            ClassicAssert.IsTrue(arrowCover.HasSimultaneousLimit);
            ClassicAssert.AreSame(fastCover, firstCalc.CoverTarget);
            ClassicAssert.IsTrue(firstCalc.NullifyPhysicalDamage);
            ClassicAssert.AreSame(fastCover, secondCalc.CoverTarget);
            ClassicAssert.IsTrue(secondCalc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(0, fastCover.CurrentPp);
            ClassicAssert.AreEqual(2, slowCover.CurrentPp);
            ClassicAssert.AreEqual(2, logs.Count(log => log.Contains("pas_cheer_shout") || log.Contains(arrowCover.Name)));
        }

        [Test]
        public void RealPassiveJson_ArrowCover_DoesNotCoverLongThrust()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var arrowCover = repository.PassiveSkills["pas_cheer_shout"];
            var longThrust = new ActiveSkill(repository.ActiveSkills["act_enhanced_spear"], repository);
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", null, hp: 200, str: 50, def: 0, spd: 10, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var cover = new BattleUnit(CreatePassiveBattleCharacter(
                "cover", null, hp: 200, str: 1, def: 0, spd: 20, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4,
                CurrentPp = 1
            };
            cover.EquippedPassiveSkillIds.Add("pas_cheer_shout");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, cover },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var calc = TestDataFactory.CreateCalc(attacker, defender, longThrust);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = longThrust,
                Context = context,
                Calc = calc
            });

            ClassicAssert.AreEqual(AttackType.Melee, longThrust.AttackType);
            ClassicAssert.AreEqual(TargetType.FrontAndBack, longThrust.TargetType);
            CollectionAssert.Contains(arrowCover.Tags, "RangedCover");
            ClassicAssert.IsNull(calc.CoverTarget);
            ClassicAssert.IsFalse(calc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(1, cover.CurrentPp);
            Assert.That(logs, Has.None.Contains(arrowCover.Name));
        }

        [Test]
        public void RealPassiveJson_ArrowCover_DoesNotTriggerWhenIncomingAttackCannotBeCovered()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var arrowCover = repository.PassiveSkills["pas_cheer_shout"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", null, hp: 200, str: 50, def: 0, spd: 10, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var cover = new BattleUnit(CreatePassiveBattleCharacter(
                "cover", null, hp: 200, str: 1, def: 0, spd: 20, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4,
                CurrentPp = 1
            };
            cover.EquippedPassiveSkillIds.Add("pas_cheer_shout");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, cover },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);
            calc.CannotBeCovered = true;

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });

            CollectionAssert.Contains(arrowCover.Tags, "RangedCover");
            ClassicAssert.IsNull(calc.CoverTarget);
            ClassicAssert.IsFalse(calc.NullifyPhysicalDamage);
            ClassicAssert.AreEqual(1, cover.CurrentPp);
            Assert.That(logs, Has.None.Contains(arrowCover.Name));
        }

        [Test]
        public void RealPassiveJson_QuickGuard_DoesNotTriggerWhenIncomingAttackCannotBeBlocked()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var quickGuard = repository.PassiveSkills["pas_end_heal25"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", null, hp: 200, str: 50, def: 0, spd: 10, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 1,
                CurrentPp = 1
            };
            defender.EquippedPassiveSkillIds.Add("pas_end_heal25");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Melee);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);
            calc.CannotBeBlocked = true;

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });

            CollectionAssert.Contains(quickGuard.Tags, "MediumGuard");
            ClassicAssert.IsNull(calc.ForceBlock);
            ClassicAssert.IsNull(calc.ForcedBlockReduction);
            ClassicAssert.AreEqual(1, defender.CurrentPp);
            Assert.That(logs, Has.None.Contains(quickGuard.Name));
        }

        [Test]
        public void RealPassiveJson_Concentration_RecoversApAndAddsHitForCurrentActiveUse()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_probe_magic"] = new ActiveSkillData
            {
                Id = "act_probe_magic",
                Name = "Probe Magic",
                ApCost = 1,
                Type = SkillType.Magical,
                AttackType = AttackType.Magic,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var concentration = repository.PassiveSkills["pas_concentration"];
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_probe_magic", hp: 200, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 2, pp: 2, mag: 100),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1,
                CurrentLevel = 30
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 300, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_concentration");
            caster.Strategies.Add(new Strategy { SkillId = "act_probe_magic" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();
            int hitDuringCurrentActiveSkill = -1;
            engine.EventBus.Subscribe<BeforeHitEvent>(evt =>
            {
                if (evt.Attacker == caster)
                    hitDuringCurrentActiveSkill = evt.Attacker.GetCurrentStat("Hit");
            });

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "RecoverAp", "AddBuff" },
                concentration.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(1, caster.CurrentPp);
            ClassicAssert.AreEqual(1, caster.CurrentAp);
            ClassicAssert.AreEqual(1040, hitDuringCurrentActiveSkill);
            ClassicAssert.AreEqual(1000, caster.GetCurrentStat("Hit"));
            ClassicAssert.Less(enemy.CurrentHp, 300);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_HawkEye_StacksDefAndBlockBuffsWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var hawkEye = repository.PassiveSkills["pas_hawk_eye"];
            hawkEye.Tags.Clear();
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 2), repository, true);
            defender.Data.BaseStats["Def"] = 100;
            defender.Data.BaseStats["Block"] = 30;
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            defender.EquippedPassiveSkillIds.Add("pas_hawk_eye");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var physicalSkill = TestDataFactory.CreateSkill(type: SkillType.Physical);
            var magicalSkill = TestDataFactory.CreateSkill(type: SkillType.Magical, attackType: AttackType.Magic);

            eventBus.Publish(new AfterHitEvent { Attacker = attacker, Defender = defender, Skill = magicalSkill, Context = context, DamageDealt = 1, IsHit = true });
            ClassicAssert.AreEqual(2, defender.CurrentPp);
            ClassicAssert.AreEqual(100, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(30, defender.GetCurrentBlockRate());

            eventBus.Publish(new AfterHitEvent { Attacker = attacker, Defender = defender, Skill = physicalSkill, Context = context, DamageDealt = 1, IsHit = true });
            eventBus.Publish(new AfterHitEvent { Attacker = attacker, Defender = defender, Skill = physicalSkill, Context = context, DamageDealt = 1, IsHit = true });

            CollectionAssert.AreEqual(new[] { "AddBuff", "AddBuff" }, hawkEye.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.AreEqual(140, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(70, defender.GetCurrentBlockRate());
            ClassicAssert.AreEqual(2, defender.Buffs.Count(buff => buff.SkillId == "pas_hawk_eye" && buff.TargetStat == "Def"));
            ClassicAssert.AreEqual(2, defender.Buffs.Count(buff => buff.SkillId == "pas_hawk_eye" && buff.TargetStat == "Block"));
            ClassicAssert.IsTrue(defender.Buffs.All(buff => buff.TargetStat != "Block" || buff.FlatAmount == 20));
            Assert.That(logs, Has.Some.Contains("defender.Def").And.Contains("defender.Block"));
        }

        [Test]
        public void RealPassiveJson_EmergencyCover_StacksStrAndHitBuffsWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var emergencyCover = repository.PassiveSkills["pas_emergency_cover"];
            emergencyCover.Tags.Clear();
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var caster = new BattleUnit(CreateCharacter("caster", hp: 100, pp: 2), repository, true);
            caster.Data.BaseStats["Str"] = 100;
            caster.Data.BaseStats["Hit"] = 70;
            var ally = new BattleUnit(CreateCharacter("ally", hp: 100, pp: 0), repository, true);
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            caster.EquippedPassiveSkillIds.Add("pas_emergency_cover");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, ally },
                EnemyUnits = new List<BattleUnit> { attacker }
            };

            eventBus.Publish(new AfterHitEvent { Attacker = attacker, Defender = ally, Context = context, DamageDealt = 1, IsHit = true });
            eventBus.Publish(new AfterHitEvent { Attacker = attacker, Defender = ally, Context = context, DamageDealt = 1, IsHit = true });

            CollectionAssert.AreEqual(new[] { "AddBuff", "AddBuff" }, emergencyCover.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.AreEqual(140, caster.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(110, caster.GetCurrentHitRate());
            ClassicAssert.AreEqual(2, caster.Buffs.Count(buff => buff.SkillId == "pas_emergency_cover" && buff.TargetStat == "Str"));
            ClassicAssert.AreEqual(2, caster.Buffs.Count(buff => buff.SkillId == "pas_emergency_cover" && buff.TargetStat == "Hit"));
            ClassicAssert.IsTrue(caster.Buffs.All(buff => buff.TargetStat != "Hit" || buff.FlatAmount == 20));
            Assert.That(logs, Has.Some.Contains("caster.Str").And.Contains("caster.Hit"));
        }

        [Test]
        public void RealPassiveJson_GriffinGlide_EvadesRangedActiveHitAndRestoresAp()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var griffinGlide = repository.PassiveSkills["pas_griffin_guard"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, true);
            defender.CurrentAp = 0;
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            defender.EquippedPassiveSkillIds.Add("pas_griffin_guard");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                HitCount = 1
            };

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });

            CollectionAssert.AreEqual(new[] { "ModifyDamageCalc", "RecoverAp" }, griffinGlide.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.IsTrue(griffinGlide.Effects.All(effect =>
                ReadString(effect.Parameters, "requiresIncomingAttackType") == "Ranged"
                && ReadString(effect.Parameters, "requiresSourceKind") == "ActiveAttack"));
            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.IsTrue(calc.ForceEvasion);
            ClassicAssert.AreEqual(1, defender.CurrentAp);
            ClassicAssert.IsFalse(calc.ForceBlock.GetValueOrDefault());
            Assert.That(logs, Has.Some.Contains("ForceEvasion").And.Contains("defender.AP 0->1"));
        }

        [Test]
        public void RealPassiveJson_GriffinGlide_DoesNotTriggerForPendingRangedHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, true)
            {
                CurrentAp = 0
            };
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false);
            defender.EquippedPassiveSkillIds.Add("pas_griffin_guard");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc,
                SourceKind = BattleActionSourceKind.PendingAction
            });

            ClassicAssert.AreEqual(1, defender.CurrentPp);
            ClassicAssert.AreEqual(0, defender.CurrentAp);
            ClassicAssert.IsFalse(calc.ForceEvasion);
            ClassicAssert.IsEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_WingRest_HealsMoreWhenSelfHasAilmentAtBattleEnd()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var wingRest = repository.PassiveSkills["pas_griffin_wing_rest"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var normal = new BattleUnit(CreateCharacter("normal", hp: 100, pp: 1), repository, true)
            {
                CurrentHp = 50
            };
            var poisoned = new BattleUnit(CreateCharacter("poisoned", hp: 100, pp: 1), repository, true)
            {
                CurrentHp = 50
            };
            normal.EquippedPassiveSkillIds.Add("pas_griffin_wing_rest");
            poisoned.EquippedPassiveSkillIds.Add("pas_griffin_wing_rest");
            poisoned.Ailments.Add(StatusAilment.Poison);
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { normal, poisoned }
            };

            eventBus.Publish(new BattleEndEvent { Context = context });

            CollectionAssert.AreEqual(new[] { "HealRatio", "HealRatio" }, wingRest.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(75, normal.CurrentHp);
            ClassicAssert.AreEqual(100, poisoned.CurrentHp);
            ClassicAssert.AreEqual(0, normal.CurrentPp);
            ClassicAssert.AreEqual(0, poisoned.CurrentPp);
        }

        [Test]
        public void RealPassiveJson_WingStroke_BuffsSameRowAlliesBeforeOwnActiveAttack()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var wingStroke = repository.PassiveSkills["pas_griffin_wing_stroke"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var caster = new BattleUnit(CreateCharacter("caster", hp: 100, pp: 1), repository, true)
            {
                Position = 1
            };
            var sameRowAlly = new BattleUnit(CreateCharacter("sameRowAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 2
            };
            var backRowAlly = new BattleUnit(CreateCharacter("backRowAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 4
            };
            caster.EquippedPassiveSkillIds.Add("pas_griffin_wing_stroke");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, sameRowAlly, backRowAlly }
            };
            var skill = TestDataFactory.CreateSkill();

            eventBus.Publish(new BeforeAttackCalculationEvent
            {
                Caster = caster,
                Skill = skill,
                Context = context
            });

            CollectionAssert.AreEqual(new[] { "AddBuff" }, wingStroke.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.AreEqual(25, caster.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(25, sameRowAlly.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(10, backRowAlly.GetCurrentStat("Spd"));
            Assert.That(logs, Has.Some.Contains("caster.Spd").And.Contains("sameRowAlly.Spd"));
        }

        [Test]
        public void RealPassiveJson_RapidReload_AugmentsCurrentActionOnHitAndDoesNotNeedLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_reload_probe"] = new ActiveSkillData
            {
                Id = "act_reload_probe",
                Name = "Reload Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.AllEnemies,
                Effects = new List<SkillEffectData>()
            };
            var rapidReload = repository.PassiveSkills["pas_rapid_reload"];
            rapidReload.Tags.Clear();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_reload_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 2, pp: 1),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var firstEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "firstEnemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var secondEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "secondEnemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 2
            };
            caster.EquippedPassiveSkillIds.Add("pas_rapid_reload");
            caster.Strategies.Add(new Strategy { SkillId = "act_reload_probe" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { firstEnemy, secondEnemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, rapidReload.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            CollectionAssert.Contains(firstEnemy.Ailments, StatusAilment.BlockSeal);
            CollectionAssert.Contains(secondEnemy.Ailments, StatusAilment.BlockSeal);
            ClassicAssert.AreEqual(0, context.CurrentActionAugments.Count);
            var activeEntries = engine.BattleLogEntries
                .Where(entry => entry.SkillId == "act_reload_probe" && entry.Flags.Contains("ActiveAttack"))
                .ToList();
            ClassicAssert.AreEqual(2, activeEntries.Count);
            Assert.That(activeEntries, Has.All.Matches<BattleLogEntry>(entry =>
                entry.Flags.Contains("Augment:pas_rapid_reload")
                && entry.Text.Contains("augments=pas_rapid_reload", StringComparison.Ordinal)));
            Assert.That(logs, Has.Some.Contains("AugmentCurrentAction").And.Contains("pas_rapid_reload"));
            Assert.That(logs, Has.Some.Contains("augment post effects:").And.Contains("BlockSeal"));
        }

        [Test]
        public void RealPassiveJson_QuickReload_QueuesRangedPursuitAgainstCurrentActiveTarget()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_quick_reload_probe"] = new ActiveSkillData
            {
                Id = "act_quick_reload_probe",
                Name = "Quick Reload Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var quickReload = repository.PassiveSkills["pas_quick_reload"];
            quickReload.Tags.Clear();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_quick_reload_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 1),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_quick_reload");
            caster.Strategies.Add(new Strategy { SkillId = "act_quick_reload_probe" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, quickReload.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.Less(enemy.CurrentHp, 200 - 5, "pending pursuit should add damage after the active hit");
            var pursuitEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_quick_reload");
            ClassicAssert.AreEqual(caster.Data.Id, pursuitEntry.ActorId);
            CollectionAssert.Contains(pursuitEntry.Flags, "PassiveTrigger");
            CollectionAssert.Contains(pursuitEntry.Flags, "Pursuit");
            CollectionAssert.Contains(pursuitEntry.Flags, "Ranged");
            StringAssert.Contains("快速装填", pursuitEntry.Text);
            StringAssert.Contains("caster追击enemy", pursuitEntry.Text);
            Assert.That(logs, Has.Some.Contains("准备追击").And.Contains("威力50"));
        }

        [Test]
        public void RealPassiveJson_QuickAction_RecoversActiveAllyApAfterCost()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_quick_action_probe"] = new ActiveSkillData
            {
                Id = "act_quick_action_probe",
                Name = "Quick Action Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var quickAction = repository.PassiveSkills["pas_quick_action"];
            var activeUser = new BattleUnit(CreatePassiveBattleCharacter(
                "active_user", "act_quick_action_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 2, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var support = new BattleUnit(CreatePassiveBattleCharacter(
                "support", null, hp: 200, str: 10, def: 0, spd: 50, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4,
                CurrentLevel = 10
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            support.EquippedPassiveSkillIds.Add("pas_quick_action");
            activeUser.Strategies.Add(new Strategy { SkillId = "act_quick_action_probe" });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { activeUser, support },
                EnemyUnits = new List<BattleUnit> { enemy }
            })
            { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "RecoverAp" }, quickAction.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(1, activeUser.CurrentAp);
            ClassicAssert.AreEqual(0, support.CurrentPp);
            ClassicAssert.IsTrue(logs.Any(log => log.Contains("pas_quick_action") || log.Contains("快速行动")));
        }

        [Test]
        public void RealPassiveJson_ShadowStep_BuffsOwnSpeedAndEvasionBeforeActiveAttack()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_shadow_step_probe"] = new ActiveSkillData
            {
                Id = "act_shadow_step_probe",
                Name = "Shadow Step Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var shadowStep = repository.PassiveSkills["pas_shadow_step"];
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_shadow_step_probe", hp: 200, str: 50, def: 0, spd: 40, hit: 1000, eva: 20, ap: 1, pp: 2),
                repository,
                true)
            {
                Position = 1,
                CurrentLevel = 25
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_shadow_step");
            caster.Strategies.Add(new Strategy { SkillId = "act_shadow_step_probe" });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            })
            { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AddBuff", "AddBuff" }, shadowStep.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(70, caster.GetCurrentSpeed());
            ClassicAssert.AreEqual(50, caster.GetCurrentEvasion());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.IsTrue(logs.Any(log => log.Contains("pas_shadow_step") || log.Contains("暗影步")));
        }

        [Test]
        public void RealPassiveJson_CutGrass_QueuesPursuitAgainstCurrentActiveTargetWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_cut_grass_probe"] = new ActiveSkillData
            {
                Id = "act_cut_grass_probe",
                Name = "Cut Grass Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var cutGrass = repository.PassiveSkills["pas_cut_grass"];
            cutGrass.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_cut_grass_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var hunter = new BattleUnit(CreatePassiveBattleCharacter(
                "hunter", null, hp: 200, str: 60, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_cut_grass_probe" });
            hunter.EquippedPassiveSkillIds.Add("pas_cut_grass");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, hunter },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, cutGrass.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, hunter.CurrentPp);
            ClassicAssert.AreEqual(100, cutGrass.HitRate);
            var queuedPursuit = ReadNestedEffects(cutGrass.Effects.Single().Parameters["queuedActions"]);
            ClassicAssert.AreEqual(100, ReadInt(queuedPursuit.Single().Parameters["hitRate"]));
            var pursuitEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_cut_grass");
            ClassicAssert.AreEqual(hunter.Data.Id, pursuitEntry.ActorId);
            CollectionAssert.AreEqual(new[] { enemy.Data.Id }, pursuitEntry.TargetIds);
            CollectionAssert.Contains(pursuitEntry.Flags, "PassiveTrigger");
            CollectionAssert.Contains(pursuitEntry.Flags, "Pursuit");
            ClassicAssert.Greater(pursuitEntry.Damage, 0);
            ClassicAssert.Less(enemy.CurrentHp, 200 - 5, "pursuit should add damage after the active hit");
            Assert.That(logs, Has.Some.Contains("AugmentCurrentAction").And.Contains("pas_cut_grass"));
            Assert.That(logs, Has.Some.Contains("准备追击").And.Contains("威力75"));
        }

        [Test]
        public void RealPassiveJson_Fervor_QueuesPower50CounterAndAddsFlyingPowerBonusWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_fervor_probe"] = new ActiveSkillData
            {
                Id = "act_fervor_probe",
                Name = "Fervor Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var fervor = repository.PassiveSkills["pas_fervor"];
            fervor.Tags.Clear();

            int RunCounterScenario(string attackerId, List<UnitClass> attackerClasses)
            {
                var hunter = new BattleUnit(CreatePassiveBattleCharacter(
                    $"hunter_{attackerId}", null, hp: 500, str: 100, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 1),
                    repository,
                    true)
                {
                    Position = 4
                };
                var ally = new BattleUnit(CreatePassiveBattleCharacter(
                    $"ally_{attackerId}", null, hp: 500, str: 1, def: 0, spd: 10, hit: 0, eva: 0, ap: 0, pp: 0),
                    repository,
                    true)
                {
                    Position = 1
                };
                var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                    attackerId, "act_fervor_probe", hp: 500, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, classes: attackerClasses),
                    repository,
                    false)
                {
                    Position = 1,
                    CurrentAp = 1
                };
                attacker.Strategies.Add(new Strategy
                {
                    SkillId = "act_fervor_probe",
                    Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                    Mode1 = ConditionMode.Priority
                });
                hunter.EquippedPassiveSkillIds.Add("pas_fervor");
                var context = new BattleContext(repository)
                {
                    PlayerUnits = new List<BattleUnit> { ally, hunter },
                    EnemyUnits = new List<BattleUnit> { attacker }
                };
                var logs = new List<string>();
                var engine = new BattleEngine(context) { OnLog = logs.Add };
                var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
                processor.SubscribeAll();

                var result = engine.StepOneAction();

                ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
                ClassicAssert.AreEqual(0, hunter.CurrentPp);
                var counterEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_fervor");
                ClassicAssert.AreEqual(hunter.Data.Id, counterEntry.ActorId);
                CollectionAssert.AreEqual(new[] { attacker.Data.Id }, counterEntry.TargetIds);
                CollectionAssert.Contains(counterEntry.Flags, "PassiveTrigger");
                CollectionAssert.Contains(counterEntry.Flags, "Counter");
                Assert.That(logs, Has.Some.Contains("准备反击").And.Contains("威力50"));
                return counterEntry.Damage;
            }

            var infantryDamage = RunCounterScenario("infantry_attacker", new List<UnitClass> { UnitClass.Infantry });
            var flyingDamage = RunCounterScenario("flying_attacker", new List<UnitClass> { UnitClass.Flying });

            CollectionAssert.AreEqual(
                new[] { "CounterAttack", "ModifyDamageCalc" },
                fervor.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(50, infantryDamage);
            ClassicAssert.AreEqual(150, flyingDamage);
        }

        [Test]
        public void RealPassiveJson_Fervor_DoesNotTriggerFromPendingCounterHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_fervor_pending_probe"] = new ActiveSkillData
            {
                Id = "act_fervor_pending_probe",
                Name = "Fervor Pending Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var playerAlly = new BattleUnit(CreatePassiveBattleCharacter(
                "player_ally", null, hp: 500, str: 1, def: 0, spd: 10, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var playerHunter = new BattleUnit(CreatePassiveBattleCharacter(
                "player_hunter", null, hp: 500, str: 100, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4
            };
            var enemyAttacker = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy_attacker", "act_fervor_pending_probe", hp: 500, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1,
                CurrentAp = 1
            };
            var enemySniper = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy_sniper", null, hp: 500, str: 100, def: 0, spd: 40, hit: 1000, eva: 0, ap: 0, pp: 1),
                repository,
                false)
            {
                Position = 4
            };
            playerHunter.EquippedPassiveSkillIds.Add("pas_fervor");
            enemySniper.EquippedPassiveSkillIds.Add("pas_fervor");
            enemyAttacker.Strategies.Add(new Strategy { SkillId = "act_fervor_pending_probe" });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { playerAlly, playerHunter },
                EnemyUnits = new List<BattleUnit> { enemyAttacker, enemySniper }
            })
            { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(0, playerHunter.CurrentPp);
            ClassicAssert.AreEqual(1, enemySniper.CurrentPp);
            ClassicAssert.AreEqual(1, engine.BattleLogEntries.Count(entry => entry.SkillId == "pas_fervor"));
            ClassicAssert.AreEqual(playerHunter.Data.Id, engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_fervor").ActorId);
        }

        [Test]
        public void AllyOnAttackedPassives_TriggerWhenOwnerIsTheDefender()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var logs = new List<string>();
            var queued = new List<PendingAction>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add, queued.Add);
            processor.SubscribeAll();
            var breaker = new BattleUnit(CreatePassiveBattleCharacter(
                "breaker", null, hp: 200, str: 50, def: 0, spd: 50, hit: 100, eva: 0, ap: 0, pp: 1),
                repository,
                true);
            var sniper = new BattleUnit(CreatePassiveBattleCharacter(
                "sniper", null, hp: 200, str: 60, def: 0, spd: 40, hit: 100, eva: 0, ap: 0, pp: 1),
                repository,
                true);
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 10, def: 0, spd: 10, hit: 100, eva: 0, ap: 0, pp: 0),
                repository,
                false);
            breaker.EquippedPassiveSkillIds.Add("pas_emergency_cover");
            sniper.EquippedPassiveSkillIds.Add("pas_fervor");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { breaker, sniper },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var skill = TestDataFactory.CreateSkill(power: 10, attackType: AttackType.Ranged);

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = enemy,
                Defender = breaker,
                Skill = skill,
                Context = context,
                DamageDealt = 1,
                IsHit = true,
                SourceKind = BattleActionSourceKind.ActiveAttack
            });

            ClassicAssert.AreEqual(0, breaker.CurrentPp);
            ClassicAssert.AreEqual(1, breaker.Buffs.Count(buff => buff.SkillId == "pas_emergency_cover" && buff.TargetStat == "Str"));
            ClassicAssert.AreEqual(1, breaker.Buffs.Count(buff => buff.SkillId == "pas_emergency_cover" && buff.TargetStat == "Hit"));
            queued.Clear();

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = enemy,
                Defender = sniper,
                Skill = skill,
                Context = context,
                DamageDealt = 1,
                IsHit = true,
                SourceKind = BattleActionSourceKind.ActiveAttack
            });

            var action = queued.Single(action => action.SourcePassiveId == "pas_fervor");
            ClassicAssert.AreSame(sniper, action.Actor);
            CollectionAssert.AreEqual(new[] { enemy }, action.Targets);
            ClassicAssert.AreEqual(50, action.Power);
            ClassicAssert.AreEqual(1, action.SourcePpCost);
            ClassicAssert.AreEqual(1, sniper.CurrentPp, "Counter PP is still deferred until the pending action resolves.");
        }

        [Test]
        public void RealPassiveJson_MagicPursuit_QueuesPursuitAfterAllyMagicalActiveOnly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_magic_absorb_probe"] = new ActiveSkillData
            {
                Id = "act_magic_absorb_probe",
                Name = "Magic Absorb Probe",
                ApCost = 1,
                Type = SkillType.Magical,
                AttackType = AttackType.Magic,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            repository.ActiveSkills["act_magic_absorb_physical_probe"] = new ActiveSkillData
            {
                Id = "act_magic_absorb_physical_probe",
                Name = "Magic Absorb Physical Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var magicAttacker = new BattleUnit(CreatePassiveBattleCharacter(
                "magicAttacker", "act_magic_absorb_probe", hp: 200, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 50),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var wizard = new BattleUnit(CreatePassiveBattleCharacter(
                "wizard", null, hp: 200, str: 1, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 2, mag: 80),
                repository,
                true)
            {
                Position = 4,
                CurrentPp = 1
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            magicAttacker.Strategies.Add(new Strategy { SkillId = "act_magic_absorb_probe" });
            wizard.EquippedPassiveSkillIds.Add("pas_magic_pursuit");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { magicAttacker, wizard },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, repository.PassiveSkills["pas_magic_pursuit"].Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, wizard.CurrentPp);
            ClassicAssert.Less(enemy.CurrentHp, 195, "Active probe alone deals 5 damage; magical pursuit should add a second hit.");
            Assert.That(engine.BattleLogEntries, Has.Some.Matches<BattleLogEntry>(entry => entry.SkillId == "pas_magic_pursuit"));

            var physicalAttacker = new BattleUnit(CreatePassiveBattleCharacter(
                "physicalAttacker", "act_magic_absorb_physical_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var physicalWizard = new BattleUnit(CreatePassiveBattleCharacter(
                "physicalWizard", null, hp: 200, str: 1, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 2, mag: 80),
                repository,
                true)
            {
                Position = 4,
                CurrentPp = 1
            };
            var physicalEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "physicalEnemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            physicalAttacker.Strategies.Add(new Strategy { SkillId = "act_magic_absorb_physical_probe" });
            physicalWizard.EquippedPassiveSkillIds.Add("pas_magic_pursuit");
            var physicalContext = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { physicalAttacker, physicalWizard },
                EnemyUnits = new List<BattleUnit> { physicalEnemy }
            };
            var physicalLogs = new List<string>();
            var physicalEngine = new BattleEngine(physicalContext) { OnLog = physicalLogs.Add };
            var physicalProcessor = new PassiveSkillProcessor(physicalEngine.EventBus, repository, physicalLogs.Add, physicalEngine.EnqueueAction);
            physicalProcessor.SubscribeAll();

            result = physicalEngine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(1, physicalWizard.CurrentPp);
            Assert.That(physicalEngine.BattleLogEntries, Has.None.Matches<BattleLogEntry>(entry => entry.SkillId == "pas_magic_pursuit"));
        }

        [Test]
        public void RealPassiveJson_MagicBlade_AddsMagicalDamageToCurrentPhysicalActionOnly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_magic_blade_physical_probe"] = new ActiveSkillData
            {
                Id = "act_magic_blade_physical_probe",
                Name = "Magic Blade Physical Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            repository.ActiveSkills["act_magic_blade_magical_probe"] = new ActiveSkillData
            {
                Id = "act_magic_blade_magical_probe",
                Name = "Magic Blade Magical Probe",
                ApCost = 1,
                Type = SkillType.Magical,
                AttackType = AttackType.Magic,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var magicBlade = repository.PassiveSkills["pas_magic_blade"];
            magicBlade.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_magic_blade_physical_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 50),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var wizard = new BattleUnit(CreatePassiveBattleCharacter(
                "wizard", null, hp: 200, str: 1, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 1, mag: 80),
                repository,
                true)
            {
                Position = 4
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_magic_blade_physical_probe" });
            wizard.EquippedPassiveSkillIds.Add("pas_magic_blade");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, wizard },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();
            float additionalMagicalPowerDuringCurrentActiveSkill = -1;
            bool hasMixedDamageDuringCurrentActiveSkill = false;
            engine.EventBus.Subscribe<BeforeHitEvent>(evt =>
            {
                if (evt.Attacker == attacker)
                {
                    additionalMagicalPowerDuringCurrentActiveSkill = evt.Calc.AdditionalMagicalPower;
                    hasMixedDamageDuringCurrentActiveSkill = evt.Calc.HasMixedDamage;
                }
            });

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, magicBlade.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, wizard.CurrentPp);
            ClassicAssert.AreEqual(50, additionalMagicalPowerDuringCurrentActiveSkill);
            ClassicAssert.IsTrue(hasMixedDamageDuringCurrentActiveSkill);
            ClassicAssert.AreEqual(170, enemy.CurrentHp);
            ClassicAssert.AreEqual(50, attacker.GetCurrentStat("Mag"));
            Assert.That(logs, Has.Some.Contains("魔法剑刃").And.Contains("友方攻击前"));

            var magicAttacker = new BattleUnit(CreatePassiveBattleCharacter(
                "magicAttacker", "act_magic_blade_magical_probe", hp: 200, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 50),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var magicWizard = new BattleUnit(CreatePassiveBattleCharacter(
                "magicWizard", null, hp: 200, str: 1, def: 0, spd: 50, hit: 1000, eva: 0, ap: 0, pp: 1, mag: 80),
                repository,
                true)
            {
                Position = 4
            };
            var magicEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "magicEnemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            magicAttacker.Strategies.Add(new Strategy { SkillId = "act_magic_blade_magical_probe" });
            magicWizard.EquippedPassiveSkillIds.Add("pas_magic_blade");
            var magicContext = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { magicAttacker, magicWizard },
                EnemyUnits = new List<BattleUnit> { magicEnemy }
            };
            var magicLogs = new List<string>();
            var magicEngine = new BattleEngine(magicContext) { OnLog = magicLogs.Add };
            var magicProcessor = new PassiveSkillProcessor(magicEngine.EventBus, repository, magicLogs.Add, magicEngine.EnqueueAction);
            magicProcessor.SubscribeAll();

            result = magicEngine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(1, magicWizard.CurrentPp);
            Assert.That(magicEngine.BattleLogEntries, Has.None.Matches<BattleLogEntry>(entry => entry.SkillId == "pas_magic_blade"));
            Assert.That(magicLogs, Has.None.Contains("pas_magic_blade"));
        }

        [Test]
        public void RealPassiveJson_CurseSwamp_BattleStartDebuffsEnemiesAndDamagesOnlyEffectiveCavalryPp()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var curseSwamp = repository.PassiveSkills["pas_curse_swamp"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 1
            };
            var allyCavalry = new BattleUnit(CreatePassiveBattleCharacter(
                "allyCavalry", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 2,
                classes: new List<UnitClass> { UnitClass.Cavalry }),
                repository,
                true)
            {
                Position = 2
            };
            var enemyInfantry = new BattleUnit(CreatePassiveBattleCharacter(
                "enemyInfantry", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 2),
                repository,
                false)
            {
                Position = 1
            };
            var enemyCavalry = new BattleUnit(CreatePassiveBattleCharacter(
                "enemyCavalry", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 2,
                classes: new List<UnitClass> { UnitClass.Cavalry }),
                repository,
                false)
            {
                Position = 2
            };
            var enemyCcCavalry = new BattleUnit(CreatePassiveBattleCharacter(
                "enemyCcCavalry", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 1,
                classes: new List<UnitClass> { UnitClass.Infantry },
                ccClasses: new List<UnitClass> { UnitClass.Cavalry }),
                repository,
                false,
                isCc: true)
            {
                Position = 3
            };
            var enemyCavalryZeroPp = new BattleUnit(CreatePassiveBattleCharacter(
                "enemyCavalryZeroPp", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 50, ap: 0, pp: 0,
                classes: new List<UnitClass> { UnitClass.Cavalry }),
                repository,
                false)
            {
                Position = 4
            };
            caster.EquippedPassiveSkillIds.Add("pas_curse_swamp");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, allyCavalry },
                EnemyUnits = new List<BattleUnit> { enemyInfantry, enemyCavalry, enemyCcCavalry, enemyCavalryZeroPp }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            CollectionAssert.AreEqual(
                new[] { "AddDebuff", "AddDebuff", "PpDamage" },
                curseSwamp.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            foreach (var enemy in context.EnemyUnits)
            {
                ClassicAssert.AreEqual(40, enemy.GetCurrentStat("Spd"), enemy.Data.Id);
                ClassicAssert.AreEqual(20, enemy.GetCurrentStat("Eva"), enemy.Data.Id);
            }
            ClassicAssert.AreEqual(2, enemyInfantry.CurrentPp);
            ClassicAssert.AreEqual(1, enemyCavalry.CurrentPp);
            ClassicAssert.AreEqual(0, enemyCcCavalry.CurrentPp);
            ClassicAssert.AreEqual(0, enemyCavalryZeroPp.CurrentPp);
            ClassicAssert.AreEqual(50, caster.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(50, caster.GetCurrentStat("Eva"));
            ClassicAssert.AreEqual(50, allyCavalry.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(50, allyCavalry.GetCurrentStat("Eva"));
            ClassicAssert.AreEqual(2, allyCavalry.CurrentPp);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_RapidOrder_BattleStartBuffsAliveAlliesOnlyAndConsumesCasterPp()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var rapidOrder = repository.PassiveSkills["pas_rapid_order"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", null, hp: 100, str: 1, def: 0, spd: 30, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 1
            };
            var ally = new BattleUnit(CreatePassiveBattleCharacter(
                "ally", null, hp: 100, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_rapid_order");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, ally },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            CollectionAssert.AreEqual(new[] { "AddBuff" }, rapidOrder.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(50, caster.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(60, ally.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(50, enemy.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(1, caster.CurrentPp);
            ClassicAssert.AreEqual(1, ally.CurrentPp);
            ClassicAssert.AreEqual(1, enemy.CurrentPp);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_RapidOrder_SimultaneousLimitAllowsOnlyFastestCasterPerSide()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var rapidOrder = repository.PassiveSkills["pas_rapid_order"];
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();
            var fast = new BattleUnit(CreatePassiveBattleCharacter(
                "fast", null, hp: 100, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 1
            };
            var slow = new BattleUnit(CreatePassiveBattleCharacter(
                "slow", null, hp: 100, str: 1, def: 0, spd: 20, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                false)
            {
                Position = 1
            };
            fast.EquippedPassiveSkillIds.Add("pas_rapid_order");
            slow.EquippedPassiveSkillIds.Add("pas_rapid_order");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { fast, slow },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });

            ClassicAssert.IsTrue(rapidOrder.HasSimultaneousLimit);
            ClassicAssert.AreEqual(60, fast.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(40, slow.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(50, enemy.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(1, fast.CurrentPp);
            ClassicAssert.AreEqual(2, slow.CurrentPp);
        }

        [Test]
        public void RealPassiveJson_MuscleSwelling_AugmentsOtherAllyAttackWithForcedCritAndSimultaneousLimit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_muscle_probe"] = new ActiveSkillData
            {
                Id = "act_muscle_probe",
                Name = "Muscle Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var muscleSwelling = repository.PassiveSkills["pas_muscle_swelling"];
            muscleSwelling.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_muscle_probe", hp: 500, str: 60, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var fastSupport = new BattleUnit(CreatePassiveBattleCharacter(
                "fastSupport", null, hp: 500, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 2
            };
            fastSupport.Data.BaseStats["Crit"] = 80;
            var slowSupport = new BattleUnit(CreatePassiveBattleCharacter(
                "slowSupport", null, hp: 500, str: 1, def: 0, spd: 30, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 3
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 10, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            fastSupport.EquippedPassiveSkillIds.Add("pas_muscle_swelling");
            slowSupport.EquippedPassiveSkillIds.Add("pas_muscle_swelling");
            attacker.Strategies.Add(new Strategy { SkillId = "act_muscle_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, fastSupport, slowSupport },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, muscleSwelling.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, fastSupport.CurrentPp);
            ClassicAssert.AreEqual(1, slowSupport.CurrentPp);
            ClassicAssert.AreEqual(0, attacker.GetCurrentCritRate());
            ClassicAssert.AreEqual(80, fastSupport.GetCurrentCritRate());
            ClassicAssert.AreEqual(0, fastSupport.ActionOrderPriority);
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_muscle_probe");
            CollectionAssert.Contains(activeEntry.Flags, "Critical");
            CollectionAssert.Contains(activeEntry.Flags, "Augment:pas_muscle_swelling");
            ClassicAssert.AreEqual(75, activeEntry.Damage);
            Assert.That(logs, Has.Some.Contains("AugmentCurrentAction").And.Contains("pas_muscle_swelling"));
            Assert.That(logs, Has.Some.Contains("augment calc effects:").And.Contains("ForceCrit"));
        }

        [Test]
        public void RealPassiveJson_MuscleSwelling_DoesNotTriggerForOwnAttack()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_muscle_self_probe"] = new ActiveSkillData
            {
                Id = "act_muscle_self_probe",
                Name = "Muscle Self Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var muscleSwelling = repository.PassiveSkills["pas_muscle_swelling"];
            muscleSwelling.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_muscle_self_probe", hp: 500, str: 60, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 1),
                repository,
                true)
            {
                Position = 1
            };
            attacker.Data.BaseStats["Crit"] = 0;
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 10, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            attacker.EquippedPassiveSkillIds.Add("pas_muscle_swelling");
            attacker.Strategies.Add(new Strategy { SkillId = "act_muscle_self_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(1, attacker.CurrentPp);
            ClassicAssert.AreEqual(0, attacker.ActionOrderPriority);
            ClassicAssert.AreEqual(0, attacker.GetCurrentCritRate());
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_muscle_self_probe");
            CollectionAssert.DoesNotContain(activeEntry.Flags, "Critical");
            CollectionAssert.DoesNotContain(activeEntry.Flags, "Augment:pas_muscle_swelling");
            ClassicAssert.AreEqual(50, activeEntry.Damage);
            Assert.That(logs, Has.None.Contains("pas_muscle_swelling"));
        }

        [Test]
        public void RealPassiveJson_MuscleSwelling_DoesNotTriggerForHealAction()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_heal_probe"] = new ActiveSkillData
            {
                Id = "act_heal_probe",
                Name = "Heal Probe",
                ApCost = 1,
                Type = SkillType.Heal,
                AttackType = AttackType.Magic,
                Power = 0,
                HitRate = null,
                TargetType = TargetType.SingleAlly,
                Effects = new List<SkillEffectData>()
            };
            var muscleSwelling = repository.PassiveSkills["pas_muscle_swelling"];
            muscleSwelling.Tags.Clear();
            var healer = new BattleUnit(CreatePassiveBattleCharacter(
                "healer", "act_heal_probe", hp: 500, str: 1, def: 0, spd: 100, hit: 0, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var support = new BattleUnit(CreatePassiveBattleCharacter(
                "support", null, hp: 500, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            healer.Strategies.Add(new Strategy { SkillId = "act_heal_probe" });
            support.EquippedPassiveSkillIds.Add("pas_muscle_swelling");
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { healer, support },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(1, support.CurrentPp);
            Assert.That(logs, Has.None.Contains("pas_muscle_swelling"));
        }

        [Test]
        public void RealPassiveJson_QuickCast_BattleStartSetsFastestActionOrderAndCritDebuffWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_order_probe"] = new ActiveSkillData
            {
                Id = "act_order_probe",
                Name = "Order Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var quickCast = repository.PassiveSkills["pas_quick_cast"];
            quickCast.Tags.Clear();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_order_probe", hp: 500, str: 30, def: 0, spd: 1, hit: 1000, eva: 0, ap: 1, pp: 2),
                repository,
                true)
            {
                Position = 1
            };
            caster.Data.BaseStats["Crit"] = 80;
            var fasterEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "fasterEnemy", "act_order_probe", hp: 500, str: 30, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_quick_cast");
            caster.Strategies.Add(new Strategy { SkillId = "act_order_probe" });
            fasterEnemy.Strategies.Add(new Strategy { SkillId = "act_order_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { fasterEnemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();
            int critDuringCurrentAction = -1;
            engine.EventBus.Subscribe<BeforeHitEvent>(evt =>
            {
                if (evt.Attacker == caster)
                    critDuringCurrentAction = evt.Attacker.GetCurrentCritRate();
            });

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "ActionOrderPriority", "AddDebuff" },
                quickCast.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(1000, caster.ActionOrderPriority);
            ClassicAssert.AreEqual(40, critDuringCurrentAction);
            ClassicAssert.AreEqual(80, caster.GetCurrentCritRate(), "Quick Cast crit debuff is one-time and clears after the current action.");
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.AreEqual(0, caster.CurrentAp);
            ClassicAssert.AreEqual(1, fasterEnemy.CurrentAp);
            Assert.That(engine.BattleLogEntries.First(entry => entry.Flags.Contains("ActiveAttack")).ActorId, Is.EqualTo("caster"));
            Assert.That(logs, Has.Some.Contains("ActionOrderPriority").And.Contains("caster.Crit"));
        }

        [Test]
        public void RealPassiveJson_HundredCrit_BattleStartBuffsBlockAndForcesEnemyTargetWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_taunt_probe"] = new ActiveSkillData
            {
                Id = "act_taunt_probe",
                Name = "Taunt Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var hundredCrit = repository.PassiveSkills["pas_hundred_crit"];
            hundredCrit.Tags.Clear();
            var taunter = new BattleUnit(CreatePassiveBattleCharacter(
                "taunter", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 1
            };
            taunter.Data.BaseStats["Block"] = 10;
            var lowHpAlly = new BattleUnit(CreatePassiveBattleCharacter(
                "lowHpAlly", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 2,
                CurrentHp = 50
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", "act_taunt_probe", hp: 500, str: 30, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            taunter.EquippedPassiveSkillIds.Add("pas_hundred_crit");
            enemy.Strategies.Add(new Strategy
            {
                SkillId = "act_taunt_probe",
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { taunter, lowHpAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "AddBuff", "ForcedTarget" },
                hundredCrit.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(15, taunter.GetCurrentBlockRate());
            ClassicAssert.AreEqual(0, taunter.CurrentPp);
            ClassicAssert.IsTrue(taunter.TemporalStates.Any(state => state.Key == "ForcedTarget"));
            var activeEntry = engine.BattleLogEntries.First(entry => entry.SkillId == "act_taunt_probe");
            CollectionAssert.AreEqual(new[] { "taunter" }, activeEntry.TargetIds);
            ClassicAssert.AreEqual(50, lowHpAlly.CurrentHp);
            Assert.That(logs, Has.Some.Contains("taunter.Block").And.Contains("ForcedTarget"));
        }

        [Test]
        public void RealPassiveJson_ForcedTarget_AffectsOnlyOneEnemyRowAndKeepsMeleeLegality()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();
            var taunter = new BattleUnit(CreatePassiveBattleCharacter(
                "taunter", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4
            };
            taunter.Data.BaseStats["Block"] = 10;
            var frontAlly = new BattleUnit(CreatePassiveBattleCharacter(
                "frontAlly", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentHp = 50
            };
            var frontRangedEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "frontRangedEnemy", null, hp: 500, str: 30, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var frontMeleeEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "frontMeleeEnemy", null, hp: 500, str: 30, def: 0, spd: 90, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 2
            };
            var backRangedEnemy = new BattleUnit(CreatePassiveBattleCharacter(
                "backRangedEnemy", null, hp: 500, str: 30, def: 0, spd: 80, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 4
            };
            taunter.EquippedPassiveSkillIds.Add("pas_hundred_crit");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { taunter, frontAlly },
                EnemyUnits = new List<BattleUnit> { frontRangedEnemy, frontMeleeEnemy, backRangedEnemy }
            };
            var rangedSkill = new ActiveSkillData
            {
                Id = "act_forced_target_ranged_probe",
                Name = "Forced Target Ranged Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var meleeSkill = new ActiveSkillData
            {
                Id = "act_forced_target_melee_probe",
                Name = "Forced Target Melee Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var lowestHpPriority = new Strategy
            {
                SkillId = rangedSkill.Id,
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            };
            var selector = new TargetSelector(context);

            eventBus.Publish(new BattleStartEvent { Context = context });
            var forcedState = taunter.TemporalStates.Single(state => state.Key == "ForcedTarget");
            var frontRangedTargets = selector.SelectTargets(frontRangedEnemy, lowestHpPriority, rangedSkill);
            var backRangedTargets = selector.SelectTargets(backRangedEnemy, lowestHpPriority, rangedSkill);
            lowestHpPriority.SkillId = meleeSkill.Id;
            var frontMeleeTargets = selector.SelectTargets(frontMeleeEnemy, lowestHpPriority, meleeSkill);

            CollectionAssert.AreEquivalent(
                new[] { "frontRangedEnemy", "frontMeleeEnemy" },
                forcedState.AffectedUnitIds);
            CollectionAssert.AreEqual(new[] { taunter }, frontRangedTargets);
            CollectionAssert.AreEqual(new[] { frontAlly }, backRangedTargets);
            CollectionAssert.AreEqual(new[] { frontAlly }, frontMeleeTargets);
        }

        [Test]
        public void RealPassiveJson_MagicBarrier_NullifiesMagicDamageAndAilmentWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_magic_barrier_probe"] = CreateBarrierProbeSkill(
                "act_magic_barrier_probe",
                SkillType.Magical,
                AttackType.Magic,
                TargetType.SingleEnemy);
            var magicBarrier = repository.PassiveSkills["pas_magic_barrier"];
            magicBarrier.Tags.Clear();
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 300, str: 1, def: 0, spd: 1, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var barrierUser = new BattleUnit(CreatePassiveBattleCharacter(
                "barrierUser", null, hp: 300, str: 1, def: 0, spd: 1, hit: 1000, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", "act_magic_barrier_probe", hp: 300, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 100),
                repository,
                false)
            {
                Position = 1
            };
            barrierUser.EquippedPassiveSkillIds.Add("pas_magic_barrier");
            enemy.Strategies.Add(new Strategy
            {
                SkillId = "act_magic_barrier_probe",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Priority
            });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, barrierUser },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "TemporalMark", "TemporalMark" },
                magicBarrier.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(300, defender.CurrentHp);
            CollectionAssert.DoesNotContain(defender.Ailments, StatusAilment.Poison);
            ClassicAssert.AreEqual(0, barrierUser.CurrentPp);
            ClassicAssert.IsFalse(defender.TemporalStates.Any(state =>
                state.Key is "MagicDamageNullify" or "AilmentNullify"));
            Assert.That(logs, Has.Some.Contains("MagicDamageNullify"));
            Assert.That(logs, Has.Some.Contains("AilmentNullified"));
        }

        [Test]
        public void RealPassiveJson_MagicBarrier_NullifiesAddedMagicComponentOnMixedPhysicalAction()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var magicBarrier = repository.PassiveSkills["pas_magic_barrier"];
            magicBarrier.Tags.Clear();
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 300, str: 1, def: 0, spd: 1, hit: 1000, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var barrierUser = new BattleUnit(CreatePassiveBattleCharacter(
                "barrierUser", null, hp: 300, str: 1, def: 0, spd: 1, hit: 1000, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 4
            };
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", null, hp: 300, str: 100, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 100),
                repository,
                false)
            {
                Position = 1
            };
            barrierUser.EquippedPassiveSkillIds.Add("pas_magic_barrier");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, barrierUser },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var skillData = new ActiveSkillData
            {
                Id = "act_mixed_probe",
                Name = "Mixed Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var skill = new ActiveSkill(skillData, repository);
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                AdditionalMagicalPower = 50
            };

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });
            var result = new DamageCalculator().Calculate(calc);

            ClassicAssert.AreEqual(0, barrierUser.CurrentPp);
            ClassicAssert.Greater(result.PhysicalDamage, 0);
            ClassicAssert.AreEqual(0, result.MagicalDamage);
            Assert.That(logs, Has.Some.Contains("MagicDamageNullify"));
        }

        [Test]
        public void RealPassiveJson_MagicBarrier_DoesNotTriggerForPhysicalHitWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var magicBarrier = repository.PassiveSkills["pas_magic_barrier"];
            magicBarrier.Tags.Clear();
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 0), repository, true)
            {
                Position = 1
            };
            var barrierUser = new BattleUnit(CreateCharacter("barrierUser", hp: 100, pp: 1), repository, true)
            {
                Position = 4
            };
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false)
            {
                Position = 1
            };
            barrierUser.EquippedPassiveSkillIds.Add("pas_magic_barrier");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, barrierUser },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Physical, attackType: AttackType.Ranged);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                Calc = calc
            });

            ClassicAssert.AreEqual(1, barrierUser.CurrentPp);
            ClassicAssert.IsEmpty(defender.TemporalStates);
            ClassicAssert.IsEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_RowBarrier_MarksTargetFrontOrBackRowNotColumnWithoutLegacyTags()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var rowBarrier = repository.PassiveSkills["pas_row_barrier"];
            rowBarrier.Tags.Clear();
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 0), repository, true)
            {
                Position = 1
            };
            var frontAlly = new BattleUnit(CreateCharacter("frontAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 2
            };
            var otherFrontAlly = new BattleUnit(CreateCharacter("otherFrontAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 3
            };
            var sameColumnBackAlly = new BattleUnit(CreateCharacter("sameColumnBackAlly", hp: 100, pp: 0), repository, true)
            {
                Position = 4
            };
            var barrierUser = new BattleUnit(CreateCharacter("barrierUser", hp: 100, pp: 2), repository, true)
            {
                Position = 5
            };
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, false)
            {
                Position = 1
            };
            barrierUser.EquippedPassiveSkillIds.Add("pas_row_barrier");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit>
                {
                    defender,
                    frontAlly,
                    otherFrontAlly,
                    sameColumnBackAlly,
                    barrierUser
                },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var skill = TestDataFactory.CreateSkill(type: SkillType.Magical, attackType: AttackType.Magic);
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
                new[] { "TemporalMark", "TemporalMark" },
                rowBarrier.Effects.Select(effect => effect.EffectType).ToList());
            foreach (var unit in new[] { defender, frontAlly, otherFrontAlly })
            {
                ClassicAssert.IsTrue(unit.TemporalStates.Any(state =>
                    state.Key == "MagicDamageNullify" && state.SourceSkillId == "pas_row_barrier"));
                ClassicAssert.IsTrue(unit.TemporalStates.Any(state =>
                    state.Key == "AilmentNullify" && state.SourceSkillId == "pas_row_barrier"));
            }
            ClassicAssert.IsFalse(sameColumnBackAlly.TemporalStates.Any());
            ClassicAssert.IsFalse(barrierUser.TemporalStates.Any());
            ClassicAssert.AreEqual(0, barrierUser.CurrentPp);
            Assert.That(logs, Has.Some.Contains("MagicDamageNullify"));
        }

        [Test]
        public void RealPassiveJson_RowBarrier_MultiTargetMagicTriggersOncePerAction()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_row_barrier_multi_probe"] = new ActiveSkillData
            {
                Id = "act_row_barrier_multi_probe",
                Name = "Row Barrier Multi Probe",
                ApCost = 1,
                Type = SkillType.Magical,
                AttackType = AttackType.Magic,
                Power = 200,
                HitRate = 1000,
                TargetType = TargetType.Row,
                Effects = new List<SkillEffectData>()
            };
            var rowBarrier = repository.PassiveSkills["pas_row_barrier"];
            rowBarrier.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_row_barrier_multi_probe", hp: 500, str: 1, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0, mag: 100),
                repository,
                false)
            {
                Position = 1,
                CurrentAp = 1
            };
            var firstTarget = new BattleUnit(CreatePassiveBattleCharacter(
                "firstTarget", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var secondTarget = new BattleUnit(CreatePassiveBattleCharacter(
                "secondTarget", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 2
            };
            var barrierUser = new BattleUnit(CreatePassiveBattleCharacter(
                "barrierUser", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 4),
                repository,
                true)
            {
                Position = 5,
                CurrentPp = 4
            };
            attacker.Strategies.Add(new Strategy
            {
                SkillId = "act_row_barrier_multi_probe",
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Only
            });
            barrierUser.EquippedPassiveSkillIds.Add("pas_row_barrier");
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { firstTarget, secondTarget, barrierUser },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(2, rowBarrier.PpCost);
            ClassicAssert.AreEqual(2, barrierUser.CurrentPp);
            ClassicAssert.AreEqual(500, firstTarget.CurrentHp);
            ClassicAssert.AreEqual(500, secondTarget.CurrentHp);
            ClassicAssert.AreEqual(
                1,
                logs.Count(log => log.Contains(rowBarrier.Name) || log.Contains("pas_row_barrier")));
            Assert.That(logs, Has.Some.Contains("MagicDamageNullify"));
        }

        [Test]
        public void RealPassiveJson_MagicCounter_CountersAfterIncomingActiveSkill()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_reflect_probe"] = new ActiveSkillData
            {
                Id = "act_reflect_probe",
                Name = "Reflect Probe",
                ApCost = 1,
                Type = SkillType.Magical,
                AttackType = AttackType.Magic,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var defender = new BattleUnit(CreateCharacter("defender", hp: 100, pp: 1), repository, false)
            {
                Position = 1
            };
            defender.Data.BaseStats["MDef"] = 0;
            var attacker = new BattleUnit(CreateCharacter("attacker", hp: 100, pp: 0), repository, true)
            {
                Position = 1
            };
            attacker.Data.InnateActiveSkillIds.Add("act_reflect_probe");
            attacker.Data.BaseStats["AP"] = 1;
            attacker.Data.BaseStats["Mag"] = 50;
            attacker.Data.BaseStats["Hit"] = 1000;
            attacker.Data.BaseStats["Spd"] = 100;
            attacker.CurrentAp = 1;
            attacker.Strategies.Add(new Strategy { SkillId = "act_reflect_probe" });
            defender.EquippedPassiveSkillIds.Add("pas_magic_counter");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(50, defender.CurrentHp);
            ClassicAssert.Less(attacker.CurrentHp, 100);
            ClassicAssert.AreEqual(0, defender.CurrentPp);
            Assert.That(engine.BattleLogEntries, Has.Some.Matches<BattleLogEntry>(entry => entry.SkillId == "pas_magic_counter"));
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

            eventBus.Publish(new AfterHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                Context = context,
                DamageDealt = 1,
                IsHit = true,
                SourceKind = BattleActionSourceKind.ActiveAttack
            });

            ClassicAssert.AreEqual("CounterAttack", blockSeal.Effects.Single().EffectType);
            ClassicAssert.IsFalse(calc.CannotBeBlocked);
            var action = queued.Single();
            ClassicAssert.AreEqual(PendingActionType.Counter, action.Type);
            ClassicAssert.AreSame(defender, action.Actor);
            CollectionAssert.AreEqual(new[] { attacker }, action.Targets);
            ClassicAssert.AreEqual(1f, action.IgnoreDefenseRatio);
            ClassicAssert.AreEqual(UnitClass.Heavy, action.IgnoreDefenseTargetClass);
            CollectionAssert.Contains(action.Tags, "CannotBeBlocked");
            ClassicAssert.AreEqual(1, action.SourcePpCost);
            ClassicAssert.AreEqual(1, defender.CurrentPp);
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

        [Test]
        public void RealPassiveJson_CalmCover_AddsSideAuraThatPreventsFriendlyBlocksOnly()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_calm_cover_probe"] = new ActiveSkillData
            {
                Id = "act_calm_cover_probe",
                Name = "Calm Cover Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var calmCover = repository.PassiveSkills["pas_calm_cover"];
            calmCover.Tags.Clear();
            var horn = new BattleUnit(CreatePassiveBattleCharacter(
                "horn", null, hp: 200, str: 1, def: 0, spd: 10, hit: 1000, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 4
            };
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_calm_cover_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var defender = new BattleUnit(CreatePassiveBattleCharacter(
                "defender", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            defender.Data.BaseStats["Block"] = 100;
            attacker.Strategies.Add(new Strategy { SkillId = "act_calm_cover_probe" });
            horn.EquippedPassiveSkillIds.Add("pas_calm_cover");
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { horn, attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentOutgoingActions" }, calmCover.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, horn.CurrentPp);
            ClassicAssert.AreEqual(1, context.OutgoingActionAugments.Count);
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_calm_cover_probe");
            CollectionAssert.Contains(activeEntry.Flags, "Augment:pas_calm_cover");
            CollectionAssert.Contains(activeEntry.Flags, "CannotBeBlocked");
            CollectionAssert.DoesNotContain(activeEntry.Flags, "Blocked");
            Assert.That(logs, Has.Some.Contains("AugmentOutgoingActions").And.Contains("pas_calm_cover"));

            var enemyAttacker = new BattleUnit(CreatePassiveBattleCharacter(
                "enemyAttacker", "act_calm_cover_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            var blockingPlayer = new BattleUnit(CreatePassiveBattleCharacter(
                "blockingPlayer", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            blockingPlayer.Data.BaseStats["Block"] = 100;
            var secondHorn = new BattleUnit(CreatePassiveBattleCharacter(
                "secondHorn", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 2),
                repository,
                true)
            {
                Position = 4
            };
            enemyAttacker.Strategies.Add(new Strategy { SkillId = "act_calm_cover_probe" });
            secondHorn.EquippedPassiveSkillIds.Add("pas_calm_cover");
            var enemyContext = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { blockingPlayer, secondHorn },
                EnemyUnits = new List<BattleUnit> { enemyAttacker }
            };
            var enemyEngine = new BattleEngine(enemyContext) { OnLog = _ => { } };
            var enemyProcessor = new PassiveSkillProcessor(enemyEngine.EventBus, repository, _ => { }, enemyEngine.EnqueueAction);
            enemyProcessor.SubscribeAll();

            enemyEngine.InitBattle();
            enemyEngine.StepOneAction();

            var enemyEntry = enemyEngine.BattleLogEntries.Single(entry => entry.SkillId == "act_calm_cover_probe");
            CollectionAssert.Contains(enemyEntry.Flags, "Blocked");
            CollectionAssert.DoesNotContain(enemyEntry.Flags, "CannotBeBlocked");
            Assert.That(enemyEntry.Flags, Has.None.EqualTo("Augment:pas_calm_cover"));
        }

        [Test]
        public void RealPassiveJson_Berserk_RecoversApSacrificesRemainingPpAndGrantsDeathResist()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_berserk_probe"] = new ActiveSkillData
            {
                Id = "act_berserk_probe",
                Name = "Berserk Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var berserk = repository.PassiveSkills["pas_berserk"];
            berserk.Tags.Clear();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_berserk_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 2, pp: 3),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_berserk");
            caster.Strategies.Add(new Strategy { SkillId = "act_berserk_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "RecoverAp", "PpDamage", "TemporalMark" }, berserk.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(1, caster.CurrentAp);
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.IsTrue(caster.TemporalStates.Any(state => state.Key == "DeathResist"));
            Assert.That(logs, Has.Some.Contains("caster.AP").And.Contains("caster.PP").And.Contains("DeathResist"));

            caster.CurrentHp = 101;
            caster.TakeDamage(100);

            ClassicAssert.AreEqual(1, caster.CurrentHp);
            ClassicAssert.IsTrue(caster.TemporalStates.Any(state => state.Key == "DeathResist"));

            caster.TakeDamage(2);

            ClassicAssert.AreEqual(1, caster.CurrentHp);
            ClassicAssert.IsFalse(caster.TemporalStates.Any(state => state.Key == "DeathResist"));
        }

        [Test]
        public void SelfBeforeAttack_SimultaneousLimit_AllowsOnlyFirstLimitedPassiveForAction()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_berserk_sim_probe"] = new ActiveSkillData
            {
                Id = "act_berserk_sim_probe",
                Name = "Berserk Sim Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var berserk = repository.PassiveSkills["pas_berserk"];
            berserk.Tags.Clear();
            berserk.HasSimultaneousLimit = true;
            repository.PassiveSkills["pas_self_before_attack_after_probe"] = new PassiveSkillData
            {
                Id = "pas_self_before_attack_after_probe",
                Name = "After Probe",
                PpCost = 0,
                TriggerTiming = PassiveTriggerTiming.SelfBeforeAttack,
                Type = SkillType.Assist,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "TemporalMark",
                        Parameters = new Dictionary<string, object>
                        {
                            { "target", "Self" },
                            { "key", "AfterProbe" },
                            { "count", 1 }
                        }
                    }
                },
                HasSimultaneousLimit = true
            };
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_berserk_sim_probe", hp: 200, str: 50, def: 0, spd: 100, hit: 1000, eva: 0, ap: 2, pp: 3),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_berserk");
            caster.EquippedPassiveSkillIds.Add("pas_self_before_attack_after_probe");
            caster.PassiveStrategies.Add(new PassiveStrategy { SkillId = "pas_berserk" });
            caster.PassiveStrategies.Add(new PassiveStrategy { SkillId = "pas_self_before_attack_after_probe" });
            caster.Strategies.Add(new Strategy { SkillId = "act_berserk_sim_probe" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, _ => { }, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.StepOneAction();

            ClassicAssert.IsTrue(caster.TemporalStates.Any(state => state.Key == "DeathResist"));
            ClassicAssert.IsFalse(caster.TemporalStates.Any(state => state.Key == "AfterProbe"));
        }

        [Test]
        public void RealPassiveJson_RapidOrder_BattleLongBuffSurvivesTurnAndActionCleanup()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", null, hp: 100, str: 1, def: 0, spd: 30, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 1
            };
            var ally = new BattleUnit(CreatePassiveBattleCharacter(
                "ally", null, hp: 100, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 100, str: 1, def: 0, spd: 50, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_rapid_order");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, ally },
                EnemyUnits = new List<BattleUnit> { enemy }
            };

            eventBus.Publish(new BattleStartEvent { Context = context });
            BuffManager.CleanupAfterAction(ally);
            foreach (var unit in context.AllUnits)
                BuffManager.CleanupEndOfTurn(unit);

            ClassicAssert.AreEqual(60, ally.GetCurrentStat("Spd"));
            ClassicAssert.IsTrue(ally.Buffs.Any(buff =>
                buff.SkillId == "pas_rapid_order"
                && buff.TargetStat == "Spd"
                && buff.FlatAmount == 20
                && buff.RemainingTurns == -1));
        }

        [Test]
        public void RealPassiveJson_ForcedTarget_PersistsAcrossSelectionAndTurnCleanup()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, _ => { });
            processor.SubscribeAll();
            var taunter = new BattleUnit(CreatePassiveBattleCharacter(
                "taunter", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 1
            };
            var lowHpAlly = new BattleUnit(CreatePassiveBattleCharacter(
                "lowHpAlly", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                true)
            {
                Position = 2,
                CurrentHp = 50
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 30, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            taunter.EquippedPassiveSkillIds.Add("pas_hundred_crit");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { taunter, lowHpAlly },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var skill = new ActiveSkillData
            {
                Id = "act_forced_target_probe",
                Name = "Forced Target Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                Power = 10,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var strategy = new Strategy
            {
                SkillId = skill.Id,
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            };
            var selector = new TargetSelector(context);

            eventBus.Publish(new BattleStartEvent { Context = context });
            var firstSelection = selector.SelectTargets(enemy, strategy, skill);
            BuffManager.CleanupAfterAction(enemy);
            foreach (var unit in context.AllUnits)
                BuffManager.CleanupEndOfTurn(unit);
            var secondSelection = selector.SelectTargets(enemy, strategy, skill);

            CollectionAssert.AreEqual(new[] { taunter }, firstSelection);
            CollectionAssert.AreEqual(new[] { taunter }, secondSelection);
            ClassicAssert.IsTrue(taunter.TemporalStates.Any(state =>
                state.Key == "ForcedTarget"
                && state.RemainingCount != 0
                && state.RemainingTurns == -1));
        }

        [Test]
        public void RealPassiveJson_UnstoppableRage_AugmentsCurrentActionWithForceHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_force_hit_probe"] = ForceHitProbeSkill("act_force_hit_probe", SkillType.Physical);
            var passive = repository.PassiveSkills["pas_unstoppable_rage"];
            passive.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_force_hit_probe", hp: 500, str: 80, def: 0, spd: 100, hit: 0, eva: 0, ap: 1, pp: 1),
                repository,
                true)
            {
                Position = 1,
                CurrentLevel = 99,
                CurrentAp = 1,
                CurrentPp = 1
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 1000, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            attacker.EquippedPassiveSkillIds.Add("pas_unstoppable_rage");
            attacker.Strategies.Add(new Strategy { SkillId = "act_force_hit_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, passive.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, attacker.CurrentPp);
            ClassicAssert.Less(enemy.CurrentHp, 500);
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_force_hit_probe");
            CollectionAssert.Contains(activeEntry.Flags, "Augment:pas_unstoppable_rage");
            Assert.That(logs, Has.Some.Contains("augment calc effects:").And.Contains("pas_unstoppable_rage").And.Contains("ForceHit"));
        }

        [Test]
        public void RealPassiveJson_Focus_MakesCurrentAllyPhysicalActionSureHit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_focus_probe"] = new ActiveSkillData
            {
                Id = "act_focus_probe",
                Name = "Focus Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var focus = repository.PassiveSkills["pas_focus"];
            focus.Tags.Clear();
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_focus_probe", hp: 500, str: 80, def: 0, spd: 100, hit: 0, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1
            };
            var support = new BattleUnit(CreatePassiveBattleCharacter(
                "support", null, hp: 500, str: 1, def: 0, spd: 50, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 0, spd: 1, hit: 0, eva: 1000, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            support.EquippedPassiveSkillIds.Add("pas_focus");
            attacker.Strategies.Add(new Strategy { SkillId = "act_focus_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, support },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(new[] { "AugmentCurrentAction" }, focus.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, support.CurrentPp);
            ClassicAssert.Less(enemy.CurrentHp, 500);
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_focus_probe");
            CollectionAssert.Contains(activeEntry.Flags, "SureHit");
            CollectionAssert.Contains(activeEntry.Flags, "Augment:pas_focus");
            Assert.That(logs, Has.Some.Contains("聚焦").And.Contains("友方攻击前"));
        }

        [Test]
        public void RealPassiveJson_KeenCall_ForcesEveryLandedHitOfMultiHitActionToCrit()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_keen_multi_probe"] = new ActiveSkillData
            {
                Id = "act_keen_multi_probe",
                Name = "Keen Multi Probe",
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
                        EffectType = "ModifyDamageCalc",
                        Parameters = new Dictionary<string, object>
                        {
                            { "HitCount", 4 }
                        }
                    }
                }
            };
            var attacker = new BattleUnit(CreatePassiveBattleCharacter(
                "attacker", "act_keen_multi_probe", hp: 500, str: 60, def: 0, spd: 100, hit: 1000, eva: 0, ap: 1, pp: 0),
                repository,
                true)
            {
                Position = 1
            };
            var support = new BattleUnit(CreatePassiveBattleCharacter(
                "support", null, hp: 500, str: 1, def: 0, spd: 40, hit: 0, eva: 0, ap: 0, pp: 1),
                repository,
                true)
            {
                Position = 2
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 500, str: 1, def: 10, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            support.EquippedPassiveSkillIds.Add("pas_muscle_swelling");
            attacker.Strategies.Add(new Strategy { SkillId = "act_keen_multi_probe" });
            var logs = new List<string>();
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker, support },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(0, support.CurrentPp);
            var activeEntry = engine.BattleLogEntries.Single(entry => entry.SkillId == "act_keen_multi_probe");
            ClassicAssert.AreEqual(300, activeEntry.Damage);
            CollectionAssert.Contains(activeEntry.Flags, "Critical");
            CollectionAssert.Contains(activeEntry.Flags, "Augment:pas_muscle_swelling");
            Assert.That(logs, Has.Some.Contains("HitCount=4"));
            Assert.That(logs, Has.Some.Contains("ForceCrit"));
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

        private static ActiveSkillData ForceHitProbeSkill(string id, SkillType skillType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = "Force Hit Probe",
                ApCost = 1,
                Type = skillType,
                AttackType = skillType == SkillType.Magical ? AttackType.Magic : AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
        }

        private static CharacterData CreatePassiveBattleCharacter(
            string id,
            string? activeSkillId,
            int hp,
            int str,
            int def,
            int spd,
            int hit,
            int eva,
            int ap,
            int pp,
            int mag = 0,
            List<UnitClass>? classes = null,
            List<UnitClass>? ccClasses = null)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = classes ?? new List<UnitClass> { UnitClass.Infantry },
                CcClasses = ccClasses ?? new List<UnitClass>(),
                InnateActiveSkillIds = activeSkillId == null ? new List<string>() : new List<string> { activeSkillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", mag },
                    { "MDef", 0 },
                    { "Hit", hit },
                    { "Eva", eva },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", pp }
                }
            };
        }

        private static List<SkillEffectData> ReadNestedEffects(object raw)
        {
            if (raw is List<SkillEffectData> list)
                return list;

            if (raw is System.Text.Json.JsonElement element)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<SkillEffectData>>(
                    element.GetRawText(),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<SkillEffectData>();
            }

            return new List<SkillEffectData>();
        }

        private static int ReadInt(object raw)
        {
            return raw switch
            {
                int value => value,
                long value => (int)value,
                double value => (int)value,
                System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.Number
                    => element.GetInt32(),
                _ => int.Parse(raw.ToString() ?? "0")
            };
        }

        private static string ReadString(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var raw))
                return string.Empty;

            return raw is System.Text.Json.JsonElement element
                ? element.GetString() ?? string.Empty
                : raw?.ToString() ?? string.Empty;
        }

        private static ActiveSkillData CreateBarrierProbeSkill(
            string id,
            SkillType skillType,
            AttackType attackType,
            TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 1,
                Type = skillType,
                AttackType = attackType,
                Power = 200,
                HitRate = 1000,
                TargetType = targetType,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "OnHitEffect",
                        Parameters = new Dictionary<string, object>
                        {
                            {
                                "effects",
                                new List<SkillEffectData>
                                {
                                    new()
                                    {
                                        EffectType = "StatusAilment",
                                        Parameters = new Dictionary<string, object>
                                        {
                                            { "target", "Target" },
                                            { "ailment", "Poison" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
