using System.Reflection;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class PassiveSystemAuditTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static readonly HashSet<string> CalculationEffectTypes = new(StringComparer.Ordinal)
        {
            "ModifyDamageCalc",
            "ConsumeCounter",
            "CoverAlly"
        };

        [Test]
        public void RealPassiveJson_EffectTypes_AreRecognizedBySharedSkillEffectExecutor()
        {
            var repository = LoadRepository();
            var effectTypes = repository.PassiveSkills.Values
                .SelectMany(skill => skill.Effects ?? new List<SkillEffectData>())
                .Select(effect => effect.EffectType)
                .Where(effectType => !string.IsNullOrWhiteSpace(effectType))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(effectType => effectType, StringComparer.Ordinal)
                .ToList();

            var unsupportedByExecutor = effectTypes
                .Where(effectType => !SharedExecutorAccepts(effectType, repository))
                .ToList();
            var notRoutedByPassiveProcessor = effectTypes
                .Where(effectType => !PassiveProcessorRoutesToSharedExecutor(effectType))
                .ToList();

            TestContext.WriteLine("Passive effect types: " + string.Join(", ", effectTypes));

            Assert.Multiple(() =>
            {
                Assert.That(effectTypes, Is.Not.Empty, "Real passive JSON should exercise structured effects.");
                Assert.That(unsupportedByExecutor, Is.Empty,
                    "Every passive effectType in JSON must be recognized by SkillEffectExecutor.");
                Assert.That(notRoutedByPassiveProcessor, Is.Empty,
                    "Every passive effectType in JSON must be routed through PassiveSkillProcessor to the shared executor.");
            });
        }

        [Test]
        public void RealPassiveJson_TagOnlyPassives_AreCoveredByDataContractAllowlist()
        {
            var repository = LoadRepository();
            var dataContractAllowlist = LoadDataContractLegacyTagOnlyAllowlist();
            var tagOnlyPassiveKeys = repository.PassiveSkills.Values
                .Where(skill => (skill.Tags?.Count ?? 0) > 0 && (skill.Effects?.Count ?? 0) == 0)
                .Select(skill => $"passive:{skill.Id}")
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            var missingAllowlistEntries = tagOnlyPassiveKeys
                .Where(key => !dataContractAllowlist.ContainsKey(key))
                .ToList();
            var blankAllowlistReasons = tagOnlyPassiveKeys
                .Where(key => dataContractAllowlist.TryGetValue(key, out var reason) && string.IsNullOrWhiteSpace(reason))
                .ToList();

            TestContext.WriteLine("Tag-only passive skills: " + string.Join(", ", tagOnlyPassiveKeys));

            Assert.Multiple(() =>
            {
                Assert.That(missingAllowlistEntries, Is.Empty,
                    "New passive skills with tags but no structured effects must be added to DataContractTest's explicit allowlist or migrated to effects.");
                Assert.That(blankAllowlistReasons, Is.Empty,
                    "DataContractTest tag-only allowlist entries must keep a reason.");
            });
        }

        [Test]
        public void InitBattle_RealQuickStrikePassive_DamagesConsumesPpAndWritesStructuredLog()
        {
            var repository = LoadRepository();
            var passive = repository.PassiveSkills["pas_quick_strike"];
            var swordsman = new BattleUnit(
                CreateCharacter("swordsman", hp: 100, str: 45, def: 10, spd: 30, ap: 0, pp: 1),
                repository,
                isPlayer: true)
            {
                Position = 1
            };
            var enemy = new BattleUnit(
                CreateCharacter("enemy", hp: 100, str: 10, def: 10, spd: 10, ap: 0, pp: 0),
                repository,
                isPlayer: false)
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

            var passiveLog = engine.BattleLogEntries.Single(entry => entry.SkillId == "pas_quick_strike");
            Assert.Multiple(() =>
            {
                Assert.That(passive.Effects.Select(effect => effect.EffectType), Is.EquivalentTo(new[] { "PreemptiveAttack" }));
                Assert.That(swordsman.CurrentPp, Is.EqualTo(0), "pas_quick_strike should consume PP at BattleStart.");
                Assert.That(enemy.CurrentHp, Is.LessThan(100), "pas_quick_strike should deal real preemptive damage.");
                Assert.That(passiveLog.Damage, Is.EqualTo(100 - enemy.CurrentHp));
                Assert.That(passiveLog.Damage, Is.GreaterThan(0));
                Assert.That(passiveLog.ActorId, Is.EqualTo("swordsman"));
                Assert.That(passiveLog.TargetIds, Is.EqualTo(new[] { "enemy" }));
                Assert.That(passiveLog.Flags, Does.Contain("PassiveTrigger"));
                Assert.That(passiveLog.Flags, Does.Contain("Preemptive"));
                Assert.That(passiveLog.Flags, Does.Contain("Hit"));
                Assert.That(passiveLog.Flags, Does.Contain("ForceHit"));
                Assert.That(passiveLog.Flags, Does.Contain("SureHit"));
                Assert.That(passiveLog.Text, Does.Contain("passive="));
                Assert.That(passiveLog.Text, Does.Contain("damage="));
            });
        }

        [Test]
        public void RealPassiveJson_ElfSibylHealingTouch_IsStructuredAndMutatesRuntimeState()
        {
            var repository = LoadRepository();
            var elfSibyl = repository.Characters["elf_sibyl"];
            var healingTouch = repository.PassiveSkills["pas_healing_touch"];
            var logs = new List<string>();
            var eventBus = new EventBus();
            var processor = new PassiveSkillProcessor(eventBus, repository, logs.Add);
            processor.SubscribeAll();
            var sibyl = new BattleUnit(
                CreateCharacter("sibyl", hp: 100, str: 10, def: 10, spd: 20, ap: 0, pp: 1),
                repository,
                isPlayer: true);
            var ally = new BattleUnit(
                CreateCharacter("ally", hp: 100, str: 10, def: 10, spd: 10, ap: 0, pp: 0),
                repository,
                isPlayer: true);
            var enemy = new BattleUnit(
                CreateCharacter("enemy", hp: 100, str: 10, def: 10, spd: 10, ap: 0, pp: 0),
                repository,
                isPlayer: false);
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
                Skill = TestDataFactory.CreateSkill(),
                DamageDealt = 10,
                IsHit = true,
                Context = context
            });

            Assert.Multiple(() =>
            {
                Assert.That(elfSibyl.InnatePassiveSkillIds, Does.Contain("pas_healing_touch"));
                Assert.That(healingTouch.Tags, Does.Contain("Heal"));
                Assert.That(healingTouch.Tags, Does.Contain("CleanseDebuff"));
                Assert.That(healingTouch.Effects.Select(effect => effect.EffectType),
                    Is.EqualTo(new[] { "HealRatio", "CleanseDebuff" }));
                Assert.That(sibyl.CurrentPp, Is.EqualTo(0),
                    "The structured passive should still pay its PP cost.");
                Assert.That(ally.CurrentHp, Is.EqualTo(65),
                    "HealRatio should restore the attacked ally by 25% max HP.");
                Assert.That(ally.Buffs.Count(buff => buff.Ratio < 0), Is.EqualTo(0),
                    "CleanseDebuff should remove the attacked ally's debuff.");
                Assert.That(logs, Is.Not.Empty, "The passive trigger path should be visible in text logs.");
            });
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static IReadOnlyDictionary<string, string> LoadDataContractLegacyTagOnlyAllowlist()
        {
            var field = typeof(DataContractTest).GetField(
                "LegacyTagOnlySkillAllowlist",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new InvalidOperationException("DataContractTest.LegacyTagOnlySkillAllowlist was not found.");

            return field.GetValue(null) as IReadOnlyDictionary<string, string>
                ?? throw new InvalidOperationException("DataContractTest.LegacyTagOnlySkillAllowlist has an unexpected type.");
        }

        private static bool PassiveProcessorRoutesToSharedExecutor(string effectType)
        {
            var method = typeof(PassiveSkillProcessor).GetMethod(
                "CanExecuteThroughSharedExecutor",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("PassiveSkillProcessor.CanExecuteThroughSharedExecutor was not found.");

            return (bool)(method.Invoke(null, new object[] { effectType }) ?? false);
        }

        private static bool SharedExecutorAccepts(string effectType, GameDataRepository repository)
        {
            var queuedActions = new List<PendingAction>();
            var executor = new SkillEffectExecutor(queuedActions.Add);
            var world = CreateExecutorSmokeWorld(repository);
            var effect = new SkillEffectData
            {
                EffectType = effectType,
                Parameters = CreateSmokeParameters(effectType)
            };
            var logs = CalculationEffectTypes.Contains(effectType)
                ? executor.ExecuteCalculationEffects(
                    world.Context,
                    world.Caster,
                    new List<BattleUnit> { world.Target },
                    new List<SkillEffectData> { effect },
                    "audit_passive",
                    world.Calc,
                    new SkillEffectExecutionState())
                : executor.ExecuteActionEffects(
                    world.Context,
                    world.Caster,
                    new List<BattleUnit> { world.Target },
                    new List<SkillEffectData> { effect },
                    "audit_passive");

            return logs.All(log => !log.Contains($"{effectType}: unsupported", StringComparison.Ordinal));
        }

        private static Dictionary<string, object> CreateSmokeParameters(string effectType)
        {
            return effectType switch
            {
                "ModifyDamageCalc" => new()
                {
                    { "ForceHit", true },
                    { "CannotBeBlocked", true }
                },
                "ConsumeCounter" => new()
                {
                    { "key", "AuditCounter" },
                    { "powerPerCounter", 1 }
                },
                "AddBuff" => new()
                {
                    { "target", "Self" },
                    { "stat", "Str" },
                    { "ratio", 0.1 },
                    { "turns", -1 }
                },
                "AddDebuff" => new()
                {
                    { "target", "Target" },
                    { "stat", "Def" },
                    { "ratio", 0.1 },
                    { "turns", -1 }
                },
                "RemoveBuff" => new()
                {
                    { "target", "Target" },
                    { "kind", "Buff" }
                },
                "RemoveDebuff" or "CleanseDebuff" => new()
                {
                    { "target", "Target" }
                },
                "RecoverAp" or "ApDamage" or "RecoverPp" or "PpDamage" => new()
                {
                    { "target", effectType.EndsWith("Damage", StringComparison.Ordinal) ? "Target" : "Self" },
                    { "amount", 1 }
                },
                "RecoverHp" or "Heal" => new()
                {
                    { "target", "Self" },
                    { "amount", 25 }
                },
                "HealRatio" => new()
                {
                    { "target", "Self" },
                    { "ratio", 0.25 }
                },
                "StatusAilment" => new()
                {
                    { "target", "Target" },
                    { "ailment", "Poison" }
                },
                "TemporalMark" => new()
                {
                    { "target", "Self" },
                    { "key", "AuditMark" },
                    { "count", 1 }
                },
                "ForcedTarget" => new()
                {
                    { "target", "Self" }
                },
                "ActionOrderPriority" => new()
                {
                    { "target", "Self" },
                    { "mode", "Fastest" }
                },
                "ModifyCounter" => new()
                {
                    { "target", "Self" },
                    { "key", "AuditCounter" },
                    { "delta", 1 }
                },
                "GrantSkill" => new()
                {
                    { "target", "Self" },
                    { "skillId", "audit_granted_skill" },
                    { "skillType", "Active" }
                },
                "CounterAttack" or "PursuitAttack" or "PreemptiveAttack" or "BattleEndAttack" => PendingAttackParameters(),
                "PendingAttack" => new(PendingAttackParameters())
                {
                    { "pendingActionType", "BattleEnd" }
                },
                "AugmentCurrentAction" => new()
                {
                    { "tags", new List<string> { "CannotBeBlocked" } },
                    {
                        "calculationEffects",
                        new List<SkillEffectData>
                        {
                            new()
                            {
                                EffectType = "ModifyDamageCalc",
                                Parameters = new Dictionary<string, object>
                                {
                                    { "ForceHit", true }
                                }
                            }
                        }
                    },
                    {
                        "onHitEffects",
                        new List<SkillEffectData>
                        {
                            new()
                            {
                                EffectType = "StatusAilment",
                                Parameters = new Dictionary<string, object>
                                {
                                    { "target", "Target" },
                                    { "ailment", "BlockSeal" }
                                }
                            }
                        }
                    }
                },
                _ => new Dictionary<string, object>()
            };
        }

        private static Dictionary<string, object> PendingAttackParameters() => new()
        {
            { "power", 50 },
            { "hitRate", 100 },
            { "damageType", "Physical" },
            { "attackType", "Melee" },
            { "targetType", "SingleEnemy" },
            { "tags", new List<string> { "SureHit" } }
        };

        private static ExecutorSmokeWorld CreateExecutorSmokeWorld(GameDataRepository repository)
        {
            var caster = new BattleUnit(
                CreateCharacter("audit_caster", hp: 100, str: 30, def: 10, spd: 20, ap: 2, pp: 2),
                repository,
                isPlayer: true)
            {
                Position = 1
            };
            var target = new BattleUnit(
                CreateCharacter("audit_target", hp: 100, str: 10, def: 5, spd: 10, ap: 2, pp: 2),
                repository,
                isPlayer: false)
            {
                Position = 1
            };
            target.Buffs.Add(new Buff
            {
                SkillId = "audit_buff",
                TargetStat = "Str",
                Ratio = 0.2f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            target.Buffs.Add(new Buff
            {
                SkillId = "audit_debuff",
                TargetStat = "Def",
                Ratio = -0.2f,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
            caster.ModifyCounter("AuditCounter", 2);
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { target }
            };
            var skill = new ActiveSkill(new ActiveSkillData
            {
                Id = "audit_active",
                Name = "Audit Active",
                ApCost = 0,
                Type = SkillType.Physical,
                AttackType = AttackType.Melee,
                Power = 100,
                HitRate = 100,
                TargetType = TargetType.SingleEnemy
            }, repository);
            var calc = new DamageCalculation
            {
                Attacker = caster,
                Defender = target,
                Skill = skill,
                HitCount = 1
            };

            return new ExecutorSmokeWorld(context, caster, target, calc);
        }

        private static CharacterData CreateCharacter(
            string id,
            int hp,
            int str,
            int def,
            int spd,
            int ap,
            int pp)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
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

        private sealed record ExecutorSmokeWorld(
            BattleContext Context,
            BattleUnit Caster,
            BattleUnit Target,
            DamageCalculation Calc);
    }
}
