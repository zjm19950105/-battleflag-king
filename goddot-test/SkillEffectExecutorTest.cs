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
    public class SkillEffectExecutorTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void ModifyDamageCalc_主动效果_修改当前伤害上下文()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var attacker = TestDataFactory.CreateUnit();
            var defender = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ModifyDamageCalc",
                    Parameters = new()
                    {
                        { "ForceHit", true },
                        { "HitCount", 9 },
                        { "CannotBeBlocked", true },
                        { "IgnoreDefenseRatio", 0.5 }
                    }
                }
            });
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            executor.ExecuteCalculationEffects(
                context, attacker, new List<BattleUnit> { defender }, skill.Data.Effects,
                skill.Data.Id, calc, new SkillEffectExecutionState());

            ClassicAssert.IsTrue(calc.ForceHit);
            ClassicAssert.IsTrue(calc.CannotBeBlocked);
            ClassicAssert.AreEqual(9, calc.HitCount);
            ClassicAssert.AreEqual(0.5f, calc.IgnoreDefenseRatio);
        }

        [Test]
        public void AddBuff_主动效果_按JSON参数修改目标面板()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit(str: 100);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "AddBuff",
                    Parameters = new()
                    {
                        { "target", "Self" },
                        { "stat", "Str" },
                        { "ratio", 0.2 },
                        { "turns", -1 }
                    }
                }
            });

            executor.ExecuteActionEffects(context, caster, Array.Empty<BattleUnit>(), skill.Data.Effects, skill.Data.Id);

            ClassicAssert.AreEqual(120, caster.GetCurrentStat("Str"));
        }

        [Test]
        public void CounterEffects_主动效果_一次消耗并作用到同次技能所有目标()
        {
            var executor = new SkillEffectExecutor();
            var context = new BattleContext(new GameDataRepository());
            var caster = TestDataFactory.CreateUnit();
            caster.ModifyCounter("Sprite", 2);
            var first = TestDataFactory.CreateUnit(isPlayer: false);
            var second = TestDataFactory.CreateUnit(isPlayer: false);
            var skill = TestDataFactory.CreateSkill(effects: new()
            {
                new SkillEffectData
                {
                    EffectType = "ConsumeCounter",
                    Parameters = new()
                    {
                        { "key", "Sprite" },
                        { "powerPerCounter", 30 }
                    }
                }
            });
            var state = new SkillEffectExecutionState();
            var firstCalc = TestDataFactory.CreateCalc(caster, first, skill);
            var secondCalc = TestDataFactory.CreateCalc(caster, second, skill);

            executor.ExecuteCalculationEffects(context, caster, new List<BattleUnit> { first }, skill.Data.Effects, skill.Data.Id, firstCalc, state);
            executor.ExecuteCalculationEffects(context, caster, new List<BattleUnit> { second }, skill.Data.Effects, skill.Data.Id, secondCalc, state);

            ClassicAssert.AreEqual(0, caster.GetCounter("Sprite"));
            ClassicAssert.AreEqual(60, firstCalc.CounterPowerBonus);
            ClassicAssert.AreEqual(60, secondCalc.CounterPowerBonus);
        }

        [Test]
        public void BattleEngine_主动技能effects_HitCount进入真实战斗路径()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var attacker = new BattleUnit(CreateCharacter("attacker", "act_meteor_slash", hp: 1000, str: 100, def: 0, spd: 100), repository, true)
            {
                Position = 1,
                CurrentLevel = 20
            };
            var defender = new BattleUnit(CreateCharacter("defender", null, hp: 1000, str: 10, def: 50, spd: 1), repository, false)
            {
                Position = 1
            };
            attacker.Strategies.Add(new Strategy { SkillId = "act_meteor_slash" });
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { attacker },
                EnemyUnits = new List<BattleUnit> { defender }
            };
            var engine = new BattleEngine(context) { OnLog = _ => { } };

            var result = engine.StepOneAction();

            ClassicAssert.AreEqual(SingleActionResult.ActionDone, result);
            ClassicAssert.AreEqual(910, defender.CurrentHp);
        }

        private static CharacterData CreateCharacter(string id, string skillId, int hp, int str, int def, int spd)
        {
            return new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = skillId == null ? new List<string>() : new List<string> { skillId },
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
                    { "AP", 3 },
                    { "PP", 0 }
                }
            };
        }
    }
}
