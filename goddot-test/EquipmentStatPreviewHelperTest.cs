using System.Linq;
using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class EquipmentStatPreviewHelperTest
    {
        [Test]
        public void Build_WhenCandidateAddsHp_ReturnsHpPreviewWithoutEquipping()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100);
            var charm = TestDataFactory.CreateEquipment(
                "eq_hp_charm",
                "Hp Charm",
                EquipmentCategory.Accessory,
                new() { { "HP", 15 } });

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", charm);
            var hp = preview.GetRow("HP");

            ClassicAssert.AreEqual(12, preview.Rows.Count);
            ClassicAssert.AreEqual("Accessory1", preview.SlotName);
            ClassicAssert.IsNull(preview.CurrentEquipment);
            ClassicAssert.AreSame(charm, preview.CandidateEquipment);
            ClassicAssert.AreEqual(100, hp.Current);
            ClassicAssert.AreEqual(115, hp.Preview);
            ClassicAssert.AreEqual(15, hp.Delta);
            ClassicAssert.IsNull(unit.Equipment.Accessory1);
        }

        [Test]
        public void Build_WhenCandidateAddsAp_ReturnsApPreview()
        {
            var unit = TestDataFactory.CreateUnit(ap: 2);
            var bracelet = TestDataFactory.CreateEquipment(
                "eq_ap_bracelet",
                "Ap Bracelet",
                EquipmentCategory.Accessory,
                new() { { "AP", 1 } });

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", bracelet);
            var ap = preview.GetRow("AP");

            ClassicAssert.AreEqual(2, ap.Current);
            ClassicAssert.AreEqual(3, ap.Preview);
            ClassicAssert.AreEqual(1, ap.Delta);
            ClassicAssert.IsNull(unit.Equipment.Accessory1);
        }

        [Test]
        public void Build_WhenReplacingWeapon_UsesPhysAtkInStrPreviewAndKeepsOriginalWeapon()
        {
            var unit = TestDataFactory.CreateUnit(str: 50);
            var oldSword = TestDataFactory.CreateEquipment(
                "eq_old_sword",
                "Old Sword",
                EquipmentCategory.Sword,
                new() { { "phys_atk", 8 } });
            var newSword = TestDataFactory.CreateEquipment(
                "eq_new_sword",
                "New Sword",
                EquipmentCategory.Sword,
                new() { { "phys_atk", 15 } });
            unit.Equipment.EquipToSlot("MainHand", oldSword);

            var preview = EquipmentStatPreviewHelper.Build(unit, "MainHand", newSword);
            var str = preview.GetRow("Str");

            ClassicAssert.AreSame(oldSword, preview.CurrentEquipment);
            ClassicAssert.AreSame(newSword, preview.CandidateEquipment);
            ClassicAssert.AreEqual(58, str.Current);
            ClassicAssert.AreEqual(65, str.Preview);
            ClassicAssert.AreEqual(7, str.Delta);
            ClassicAssert.AreEqual("eq_old_sword", unit.Equipment.MainHand.Data.Id);
        }

        [Test]
        public void Build_WhenCandidateIsNull_ReturnsUnequipPreviewWithoutUnequipping()
        {
            var unit = TestDataFactory.CreateUnit(hp: 100, ap: 2);
            var charm = TestDataFactory.CreateEquipment(
                "eq_hp_ap_charm",
                "Hp Ap Charm",
                EquipmentCategory.Accessory,
                new() { { "HP", 10 }, { "AP", 1 } });
            unit.Equipment.EquipToSlot("Accessory1", charm);

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", null);
            var hp = preview.GetRow("HP");
            var ap = preview.GetRow("AP");

            ClassicAssert.AreSame(charm, preview.CurrentEquipment);
            ClassicAssert.IsNull(preview.CandidateEquipment);
            ClassicAssert.AreEqual(110, hp.Current);
            ClassicAssert.AreEqual(100, hp.Preview);
            ClassicAssert.AreEqual(-10, hp.Delta);
            ClassicAssert.AreEqual(3, ap.Current);
            ClassicAssert.AreEqual(2, ap.Preview);
            ClassicAssert.AreEqual(-1, ap.Delta);
            ClassicAssert.AreEqual("eq_hp_ap_charm", unit.Equipment.Accessory1.Data.Id);
        }

        [Test]
        public void Build_ReturnsRowsInExpectedOrder()
        {
            var unit = TestDataFactory.CreateUnit();

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", null);

            CollectionAssert.AreEqual(
                new[] { "HP", "AP", "PP", "Str", "Def", "Mag", "MDef", "Hit", "Eva", "Crit", "Block", "Spd" },
                preview.Rows.Select(row => row.StatName).ToArray());
        }

        [Test]
        public void DeltaBbcode_FormatsOnlyChangedStatsWithoutPreviewEquals()
        {
            var unchanged = new EquipmentStatPreviewRow("HP", 100, 100);
            var increased = new EquipmentStatPreviewRow("Str", 50, 57);
            var decreased = new EquipmentStatPreviewRow("Def", 20, 17);

            ClassicAssert.AreEqual(string.Empty, unchanged.DeltaBbcode);
            ClassicAssert.AreEqual(" [color=#88ff88]+7[/color]", increased.DeltaBbcode);
            ClassicAssert.AreEqual(" [color=#ff8888]-3[/color]", decreased.DeltaBbcode);
            ClassicAssert.False(increased.DeltaBbcode.Contains(" = "));
            ClassicAssert.False(decreased.DeltaBbcode.Contains(" = "));
        }
    }
}
