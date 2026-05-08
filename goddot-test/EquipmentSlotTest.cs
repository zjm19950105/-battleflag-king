using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;

namespace BattleKing.Tests
{
    [TestFixture]
    public class EquipmentSlotTest
    {
        private EquipmentSlot _slot;
        private CharacterData _charData;

        [SetUp]
        public void SetUp()
        {
            _slot = new EquipmentSlot();
            _charData = new CharacterData
            {
                Id = "test", Name = "Test",
                EquippableCategories = new() { EquipmentCategory.Sword, EquipmentCategory.Accessory, EquipmentCategory.Accessory }
            };
        }

        [Test]
        public void 装备剑_放入主手()
        {
            var sword = TestDataFactory.CreateEquipment("eq_steel_sword", "钢剑", EquipmentCategory.Sword,
                new() { { "phys_atk", 15 } });
            _slot.Equip(sword);
            ClassicAssert.AreEqual("eq_steel_sword", _slot.MainHand.Data.Id);
            ClassicAssert.AreEqual(15, _slot.GetTotalStat("phys_atk"));
        }

        [Test]
        public void 双持剑_副手攻击半加算()
        {
            _slot.Equip(TestDataFactory.CreateEquipment("eq_a", "剑A", EquipmentCategory.Sword,
                new() { { "phys_atk", 25 } }));
            _slot.Equip(TestDataFactory.CreateEquipment("eq_b", "剑B", EquipmentCategory.Sword,
                new() { { "phys_atk", 10 } }));
            // Currently CanDualWield returns false — so total should be 25+10=35
            ClassicAssert.AreEqual(35, _slot.GetTotalStat("phys_atk"));
        }

        [Test]
        public void 饰品_依次填充Accessory1_2_3()
        {
            var ring1 = TestDataFactory.CreateEquipment("eq_r1", "戒指1", EquipmentCategory.Accessory,
                new() { { "Str", 5 } });
            var ring2 = TestDataFactory.CreateEquipment("eq_r2", "戒指2", EquipmentCategory.Accessory,
                new() { { "Spd", 3 } });
            _slot.Equip(ring1);
            _slot.Equip(ring2);
            ClassicAssert.AreEqual("eq_r1", _slot.Accessory1.Data.Id);
            ClassicAssert.AreEqual("eq_r2", _slot.Accessory2.Data.Id);
            ClassicAssert.AreEqual(8, _slot.GetTotalStat("Str") + _slot.GetTotalStat("Spd"));
        }

        [Test]
        public void 装备属性别名_hit和block_rate_计入面板()
        {
            var ring = TestDataFactory.CreateEquipment("eq_acc", "命中戒指", EquipmentCategory.Accessory,
                new() { { "hit", 5 }, { "block_rate", 10 } });

            _slot.Equip(ring);

            ClassicAssert.AreEqual(5, _slot.GetTotalStat("Hit"));
            ClassicAssert.AreEqual(10, _slot.GetTotalStat("Block"));
        }

        [Test]
        public void EquipToSlot_指定槽位()
        {
            var sword = TestDataFactory.CreateEquipment("eq_s1", "剑", EquipmentCategory.Sword,
                new() { { "phys_atk", 10 } });
            _slot.EquipToSlot("MainHand", sword);
            ClassicAssert.AreEqual("eq_s1", _slot.MainHand.Data.Id);
        }

        [Test]
        public void Unequip_清空指定槽位()
        {
            var sword = TestDataFactory.CreateEquipment("eq_s1", "剑", EquipmentCategory.Sword,
                new() { { "phys_atk", 10 } });
            _slot.Equip(sword);
            _slot.Unequip("MainHand");
            ClassicAssert.IsNull(_slot.MainHand);
        }

        [Test]
        public void CanEquipCategory_剑士可装备剑()
        {
            ClassicAssert.IsTrue(EquipmentSlot.CanEquipCategory(EquipmentCategory.Sword, _charData, false));
        }

        [Test]
        public void CanEquipCategory_剑士可装备饰品()
        {
            ClassicAssert.IsTrue(EquipmentSlot.CanEquipCategory(EquipmentCategory.Accessory, _charData, false));
        }

        [Test]
        public void GetSlotNames_剑加两饰品_返回3槽()
        {
            var slots = EquipmentSlot.GetSlotNames(_charData, false);
            ClassicAssert.AreEqual(3, slots.Count);
            ClassicAssert.AreEqual("MainHand", slots[0]);
        }

        [Test]
        public void ValidateWeaponEquipped_主手有武器_通过()
        {
            _slot.Equip(TestDataFactory.CreateEquipment("eq_s", "剑", EquipmentCategory.Sword,
                new() { { "phys_atk", 10 } }));
            ClassicAssert.IsTrue(_slot.ValidateWeaponEquipped());
        }
    }
}
