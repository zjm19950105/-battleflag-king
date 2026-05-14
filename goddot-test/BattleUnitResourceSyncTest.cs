using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.IO;

namespace BattleKing.Tests
{
    [TestFixture]
    public class BattleUnitResourceSyncTest
    {
        [Test]
        public void SyncResourceCapsFromStats_EquippingHpApPpGearUpdatesInitialResourcesAndFixedCaps()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100, ap: 2, pp: 1);
            var charm = TestDataFactory.CreateEquipment(
                "eq_resource_charm",
                "Resource Charm",
                EquipmentCategory.Accessory,
                new() { { "HP", 15 }, { "AP", 1 }, { "PP", 2 } });

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", charm);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.AreEqual(115, unit.GetCurrentStat("HP"));
            ClassicAssert.AreEqual(115, unit.CurrentHp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxAp);
            ClassicAssert.AreEqual(3, unit.InitialAp);
            ClassicAssert.AreEqual(3, unit.CurrentAp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxPp);
            ClassicAssert.AreEqual(3, unit.InitialPp);
            ClassicAssert.AreEqual(3, unit.PassivePpBudget);
            ClassicAssert.AreEqual(3, unit.CurrentPp);
        }

        [Test]
        public void RecoverApAndPp_UseFixedResourceCapInsteadOfInitialResources()
        {
            var unit = TestDataFactory.CreateUnit(ap: 2, pp: 1);

            unit.RecoverAp(1);
            unit.RecoverPp(2);
            unit.RecoverAp(99);
            unit.RecoverPp(99);

            ClassicAssert.AreEqual(2, unit.InitialAp);
            ClassicAssert.AreEqual(1, unit.InitialPp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.CurrentAp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.CurrentPp);
        }

        [Test]
        public void ConstructorAndEquipmentClampInitialResourcesToFixedCap()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100, ap: 3, pp: 3);
            var charm = TestDataFactory.CreateEquipment(
                "eq_over_cap_charm",
                "Over Cap Charm",
                EquipmentCategory.Accessory,
                new() { { "AP", 3 }, { "PP", 3 } });

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", charm);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.InitialAp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.InitialPp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.CurrentAp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.CurrentPp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxAp);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxPp);
        }

        [Test]
        public void PassiveBudget_UsesInitialPpInsteadOfFixedBattleResourceCap()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            repository.PassiveSkills["cost_two"] = TestDataFactory.CreatePassiveData("cost_two", ppCost: 2);
            var unit = new BattleUnit(new CharacterData
            {
                Id = "budget_unit",
                Name = "Budget Unit",
                BaseStats = new() { { "HP", 100 }, { "AP", 2 }, { "PP", 1 } }
            }, repository, isPlayer: true);

            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxPp);
            ClassicAssert.AreEqual(1, unit.PassivePpBudget);
            ClassicAssert.IsFalse(unit.CanEquipPassive("cost_two"));
        }

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void SyncResourceCapsFromStats_EquippingHpGearPreservesMissingHp()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100);
            unit.TakeDamage(20);
            var charm = TestDataFactory.CreateEquipment(
                "eq_hp_charm",
                "HP Charm",
                EquipmentCategory.Accessory,
                new() { { "HP", 15 } });

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", charm);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.AreEqual(115, unit.GetCurrentStat("HP"));
            ClassicAssert.AreEqual(95, unit.CurrentHp);
        }

        [Test]
        public void SyncResourceCapsFromStats_EquippingHpGearDoesNotReviveDeadUnit()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100);
            unit.TakeDamage(100);
            var charm = TestDataFactory.CreateEquipment(
                "eq_hp_charm",
                "HP Charm",
                EquipmentCategory.Accessory,
                new() { { "HP", 15 } });

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", charm);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.AreEqual(115, unit.GetCurrentStat("HP"));
            ClassicAssert.AreEqual(0, unit.CurrentHp);
            ClassicAssert.IsFalse(unit.IsAlive);
        }
    }
}
