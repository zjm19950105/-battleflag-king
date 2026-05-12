using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Text.Json;

namespace BattleKing.Tests
{
    [TestFixture]
    public class RealActiveRangedJsonTest
    {
        private const string FrontlineHeavyBoltSkillId = "act_frontline_heavy_bolt";

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [TestCase("act_dual_shot", SkillType.Physical)]
        [TestCase("act_magic_missile", SkillType.Magical)]
        public void StepOneAction_RealJsonTwoEnemyRangedSkills_DamageOnlyTwoEnemies(string skillId, SkillType skillType)
        {
            var repository = LoadRepository();
            var attacker = CreateUnit(repository, "attacker", true, 1, skillId, skillType, spd: 100);
            var first = CreateUnit(repository, "first", false, 1, null, SkillType.Physical, spd: 1);
            var second = CreateUnit(repository, "second", false, 2, null, SkillType.Physical, spd: 1);
            var third = CreateUnit(repository, "third", false, 3, null, SkillType.Physical, spd: 1);
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { first, second, third }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.Less(first.CurrentHp, first.Data.BaseStats["HP"]);
            ClassicAssert.Less(second.CurrentHp, second.Data.BaseStats["HP"]);
            ClassicAssert.AreEqual(third.Data.BaseStats["HP"], third.CurrentHp);
            AssertAttackTargets(engine, skillId, "first", "second");
        }

        [Test]
        public void StepOneAction_RealJsonArrayShot_DamagesOnlyAnchorRow()
        {
            const string skillId = "act_array_shot";
            var repository = LoadRepository();
            var attacker = CreateUnit(repository, "attacker", true, 1, skillId, SkillType.Physical, spd: 100);
            var frontLeft = CreateUnit(repository, "front_left", false, 1, null, SkillType.Physical, spd: 1);
            var frontRight = CreateUnit(repository, "front_right", false, 2, null, SkillType.Physical, spd: 1);
            var backLeft = CreateUnit(repository, "back_left", false, 4, null, SkillType.Physical, spd: 1);
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { frontLeft, frontRight, backLeft }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.Less(frontLeft.CurrentHp, frontLeft.Data.BaseStats["HP"]);
            ClassicAssert.Less(frontRight.CurrentHp, frontRight.Data.BaseStats["HP"]);
            ClassicAssert.AreEqual(backLeft.Data.BaseStats["HP"], backLeft.CurrentHp);
            AssertAttackTargets(engine, skillId, "front_left", "front_right");
        }

        [Test]
        public void StepOneAction_RealJsonSingleShot_CanDamageBackRowFlyingThroughFrontRow()
        {
            const string skillId = "act_single_shot";
            var repository = LoadRepository();
            var attacker = CreateUnit(repository, "attacker", true, 1, skillId, SkillType.Physical, spd: 100);
            var front = CreateUnit(repository, "front", false, 1, null, SkillType.Physical, spd: 1);
            var backFlying = CreateUnit(
                repository,
                "back_flying",
                false,
                4,
                null,
                SkillType.Physical,
                hp: 400,
                classes: new List<UnitClass> { UnitClass.Flying },
                spd: 1);
            attacker.Strategies.Add(new Strategy
            {
                SkillId = skillId,
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { front, backFlying }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(front.Data.BaseStats["HP"], front.CurrentHp);
            ClassicAssert.Less(backFlying.CurrentHp, backFlying.Data.BaseStats["HP"]);
            AssertAttackTargets(engine, skillId, "back_flying");
        }

        [TestCase("act_single_shot")]
        [TestCase("act_heavy_bolt")]
        public void StepOneAction_RealJsonSingleTargetRangedSkills_DamageOneEnemy(string skillId)
        {
            var repository = LoadRepository();
            var attacker = CreateUnit(repository, "attacker", true, 1, skillId, SkillType.Physical, spd: 100);
            var enemy = CreateUnit(repository, "enemy", false, 1, null, SkillType.Physical, spd: 1);
            attacker.Strategies.Add(new Strategy { SkillId = skillId });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { enemy }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.Less(enemy.CurrentHp, enemy.Data.BaseStats["HP"]);
            AssertAttackTargets(engine, skillId, "enemy");
        }

        [Test]
        public void StepOneAction_RealJsonFrontlineHeavyBolt_FrontRowAdds100PowerOnly()
        {
            int frontDamage = RunFrontlineHeavyBoltDamage(casterPosition: 1, out var skill, out var frontLogs);
            int backDamage = RunFrontlineHeavyBoltDamage(casterPosition: 4, out _, out var backLogs);

            AssertFrontlineHeavyBoltShape(skill);
            ClassicAssert.AreEqual(360, frontDamage);
            ClassicAssert.AreEqual(180, backDamage);
            ClassicAssert.AreEqual(180, frontDamage - backDamage);
            Assert.That(frontLogs, Has.Some.Contains("calc effects:").And.Contains("PowerBonus=100"));
            Assert.That(backLogs, Has.None.Contains("calc effects:"));
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static int RunFrontlineHeavyBoltDamage(
            int casterPosition,
            out ActiveSkillData skill,
            out List<string> logs)
        {
            var repository = LoadRepository();
            skill = repository.ActiveSkills[FrontlineHeavyBoltSkillId];
            var caster = CreateUnit(
                repository,
                "caster",
                true,
                casterPosition,
                FrontlineHeavyBoltSkillId,
                SkillType.Physical,
                hp: 1000,
                spd: 100);
            var enemy = CreateUnit(
                repository,
                "enemy",
                false,
                1,
                null,
                SkillType.Physical,
                hp: 1000,
                spd: 1);
            caster.Strategies.Add(new Strategy { SkillId = FrontlineHeavyBoltSkillId });
            logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            AssertAttackTargets(engine, FrontlineHeavyBoltSkillId, "enemy");
            return enemy.Data.BaseStats["HP"] - enemy.CurrentHp;
        }

        private static void AssertFrontlineHeavyBoltShape(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(SkillType.Physical, skill.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, skill.AttackType);
            ClassicAssert.AreEqual(100, skill.Power);
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());

            var parameters = skill.Effects.Single().Parameters;
            ClassicAssert.AreEqual("Front", ((JsonElement)parameters["casterRow"]).GetString());
            ClassicAssert.AreEqual(100, ((JsonElement)parameters["SkillPowerBonus"]).GetInt32());
        }

        private static BattleUnit CreateUnit(
            GameDataRepository repository,
            string id,
            bool isPlayer,
            int position,
            string? skillId,
            SkillType primaryAttackType,
            int hp = 500,
            int spd = 20,
            List<UnitClass>? classes = null)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = classes ?? new List<UnitClass> { UnitClass.Archer },
                InnateActiveSkillIds = string.IsNullOrWhiteSpace(skillId)
                    ? new List<string>()
                    : new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", primaryAttackType == SkillType.Physical ? 200 : 20 },
                    { "Def", 20 },
                    { "Mag", primaryAttackType == SkillType.Magical ? 200 : 20 },
                    { "MDef", 20 },
                    { "Hit", 100 },
                    { "Eva", 0 },
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

        private static void AssertAttackTargets(BattleEngine engine, string skillId, params string[] targetIds)
        {
            var entries = engine.BattleLogEntries
                .Where(entry => entry.SkillId == skillId && entry.Flags.Contains("ActiveAttack"))
                .ToList();

            ClassicAssert.AreEqual(targetIds.Length, entries.Count);
            CollectionAssert.AreEquivalent(targetIds, entries.SelectMany(entry => entry.TargetIds).ToArray());
        }
    }
}
