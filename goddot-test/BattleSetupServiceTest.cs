using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class BattleSetupServiceTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void CreateUnit_AppliesInitialEquipmentDefaultStrategiesAndAutoPassives()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var unit = service.CreateUnit("swordsman", isPlayer: true, position: 2, day: 1, isCc: false);

            ClassicAssert.AreEqual("swordsman", unit.Data.Id);
            ClassicAssert.IsTrue(unit.IsPlayer);
            ClassicAssert.AreEqual(2, unit.Position);
            ClassicAssert.AreEqual(1, unit.CurrentLevel);
            ClassicAssert.IsFalse(unit.IsCc);
            ClassicAssert.AreEqual("equ_recruit_sword", unit.Equipment.MainHand?.Data.Id);
            ClassicAssert.AreEqual(1, unit.Strategies.Count);
            ClassicAssert.AreEqual("act_sharp_slash", unit.Strategies[0].SkillId);
            ClassicAssert.IsNull(unit.Strategies[0].Condition1);
            ClassicAssert.IsNull(unit.Strategies[0].Condition2);
            CollectionAssert.AreEqual(new[] { "pas_quick_strike" }, unit.EquippedPassiveSkillIds);
            ClassicAssert.AreEqual(1, unit.PassiveStrategies.Count);
            ClassicAssert.AreEqual("pas_quick_strike", unit.PassiveStrategies[0].SkillId);
            ClassicAssert.IsNull(unit.PassiveStrategies[0].Condition1);
            ClassicAssert.IsNull(unit.PassiveStrategies[0].Condition2);
        }

        [Test]
        public void EquipmentPpBonus_UpdatesPassiveBudgetForTwoPpPassive()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);
            var unit = service.CreateUnit("fighter", isPlayer: true, position: 1, day: 6, isCc: true);
            unit.EquippedPassiveSkillIds.Clear();
            unit.PassiveStrategies.Clear();

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", repository.Equipments["equ_pp_crystal_pendant"]);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.GreaterOrEqual(unit.PassivePpBudget, 3);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxPp);
            ClassicAssert.AreEqual(unit.PassivePpBudget, unit.CurrentPp);
            ClassicAssert.IsTrue(unit.CanEquipPassive("pas_hundred_crit"));
        }
    }
}
