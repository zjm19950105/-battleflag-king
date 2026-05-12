using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    public abstract class StrategyConditionManualPlanTestBase
    {
        protected const string SingleShotId = "act_single_shot_manual_plan_test";
        protected const string DualShotId = "act_dual_shot_manual_plan_test";
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        protected GameDataRepository Repository = null!;
        protected BattleContext Context = null!;
        protected BattleUnit Hunter = null!;
        protected TargetSelector Selector = null!;

        [SetUp]
        public void SetUp()
        {
            Repository = CreateRepository();
            Context = new BattleContext(Repository);
            Hunter = CreateUnit("hunter", true, 1, spd: 50, classes: new() { UnitClass.Archer }, activeSkillIds: new() { SingleShotId, DualShotId });
            Context.PlayerUnits.Add(Hunter);
            Selector = new TargetSelector(Context);
        }

        protected List<BattleUnit> Select(Strategy strategy)
        {
            return Selector.SelectTargets(Hunter, strategy, Repository.ActiveSkills[SingleShotId]);
        }

        protected (ActiveSkillData Skill, List<BattleUnit> Targets) Evaluate()
        {
            var evaluator = new StrategyEvaluator(Context);
            var (skill, targets) = evaluator.Evaluate(Hunter);
            return (skill!.Data, targets!);
        }

        protected Strategy SingleStrategy(string conditionId, string skillId = SingleShotId)
        {
            var item = StrategyConditionCatalog.FindById(conditionId);
            return new Strategy
            {
                SkillId = skillId,
                Condition1 = item.BuildCondition(),
                Mode1 = item.Kind == StrategyConditionKind.Only
                    ? ConditionMode.Only
                    : ConditionMode.Priority
            };
        }

        protected void AddFallbackStrategy(string skillId = DualShotId)
        {
            Hunter.Strategies.Add(new Strategy { SkillId = skillId });
        }

        protected BattleUnit AddAlly(int position, List<UnitClass>? classes = null)
        {
            var unit = CreateUnit("ally_" + position, true, position, spd: 20, classes: classes ?? new() { UnitClass.Infantry });
            Context.PlayerUnits.Add(unit);
            return unit;
        }

        protected BattleUnit AddEnemy(int position, List<UnitClass>? classes = null)
        {
            var unit = CreateUnit("enemy_" + position, false, position, spd: 10, classes: classes ?? new() { UnitClass.Infantry });
            Context.EnemyUnits.Add(unit);
            return unit;
        }

        protected void AddEnemies(params int[] positions)
        {
            foreach (var position in positions)
                AddEnemy(position);
        }

        protected BattleUnit CreateUnit(
            string id,
            bool isPlayer,
            int position,
            int spd,
            List<UnitClass> classes,
            List<string>? activeSkillIds = null)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = classes,
                InnateActiveSkillIds = activeSkillIds ?? new List<string>(),
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", 100 },
                    { "Str", 20 },
                    { "Def", 0 },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", 100 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", 5 },
                    { "PP", 0 }
                }
            };

            return new BattleUnit(data, Repository, isPlayer)
            {
                Position = position
            };
        }

        private static GameDataRepository CreateRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills[SingleShotId] = CreateSkill(SingleShotId, TargetType.SingleEnemy);
            repository.ActiveSkills[DualShotId] = CreateSkill(DualShotId, TargetType.TwoEnemies);
            return repository;
        }

        private static ActiveSkillData CreateSkill(string id, TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                TargetType = targetType,
                Power = 10,
                HitRate = 100,
                Effects = new List<SkillEffectData>()
            };
        }
    }

    [TestFixture]
    public class TeamSizeStrategyManualPlanTest : StrategyConditionManualPlanTestBase
    {
        [Test]
        public void 用例1_敌方数量不少于2_等于2时满足_减员到1后跳过()
        {
            var enemy = AddEnemy(1);
            AddEnemy(2);
            var strategy = SingleStrategy("team-enemy-ge-2");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            enemy.CurrentHp = 0;
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例2_敌方数量不少于3_只有2人时跳过并使用兜底策略()
        {
            AddEnemies(1, 2);
            Hunter.Strategies.Add(SingleStrategy("team-enemy-ge-3"));
            AddFallbackStrategy();

            var result = Evaluate();

            ClassicAssert.AreEqual(DualShotId, result.Skill.Id);
            ClassicAssert.AreEqual(2, result.Targets.Count);
        }

        [Test]
        public void 用例3_敌方数量不多于1_等于1时满足_增加到2后跳过()
        {
            AddEnemy(1);
            var strategy = SingleStrategy("team-enemy-le-1");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            AddEnemy(2);
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例4_友方数量不少于2_包含自身_友方只剩1人后跳过()
        {
            var ally = AddAlly(2);
            AddAlly(4);
            AddEnemy(1);
            var strategy = SingleStrategy("team-ally-ge-2");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            ally.CurrentHp = 0;
            Context.PlayerUnits.Single(unit => unit.Position == 4).CurrentHp = 0;
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例5_友方数量不多于2_等于2时满足_增加到3后跳过()
        {
            AddAlly(2);
            AddEnemy(1);
            var strategy = SingleStrategy("team-ally-le-2");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            AddAlly(4);
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例6_动态减员_敌方4人时不满足不多于3_减员到3后生效()
        {
            var enemy = AddEnemy(1);
            AddEnemies(2, 3, 4);
            var strategy = SingleStrategy("team-enemy-le-3");

            ClassicAssert.IsNull(Select(strategy));

            enemy.CurrentHp = 0;
            ClassicAssert.AreEqual(1, Select(strategy).Count);
        }
    }

    [TestFixture]
    public class UnitClassStrategyManualPlanTest : StrategyConditionManualPlanTestBase
    {
        [TestCase("class-infantry-only", UnitClass.Infantry)]
        [TestCase("class-cavalry-only", UnitClass.Cavalry)]
        [TestCase("class-flying-only", UnitClass.Flying)]
        [TestCase("class-heavy-only", UnitClass.Heavy)]
        [TestCase("class-scout-only", UnitClass.Scout)]
        [TestCase("class-archer-only", UnitClass.Archer)]
        [TestCase("class-mage-only", UnitClass.Mage)]
        public void 用例1到7_仅指定兵种_只选择匹配兵种(string conditionId, UnitClass unitClass)
        {
            var expected = AddEnemy(1, new() { unitClass });
            AddEnemy(2, new() { OtherClass(unitClass) });
            AddEnemy(4, new() { OtherClass(unitClass) });
            var strategy = SingleStrategy(conditionId);

            var targets = Select(strategy);

            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.AreSame(expected, targets[0]);
        }

        [Test]
        public void 用例8_优先步兵_有步兵时优先步兵_步兵死亡后不屏蔽其它目标()
        {
            var infantry = AddEnemy(1, new() { UnitClass.Infantry });
            var mage = AddEnemy(2, new() { UnitClass.Mage });
            AddEnemy(3, new() { UnitClass.Flying });
            var strategy = SingleStrategy("class-infantry-priority");

            ClassicAssert.AreSame(infantry, Select(strategy)[0]);

            infantry.CurrentHp = 0;
            ClassicAssert.AreSame(mage, Select(strategy)[0]);
        }

        [Test]
        public void 用例9_限定兵种没有匹配目标时_策略跳过并使用兜底()
        {
            AddEnemy(1, new() { UnitClass.Mage });
            AddEnemy(2, new() { UnitClass.Flying });
            Hunter.Strategies.Add(SingleStrategy("class-infantry-only"));
            AddFallbackStrategy();

            var result = Evaluate();

            ClassicAssert.AreEqual(DualShotId, result.Skill.Id);
            ClassicAssert.AreEqual(2, result.Targets.Count);
        }

        [TestCase("class-elf-only")]
        [TestCase("class-beastman-only")]
        [TestCase("class-winged-only")]
        public void 用例10_当前阵容无精灵兽人有翼人时_限定条件不满足(string conditionId)
        {
            AddEnemy(1, new() { UnitClass.Infantry });
            AddEnemy(2, new() { UnitClass.Mage });

            ClassicAssert.IsNull(Select(SingleStrategy(conditionId)));
        }

        private static UnitClass OtherClass(UnitClass unitClass)
        {
            return unitClass == UnitClass.Infantry ? UnitClass.Mage : UnitClass.Infantry;
        }
    }

    [TestFixture]
    public class EnemyClassExistsStrategyManualPlanTest : StrategyConditionManualPlanTestBase
    {
        [Test]
        public void 用例1_敌方有术师时策略触发_术师死亡后跳过()
        {
            var mage = AddEnemy(1, new() { UnitClass.Mage });
            AddEnemy(2, new() { UnitClass.Infantry });
            var strategy = SingleStrategy("enemy-class-exists-mage");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            mage.CurrentHp = 0;
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例2_敌方有飞行时策略触发_飞行死亡后跳过()
        {
            var flyer = AddEnemy(1, new() { UnitClass.Flying });
            AddEnemy(2, new() { UnitClass.Infantry });
            var strategy = SingleStrategy("enemy-class-exists-flying");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            flyer.CurrentHp = 0;
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例3_敌方无术师时策略触发_加入术师后跳过()
        {
            AddEnemies(1, 2);
            var strategy = SingleStrategy("enemy-class-missing-mage");

            ClassicAssert.AreEqual(1, Select(strategy).Count);

            AddEnemy(3, new() { UnitClass.Mage });
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例4_敌方有重装时策略触发()
        {
            AddEnemy(1, new() { UnitClass.Heavy });
            AddEnemy(2, new() { UnitClass.Infantry });

            ClassicAssert.AreEqual(1, Select(SingleStrategy("enemy-class-exists-heavy")).Count);
        }

        [Test]
        public void 用例5_动态消亡_术师活着走策略1_术师死亡后走兜底策略2()
        {
            var mage = AddEnemy(1, new() { UnitClass.Mage });
            AddEnemy(2, new() { UnitClass.Infantry });
            Hunter.Strategies.Add(SingleStrategy("enemy-class-exists-mage"));
            AddFallbackStrategy();

            ClassicAssert.AreEqual(SingleShotId, Evaluate().Skill.Id);

            mage.CurrentHp = 0;
            var result = Evaluate();

            ClassicAssert.AreEqual(DualShotId, result.Skill.Id);
            ClassicAssert.AreEqual(1, result.Targets.Count);
        }

        [Test]
        public void 用例6_敌方无步兵_全特殊兵种时策略触发()
        {
            AddEnemy(1, new() { UnitClass.Flying });
            AddEnemy(2, new() { UnitClass.Mage });

            ClassicAssert.AreEqual(1, Select(SingleStrategy("enemy-class-missing-infantry")).Count);
        }
    }
}
