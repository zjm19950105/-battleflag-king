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
        public void RealPassiveJson_Bounce_SelfBeforeHitHealsOnlyDefenderAndPaysPp()
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
            ClassicAssert.AreEqual(0, defender.CurrentPp);
            ClassicAssert.AreEqual(120, defender.CurrentHp);
            ClassicAssert.AreEqual(40, attacker.CurrentHp);
            ClassicAssert.AreEqual(40, ally.CurrentHp);
            ClassicAssert.IsNotEmpty(logs);
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
            ClassicAssert.IsTrue(calc.IsBlocked);
            ClassicAssert.AreEqual(0, cover.CurrentPp);
            ClassicAssert.AreEqual(90, defender.CurrentHp);
            ClassicAssert.AreEqual(25, cover.CurrentHp);
            CollectionAssert.AreEqual(new[] { cover }, afterHitDefenders);
            ClassicAssert.IsNotEmpty(logs);
        }

        [Test]
        public void RealPassiveJson_Concentration_RecoversApBuffsCurrentActiveHitAndCleansAfterAction()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills["act_probe_hit"] = new ActiveSkillData
            {
                Id = "act_probe_hit",
                Name = "Probe Hit",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 10,
                HitRate = 60,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var concentration = repository.PassiveSkills["pas_concentration"];
            var caster = new BattleUnit(CreatePassiveBattleCharacter(
                "caster", "act_probe_hit", hp: 200, str: 50, def: 0, spd: 100, hit: 0, eva: 0, ap: 2, pp: 1),
                repository,
                true)
            {
                Position = 1,
                CurrentAp = 1,
                CurrentLevel = 30
            };
            var enemy = new BattleUnit(CreatePassiveBattleCharacter(
                "enemy", null, hp: 200, str: 1, def: 0, spd: 1, hit: 0, eva: 0, ap: 0, pp: 0),
                repository,
                false)
            {
                Position = 1
            };
            caster.EquippedPassiveSkillIds.Add("pas_concentration");
            caster.Strategies.Add(new Strategy { SkillId = "act_probe_hit" });
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
                    hitDuringCurrentActiveSkill = evt.Attacker.GetCurrentHitRate();
            });

            engine.InitBattle();
            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            CollectionAssert.AreEqual(
                new[] { "RecoverAp", "AddBuff" },
                concentration.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(0, caster.CurrentPp);
            ClassicAssert.AreEqual(1, caster.CurrentAp);
            ClassicAssert.AreEqual(40, hitDuringCurrentActiveSkill);
            ClassicAssert.Less(enemy.CurrentHp, 200);
            ClassicAssert.AreEqual(0, caster.GetCurrentHitRate());
            ClassicAssert.IsFalse(caster.Buffs.Any(buff =>
                buff.TargetStat == "Hit" && buff.IsOneTime && buff.SkillId == "pas_concentration"));
            ClassicAssert.IsNotEmpty(logs);
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
                    { "Mag", 0 },
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
    }
}
