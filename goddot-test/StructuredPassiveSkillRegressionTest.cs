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

            AssertIronGuardShape(repository.PassiveSkills["pas_iron_guard"]);
            AssertFluorescentCoverShape(repository.PassiveSkills["pas_fluorescent_cover"]);
            AssertNobleBlockShape(repository.PassiveSkills["pas_noble_block"]);
            AssertStealthBladeShape(repository.PassiveSkills["pas_stealth_blade"]);
            AssertHolyGuardShape(repository.PassiveSkills["pas_holy_guard"]);
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
            Assert.That(logs, Has.Some.Contains("Counter queued").And.Contains("pas_iron_guard"));
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
            var debuff = NestedEffects(skill.Effects[1]).Single();
            ClassicAssert.AreEqual("AddDebuff", debuff.EffectType);
            ClassicAssert.AreEqual("Target", StringParam(debuff, "target"));
            ClassicAssert.AreEqual("Def", StringParam(debuff, "stat"));
            ClassicAssert.AreEqual(0.15d, DoubleParam(debuff, "ratio"), 0.0001d);
            ClassicAssert.AreEqual(-1, IntParam(debuff, "turns"));
        }

        private static void AssertFluorescentCoverShape(PassiveSkillData skill)
        {
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
            CollectionAssert.AreEqual(
                new[] { "PreemptiveAttack", "OnHitEffect" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(50, IntParam(skill.Effects[0], "power"));
            ClassicAssert.AreEqual(100, IntParam(skill.Effects[0], "hitRate"));
            ClassicAssert.AreEqual(2, IntParam(skill.Effects[0], "HitCount"));
            var ailments = NestedEffects(skill.Effects[1])
                .Select(effect => StringParam(effect, "ailment"))
                .ToArray();
            CollectionAssert.AreEqual(new[] { "BlockSeal", "PassiveSeal" }, ailments);
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
