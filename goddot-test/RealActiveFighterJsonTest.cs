using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class RealActiveFighterJsonTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void StepOneAction_RealJsonArrowCover_DamagesEnemyAndBuffsSelfDefense()
        {
            const string skillId = "act_arrow_cover";
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[skillId];
            var caster = CreateUnit(repository, "caster", true, 1, skillId, str: 160, def: 100, spd: 100);
            var ally = CreateUnit(repository, "ally", true, 2, null, hp: 300, spd: 1);
            var enemy = CreateUnit(repository, "enemy", false, 1, null, hp: 300, def: 20, spd: 1);
            caster.Strategies.Add(new Strategy
            {
                SkillId = skillId,
                Condition1 = new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" },
                Mode1 = ConditionMode.Priority
            });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, ally },
                EnemyUnits = new List<BattleUnit> { enemy }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(SkillType.Physical, skill.Type);
            ClassicAssert.AreEqual(TargetType.SingleEnemy, skill.TargetType);
            CollectionAssert.AreEqual(new[] { "AddBuff" }, skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(120, caster.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(2, caster.CurrentAp);
            ClassicAssert.Less(enemy.CurrentHp, enemy.Data.BaseStats["HP"]);
            ClassicAssert.AreEqual(ally.Data.BaseStats["HP"], ally.CurrentHp);
            AssertActiveTargets(engine, skillId, "enemy");
            Assert.That(logs, Has.Some.Contains("effects:").And.Contains("caster.Def 100->120"));
        }

        [Test]
        public void StepOneAction_RealJsonAttractAttention_HitsOneEnemyTwiceBuffsSelfAndRecoversPp()
        {
            const string skillId = "act_attract_attention";
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[skillId];
            var caster = CreateUnit(repository, "caster", true, 1, skillId, str: 160, def: 100, spd: 100, pp: 2);
            caster.CurrentPp = 0;
            var firstEnemy = CreateUnit(repository, "first_enemy", false, 1, null, hp: 500, def: 20, spd: 1);
            var secondEnemy = CreateUnit(repository, "second_enemy", false, 2, null, hp: 500, def: 20, spd: 1);
            caster.Strategies.Add(new Strategy
            {
                SkillId = skillId,
                Condition1 = new Condition { Category = ConditionCategory.Hp, Operator = "lowest" },
                Mode1 = ConditionMode.Priority
            });
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { firstEnemy, secondEnemy }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(SkillType.Physical, skill.Type);
            ClassicAssert.AreEqual(TargetType.SingleEnemy, skill.TargetType);
            CollectionAssert.AreEqual(
                new[] { "ModifyDamageCalc", "AddBuff", "OnHitEffect" },
                skill.Effects.Select(effect => effect.EffectType).ToArray());
            ClassicAssert.AreEqual(2, skill.ApCost);
            ClassicAssert.AreEqual(1, caster.CurrentAp);
            ClassicAssert.AreEqual(150, caster.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(1, caster.CurrentPp);
            ClassicAssert.Less(firstEnemy.CurrentHp, firstEnemy.Data.BaseStats["HP"]);
            ClassicAssert.AreEqual(secondEnemy.Data.BaseStats["HP"], secondEnemy.CurrentHp);
            AssertActiveTargets(engine, skillId, "first_enemy");
            Assert.That(logs, Has.Some.Contains("effects:").And.Contains("caster.Def 100->150"));
            Assert.That(logs, Has.Some.Contains("calc effects:").And.Contains("HitCount=2"));
            Assert.That(logs, Has.Some.Contains("段数: 2 hit"));
            Assert.That(logs, Has.Some.Contains("post effects:").And.Contains("caster.PP 0->1"));
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
            int spd = 20,
            int pp = 0)
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
                    { "Hit", 1000 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", string.IsNullOrWhiteSpace(skillId) ? 0 : 3 },
                    { "PP", pp }
                }
            };

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 99
            };
        }

        private static void AssertActiveTargets(BattleEngine engine, string skillId, params string[] targetIds)
        {
            var entries = engine.BattleLogEntries
                .Where(entry => entry.SkillId == skillId && entry.Flags.Contains("ActiveAttack"))
                .ToList();

            ClassicAssert.AreEqual(targetIds.Length, entries.Count);
            CollectionAssert.AreEquivalent(targetIds, entries.SelectMany(entry => entry.TargetIds).ToArray());
        }
    }
}
