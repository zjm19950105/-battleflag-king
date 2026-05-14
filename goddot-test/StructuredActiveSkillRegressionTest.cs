using System.Text.Json;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class StructuredActiveSkillRegressionTest
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
        public void RealActiveJson_MissedMigrationSkills_DeclareStructuredEffects()
        {
            var repository = LoadRepository();

            AssertLordSlash(repository.ActiveSkills["act_lord_slash"]);
            AssertSpiralBlade(repository.ActiveSkills["act_spiral_blade"]);
            AssertResourceSteal(repository.ActiveSkills["act_passive_steal"], "PP", "All", 2);
            AssertResourceSteal(repository.ActiveSkills["act_active_steal"], "AP", "1", 4);
            AssertHitCount(repository.ActiveSkills["act_poison_throw"], 2);
            AssertHitCount(repository.ActiveSkills["act_shadow_bite"], 2);
            AssertIceCoffin(repository.ActiveSkills["act_ice_coffin"]);
            AssertHolyBlade(repository.ActiveSkills["act_holy_blade"]);
            AssertPrimalEdge(repository.ActiveSkills["act_primal_edge"]);
            AssertFairyHeal(repository.ActiveSkills["act_fairy_heal"]);
        }

        [Test]
        public void RealActiveJson_KnownPowerPlus50Skills_UseFlatSkillPowerBonus()
        {
            var repository = LoadRepository();

            AssertFlatPowerBonus(repository.ActiveSkills["act_enhanced_spear"], 50, ("targetClass", "Cavalry"), ("CannotBeBlocked", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_spear_pierce"], 50, ("targetClass", "Flying"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_throwing_spear"], 50, ("casterHasBuff", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_break_formation"], 50, ("targetHasDebuff", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_spike"], 50, ("casterHpRatioMax", "0.5"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_full_assault"], 50, ("casterHpRatioMin", "1"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_accumulate"], 50, ("casterHpRatioMin", "1"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_dive_strike"], 50, ("targetClass", "Cavalry"), ("CannotBeBlocked", "True"));
        }

        [Test]
        public void StepOneAction_LordSlash_HealsOnHitAndOnKill()
        {
            const string skillId = "act_lord_slash";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "lord", true, 1, skillId, hp: 200, str: 200, spd: 100);
            caster.CurrentHp = 100;
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var enemy = CreateUnit(repository, "enemy", false, 1, null, hp: 80, def: 0, spd: 1);
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            });

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            ClassicAssert.IsFalse(enemy.IsAlive);
            ClassicAssert.AreEqual(200, caster.CurrentHp);
        }

        [Test]
        public void StepOneAction_IceCoffin_BonusUsesPreexistingFreezeOnly()
        {
            int cleanDamage = RunIceCoffin(preFrozen: false, out var cleanTarget, out var cleanLogs);
            int frozenDamage = RunIceCoffin(preFrozen: true, out var frozenTarget, out var frozenLogs);

            ClassicAssert.AreEqual(100, cleanDamage);
            ClassicAssert.AreEqual(150, frozenDamage);
            CollectionAssert.Contains(cleanTarget.Ailments, StatusAilment.Freeze);
            CollectionAssert.Contains(frozenTarget.Ailments, StatusAilment.Freeze);
            Assert.That(cleanLogs, Has.None.Contains("PowerBonus=50"));
            Assert.That(frozenLogs, Has.Some.Contains("PowerBonus=50"));
        }

        [Test]
        public void StepOneAction_FairyHeal_HealsTargetRowGrantsDamageNullifyAndSprite()
        {
            const string skillId = "act_fairy_heal";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "sibyl", true, 4, skillId, hp: 200, mag: 100, spd: 100);
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var frontLeft = CreateUnit(repository, "front_left", true, 1, null, hp: 200, spd: 1);
            frontLeft.CurrentHp = 80;
            var frontRight = CreateUnit(repository, "front_right", true, 2, null, hp: 200, spd: 1);
            frontRight.CurrentHp = 150;
            var enemy = CreateUnit(repository, "enemy", false, 1, null, hp: 200, spd: 1);
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, frontLeft, frontRight },
                EnemyUnits = new List<BattleUnit> { enemy }
            });

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            ClassicAssert.AreEqual(180, frontLeft.CurrentHp);
            ClassicAssert.AreEqual(200, frontRight.CurrentHp);
            ClassicAssert.AreEqual(1, caster.GetCounter("Sprite"));
            AssertDamageNullify(frontLeft, skillId);
            AssertDamageNullify(frontRight, skillId);
            ClassicAssert.IsFalse(caster.TemporalStates.Any(state => state.Key == "DamageNullify"));
        }

        [TestCase("act_passive_steal", "PP", "All", 2)]
        [TestCase("act_active_steal", "AP", "1", 4)]
        public void ExecutePostDamageEffects_ResourceSteals_RequireOnlyFirstHitUnblocked(
            string skillId,
            string resource,
            string amount,
            int hitCount)
        {
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[skillId];
            AssertResourceSteal(skill, resource, amount, hitCount);

            var firstBlocked = RunStealPostEffects(repository, skill, resource, firstHitBlocked: true);
            ClassicAssert.AreEqual(0, firstBlocked.CasterGained);
            ClassicAssert.AreEqual(3, firstBlocked.TargetRemaining);

            var secondBlockedOnly = RunStealPostEffects(repository, skill, resource, firstHitBlocked: false);
            ClassicAssert.AreEqual(resource == "PP" ? 3 : 1, secondBlockedOnly.CasterGained);
            ClassicAssert.AreEqual(resource == "PP" ? 0 : 2, secondBlockedOnly.TargetRemaining);
        }

        private static void AssertLordSlash(ActiveSkillData skill)
        {
            var calc = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.IsTrue(GetBool(calc.Parameters, "ForceHit"));

            AssertNestedHealRatio(EffectAt(skill, "OnHitEffect"), "Self", 0.25);
            AssertNestedHealRatio(EffectAt(skill, "OnKillEffect"), "Self", 0.25);
        }

        private static void AssertSpiralBlade(ActiveSkillData skill)
        {
            var calc = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.AreEqual(2, GetInt(calc.Parameters, "HitCount"));
            ClassicAssert.AreEqual(0.5d, GetDouble(calc.Parameters, "IgnoreDefenseRatio"), 0.0001d);

            var recover = NestedEffects(EffectAt(skill, "OnKillEffect")).Single();
            ClassicAssert.AreEqual("RecoverPp", recover.EffectType);
            ClassicAssert.AreEqual("Self", GetString(recover.Parameters, "target"));
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
        }

        private static void AssertResourceSteal(ActiveSkillData skill, string resource, string amount, int hitCount)
        {
            AssertHitCount(skill, hitCount);
            var onHit = EffectAt(skill, "OnHitEffect");
            ClassicAssert.IsTrue(GetBool(onHit.Parameters, "requireFirstHitUnblocked"));
            ClassicAssert.IsFalse(onHit.Parameters.ContainsKey("requireUnblocked"));

            var transfer = NestedEffects(onHit).Single();
            ClassicAssert.AreEqual("TransferResource", transfer.EffectType);
            ClassicAssert.AreEqual(resource, GetString(transfer.Parameters, "resource"));
            ClassicAssert.AreEqual("Target", GetString(transfer.Parameters, "from"));
            ClassicAssert.AreEqual("Self", GetString(transfer.Parameters, "to"));
            ClassicAssert.AreEqual(amount, GetScalarString(transfer.Parameters, "amount"));
        }

        private static void AssertHitCount(ActiveSkillData skill, int expected)
        {
            var calc = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.AreEqual(expected, GetInt(calc.Parameters, "HitCount"));
        }

        private static void AssertIceCoffin(ActiveSkillData skill)
        {
            var bonus = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.AreEqual("Freeze", GetString(bonus.Parameters, "targetHasAilment"));
            ClassicAssert.AreEqual(50, GetInt(bonus.Parameters, "SkillPowerBonus"));

            var ailment = NestedEffects(EffectAt(skill, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("StatusAilment", ailment.EffectType);
            ClassicAssert.AreEqual("Freeze", GetString(ailment.Parameters, "ailment"));
        }

        private static void AssertHolyBlade(ActiveSkillData skill)
        {
            AssertHitCount(skill, 2);
            var bonus = skill.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && effect.Parameters.ContainsKey("SkillPowerBonus"));
            ClassicAssert.AreEqual(1.0d, GetDouble(bonus.Parameters, "casterHpRatioMin"), 0.0001d);
            ClassicAssert.AreEqual(25, GetInt(bonus.Parameters, "SkillPowerBonus"));

            var recover = NestedEffects(EffectAt(skill, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("RecoverPp", recover.EffectType);
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
        }

        private static void AssertPrimalEdge(ActiveSkillData skill)
        {
            var recover = EffectAt(skill, "RecoverPp");
            ClassicAssert.AreEqual("Self", GetString(recover.Parameters, "target"));
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
            ClassicAssert.AreEqual(1.0d, GetDouble(recover.Parameters, "casterHpRatioMin"), 0.0001d);

            var counter = EffectAt(skill, "ModifyCounter");
            ClassicAssert.AreEqual("Sprite", GetString(counter.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(counter.Parameters, "delta"));
            ClassicAssert.IsFalse(counter.Parameters.ContainsKey("casterHpRatioMin"));
        }

        private static void AssertFairyHeal(ActiveSkillData skill)
        {
            var heal = EffectAt(skill, "HealRatio");
            ClassicAssert.AreEqual("AllTargets", GetString(heal.Parameters, "target"));
            ClassicAssert.AreEqual(0.5d, GetDouble(heal.Parameters, "ratio"), 0.0001d);

            var mark = EffectAt(skill, "TemporalMark");
            ClassicAssert.AreEqual("AllTargets", GetString(mark.Parameters, "target"));
            ClassicAssert.AreEqual("DamageNullify", GetString(mark.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(mark.Parameters, "count"));

            var counter = EffectAt(skill, "ModifyCounter");
            ClassicAssert.AreEqual("Sprite", GetString(counter.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(counter.Parameters, "delta"));
        }

        private static void AssertFlatPowerBonus(
            ActiveSkillData skill,
            int expectedBonus,
            params (string Key, string Value)[] preservedParameters)
        {
            string serializedEffects = JsonSerializer.Serialize(skill.Effects);
            Assert.That(serializedEffects, Does.Not.Contain("SkillPowerMultiplier"));

            var bonus = skill.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && effect.Parameters.ContainsKey("SkillPowerBonus"));
            ClassicAssert.AreEqual(expectedBonus, GetInt(bonus.Parameters, "SkillPowerBonus"));

            foreach (var parameter in preservedParameters)
            {
                ClassicAssert.AreEqual(parameter.Value, GetScalarString(bonus.Parameters, parameter.Key));
            }
        }

        private static void AssertNestedHealRatio(SkillEffectData wrapper, string target, double ratio)
        {
            var heal = NestedEffects(wrapper).Single();
            ClassicAssert.AreEqual("HealRatio", heal.EffectType);
            ClassicAssert.AreEqual(target, GetString(heal.Parameters, "target"));
            ClassicAssert.AreEqual(ratio, GetDouble(heal.Parameters, "ratio"), 0.0001d);
        }

        private static int RunIceCoffin(bool preFrozen, out BattleUnit target, out List<string> logs)
        {
            const string skillId = "act_ice_coffin";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "witch", true, 1, skillId, hp: 500, mag: 200, spd: 100);
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            target = CreateUnit(repository, "target", false, 1, null, hp: 1000, mdef: 100, spd: 1);
            if (preFrozen)
                target.Ailments.Add(StatusAilment.Freeze);
            logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { target }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            return target.Data.BaseStats["HP"] - target.CurrentHp;
        }

        private static (int CasterGained, int TargetRemaining) RunStealPostEffects(
            GameDataRepository repository,
            ActiveSkillData skill,
            string resource,
            bool firstHitBlocked)
        {
            var caster = CreateUnit(repository, "thief", true, 1, skill.Id, hp: 500);
            var target = CreateUnit(repository, "target", false, 1, null, hp: 500);
            caster.CurrentAp = 0;
            caster.CurrentPp = 0;
            target.CurrentAp = resource == "AP" ? 3 : 0;
            target.CurrentPp = resource == "PP" ? 3 : 0;
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { target }
            };
            var calculation = new DamageCalculation
            {
                Attacker = caster,
                Defender = target,
                Skill = new ActiveSkill(skill, repository),
                HitCount = GetInt(EffectAt(skill, "ModifyDamageCalc").Parameters, "HitCount")
            };
            var result = new DamageResult(
                physicalDamage: 10,
                magicalDamage: 0,
                isHit: true,
                isCritical: false,
                isBlocked: true,
                isEvaded: false,
                appliedAilments: new List<StatusAilment>(),
                resolvedDefender: target,
                hitResults: new List<DamageHitResult>
                {
                    new() { HitIndex = 1, Landed = true, Blocked = firstHitBlocked },
                    new() { HitIndex = 2, Landed = true, Blocked = !firstHitBlocked }
                });

            _ = new SkillEffectExecutor().ExecutePostDamageEffects(
                context,
                caster,
                target,
                skill.Effects,
                skill.Id,
                calculation,
                result,
                killed: false);

            return resource == "PP"
                ? (caster.CurrentPp, target.CurrentPp)
                : (caster.CurrentAp, target.CurrentAp);
        }

        private static void AssertDamageNullify(BattleUnit unit, string sourceSkillId)
        {
            Assert.That(unit.TemporalStates, Has.Some.Matches<TemporalState>(state =>
                state.Key == "DamageNullify"
                && state.RemainingCount == 1
                && state.SourceSkillId == sourceSkillId));
        }

        private static void AssertActionCompleted(SingleActionResult result)
        {
            ClassicAssert.IsTrue(
                result == SingleActionResult.ActionDone || result == SingleActionResult.PlayerWin,
                $"Unexpected action result: {result}");
        }

        private static SkillEffectData EffectAt(ActiveSkillData skill, string effectType, int occurrence = 0)
        {
            return skill.Effects
                .Where(effect => effect.EffectType == effectType)
                .ElementAt(occurrence);
        }

        private static List<SkillEffectData> NestedEffects(SkillEffectData effect)
        {
            var json = ((JsonElement)effect.Parameters["effects"]).GetRawText();
            return JsonSerializer.Deserialize<List<SkillEffectData>>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to read nested effects for {effect.EffectType}");
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static BattleUnit CreateUnit(
            GameDataRepository repository,
            string id,
            bool isPlayer,
            int position,
            string? skillId,
            int hp = 500,
            int str = 120,
            int def = 100,
            int mag = 20,
            int mdef = 20,
            int spd = 20)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = string.IsNullOrWhiteSpace(skillId)
                    ? new List<string>()
                    : new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", mag },
                    { "MDef", mdef },
                    { "Hit", 1000 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", string.IsNullOrWhiteSpace(skillId) ? 0 : 3 },
                    { "PP", 4 }
                }
            };

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 99
            };
        }

        private static string GetString(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetString() ?? string.Empty,
                string value => value,
                object value => value.ToString() ?? string.Empty
            };
        }

        private static string GetScalarString(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
                JsonElement { ValueKind: JsonValueKind.True } => "True",
                JsonElement { ValueKind: JsonValueKind.False } => "False",
                JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble().ToString("0.########"),
                object value => value.ToString() ?? string.Empty
            };
        }

        private static int GetInt(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.ValueKind == JsonValueKind.String
                    ? int.Parse(element.GetString() ?? "0")
                    : element.GetInt32(),
                int value => value,
                long value => (int)value,
                double value => (int)value,
                string value => int.Parse(value),
                object value => Convert.ToInt32(value)
            };
        }

        private static double GetDouble(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetDouble(),
                double value => value,
                float value => value,
                int value => value,
                string value => double.Parse(value),
                object value => Convert.ToDouble(value)
            };
        }

        private static bool GetBool(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetBoolean(),
                bool value => value,
                string value => bool.Parse(value),
                object value => Convert.ToBoolean(value)
            };
        }
    }
}
