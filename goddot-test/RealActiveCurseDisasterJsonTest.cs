using System.Text.Json;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class RealActiveCurseDisasterJsonTest
    {
        private const string SkillId = "act_curse_disaster";

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void StepOneAction_RealJsonCurseDisaster_AmplifiesOnlyExistingDebuffsInTargetRow()
        {
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[SkillId];
            var caster = CreateUnit(repository, "caster", true, 1, SkillId, hit: 0, eva: 0, spd: 200);
            var frontA = CreateUnit(repository, "frontA", false, 1, null, hit: 0, eva: 1000, spd: 100);
            var frontB = CreateUnit(repository, "frontB", false, 2, null, hit: 0, eva: 1000, spd: 1);
            var frontNoDebuff = CreateUnit(repository, "frontNoDebuff", false, 3, null, hit: 0, eva: 1000, spd: 1);
            var backDebuffed = CreateUnit(repository, "backDebuffed", false, 4, null, hit: 0, eva: 1000, spd: 1);
            AddPureDebuff(frontA, "preexisting_curse", "Str", -0.2f);
            AddPureDebuff(frontA, "preexisting_curse", "Def", -0.1f);
            AddPureBuff(frontA, "preexisting_buff", "Spd", 0.2f);
            AddPureDebuff(frontB, "preexisting_guard_break", "Def", -0.2f);
            AddPureDebuff(backDebuffed, "outside_row_debuff", "Str", -0.2f);
            caster.Strategies.Add(new Strategy { SkillId = SkillId });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { frontA, frontB, frontNoDebuff, backDebuffed }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            AssertCurseDisasterShape(skill);
            ClassicAssert.AreEqual(1, caster.CurrentAp);
            AssertPureBuff(frontA, "Str", -0.3f);
            AssertPureBuff(frontA, "Def", -0.15f);
            AssertPureBuff(frontA, "Spd", 0.2f);
            AssertPureBuff(frontB, "Def", -0.3f);
            AssertPureBuff(backDebuffed, "Str", -0.2f);
            ClassicAssert.AreEqual(2, frontA.Buffs.Count(buff => buff.SkillId == "preexisting_curse"));
            ClassicAssert.IsEmpty(frontNoDebuff.Buffs);
            ClassicAssert.AreEqual(70, frontA.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(85, frontA.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(120, frontA.GetCurrentStat("Spd"));
            ClassicAssert.AreEqual(70, frontB.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(80, backDebuffed.GetCurrentStat("Str"));
            AssertAllHpUnchanged(caster, frontA, frontB, frontNoDebuff, backDebuffed);
            Assert.That(logs, Has.Some.Contains("effects:").And.Contains("frontA.Str").And.Contains("frontB.Def"));
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
            int hit,
            int eva,
            int spd)
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
                    { "HP", 1000 },
                    { "Str", 100 },
                    { "Def", 100 },
                    { "Mag", 100 },
                    { "MDef", 100 },
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
                CurrentLevel = 30
            };
        }

        private static void AddPureDebuff(BattleUnit unit, string skillId, string stat, float ratio)
        {
            unit.Buffs.Add(new Buff
            {
                SkillId = skillId,
                TargetStat = stat,
                Ratio = ratio,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
        }

        private static void AddPureBuff(BattleUnit unit, string skillId, string stat, float ratio)
        {
            unit.Buffs.Add(new Buff
            {
                SkillId = skillId,
                TargetStat = stat,
                Ratio = ratio,
                RemainingTurns = -1,
                IsPureBuffOrDebuff = true
            });
        }

        private static void AssertPureBuff(BattleUnit unit, string stat, float expectedRatio)
        {
            var buff = unit.Buffs.Single(item => item.TargetStat == stat);
            ClassicAssert.IsTrue(buff.IsPureBuffOrDebuff);
            ClassicAssert.AreEqual(expectedRatio, buff.Ratio, 0.001f);
        }

        private static void AssertAllHpUnchanged(params BattleUnit[] units)
        {
            foreach (var unit in units)
            {
                ClassicAssert.AreEqual(unit.Data.BaseStats["HP"], unit.CurrentHp, unit.Data.Id);
            }
        }

        private static void AssertCurseDisasterShape(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(SkillType.Debuff, skill.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, skill.AttackType);
            ClassicAssert.AreEqual(0, skill.Power);
            ClassicAssert.IsNull(skill.HitRate);
            ClassicAssert.AreEqual(TargetType.Row, skill.TargetType);
            CollectionAssert.Contains(skill.Tags, "DebuffAmplify");
            CollectionAssert.Contains(skill.Tags, "SureHit");
            CollectionAssert.Contains(skill.Tags, "Ranged");
            CollectionAssert.AreEqual(new[] { "AmplifyDebuffs" }, skill.Effects.Select(effect => effect.EffectType).ToArray());

            var parameters = skill.Effects.Single().Parameters;
            ClassicAssert.AreEqual("Target", ((JsonElement)parameters["target"]).GetString());
            ClassicAssert.AreEqual(1.5d, ((JsonElement)parameters["multiplier"]).GetDouble(), 0.0001d);
        }
    }
}
