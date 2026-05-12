using System.Text.Json;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class RealActiveWhirlwindSlashJsonTest
    {
        private const string SkillId = "act_whirlwind_slash";

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
        public void StepOneAction_RealJsonWhirlwindSlash_HitsOnlyEnemyColumnAndDebuffsDefense()
        {
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[SkillId];
            var caster = CreateUnit(repository, "caster", true, 1, SkillId, hp: 1000, str: 200, hit: 1000, spd: 100);
            var sameFront = CreateUnit(repository, "same_front", false, 1, null, hp: 1000, def: 100, spd: 1);
            var otherFront = CreateUnit(repository, "other_front", false, 2, null, hp: 1000, def: 100, spd: 1);
            var sameBack = CreateUnit(repository, "same_back", false, 4, null, hp: 1000, def: 100, spd: 1);
            var otherBack = CreateUnit(repository, "other_back", false, 5, null, hp: 1000, def: 100, spd: 1);
            caster.Strategies.Add(new Strategy { SkillId = SkillId });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { sameFront, otherFront, sameBack, otherBack }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            AssertWhirlwindSlashShape(skill);
            ClassicAssert.Less(sameFront.CurrentHp, sameFront.Data.BaseStats["HP"]);
            ClassicAssert.Less(sameBack.CurrentHp, sameBack.Data.BaseStats["HP"]);
            ClassicAssert.AreEqual(otherFront.Data.BaseStats["HP"], otherFront.CurrentHp);
            ClassicAssert.AreEqual(otherBack.Data.BaseStats["HP"], otherBack.CurrentHp);
            ClassicAssert.AreEqual(85, sameFront.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(85, sameBack.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherFront.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherBack.GetCurrentStat("Def"));
            AssertAttackTargets(engine, "same_front", "same_back");
            Assert.That(logs, Has.Some.Contains("calc effects:").And.Contains("HitCount=3"));
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("same_front.Def 100->85"));
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("same_back.Def 100->85"));
        }

        [Test]
        public void StepOneAction_RealJsonWhirlwindSlash_OnMissDoesNotDebuffOrRunPostEffects()
        {
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[SkillId];
            var caster = CreateUnit(repository, "caster", true, 1, SkillId, hp: 1000, str: 200, hit: 0, spd: 100);
            var sameFront = CreateUnit(repository, "same_front", false, 1, null, hp: 1000, def: 100, eva: 1000, spd: 1);
            var otherFront = CreateUnit(repository, "other_front", false, 2, null, hp: 1000, def: 100, eva: 1000, spd: 1);
            var sameBack = CreateUnit(repository, "same_back", false, 4, null, hp: 1000, def: 100, eva: 1000, spd: 1);
            var otherBack = CreateUnit(repository, "other_back", false, 5, null, hp: 1000, def: 100, eva: 1000, spd: 1);
            caster.Strategies.Add(new Strategy { SkillId = SkillId });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { sameFront, otherFront, sameBack, otherBack }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            AssertWhirlwindSlashShape(skill);
            ClassicAssert.AreEqual(sameFront.Data.BaseStats["HP"], sameFront.CurrentHp);
            ClassicAssert.AreEqual(sameBack.Data.BaseStats["HP"], sameBack.CurrentHp);
            ClassicAssert.AreEqual(otherFront.Data.BaseStats["HP"], otherFront.CurrentHp);
            ClassicAssert.AreEqual(otherBack.Data.BaseStats["HP"], otherBack.CurrentHp);
            ClassicAssert.AreEqual(100, sameFront.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, sameBack.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherFront.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(100, otherBack.GetCurrentStat("Def"));
            AssertAttackTargets(engine, "same_front", "same_back");
            Assert.That(logs, Has.None.Contains("post effects:"));
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
            int str = 50,
            int def = 50,
            int hit = 1000,
            int eva = 0,
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
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", hit },
                    { "Eva", eva },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", string.IsNullOrWhiteSpace(skillId) ? 0 : 3 },
                    { "PP", 0 }
                }
            };

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 99
            };
        }

        private static void AssertWhirlwindSlashShape(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(TargetType.Column, skill.TargetType);
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc", "OnHitEffect" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(3, ((JsonElement)skill.Effects[0].Parameters["HitCount"]).GetInt32());

            var nestedEffects = JsonSerializer.Deserialize<List<SkillEffectData>>(
                ((JsonElement)skill.Effects[1].Parameters["effects"]).GetRawText(),
                JsonOptions);
            var debuff = nestedEffects!.Single();
            ClassicAssert.AreEqual("AddDebuff", debuff.EffectType);
            ClassicAssert.AreEqual("Target", ((JsonElement)debuff.Parameters["target"]).GetString());
            ClassicAssert.AreEqual("Def", ((JsonElement)debuff.Parameters["stat"]).GetString());
            ClassicAssert.AreEqual(0.15d, ((JsonElement)debuff.Parameters["ratio"]).GetDouble(), 0.0001d);
            ClassicAssert.AreEqual(-1, ((JsonElement)debuff.Parameters["turns"]).GetInt32());
        }

        private static void AssertAttackTargets(BattleEngine engine, params string[] targetIds)
        {
            var entries = engine.BattleLogEntries
                .Where(entry => entry.SkillId == SkillId && entry.Flags.Contains("ActiveAttack"))
                .ToList();

            ClassicAssert.AreEqual(targetIds.Length, entries.Count);
            CollectionAssert.AreEquivalent(targetIds, entries.SelectMany(entry => entry.TargetIds).ToArray());
        }
    }
}
