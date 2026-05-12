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
    public class HpAndResourceStrategyManualPlanTest
    {
        private const string SingleShotId = "act_manual_single_shot_test";
        private const string DualShotId = "act_manual_dual_shot_test";
        private const string AllEnemiesId = "act_manual_all_enemies_test";
        private const string HighDamageId = "act_manual_high_damage_test";
        private const string HealId = "act_manual_heal_test";
        private const string BasicId = "act_manual_basic_test";
        private const string ZeroApId = "act_manual_zero_ap_test";
        private const string PpSkillId = "act_manual_pp_skill_test";

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
            _hunter = CreateUnit("hunter", true, 1, hp: 100, ap: 5, pp: 2, spd: 50);
            _context.PlayerUnits.Add(_hunter);
            _selector = new TargetSelector(_context);
        }

        [Test]
        public void HP用例1_仅低于50_只选择残血目标()
        {
            AddEnemy(1, currentHp: 100);
            var wounded = AddEnemy(2, currentHp: 49);
            var strategy = Only(SingleShotId, HpLessThan(0.5f));

            var targets = Select(strategy);

            ClassicAssert.AreSame(wounded, targets[0]);
        }

        [Test]
        public void HP用例2_仅低于25无人满足_跳过策略1并使用策略2兜底()
        {
            AddEnemy(1, currentHp: 30);
            AddEnemy(2, currentHp: 30);
            _hunter.Strategies.Add(Only(SingleShotId, HpLessThan(0.25f)));
            _hunter.Strategies.Add(new Strategy { SkillId = DualShotId });

            var (skill, targets) = new StrategyEvaluator(_context).Evaluate(_hunter);

            ClassicAssert.AreEqual(DualShotId, skill.Data.Id);
            ClassicAssert.AreEqual(2, targets.Count);
        }

        [Test]
        public void HP用例3_仅高于75_只选择高血量目标()
        {
            var full = AddEnemy(1, currentHp: 100);
            AddEnemy(2, currentHp: 30);
            var strategy = Only(SingleShotId, HpGreaterThan(0.75f));

            var targets = Select(strategy);

            ClassicAssert.AreSame(full, targets[0]);
        }

        [Test]
        public void HP用例4_仅高于100无人满足_策略跳过()
        {
            AddEnemy(1, currentHp: 100);
            AddEnemy(2, currentHp: 70);
            var strategy = Only(SingleShotId, HpGreaterThan(1.0f));

            ClassicAssert.IsNull(Select(strategy));
        }

        [Test]
        public void HP用例5_优先HP比例最低_残血死亡后转向次低()
        {
            var highRatio = AddEnemy(1, maxHp: 200, currentHp: 160);
            var lowRatio = AddEnemy(2, maxHp: 100, currentHp: 30);
            var middleRatio = AddEnemy(3, maxHp: 100, currentHp: 60);
            var strategy = Priority(SingleShotId, new Condition
            {
                Category = ConditionCategory.Hp,
                Operator = "lowest",
                Value = "ratio"
            });

            var first = Select(strategy);
            lowRatio.CurrentHp = 0;
            var second = Select(strategy);

            ClassicAssert.AreSame(lowRatio, first[0]);
            ClassicAssert.AreSame(middleRatio, second[0]);
            ClassicAssert.AreNotSame(highRatio, second[0]);
        }

        [Test]
        public void HP用例6_优先HP比例最高_高血死亡后转向次高()
        {
            var highRatio = AddEnemy(1, maxHp: 200, currentHp: 160);
            var lowRatio = AddEnemy(2, maxHp: 100, currentHp: 30);
            var middleRatio = AddEnemy(3, maxHp: 100, currentHp: 60);
            var strategy = Priority(SingleShotId, new Condition
            {
                Category = ConditionCategory.Hp,
                Operator = "highest",
                Value = "ratio"
            });

            var first = Select(strategy);
            highRatio.CurrentHp = 0;
            var second = Select(strategy);

            ClassicAssert.AreSame(highRatio, first[0]);
            ClassicAssert.AreSame(middleRatio, second[0]);
            ClassicAssert.AreNotSame(lowRatio, second[0]);
        }

        [Test]
        public void HP用例7_仅低于25_过滤掉非濒死目标()
        {
            var lowA = AddEnemy(1, currentHp: 20);
            var lowB = AddEnemy(2, currentHp: 20);
            AddEnemy(3, currentHp: 90);
            var strategy = Only(AllEnemiesId, HpLessThan(0.25f));

            var targets = _selector.SelectTargets(_hunter, strategy, _repository.ActiveSkills[AllEnemiesId]);

            CollectionAssert.AreEquivalent(new[] { lowA, lowB }, targets);
        }

        [Test]
        public void HP用例8_低于75是严格边界_等于75不触发_74才触发()
        {
            AddEnemy(1, currentHp: 75);
            var strategy = Only(SingleShotId, HpLessThan(0.75f));

            ClassicAssert.IsNull(Select(strategy));

            var below = AddEnemy(2, currentHp: 74);
            var targets = Select(strategy);

            ClassicAssert.AreSame(below, targets[0]);
        }

        [Test]
        public void APPP用例1_仅AP低于1_只选择AP为0目标()
        {
            AddEnemy(1, currentHp: 100, ap: 2);
            var exhausted = AddEnemy(2, currentHp: 100, ap: 0);

            var targets = Select(Only(SingleShotId, ResourceLessThan(ConditionCategory.ApPp, "AP", 1)));

            ClassicAssert.AreSame(exhausted, targets[0]);
        }

        [Test]
        public void APPP用例2_优先AP最高_绝不打AP为0目标()
        {
            AddEnemy(1, currentHp: 100, ap: 2);
            var exhausted = AddEnemy(2, currentHp: 100, ap: 0);
            AddEnemy(3, currentHp: 100, ap: 2);

            var targets = Select(Priority(SingleShotId, ResourceRank(ConditionCategory.ApPp, "highest", "AP")));

            ClassicAssert.AreEqual(2, targets[0].CurrentAp);
            ClassicAssert.AreNotSame(exhausted, targets[0]);
        }

        [Test]
        public void APPP用例3_仅AP高于2无人满足_策略跳过()
        {
            AddEnemy(1, currentHp: 100, ap: 2);
            AddEnemy(2, currentHp: 100, ap: 2);

            ClassicAssert.IsNull(Select(Only(SingleShotId, ResourceGreaterThan(ConditionCategory.ApPp, "AP", 2))));
        }

        [Test]
        public void APPP用例4_仅PP低于1_只选择PP为0目标()
        {
            AddEnemy(1, currentHp: 100, pp: 1);
            var spent = AddEnemy(2, currentHp: 100, pp: 0);

            var targets = Select(Only(SingleShotId, ResourceLessThan(ConditionCategory.ApPp, "PP", 1)));

            ClassicAssert.AreSame(spent, targets[0]);
        }

        [Test]
        public void APPP用例5_优先PP最高_选择当前PP最高目标()
        {
            var hasPp = AddEnemy(1, currentHp: 100, pp: 1);
            AddEnemy(2, currentHp: 100, pp: 0);

            var targets = Select(Priority(SingleShotId, ResourceRank(ConditionCategory.ApPp, "highest", "PP")));

            ClassicAssert.AreSame(hasPp, targets[0]);
        }

        [Test]
        public void APPP用例6_AP大于1且小于3_只保留AP为2目标()
        {
            var sword = AddEnemy(1, currentHp: 100, ap: 2);
            AddEnemy(2, currentHp: 100, ap: 0);
            var warrior = AddEnemy(3, currentHp: 100, ap: 2);
            var strategy = new Strategy
            {
                SkillId = AllEnemiesId,
                Condition1 = ResourceGreaterThan(ConditionCategory.ApPp, "AP", 1),
                Mode1 = ConditionMode.Only,
                Condition2 = ResourceLessThan(ConditionCategory.ApPp, "AP", 3),
                Mode2 = ConditionMode.Only
            };

            var targets = _selector.SelectTargets(_hunter, strategy, _repository.ActiveSkills[AllEnemiesId]);

            CollectionAssert.AreEquivalent(new[] { sword, warrior }, targets);
        }

        [Test]
        public void 自身HP用例1_自身HP低于50时触发_恢复到50以上后走兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HealId, SelfHpLessThan(0.5f)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentHp = 49;
            var lowHp = Evaluate();
            _hunter.CurrentHp = 60;
            var recovered = Evaluate();

            ClassicAssert.AreEqual(HealId, lowHp.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, recovered.skill.Data.Id);
        }

        [Test]
        public void 自身HP用例2_自身HP高于75时触发_低于75后走兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HighDamageId, SelfHpGreaterThan(0.75f)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentHp = 100;
            var healthy = Evaluate();
            _hunter.CurrentHp = 74;
            var wounded = Evaluate();

            ClassicAssert.AreEqual(HighDamageId, healthy.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, wounded.skill.Data.Id);
        }

        [Test]
        public void 自身HP用例3_自身HP等于100_只在满血时触发()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HighDamageId, SelfHpEquals(1.0f)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentHp = 100;
            var full = Evaluate();
            _hunter.CurrentHp = 99;
            var scratched = Evaluate();

            ClassicAssert.AreEqual(HighDamageId, full.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, scratched.skill.Data.Id);
        }

        [Test]
        public void 自身HP用例4_自身HP低于25时触发_恢复后不再触发()
        {
            var guardian = CreateUnit("guardian", true, 1, hp: 148, ap: 5, pp: 2, spd: 40);
            _context.PlayerUnits.Clear();
            _context.PlayerUnits.Add(guardian);
            AddEnemy(1);
            guardian.Strategies.Add(Only(HealId, SelfHpLessThan(0.25f)));
            guardian.Strategies.Add(new Strategy { SkillId = BasicId });

            guardian.CurrentHp = 36;
            var dying = new StrategyEvaluator(_context).Evaluate(guardian);
            guardian.CurrentHp = 40;
            var stable = new StrategyEvaluator(_context).Evaluate(guardian);

            ClassicAssert.AreEqual(HealId, dying.Item1.Data.Id);
            ClassicAssert.AreEqual(BasicId, stable.Item1.Data.Id);
        }

        [Test]
        public void 自身HP用例5_多策略血量分段_满血高伤_低血保命_中血兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HighDamageId, SelfHpGreaterThan(0.75f)));
            _hunter.Strategies.Add(Only(HealId, SelfHpLessThan(0.5f)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentHp = 100;
            var full = Evaluate();
            _hunter.CurrentHp = 49;
            var critical = Evaluate();
            _hunter.CurrentHp = 60;
            var middle = Evaluate();

            ClassicAssert.AreEqual(HighDamageId, full.skill.Data.Id);
            ClassicAssert.AreEqual(HealId, critical.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, middle.skill.Data.Id);
        }

        [Test]
        public void 自身APPP用例1_自身AP高于1时触发_AP耗尽后走兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HighDamageId, ResourceGreaterThan(ConditionCategory.SelfApPp, "AP", 1)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentAp = 2;
            var hasAp = Evaluate();
            _hunter.CurrentAp = 0;
            var exhausted = Evaluate();

            ClassicAssert.AreEqual(HighDamageId, hasAp.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, exhausted.skill.Data.Id);
        }

        [Test]
        public void 自身APPP用例2_自身AP低于1_AP耗尽时触发_恢复后走兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(ZeroApId, ResourceLessThan(ConditionCategory.SelfApPp, "AP", 1)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentAp = 0;
            var exhausted = Evaluate();
            _hunter.CurrentAp = 1;
            var restored = Evaluate();

            ClassicAssert.AreEqual(ZeroApId, exhausted.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, restored.skill.Data.Id);
        }

        [Test]
        public void 自身APPP用例3_自身PP高于0时触发_PP耗尽后走兜底()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(PpSkillId, ResourceGreaterThan(ConditionCategory.SelfApPp, "PP", 0)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentPp = 1;
            var hasPp = Evaluate();
            _hunter.CurrentPp = 0;
            var spent = Evaluate();

            ClassicAssert.AreEqual(PpSkillId, hasPp.skill.Data.Id);
            ClassicAssert.AreEqual(BasicId, spent.skill.Data.Id);
        }

        [Test]
        public void 自身APPP用例4_自身AP高于2_初始AP2不触发_蓄到3后触发()
        {
            AddEnemy(1);
            _hunter.Strategies.Add(Only(HighDamageId, ResourceGreaterThan(ConditionCategory.SelfApPp, "AP", 2)));
            _hunter.Strategies.Add(new Strategy { SkillId = BasicId });

            _hunter.CurrentAp = 2;
            var initial = Evaluate();
            _hunter.CurrentAp = 3;
            var charged = Evaluate();

            ClassicAssert.AreEqual(BasicId, initial.skill.Data.Id);
            ClassicAssert.AreEqual(HighDamageId, charged.skill.Data.Id);
        }

        private List<BattleUnit> Select(Strategy strategy)
        {
            return _selector.SelectTargets(_hunter, strategy, _repository.ActiveSkills[SingleShotId]);
        }

        private (BattleKing.Skills.ActiveSkill skill, List<BattleUnit> targets) Evaluate()
        {
            return new StrategyEvaluator(_context).Evaluate(_hunter);
        }

        private static Strategy Only(string skillId, Condition condition)
        {
            return new Strategy
            {
                SkillId = skillId,
                Condition1 = condition,
                Mode1 = ConditionMode.Only
            };
        }

        private static Strategy Priority(string skillId, Condition condition)
        {
            return new Strategy
            {
                SkillId = skillId,
                Condition1 = condition,
                Mode1 = ConditionMode.Priority
            };
        }

        private static Condition HpLessThan(float threshold)
        {
            return new Condition { Category = ConditionCategory.Hp, Operator = "less_than", Value = threshold };
        }

        private static Condition HpGreaterThan(float threshold)
        {
            return new Condition { Category = ConditionCategory.Hp, Operator = "greater_than", Value = threshold };
        }

        private static Condition SelfHpLessThan(float threshold)
        {
            return new Condition { Category = ConditionCategory.SelfHp, Operator = "less_than", Value = threshold };
        }

        private static Condition SelfHpGreaterThan(float threshold)
        {
            return new Condition { Category = ConditionCategory.SelfHp, Operator = "greater_than", Value = threshold };
        }

        private static Condition SelfHpEquals(float threshold)
        {
            return new Condition { Category = ConditionCategory.SelfHp, Operator = "equals", Value = threshold };
        }

        private static Condition ResourceLessThan(ConditionCategory category, string resource, int threshold)
        {
            return ResourceCondition(category, "less_than", resource, threshold);
        }

        private static Condition ResourceGreaterThan(ConditionCategory category, string resource, int threshold)
        {
            return ResourceCondition(category, "greater_than", resource, threshold);
        }

        private static Condition ResourceCondition(ConditionCategory category, string op, string resource, int threshold)
        {
            return new Condition
            {
                Category = category,
                Operator = op,
                Value = $"{resource}:{threshold}"
            };
        }

        private static Condition ResourceRank(ConditionCategory category, string op, string resource)
        {
            return new Condition
            {
                Category = category,
                Operator = op,
                Value = resource
            };
        }

        private BattleUnit AddEnemy(
            int position,
            int maxHp = 100,
            int currentHp = 100,
            int ap = 2,
            int pp = 1)
        {
            var unit = CreateUnit("enemy_" + position, false, position, maxHp, ap, pp, spd: 10);
            unit.CurrentHp = currentHp;
            _context.EnemyUnits.Add(unit);
            return unit;
        }

        private BattleUnit CreateUnit(
            string id,
            bool isPlayer,
            int position,
            int hp,
            int ap,
            int pp,
            int spd)
        {
            var activeSkillIds = new List<string>
            {
                SingleShotId,
                DualShotId,
                AllEnemiesId,
                HighDamageId,
                HealId,
                BasicId,
                ZeroApId,
                PpSkillId
            };
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = activeSkillIds,
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", 20 },
                    { "Def", 0 },
                    { "Mag", 0 },
                    { "MDef", 0 },
                    { "Hit", 100 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", ap },
                    { "PP", pp }
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
            repository.ActiveSkills[SingleShotId] = CreateSkill(SingleShotId, TargetType.SingleEnemy);
            repository.ActiveSkills[DualShotId] = CreateSkill(DualShotId, TargetType.TwoEnemies);
            repository.ActiveSkills[AllEnemiesId] = CreateSkill(AllEnemiesId, TargetType.AllEnemies);
            repository.ActiveSkills[HighDamageId] = CreateSkill(HighDamageId, TargetType.SingleEnemy);
            repository.ActiveSkills[HealId] = CreateSkill(HealId, TargetType.SingleEnemy);
            repository.ActiveSkills[BasicId] = CreateSkill(BasicId, TargetType.SingleEnemy);
            repository.ActiveSkills[ZeroApId] = CreateSkill(ZeroApId, TargetType.SingleEnemy);
            repository.ActiveSkills[PpSkillId] = CreateSkill(PpSkillId, TargetType.SingleEnemy);
            return repository;
        }

        private static ActiveSkillData CreateSkill(string id, TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 0,
                Type = SkillType.Physical,
                AttackType = AttackType.Ranged,
                TargetType = targetType,
                Power = 10,
                HitRate = 100,
                Effects = new List<SkillEffectData>()
            };
        }
    }
}
