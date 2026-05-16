using System.Text.Json;
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
    public class StructuredPassiveSkillRegressionTest
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void RealPassiveJson_MigratedPassives_DeclareExpectedStructuredEffects()
        {
            var repository = LoadRepository();

            AssertFirstThreeCharacterPassiveSkillSourceData(repository);
            AssertIronGuardShape(repository.PassiveSkills["pas_iron_guard"]);
            AssertFluorescentCoverShape(repository.PassiveSkills["pas_fluorescent_cover"]);
            AssertNobleBlockShape(repository.PassiveSkills["pas_noble_block"]);
            AssertShooterAndThiefPassiveSourceData(repository);
            AssertDodgeStepShape(repository.PassiveSkills["pas_dodge_step"]);
            AssertQuickActionShape(repository.PassiveSkills["pas_quick_action"]);
            AssertShadowStepShape(repository.PassiveSkills["pas_shadow_step"]);
            AssertStealthBladeShape(repository.PassiveSkills["pas_stealth_blade"]);
            AssertHolyGuardShape(repository.PassiveSkills["pas_holy_guard"]);
            AssertFirstAidShape(repository.PassiveSkills["pas_give_ap"]);
            AssertChargeActionShape(repository.PassiveSkills["pas_charge_action"]);
            Assert.That(Enum.IsDefined(typeof(StatusAilment), nameof(StatusAilment.PassiveSeal)), Is.True);
            Assert.That(Enum.IsDefined(typeof(UnitState), nameof(UnitState.PassiveSeal)), Is.True);
        }

        [Test]
        public void RealPassiveJson_FluorescentCover_CoversForAllyBlocksAndBuffsOriginalDefender()
        {
            var repository = LoadRepository();
            var eventBus = new EventBus();
            var logs = new List<string>();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = CreateUnit(repository, "defender", true, 1, hp: 300, def: 100, pp: 0);
            var coverUser = CreateUnit(repository, "cover_user", true, 4, hp: 300, def: 100, pp: 1);
            var attacker = CreateUnit(repository, "attacker", false, 1, hp: 300, str: 80, pp: 0);
            coverUser.EquippedPassiveSkillIds.Add("pas_fluorescent_cover");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, coverUser },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var calc = CreateIncomingCalc(attacker, defender, SkillType.Physical, AttackType.Melee);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = calc.Skill,
                Context = context,
                Calc = calc
            });

            ClassicAssert.AreSame(coverUser, calc.CoverTarget);
            ClassicAssert.AreEqual(true, calc.ForceBlock);
            ClassicAssert.AreEqual(0.5f, calc.ForcedBlockReduction ?? -1f, 0.001f);
            ClassicAssert.AreEqual(120, defender.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(0, coverUser.CurrentPp);
        }

        [Test]
        public void RealPassiveJson_LineCover_RequiresSameRowBeforePayingPp()
        {
            var repository = LoadRepository();
            var eventBus = new EventBus();
            var logs = new List<string>();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = CreateUnit(repository, "defender", true, 1, hp: 300, def: 100, pp: 0);
            var sameRowCover = CreateUnit(repository, "same_row_cover", true, 2, hp: 300, def: 100, pp: 2);
            var otherRowCover = CreateUnit(repository, "other_row_cover", true, 4, hp: 300, def: 100, pp: 2);
            var attacker = CreateUnit(repository, "attacker", false, 1, hp: 300, str: 80, pp: 0);
            sameRowCover.EquippedPassiveSkillIds.Add("pas_pursuit");
            otherRowCover.EquippedPassiveSkillIds.Add("pas_pursuit");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender, sameRowCover, otherRowCover },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var calc = CreateIncomingCalc(attacker, defender, SkillType.Physical, AttackType.Melee);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = calc.Skill,
                Context = context,
                Calc = calc
            });

            var lineCover = repository.PassiveSkills["pas_pursuit"];
            ClassicAssert.AreEqual(2, lineCover.PpCost);
            ClassicAssert.AreEqual(SkillType.Physical, lineCover.Type);
            ClassicAssert.AreSame(sameRowCover, calc.CoverTarget);
            ClassicAssert.AreEqual(true, calc.ForceBlock);
            ClassicAssert.AreEqual(0.5f, calc.ForcedBlockReduction ?? -1f, 0.001f);
            ClassicAssert.AreEqual(0, sameRowCover.CurrentPp);
            ClassicAssert.AreEqual(2, otherRowCover.CurrentPp);
        }

        [Test]
        public void BattleEngine_RealPassiveJson_LineCover_CoversSameRowTargetsForOneAction()
        {
            var repository = LoadRepository();
            repository.ActiveSkills["act_line_cover_probe"] = new ActiveSkillData
            {
                Id = "act_line_cover_probe",
                Name = "Line Cover Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 50,
                HitRate = 100,
                TargetType = TargetType.TwoEnemies,
                Effects = new List<SkillEffectData>()
            };
            var attacker = CreateUnit(repository, "attacker", false, 1, "act_line_cover_probe", hp: 300, str: 100, def: 10, spd: 100, ap: 1, pp: 0);
            attacker.Strategies.Add(new Strategy { SkillId = "act_line_cover_probe" });
            var defenderA = CreateUnit(repository, "defender_a", true, 1, hp: 300, def: 10, pp: 0);
            var defenderB = CreateUnit(repository, "defender_b", true, 2, hp: 300, def: 10, pp: 0);
            var cover = CreateUnit(repository, "line_cover", true, 3, hp: 300, def: 10, pp: 2);
            cover.EquippedPassiveSkillIds.Add("pas_pursuit");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defenderA, defenderB, cover },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            var attackEntries = engine.BattleLogEntries
                .Where(entry => entry.SkillId == "act_line_cover_probe")
                .ToList();
            ClassicAssert.AreEqual(2, attackEntries.Count);
            foreach (var entry in attackEntries)
                CollectionAssert.AreEqual(new[] { "line_cover" }, entry.TargetIds);
            ClassicAssert.AreEqual(0, cover.CurrentPp);
            ClassicAssert.AreEqual(300, defenderA.CurrentHp);
            ClassicAssert.AreEqual(300, defenderB.CurrentHp);
            ClassicAssert.Less(cover.CurrentHp, 300);
            ClassicAssert.AreEqual(1, logs.Count(log => log.Contains("列掩护", StringComparison.Ordinal)));
        }

        [Test]
        public void RealPassiveJson_NobleBlock_RecoversPpOnlyAtHalfHpOrLower()
        {
            var lowHp = TriggerNobleBlockAtHp(50);
            var highHp = TriggerNobleBlockAtHp(51);

            ClassicAssert.AreEqual(1, lowHp.CurrentPp, "At 50% HP, the PP refund should offset the trigger cost.");
            ClassicAssert.AreEqual(0, highHp.CurrentPp, "Above 50% HP, the passive should only pay its PP cost.");
            ClassicAssert.AreEqual(true, lowHp.Calc.ForceBlock);
            ClassicAssert.AreEqual(0.5f, lowHp.Calc.ForcedBlockReduction ?? -1f, 0.001f);
            ClassicAssert.AreEqual(120, lowHp.Unit.GetCurrentStat("Def"));
        }

        [Test]
        public void RealPassiveJson_HolyGuard_AddsDebuffNullifyTemporalMark()
        {
            var repository = LoadRepository();
            var eventBus = new EventBus();
            var logs = new List<string>();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = CreateUnit(repository, "holy_guard", true, 1, hp: 300, def: 100, pp: 1);
            var attacker = CreateUnit(repository, "attacker", false, 1, hp: 300, str: 80, pp: 0);
            defender.EquippedPassiveSkillIds.Add("pas_holy_guard");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var calc = CreateIncomingCalc(attacker, defender, SkillType.Physical, AttackType.Melee);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = calc.Skill,
                Context = context,
                Calc = calc
            });

            ClassicAssert.AreEqual(true, calc.ForceBlock);
            ClassicAssert.AreEqual(0.5f, calc.ForcedBlockReduction ?? -1f, 0.001f);
            ClassicAssert.IsTrue(defender.TemporalStates.Any(state => state.Key == "DebuffNullify"));
            ClassicAssert.AreEqual(0, defender.CurrentPp);
        }

        [Test]
        public void RealPassiveJson_IronGuard_QueuesCounterAndDebuffsAttackerOnHit()
        {
            var repository = LoadRepository();
            repository.ActiveSkills["act_iron_guard_probe"] = new ActiveSkillData
            {
                Id = "act_iron_guard_probe",
                Name = "Iron Guard Probe",
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 1,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy,
                Effects = new List<SkillEffectData>()
            };
            var ally = CreateUnit(repository, "ally", true, 1, hp: 1000, def: 100, pp: 0);
            var guard = CreateUnit(repository, "guard", true, 4, hp: 1000, str: 50, hit: 1000, pp: 1);
            var attacker = CreateUnit(repository, "attacker", false, 1, "act_iron_guard_probe", hp: 1000, str: 10, def: 100, spd: 100, ap: 1, pp: 0);
            attacker.Strategies.Add(new Strategy { SkillId = "act_iron_guard_probe" });
            guard.EquippedPassiveSkillIds.Add("pas_iron_guard");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { ally, guard },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(85, attacker.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(0, guard.CurrentPp);
            Assert.That(logs, Has.Some.Contains("准备反击").And.Contains("威力50"));
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("attacker.Def 100->85"));
        }

        [Test]
        public void RealPassiveJson_StealthBlade_AppliesBlockSealAndPassiveSealAfterPendingHit()
        {
            var repository = LoadRepository();
            var stealthUser = CreateUnit(repository, "stealth_user", true, 1, hp: 500, str: 100, hit: 1000, pp: 1);
            var enemy = CreateUnit(repository, "enemy", false, 1, hp: 500, def: 0, pp: 0);
            stealthUser.EquippedPassiveSkillIds.Add("pas_stealth_blade");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { stealthUser },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var logs = new List<string>();
            var engine = new BattleEngine(context) { OnLog = logs.Add };
            var processor = new PassiveSkillProcessor(engine.EventBus, repository, logs.Add, engine.EnqueueAction);
            processor.SubscribeAll();

            engine.InitBattle();

            CollectionAssert.Contains(enemy.Ailments, StatusAilment.BlockSeal);
            CollectionAssert.Contains(enemy.Ailments, StatusAilment.PassiveSeal);
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("PassiveSeal"));
        }

        [Test]
        public void PassiveSeal_PreventsSealedUnitPassiveTriggers()
        {
            var repository = LoadRepository();
            repository.PassiveSkills["pas_passive_seal_probe"] = new PassiveSkillData
            {
                Id = "pas_passive_seal_probe",
                Name = "Passive Seal Probe",
                PpCost = 1,
                TriggerTiming = PassiveTriggerTiming.BattleStart,
                Type = SkillType.Assist,
                Effects = new List<SkillEffectData>
                {
                    new()
                    {
                        EffectType = "AddBuff",
                        Parameters = new Dictionary<string, object>
                        {
                            { "target", "Self" },
                            { "stat", "Def" },
                            { "ratio", 0.2 },
                            { "turns", -1 }
                        }
                    }
                }
            };
            var sealedUnit = CreateUnit(repository, "sealed_unit", true, 1, hp: 300, def: 100, pp: 1);
            var enemy = CreateUnit(repository, "enemy", false, 1, hp: 300, pp: 0);
            sealedUnit.EquippedPassiveSkillIds.Add("pas_passive_seal_probe");
            sealedUnit.Ailments.Add(StatusAilment.PassiveSeal);
            var eventBus = new EventBus();
            var logs = new List<string>();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();

            eventBus.Publish(new BattleStartEvent
            {
                Context = new BattleContext(repository)
                {
                    PlayerUnits = new List<BattleUnit> { sealedUnit },
                    EnemyUnits = new List<BattleUnit> { enemy }
                }
            });

            ClassicAssert.AreEqual(1, sealedUnit.CurrentPp);
            ClassicAssert.AreEqual(100, sealedUnit.GetCurrentStat("Def"));
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static void AssertIronGuardShape(PassiveSkillData skill)
        {
            CollectionAssert.AreEqual(
                new[] { "CounterAttack", "OnHitEffect" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(50, IntParam(skill.Effects[0], "power"));
            ClassicAssert.AreEqual(100, IntParam(skill.Effects[0], "hitRate"));
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[0], "HitCount"));
            ClassicAssert.AreEqual("Physical", StringParam(skill.Effects[0], "damageType"));
            ClassicAssert.AreEqual("Melee", StringParam(skill.Effects[0], "attackType"));
            ClassicAssert.AreEqual("SingleEnemy", StringParam(skill.Effects[0], "targetType"));
            var debuff = NestedEffects(skill.Effects[1]).Single();
            ClassicAssert.AreEqual("AddDebuff", debuff.EffectType);
            ClassicAssert.AreEqual("Target", StringParam(debuff, "target"));
            ClassicAssert.AreEqual("Def", StringParam(debuff, "stat"));
            ClassicAssert.AreEqual(0.15d, DoubleParam(debuff, "ratio"), 0.0001d);
            ClassicAssert.AreEqual(-1, IntParam(debuff, "turns"));
        }

        private static void AssertFluorescentCoverShape(PassiveSkillData skill)
        {
            ClassicAssert.IsTrue(skill.HasSimultaneousLimit);
            ClassicAssert.AreEqual(5, skill.UnlockLevel);
            CollectionAssert.Contains(skill.Tags, "SimultaneousLimit");
            CollectionAssert.AreEqual(
                new[] { "CoverAlly", "ModifyDamageCalc", "AddBuff" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[1], "ForceBlock"));
            ClassicAssert.AreEqual(0.5d, DoubleParam(skill.Effects[1], "BlockReduction"), 0.0001d);
            ClassicAssert.AreEqual("Defender", StringParam(skill.Effects[2], "target"));
            ClassicAssert.AreEqual("Def", StringParam(skill.Effects[2], "stat"));
            ClassicAssert.AreEqual(0.2d, DoubleParam(skill.Effects[2], "ratio"), 0.0001d);
            ClassicAssert.AreEqual(-1, IntParam(skill.Effects[2], "turns"));
        }

        private static void AssertNobleBlockShape(PassiveSkillData skill)
        {
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc", "AddBuff", "RecoverPp" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[0], "ForceBlock"));
            ClassicAssert.AreEqual(0.5d, DoubleParam(skill.Effects[0], "BlockReduction"), 0.0001d);
            ClassicAssert.AreEqual("Self", StringParam(skill.Effects[2], "target"));
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[2], "amount"));
            ClassicAssert.AreEqual(0.5d, DoubleParam(skill.Effects[2], "casterHpRatioMax"), 0.0001d);
        }

        private static void AssertStealthBladeShape(PassiveSkillData skill)
        {
            ClassicAssert.IsTrue(skill.HasSimultaneousLimit);
            CollectionAssert.Contains(skill.Tags, "Ranged");
            CollectionAssert.Contains(skill.Tags, "CannotBeCovered");
            CollectionAssert.DoesNotContain(skill.Tags, "SureHit");
            CollectionAssert.AreEqual(
                new[] { "PreemptiveAttack", "OnHitEffect" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(50, IntParam(skill.Effects[0], "power"));
            ClassicAssert.IsFalse(skill.Effects[0].Parameters.ContainsKey("hitRate"));
            ClassicAssert.IsFalse(skill.Effects[0].Parameters.ContainsKey("HitCount"));
            ClassicAssert.AreEqual("Physical", StringParam(skill.Effects[0], "damageType"));
            ClassicAssert.AreEqual("Ranged", StringParam(skill.Effects[0], "attackType"));
            ClassicAssert.AreEqual("SingleEnemy", StringParam(skill.Effects[0], "targetType"));
            CollectionAssert.AreEquivalent(new[] { "Ranged", "CannotBeCovered" }, StringListParam(skill.Effects[0], "tags"));
            var ailments = NestedEffects(skill.Effects[1])
                .Select(effect => StringParam(effect, "ailment"))
                .ToArray();
            CollectionAssert.AreEqual(new[] { "BlockSeal", "PassiveSeal" }, ailments);
        }

        private static void AssertDodgeStepShape(PassiveSkillData skill)
        {
            ClassicAssert.AreEqual(1, skill.PpCost);
            ClassicAssert.AreEqual(PassiveTriggerTiming.SelfBeforeHit, skill.TriggerTiming);
            CollectionAssert.Contains(skill.Tags, "EvasionSkill");
            CollectionAssert.DoesNotContain(skill.Tags, "ApPlus1");
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[0], "ForceEvasion"));
        }

        private static void AssertQuickActionShape(PassiveSkillData skill)
        {
            ClassicAssert.AreEqual(1, skill.PpCost);
            ClassicAssert.AreEqual(PassiveTriggerTiming.AllyOnActiveUse, skill.TriggerTiming);
            ClassicAssert.IsFalse(skill.HasSimultaneousLimit);
            CollectionAssert.AreEqual(new[] { "RecoverAp" }, skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual("Attacker", StringParam(skill.Effects[0], "target"));
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[0], "amount"));
        }

        private static void AssertShadowStepShape(PassiveSkillData skill)
        {
            ClassicAssert.AreEqual(2, skill.PpCost);
            ClassicAssert.AreEqual(PassiveTriggerTiming.SelfBeforeAttack, skill.TriggerTiming);
            ClassicAssert.IsFalse(skill.HasSimultaneousLimit);
            CollectionAssert.AreEqual(new[] { "AddBuff", "AddBuff" }, skill.Effects.Select(effect => effect.EffectType).ToArray());
            AssertFlatSelfBuff(skill.Effects, "Spd", 30);
            AssertFlatSelfBuff(skill.Effects, "Eva", 30);
        }

        private static void AssertHolyGuardShape(PassiveSkillData skill)
        {
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc", "TemporalMark" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[0], "ForceBlock"));
            ClassicAssert.AreEqual(0.5d, DoubleParam(skill.Effects[0], "BlockReduction"), 0.0001d);
            ClassicAssert.AreEqual("Self", StringParam(skill.Effects[1], "target"));
            ClassicAssert.AreEqual("DebuffNullify", StringParam(skill.Effects[1], "key"));
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[1], "count"));
        }

        private static void AssertFirstAidShape(PassiveSkillData skill)
        {
            CollectionAssert.AreEqual(
                new[] { "RecoverHp" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(1, skill.PpCost);
            ClassicAssert.AreEqual("LowestHpAlly", StringParam(skill.Effects[0], "target"));
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[0], "excludeSelf"));
            ClassicAssert.AreEqual(25, IntParam(skill.Effects[0], "amount"));
        }

        private static void AssertChargeActionShape(PassiveSkillData skill)
        {
            CollectionAssert.AreEqual(
                new[] { "RecoverAp", "AddBuff" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual("Self", StringParam(skill.Effects[0], "target"));
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[0], "amount"));
            ClassicAssert.AreEqual("Self", StringParam(skill.Effects[1], "target"));
            ClassicAssert.AreEqual("CritDmg", StringParam(skill.Effects[1], "stat"));
            ClassicAssert.AreEqual(0.5d, DoubleParam(skill.Effects[1], "ratio"), 0.0001d);
            ClassicAssert.AreEqual(1, IntParam(skill.Effects[1], "turns"));
            ClassicAssert.AreEqual(true, BoolParam(skill.Effects[1], "oneTime"));
        }

        private static void AssertShooterAndThiefPassiveSourceData(GameDataRepository repository)
        {
            var battleHorn = repository.PassiveSkills["pas_battle_horn"];
            ClassicAssert.IsFalse(battleHorn.HasSimultaneousLimit);

            var thief = repository.Characters["thief"];
            CollectionAssert.AreEqual(new[] { "pas_dodge_step" }, thief.InnatePassiveSkillIds);
            CollectionAssert.DoesNotContain(thief.InnatePassiveSkillIds, "pas_quick_action");
            CollectionAssert.DoesNotContain(thief.CcInnatePassiveSkillIds, "pas_shadow_step");
            CollectionAssert.Contains(thief.CcInnatePassiveSkillIds, "pas_stealth_blade");
        }

        private static void AssertFlatSelfBuff(IEnumerable<SkillEffectData> effects, string stat, int amount)
        {
            var buff = effects.Single(effect => effect.EffectType == "AddBuff" && StringParam(effect, "stat") == stat);
            ClassicAssert.AreEqual("Self", StringParam(buff, "target"));
            ClassicAssert.AreEqual(amount, IntParam(buff, "amount"));
            ClassicAssert.IsFalse(buff.Parameters.ContainsKey("ratio"));
            ClassicAssert.AreEqual(-1, IntParam(buff, "turns"));
        }

        private static void AssertFirstThreeCharacterPassiveSkillSourceData(GameDataRepository repository)
        {
            var quickStrike = repository.PassiveSkills["pas_quick_strike"];
            ClassicAssert.AreEqual(150, quickStrike.Power);
            CollectionAssert.Contains(quickStrike.Tags, "SureHit");
            ClassicAssert.IsTrue(quickStrike.HasSimultaneousLimit);
            ClassicAssert.AreEqual(150, IntParam(quickStrike.Effects[0], "power"));
            ClassicAssert.AreEqual(100, IntParam(quickStrike.Effects[0], "hitRate"));

            var parry = repository.PassiveSkills["pas_parry"];
            CollectionAssert.AreEqual(
                new[] { "TemporalMark", "RecoverAp" },
                parry.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual("MeleeHitNullify", StringParam(parry.Effects[0], "key"));
            ClassicAssert.AreEqual(1, IntParam(parry.Effects[0], "count"));
            ClassicAssert.AreEqual(1, IntParam(parry.Effects[1], "amount"));

            AssertChargeActionShape(repository.PassiveSkills["pas_charge_action"]);

            var pursuitSlash = repository.PassiveSkills["pas_pursuit_slash"];
            ClassicAssert.AreEqual(75, pursuitSlash.Power);
            ClassicAssert.AreEqual(90, pursuitSlash.HitRate);
            ClassicAssert.AreEqual(75, IntParam(pursuitSlash.Effects[0], "power"));
            ClassicAssert.AreEqual(90, IntParam(pursuitSlash.Effects[0], "hitRate"));
            ClassicAssert.AreEqual(1, IntParam(pursuitSlash.Effects[0], "HitCount"));
            ClassicAssert.AreEqual("Physical", StringParam(pursuitSlash.Effects[0], "damageType"));
            ClassicAssert.AreEqual("Melee", StringParam(pursuitSlash.Effects[0], "attackType"));
            ClassicAssert.AreEqual("SingleEnemy", StringParam(pursuitSlash.Effects[0], "targetType"));
            ClassicAssert.AreEqual("ActiveAttack", StringParam(pursuitSlash.Effects[0], "requiresSourceKind"));
            var pursuitRecover = NestedEffects(pursuitSlash.Effects[1]).Single();
            ClassicAssert.AreEqual("RecoverPp", pursuitRecover.EffectType);
            ClassicAssert.AreEqual(1, IntParam(pursuitRecover, "amount"));

            var vengeanceGuard = repository.PassiveSkills["pas_vengeance_guard"];
            var vengeanceBuff = vengeanceGuard.Effects.Single(effect => effect.EffectType == "AddBuff");
            ClassicAssert.AreEqual("Str", StringParam(vengeanceBuff, "stat"));
            ClassicAssert.AreEqual(0.2d, DoubleParam(vengeanceBuff, "ratio"), 0.0001d);
            ClassicAssert.AreEqual("Stack", StringParam(vengeanceBuff, "stackPolicy"));

            var bruteForce = repository.PassiveSkills["pas_brute_force"];
            var bruteSpeed = bruteForce.Effects.Single(effect =>
                effect.EffectType == "AddBuff" && StringParam(effect, "stat") == "Spd");
            ClassicAssert.AreEqual(20, IntParam(bruteSpeed, "amount"));
            ClassicAssert.IsFalse(bruteSpeed.Parameters.ContainsKey("ratio"));

            AssertNobleBlockShape(repository.PassiveSkills["pas_noble_block"]);
            AssertFluorescentCoverShape(repository.PassiveSkills["pas_fluorescent_cover"]);

            var rapidOrder = repository.PassiveSkills["pas_rapid_order"];
            ClassicAssert.IsTrue(rapidOrder.HasSimultaneousLimit);
            var speedAll = rapidOrder.Effects.Single();
            ClassicAssert.AreEqual("AllAllies", StringParam(speedAll, "target"));
            ClassicAssert.AreEqual("Spd", StringParam(speedAll, "stat"));
            ClassicAssert.AreEqual(20, IntParam(speedAll, "amount"));
        }

        private static List<SkillEffectData> NestedEffects(SkillEffectData effect)
        {
            var raw = effect.Parameters["effects"];
            return raw switch
            {
                JsonElement json => JsonSerializer.Deserialize<List<SkillEffectData>>(json.GetRawText(), JsonOptions)!,
                List<SkillEffectData> list => list,
                _ => throw new AssertionException($"Unexpected nested effects value: {raw?.GetType().Name ?? "null"}")
            };
        }

        private static string StringParam(SkillEffectData effect, string key)
        {
            var raw = effect.Parameters[key];
            return raw switch
            {
                string value => value,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString()!,
                JsonElement json => json.ToString(),
                _ => raw.ToString()!
            };
        }

        private static List<string> StringListParam(SkillEffectData effect, string key)
        {
            var raw = effect.Parameters[key];
            return raw switch
            {
                List<string> list => list,
                JsonElement json => JsonSerializer.Deserialize<List<string>>(json.GetRawText(), JsonOptions)!,
                IEnumerable<object> values => values.Select(value => value?.ToString() ?? string.Empty).ToList(),
                _ => throw new AssertionException($"Unexpected string list value: {raw?.GetType().Name ?? "null"}")
            };
        }

        private static int IntParam(SkillEffectData effect, string key)
        {
            var raw = effect.Parameters[key];
            return raw switch
            {
                int value => value,
                JsonElement { ValueKind: JsonValueKind.Number } json => json.GetInt32(),
                _ => Convert.ToInt32(raw)
            };
        }

        private static double DoubleParam(SkillEffectData effect, string key)
        {
            var raw = effect.Parameters[key];
            return raw switch
            {
                double value => value,
                float value => value,
                JsonElement { ValueKind: JsonValueKind.Number } json => json.GetDouble(),
                _ => Convert.ToDouble(raw)
            };
        }

        private static bool BoolParam(SkillEffectData effect, string key)
        {
            var raw = effect.Parameters[key];
            return raw switch
            {
                bool value => value,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => Convert.ToBoolean(raw)
            };
        }

        private static (BattleUnit Unit, DamageCalculation Calc, int CurrentPp) TriggerNobleBlockAtHp(int currentHp)
        {
            var repository = LoadRepository();
            var eventBus = new EventBus();
            var logs = new List<string>();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var defender = CreateUnit(repository, $"noble_{currentHp}", true, 1, hp: 100, def: 100, pp: 1);
            var attacker = CreateUnit(repository, $"attacker_{currentHp}", false, 1, hp: 300, str: 80, pp: 0);
            defender.CurrentHp = currentHp;
            defender.EquippedPassiveSkillIds.Add("pas_noble_block");
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { defender },
                EnemyUnits = new List<BattleUnit> { attacker }
            };
            var calc = CreateIncomingCalc(attacker, defender, SkillType.Physical, AttackType.Melee);

            eventBus.Publish(new BeforeHitEvent
            {
                Attacker = attacker,
                Defender = defender,
                Skill = calc.Skill,
                Context = context,
                Calc = calc
            });

            return (defender, calc, defender.CurrentPp);
        }

        private static DamageCalculation CreateIncomingCalc(
            BattleUnit attacker,
            BattleUnit defender,
            SkillType type,
            AttackType attackType)
        {
            var skill = TestDataFactory.CreateSkill(type: type, attackType: attackType);
            return new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                HitCount = 1
            };
        }

        private static BattleUnit CreateUnit(
            GameDataRepository repository,
            string id,
            bool isPlayer,
            int position,
            string? activeSkillId = null,
            int hp = 300,
            int str = 50,
            int def = 50,
            int mag = 0,
            int mdef = 0,
            int hit = 1000,
            int eva = 0,
            int crit = 0,
            int block = 0,
            int spd = 20,
            int ap = 0,
            int pp = 2)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = string.IsNullOrWhiteSpace(activeSkillId)
                    ? new List<string>()
                    : new List<string> { activeSkillId },
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

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 99,
                CurrentAp = ap,
                CurrentPp = pp
            };
        }
    }
}
