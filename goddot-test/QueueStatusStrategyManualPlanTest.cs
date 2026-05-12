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
    [TestFixture]
    public class QueueStatusStrategyManualPlanTest
    {
        private const string SingleShotId = "act_single_shot_test";
        private const string DualShotId = "act_dual_shot_test";
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private GameDataRepository _repository = null!;
        private BattleContext _context = null!;
        private BattleUnit _hunter = null!;
        private TargetSelector _selector = null!;

        [SetUp]
        public void SetUp()
        {
            _repository = CreateRepository();
            _context = new BattleContext(_repository);
            _hunter = CreateUnit("hunter", true, 1, spd: 50, activeSkillIds: new() { SingleShotId, DualShotId });
            _context.PlayerUnits.Add(_hunter);
            _selector = new TargetSelector(_context);
        }

        [Test]
        public void 用例1_仅前排_只选择前排_前排死光后跳过()
        {
            AddEnemies(1, 2, 3, 4, 5, 6);
            var strategy = SingleStrategy("queue-front-only");

            var targets = Select(strategy);
            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.IsTrue(targets[0].IsFrontRow);

            foreach (var enemy in _context.EnemyUnits.Where(enemy => enemy.IsFrontRow))
                enemy.CurrentHp = 0;

            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例2_仅后排_只选择后排_后排死光后跳过()
        {
            AddEnemies(1, 2, 3, 4, 5, 6);
            var strategy = SingleStrategy("queue-back-only");

            var targets = Select(strategy);
            ClassicAssert.AreEqual(1, targets.Count);
            ClassicAssert.IsFalse(targets[0].IsFrontRow);

            foreach (var enemy in _context.EnemyUnits.Where(enemy => !enemy.IsFrontRow))
                enemy.CurrentHp = 0;

            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例3_优先前排_有前排时打前排_前排死后转打后排()
        {
            var front = AddEnemy(1);
            var back = AddEnemy(4);
            AddEnemy(5);
            var strategy = SingleStrategy("queue-front-priority");

            var first = Select(strategy);
            ClassicAssert.AreSame(front, first[0]);

            front.CurrentHp = 0;
            var fallback = Select(strategy);
            ClassicAssert.AreSame(back, fallback[0]);
        }

        [Test]
        public void 用例4_优先后排_有后排时打后排_后排死后转打前排()
        {
            var front = AddEnemy(1);
            var back = AddEnemy(4);
            AddEnemy(5);
            var strategy = SingleStrategy("queue-back-priority");

            var first = Select(strategy);
            ClassicAssert.AreSame(back, first[0]);

            foreach (var enemy in _context.EnemyUnits.Where(enemy => !enemy.IsFrontRow))
                enemy.CurrentHp = 0;

            var fallback = Select(strategy);
            ClassicAssert.AreSame(front, fallback[0]);
        }

        [Test]
        public void 用例5_仅前排加近战_前排存在时打前排_前排死后不打后排()
        {
            var front = AddEnemy(1);
            AddEnemy(4);
            var strategy = SingleStrategy("queue-front-only");
            var melee = CreateSkill(SingleShotId, AttackType.Melee, TargetType.SingleEnemy);

            var first = _selector.SelectTargets(_hunter, strategy, melee);
            ClassicAssert.AreSame(front, first[0]);

            front.CurrentHp = 0;
            ClassicAssert.IsNull(_selector.SelectTargets(_hunter, strategy, melee));
        }

        [Test]
        public void 用例6_仅前后排一列_只选择前后排都有人存活的列()
        {
            var columnOneFront = AddEnemy(1);
            var columnOneBack = AddEnemy(4);
            AddEnemy(2);
            AddEnemy(3);
            AddEnemy(6);
            var strategy = SingleStrategy("queue-front-and-back-only");

            var first = Select(strategy);
            CollectionAssert.Contains(new[] { columnOneFront, columnOneBack }, first[0]);

            columnOneFront.CurrentHp = 0;
            columnOneBack.CurrentHp = 0;

            var next = Select(strategy);
            ClassicAssert.AreEqual(3, GetColumn(next[0].Position));
        }

        [Test]
        public void 用例7_仅2体以上的一排_只选择排内至少2人的目标()
        {
            var front = AddEnemy(1);
            var frontOther = AddEnemy(2);
            AddEnemy(4);
            var strategy = SingleStrategy("queue-only-column-at-least-2");

            var targets = _selector.SelectTargets(_hunter, strategy, AllEnemiesSkill());

            CollectionAssert.AreEquivalent(new[] { front, frontOther }, targets);

            frontOther.CurrentHp = 0;
            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void 用例8_仅3体以上一排不满足时_跳过策略1并使用策略2兜底()
        {
            AddEnemy(1);
            AddEnemy(4);
            var evaluator = new StrategyEvaluator(_context);
            _hunter.Strategies.Add(new Strategy
            {
                SkillId = SingleShotId,
                Condition1 = Condition("queue-only-column-at-least-3"),
                Mode1 = ConditionMode.Only
            });
            _hunter.Strategies.Add(new Strategy { SkillId = DualShotId });

            var (skill, targets) = evaluator.Evaluate(_hunter);

            ClassicAssert.AreEqual(DualShotId, skill.Data.Id);
            ClassicAssert.AreEqual(2, targets.Count);
        }

        [Test]
        public void 用例9_优先人数最多和最少的一排_选择不同排()
        {
            AddEnemy(1);
            AddEnemy(2);
            AddEnemy(3);
            var back = AddEnemy(4);

            var most = Select(SingleStrategy("queue-most-column-priority"));
            var fewest = Select(SingleStrategy("queue-fewest-column-priority"));

            ClassicAssert.IsTrue(most[0].IsFrontRow);
            ClassicAssert.IsFalse(fewest[0].IsFrontRow);
            ClassicAssert.AreSame(back, fewest[0]);
        }

        [Test]
        public void 用例10_仅白天和仅夜晚_跟随BattleContext昼夜()
        {
            var evaluator = new ConditionEvaluator(_context);
            var day = Condition("queue-only-daytime");
            var night = Condition("queue-only-nighttime");

            _context.IsDaytime = true;
            ClassicAssert.IsTrue(evaluator.Evaluate(day, _hunter));
            ClassicAssert.IsFalse(evaluator.Evaluate(night, _hunter));

            _context.IsDaytime = false;
            ClassicAssert.IsFalse(evaluator.Evaluate(day, _hunter));
            ClassicAssert.IsTrue(evaluator.Evaluate(night, _hunter));
        }

        private List<BattleUnit> Select(Strategy strategy)
        {
            return _selector.SelectTargets(_hunter, strategy, _repository.ActiveSkills[SingleShotId]);
        }

        private Strategy SingleStrategy(string conditionId)
        {
            var item = StrategyConditionCatalog.FindById(conditionId);
            var condition = item.BuildCondition();
            return new Strategy
            {
                SkillId = SingleShotId,
                Condition1 = condition,
                Mode1 = item.Kind == StrategyConditionKind.Only
                    ? ConditionMode.Only
                    : ConditionMode.Priority
            };
        }

        private Condition Condition(string id)
        {
            return StrategyConditionCatalog.BuildCondition(id);
        }

        private void AddEnemies(params int[] positions)
        {
            foreach (var position in positions)
                AddEnemy(position);
        }

        private BattleUnit AddEnemy(int position)
        {
            var unit = CreateUnit("enemy_" + position, false, position, spd: 10);
            _context.EnemyUnits.Add(unit);
            return unit;
        }

        private BattleUnit CreateUnit(string id, bool isPlayer, int position, int spd, List<string>? activeSkillIds = null)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
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

            return new BattleUnit(data, _repository, isPlayer)
            {
                Position = position
            };
        }

        private static GameDataRepository CreateRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills[SingleShotId] = CreateSkill(SingleShotId, AttackType.Ranged, TargetType.SingleEnemy);
            repository.ActiveSkills[DualShotId] = CreateSkill(DualShotId, AttackType.Ranged, TargetType.TwoEnemies);
            return repository;
        }

        private static ActiveSkillData CreateSkill(string id, AttackType attackType, TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 1,
                Type = SkillType.Physical,
                AttackType = attackType,
                TargetType = targetType,
                Power = 10,
                HitRate = 100,
                Effects = new List<SkillEffectData>()
            };
        }

        private static ActiveSkillData AllEnemiesSkill()
        {
            return CreateSkill("act_all_enemies_probe", AttackType.Ranged, TargetType.AllEnemies);
        }

        private static int GetColumn(int position)
        {
            return (position - 1) % 3 + 1;
        }
    }
}
