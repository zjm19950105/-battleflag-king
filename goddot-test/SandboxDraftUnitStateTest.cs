using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class SandboxDraftUnitStateTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void MatchesSelection_UsesDayProgressionLevel(int day)
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);
            var unit = service.CreateUnit("swordsman", isPlayer: true, position: 2, day, isCc: false);

            ClassicAssert.IsTrue(SandboxDraftUnitState.MatchesSelection(
                unit,
                "swordsman",
                position: 2,
                isPlayer: true,
                isCc: false,
                day));
        }

        [Test]
        public void MoveToSlot_UpdatesSlotIdentityWithoutReplacingConfiguration()
        {
            var unit = TestDataFactory.CreateUnit();
            unit.Strategies = new List<Strategy>
            {
                new() { SkillId = "custom_active" }
            };

            SandboxDraftUnitState.MoveToSlot(unit, isPlayer: false, slotIndex: 4);

            ClassicAssert.IsFalse(unit.IsPlayer);
            ClassicAssert.AreEqual(5, unit.Position);
            ClassicAssert.AreEqual("custom_active", unit.Strategies[0].SkillId);
        }
    }
}
