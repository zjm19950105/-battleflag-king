using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class AttackAttributeStrategyManualPlanTest
    {
        private const string PhysicalSkillId = "manual_attack_physical";
        private const string MagicalSkillId = "manual_attack_magical";
        private const string FallbackSkillId = "manual_attack_fallback";

        private BattleContext _context = null!;
        private ConditionEvaluator _evaluator = null!;
        private BattleUnit _caster = null!;
        private BattleUnit _target = null!;

        [SetUp]
        public void SetUp()
        {
            _context = new BattleContext(CreateRepository());
            _evaluator = new ConditionEvaluator(_context);
            _caster = CreateUnit("caster", true, 1, new() { PhysicalSkillId, MagicalSkillId, FallbackSkillId });
            _target = CreateUnit("target", false, 1);
            _context.PlayerUnits.Add(_caster);
            _context.EnemyUnits.Add(_target);
        }

        [TestCase("physical", SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy)]
        [TestCase("magical", SkillType.Magical, AttackType.Magic, TargetType.SingleEnemy)]
        [TestCase("melee", SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy)]
        [TestCase("ranged", SkillType.Physical, AttackType.Ranged, TargetType.SingleEnemy)]
        [TestCase("row", SkillType.Physical, AttackType.Ranged, TargetType.Row)]
        [TestCase("column", SkillType.Physical, AttackType.Ranged, TargetType.Column)]
        [TestCase("front_and_back", SkillType.Physical, AttackType.Ranged, TargetType.FrontAndBack)]
        [TestCase("all", SkillType.Physical, AttackType.Ranged, TargetType.AllEnemies)]
        public void ManualPlan_AttackAttribute_MatchingSkillPasses(
            string value,
            SkillType skillType,
            AttackType attackType,
            TargetType targetType)
        {
            var skill = Skill("probe", skillType, attackType, targetType);
            var condition = AttackCondition(value);
            _context.CurrentCalc = TestDataFactory.CreateCalc(_caster, _target, new BattleKing.Skills.ActiveSkill(skill, _context.GameData));

            ClassicAssert.IsTrue(_evaluator.Evaluate(condition, _target, _caster));
            ClassicAssert.IsTrue(_evaluator.Evaluate(condition, _target, _caster, skill));
        }

        [TestCase("physical", SkillType.Magical, AttackType.Magic, TargetType.SingleEnemy)]
        [TestCase("magical", SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy)]
        [TestCase("melee", SkillType.Physical, AttackType.Ranged, TargetType.SingleEnemy)]
        [TestCase("ranged", SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy)]
        [TestCase("row", SkillType.Physical, AttackType.Ranged, TargetType.Column)]
        [TestCase("column", SkillType.Physical, AttackType.Ranged, TargetType.Row)]
        [TestCase("front_and_back", SkillType.Physical, AttackType.Ranged, TargetType.Column)]
        [TestCase("all", SkillType.Physical, AttackType.Ranged, TargetType.SingleEnemy)]
        public void ManualPlan_AttackAttribute_NonMatchingSkillFails(
            string value,
            SkillType skillType,
            AttackType attackType,
            TargetType targetType)
        {
            var skill = Skill("probe", skillType, attackType, targetType);

            ClassicAssert.IsFalse(_evaluator.Evaluate(AttackCondition(value), _target, _caster, skill));
        }

        [Test]
        public void ManualPlan_AttackAttribute_StrategyEvaluatorSkipsMismatchedSkillAndUsesFallback()
        {
            _caster.Strategies.Add(new Strategy
            {
                SkillId = PhysicalSkillId,
                Condition1 = AttackCondition("magical"),
                Mode1 = ConditionMode.Only
            });
            _caster.Strategies.Add(new Strategy { SkillId = FallbackSkillId });
            var evaluator = new StrategyEvaluator(_context);

            var (skill, targets) = evaluator.Evaluate(_caster);

            ClassicAssert.AreEqual(FallbackSkillId, skill.Data.Id);
            CollectionAssert.AreEqual(new[] { _target }, targets);
        }

        private static Condition AttackCondition(string value)
        {
            return new Condition
            {
                Category = ConditionCategory.AttackAttribute,
                Operator = "equals",
                Value = value
            };
        }

        private static GameDataRepository CreateRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills[PhysicalSkillId] = Skill(PhysicalSkillId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy);
            repository.ActiveSkills[MagicalSkillId] = Skill(MagicalSkillId, SkillType.Magical, AttackType.Magic, TargetType.SingleEnemy);
            repository.ActiveSkills[FallbackSkillId] = Skill(FallbackSkillId, SkillType.Physical, AttackType.Ranged, TargetType.SingleEnemy);
            return repository;
        }

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static BattleUnit CreateUnit(string id, bool isPlayer, int position, List<string>? activeSkillIds = null)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = activeSkillIds ?? new List<string>(),
                BaseStats = BaseStats()
            };

            return new BattleUnit(data, CreateRepository(), isPlayer)
            {
                Position = position
            };
        }

        private static Dictionary<string, int> BaseStats()
        {
            return new Dictionary<string, int>
            {
                { "HP", 100 }, { "Str", 20 }, { "Def", 10 }, { "Mag", 20 }, { "MDef", 10 },
                { "Hit", 100 }, { "Eva", 10 }, { "Crit", 5 }, { "Block", 3 },
                { "Spd", 20 }, { "AP", 3 }, { "PP", 1 }
            };
        }

        private static ActiveSkillData Skill(string id, SkillType type, AttackType attackType, TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 1,
                Type = type,
                AttackType = attackType,
                TargetType = targetType,
                Power = 10,
                HitRate = 100,
                Effects = new List<SkillEffectData>()
            };
        }
    }

    [TestFixture]
    public class AttributeRankStrategyManualPlanTest
    {
        private BattleContext _context = null!;
        private TargetSelector _selector = null!;
        private BattleUnit _caster = null!;

        [SetUp]
        public void SetUp()
        {
            _context = new BattleContext(null!);
            _selector = new TargetSelector(_context);
            _caster = TestDataFactory.CreateUnit(isPlayer: true);
            _caster.Position = 1;
            _context.PlayerUnits.Add(_caster);
        }

        [TestCase("MaxHp")]
        [TestCase("MaxAp")]
        [TestCase("MaxPp")]
        [TestCase("HP")]
        [TestCase("Str")]
        [TestCase("Mag")]
        [TestCase("Def")]
        [TestCase("MDef")]
        [TestCase("Spd")]
        [TestCase("Hit")]
        [TestCase("Eva")]
        [TestCase("Crit")]
        [TestCase("Block")]
        public void ManualPlan_AttributeRank_HighestAndLowestPreferCorrectSameSideTargets(string statName)
        {
            var low = AddEnemyForStat(statName, position: 1, value: 10);
            AddEnemyForStat(statName, position: 2, value: 20);
            var high = AddEnemyForStat(statName, position: 3, value: 30);

            ClassicAssert.AreSame(high, SelectByRank(statName, "highest"));
            ClassicAssert.AreSame(low, SelectByRank(statName, "lowest"));
        }

        [Test]
        public void ManualPlan_AttributeRank_CurrentHpUsesCurrentBattleHpInsteadOfMaxHp()
        {
            var fullLowMax = AddEnemyForStat("MaxHp", position: 1, value: 60);
            fullLowMax.CurrentHp = 60;
            var damagedHighMax = AddEnemyForStat("MaxHp", position: 2, value: 150);
            damagedHighMax.CurrentHp = 30;

            ClassicAssert.AreSame(damagedHighMax, SelectByRank("HP", "lowest"));
        }

        [Test]
        public void ManualPlan_AttributeRank_TiesAllPassConditionEvaluation()
        {
            var firstLow = AddEnemyForStat("MDef", position: 1, value: 18);
            var secondLow = AddEnemyForStat("MDef", position: 2, value: 18);
            var high = AddEnemyForStat("MDef", position: 3, value: 70);
            var evaluator = new ConditionEvaluator(_context);
            var condition = new Condition { Category = ConditionCategory.AttributeRank, Operator = "lowest", Value = "MDef" };

            ClassicAssert.IsTrue(evaluator.Evaluate(condition, _caster, firstLow));
            ClassicAssert.IsTrue(evaluator.Evaluate(condition, _caster, secondLow));
            ClassicAssert.IsFalse(evaluator.Evaluate(condition, _caster, high));
        }

        [Test]
        public void ManualPlan_AttributeRank_RankingPoolIsTheTargetsOwnSide()
        {
            var playerLowest = TestDataFactory.CreateUnit(mdef: 5, isPlayer: true);
            playerLowest.Position = 2;
            _context.PlayerUnits.Add(playerLowest);
            AddEnemyForStat("MDef", position: 1, value: 1);
            var evaluator = new ConditionEvaluator(_context);
            var condition = new Condition { Category = ConditionCategory.AttributeRank, Operator = "lowest", Value = "MDef" };

            ClassicAssert.IsTrue(evaluator.Evaluate(condition, _caster, playerLowest));
            ClassicAssert.IsFalse(evaluator.Evaluate(condition, _caster, _caster));
        }

        private BattleUnit SelectByRank(string statName, string op)
        {
            var strategy = new Strategy
            {
                SkillId = "test_skill",
                Condition1 = new Condition { Category = ConditionCategory.AttributeRank, Operator = op, Value = statName },
                Mode1 = ConditionMode.Priority
            };
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);

            return _selector.SelectTargets(_caster, strategy, skill.Data)[0];
        }

        private BattleUnit AddEnemyForStat(string statName, int position, int value)
        {
            var unit = statName switch
            {
                "MaxHp" => TestDataFactory.CreateUnit(hp: value, isPlayer: false),
                "MaxAp" => TestDataFactory.CreateUnit(ap: value / 10, isPlayer: false),
                "MaxPp" => TestDataFactory.CreateUnit(pp: value / 10, isPlayer: false),
                "HP" => TestDataFactory.CreateUnit(hp: 100, isPlayer: false),
                "Str" => TestDataFactory.CreateUnit(str: value, isPlayer: false),
                "Mag" => TestDataFactory.CreateUnit(mag: value, isPlayer: false),
                "Def" => TestDataFactory.CreateUnit(def: value, isPlayer: false),
                "MDef" => TestDataFactory.CreateUnit(mdef: value, isPlayer: false),
                "Spd" => TestDataFactory.CreateUnit(spd: value, isPlayer: false),
                "Hit" => TestDataFactory.CreateUnit(hit: value, isPlayer: false),
                "Eva" => TestDataFactory.CreateUnit(eva: value, isPlayer: false),
                "Crit" => TestDataFactory.CreateUnit(crit: value, isPlayer: false),
                "Block" => TestDataFactory.CreateUnit(block: value, isPlayer: false),
                _ => TestDataFactory.CreateUnit(isPlayer: false)
            };

            if (statName == "HP")
                unit.CurrentHp = value;

            unit.Position = position;
            _context.EnemyUnits.Add(unit);
            return unit;
        }
    }

    [TestFixture]
    public class StatusStrategyManualPlanTest
    {
        private BattleContext _context = null!;
        private TargetSelector _selector = null!;
        private BattleUnit _caster = null!;

        [SetUp]
        public void SetUp()
        {
            _context = new BattleContext(null!);
            _selector = new TargetSelector(_context);
            _caster = TestDataFactory.CreateUnit(isPlayer: true);
            _caster.Position = 1;
            _context.PlayerUnits.Add(_caster);
        }

        [TestCase(StatusAilment.Poison, "Poison")]
        [TestCase(StatusAilment.Burn, "Burn")]
        [TestCase(StatusAilment.Freeze, "Freeze")]
        [TestCase(StatusAilment.Stun, "Stun")]
        [TestCase(StatusAilment.Darkness, "Darkness")]
        [TestCase(StatusAilment.CritSeal, "CritSeal")]
        [TestCase(StatusAilment.BlockSeal, "BlockSeal")]
        public void ManualPlan_Status_OnlySpecificAilmentFiltersTargets(StatusAilment ailment, string value)
        {
            AddEnemy(1);
            var affected = AddEnemy(2);
            affected.Ailments.Add(ailment);

            var targets = SelectOnly(StatusCondition(value));

            CollectionAssert.AreEqual(new[] { affected }, targets);
        }

        [Test]
        public void ManualPlan_Status_OnlyAilmentAndNoneAreOpposites()
        {
            var clean = AddEnemy(1);
            var frozen = AddEnemy(2);
            frozen.Ailments.Add(StatusAilment.Freeze);

            CollectionAssert.AreEqual(new[] { frozen }, SelectOnly(StrategyConditionCatalog.BuildCondition("status-ailment-only")));
            CollectionAssert.AreEqual(new[] { clean }, SelectOnly(StrategyConditionCatalog.BuildCondition("status-only-none")));
        }

        [Test]
        public void ManualPlan_Status_PriorityBuffPrefersBuffedTargetThenFallsBack()
        {
            var clean = AddEnemy(1);
            var buffed = AddEnemy(2);
            buffed.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.2f });
            var strategy = SingleStrategy(StrategyConditionCatalog.BuildCondition("status-buff-priority"), ConditionMode.Priority);
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.SingleEnemy);

            ClassicAssert.AreSame(buffed, _selector.SelectTargets(_caster, strategy, skill.Data)[0]);

            buffed.CurrentHp = 0;
            ClassicAssert.AreSame(clean, _selector.SelectTargets(_caster, strategy, skill.Data)[0]);
        }

        [Test]
        public void ManualPlan_Status_OnlyDebuffFiltersTargets()
        {
            AddEnemy(1);
            var debuffed = AddEnemy(2);
            debuffed.Buffs.Add(new Buff { TargetStat = "Def", Ratio = -0.2f });

            CollectionAssert.AreEqual(new[] { debuffed }, SelectOnly(StrategyConditionCatalog.BuildCondition("status-debuff-only")));
        }

        [Test]
        public void ManualPlan_Status_NegativePoisonFiltersPoisonedTargetsOut()
        {
            var poisoned = AddEnemy(1);
            poisoned.Ailments.Add(StatusAilment.Poison);
            var clean = AddEnemy(2);

            CollectionAssert.AreEqual(new[] { clean }, SelectOnly(StatusCondition("not:Poison")));
        }

        private List<BattleUnit> SelectOnly(Condition condition)
        {
            var skill = TestDataFactory.CreateSkill(attackType: AttackType.Ranged, targetType: TargetType.AllEnemies);
            return _selector.SelectTargets(_caster, SingleStrategy(condition, ConditionMode.Only), skill.Data);
        }

        private static Strategy SingleStrategy(Condition condition, ConditionMode mode)
        {
            return new Strategy
            {
                SkillId = "test_skill",
                Condition1 = condition,
                Mode1 = mode
            };
        }

        private static Condition StatusCondition(string value)
        {
            return new Condition
            {
                Category = ConditionCategory.Status,
                Operator = "equals",
                Value = value
            };
        }

        private BattleUnit AddEnemy(int position)
        {
            var unit = TestDataFactory.CreateUnit(isPlayer: false);
            unit.Position = position;
            _context.EnemyUnits.Add(unit);
            return unit;
        }
    }

    [TestFixture]
    public class SelfStateStrategyManualPlanTest
    {
        private const string FirstActionSkillId = "manual_self_first";
        private const string SecondActionSkillId = "manual_self_second";
        private const string BuffSkillId = "manual_self_buff";
        private const string FallbackSkillId = "manual_self_fallback";

        private BattleContext _context = null!;
        private BattleUnit _caster = null!;
        private BattleUnit _ally = null!;
        private BattleUnit _enemy = null!;

        [SetUp]
        public void SetUp()
        {
            var repository = CreateRepository();
            _context = new BattleContext(repository);
            _caster = CreateUnit("caster", true, 1, new()
            {
                FirstActionSkillId,
                SecondActionSkillId,
                BuffSkillId,
                FallbackSkillId
            }, repository);
            _ally = CreateUnit("ally", true, 2, new(), repository);
            _enemy = CreateUnit("enemy", false, 1, new(), repository);
            _context.PlayerUnits.Add(_caster);
            _context.PlayerUnits.Add(_ally);
            _context.EnemyUnits.Add(_enemy);
        }

        [Test]
        public void ManualPlan_SelfState_SelfAndNotSelfFilterAllyTargets()
        {
            var selector = new TargetSelector(_context);
            var allAllies = Skill("manual_all_allies", SkillType.Assist, AttackType.Ranged, TargetType.AllAllies);

            var selfTargets = selector.SelectTargets(_caster, Strategy("self-only-self", allAllies.Id), allAllies);
            var notSelfTargets = selector.SelectTargets(_caster, Strategy("self-only-not-self", allAllies.Id), allAllies);

            CollectionAssert.AreEqual(new[] { _caster }, selfTargets);
            CollectionAssert.AreEqual(new[] { _ally }, notSelfTargets);
        }

        [Test]
        public void ManualPlan_SelfState_BuffConditionControlsStrategySelection()
        {
            _caster.Strategies.Add(Strategy("self-only-buff", BuffSkillId));
            _caster.Strategies.Add(new Strategy { SkillId = FallbackSkillId });
            var evaluator = new StrategyEvaluator(_context);

            var (withoutBuff, _) = evaluator.Evaluate(_caster);
            _caster.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.2f });
            var (withBuff, targets) = evaluator.Evaluate(_caster);

            ClassicAssert.AreEqual(FallbackSkillId, withoutBuff.Data.Id);
            ClassicAssert.AreEqual(BuffSkillId, withBuff.Data.Id);
            CollectionAssert.AreEqual(new[] { _enemy }, targets);
        }

        [Test]
        public void ManualPlan_SelfState_DebuffConditionDetectsNegativeBuffs()
        {
            var evaluator = new ConditionEvaluator(_context);
            var condition = new Condition
            {
                Category = ConditionCategory.SelfState,
                Operator = "equals",
                Value = "debuff"
            };

            ClassicAssert.IsFalse(evaluator.Evaluate(condition, _caster));
            _caster.Buffs.Add(new Buff { TargetStat = "Def", Ratio = -0.2f });
            ClassicAssert.IsTrue(evaluator.Evaluate(condition, _caster));
        }

        [Test]
        public void ManualPlan_SelfState_ActionNumberStrategiesUseNextActionNumber()
        {
            _caster.Strategies.Add(Strategy("self-only-action-1", FirstActionSkillId));
            _caster.Strategies.Add(Strategy("self-only-action-2", SecondActionSkillId));
            _caster.Strategies.Add(new Strategy { SkillId = FallbackSkillId });
            var evaluator = new StrategyEvaluator(_context);

            _caster.ActionCount = 0;
            ClassicAssert.AreEqual(FirstActionSkillId, evaluator.Evaluate(_caster).Item1.Data.Id);

            _caster.ActionCount = 1;
            ClassicAssert.AreEqual(SecondActionSkillId, evaluator.Evaluate(_caster).Item1.Data.Id);

            _caster.ActionCount = 2;
            ClassicAssert.AreEqual(FallbackSkillId, evaluator.Evaluate(_caster).Item1.Data.Id);
        }

        [TestCase(UnitState.Charging, "charging")]
        [TestCase(UnitState.Stunned, "stunned")]
        [TestCase(UnitState.Frozen, "frozen")]
        [TestCase(UnitState.Darkness, "darkness")]
        public void ManualPlan_SelfState_RuntimeUnitStateConditionsAreDetectable(UnitState state, string value)
        {
            var evaluator = new ConditionEvaluator(_context);
            var condition = new Condition
            {
                Category = ConditionCategory.SelfState,
                Operator = "equals",
                Value = value
            };

            ClassicAssert.IsFalse(evaluator.Evaluate(condition, _caster));
            _caster.State = state;
            ClassicAssert.IsTrue(evaluator.Evaluate(condition, _caster));
        }

        private static Strategy Strategy(string conditionId, string skillId)
        {
            return new Strategy
            {
                SkillId = skillId,
                Condition1 = StrategyConditionCatalog.BuildCondition(conditionId),
                Mode1 = ConditionMode.Only
            };
        }

        private static GameDataRepository CreateRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.ActiveSkills[FirstActionSkillId] = Skill(FirstActionSkillId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy);
            repository.ActiveSkills[SecondActionSkillId] = Skill(SecondActionSkillId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy);
            repository.ActiveSkills[BuffSkillId] = Skill(BuffSkillId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy);
            repository.ActiveSkills[FallbackSkillId] = Skill(FallbackSkillId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy);
            return repository;
        }

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static BattleUnit CreateUnit(
            string id,
            bool isPlayer,
            int position,
            List<string> activeSkillIds,
            GameDataRepository repository)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = activeSkillIds,
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", 100 }, { "Str", 20 }, { "Def", 10 }, { "Mag", 20 }, { "MDef", 10 },
                    { "Hit", 100 }, { "Eva", 10 }, { "Crit", 5 }, { "Block", 3 },
                    { "Spd", 20 }, { "AP", 5 }, { "PP", 1 }
                }
            };

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position
            };
        }

        private static ActiveSkillData Skill(string id, SkillType type, AttackType attackType, TargetType targetType)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = 1,
                Type = type,
                AttackType = attackType,
                TargetType = targetType,
                Power = 10,
                HitRate = 100,
                Effects = new List<SkillEffectData>()
            };
        }
    }
}
