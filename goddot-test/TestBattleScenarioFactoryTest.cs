using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class TestBattleScenarioFactoryTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private GameDataRepository _repository = null!;

        [SetUp]
        public void SetUp()
        {
            _repository = new GameDataRepository();
            _repository.LoadAll(DataPath);
        }

        [Test]
        public void CreateContext_PreservesSparsePositionsAndUsesBattleSetupService()
        {
            var factory = new TestBattleScenarioFactory(_repository);

            var context = factory.CreateContext(new[]
            {
                new TestBattleScenarioSlot("swordsman", 1, true, 1),
                new TestBattleScenarioSlot("mercenary", 6, true, 5, true),
                new TestBattleScenarioSlot("swordsman", 2, false, 2)
            });

            ClassicAssert.AreSame(_repository, context.GameData);
            ClassicAssert.AreEqual(2, context.PlayerUnits.Count);
            ClassicAssert.AreEqual(1, context.EnemyUnits.Count);

            var frontPlayer = context.GetUnitAtPosition(isPlayer: true, position: 1);
            var backPlayer = context.GetUnitAtPosition(isPlayer: true, position: 6);
            var enemy = context.GetUnitAtPosition(isPlayer: false, position: 2);

            ClassicAssert.AreEqual("swordsman", frontPlayer.Data.Id);
            ClassicAssert.AreEqual(1, frontPlayer.Position);
            ClassicAssert.IsTrue(frontPlayer.IsPlayer);
            ClassicAssert.AreEqual(1, frontPlayer.CurrentLevel);
            ClassicAssert.AreEqual("equ_recruit_sword", frontPlayer.Equipment.MainHand?.Data.Id);

            ClassicAssert.AreEqual("mercenary", backPlayer.Data.Id);
            ClassicAssert.AreEqual(6, backPlayer.Position);
            ClassicAssert.IsTrue(backPlayer.IsPlayer);
            ClassicAssert.IsTrue(backPlayer.IsCc);
            ClassicAssert.AreEqual(40, backPlayer.CurrentLevel);
            ClassicAssert.IsTrue(backPlayer.Strategies.Count > 0);

            ClassicAssert.AreEqual("swordsman", enemy.Data.Id);
            ClassicAssert.AreEqual(2, enemy.Position);
            ClassicAssert.IsFalse(enemy.IsPlayer);
            ClassicAssert.AreEqual(10, enemy.CurrentLevel);
        }

        [Test]
        public void CreateContext_SupportsSixUnitsPerSide()
        {
            var factory = new TestBattleScenarioFactory(_repository);

            var slots = Enumerable.Range(1, 6)
                .Select(position => new TestBattleScenarioSlot("swordsman", position, true, 1))
                .Concat(Enumerable.Range(1, 6)
                    .Select(position => new TestBattleScenarioSlot("mercenary", position, false, 1)));

            var context = factory.CreateContext(slots);

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, context.PlayerUnits.Select(u => u.Position));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, context.EnemyUnits.Select(u => u.Position));
        }

        [Test]
        public void CreateContext_CreatesFreshContextAndUnitsEveryTime()
        {
            var factory = new TestBattleScenarioFactory(_repository);
            var slots = new[]
            {
                new TestBattleScenarioSlot("swordsman", 3, true, 1),
                new TestBattleScenarioSlot("mercenary", 4, false, 1)
            };

            var first = factory.CreateContext(slots);
            var second = factory.CreateContext(slots);

            ClassicAssert.AreNotSame(first, second);
            ClassicAssert.AreNotSame(first.PlayerUnits[0], second.PlayerUnits[0]);
            ClassicAssert.AreNotSame(first.EnemyUnits[0], second.EnemyUnits[0]);

            first.PlayerUnits[0].TakeDamage(999);
            ClassicAssert.IsTrue(second.PlayerUnits[0].IsAlive);
        }

        [Test]
        public void CreateContext_RejectsInvalidSideSizesAndPositions()
        {
            var factory = new TestBattleScenarioFactory(_repository);

            Assert.Throws<ArgumentException>(() => factory.CreateContext(new[]
            {
                new TestBattleScenarioSlot("swordsman", 1, true, 1)
            }));

            Assert.Throws<ArgumentOutOfRangeException>(() => factory.CreateContext(new[]
            {
                new TestBattleScenarioSlot("swordsman", 0, true, 1),
                new TestBattleScenarioSlot("mercenary", 1, false, 1)
            }));

            Assert.Throws<ArgumentException>(() => factory.CreateContext(new[]
            {
                new TestBattleScenarioSlot("swordsman", 1, true, 1),
                new TestBattleScenarioSlot("mercenary", 1, true, 1),
                new TestBattleScenarioSlot("mercenary", 1, false, 1)
            }));
        }
    }
}
