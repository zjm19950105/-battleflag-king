using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class EquipmentSlotUiPolicyTest
    {
        [Test]
        public void CanClearSlot_OnlyAllowsAccessories()
        {
            var character = new CharacterData
            {
                Id = "guard",
                Name = "Guard",
                EquippableCategories = new()
                {
                    EquipmentCategory.Sword,
                    EquipmentCategory.Shield,
                    EquipmentCategory.Accessory,
                    EquipmentCategory.Accessory
                }
            };

            ClassicAssert.False(EquipmentSlotUiPolicy.CanClearSlot("MainHand", character, false));
            ClassicAssert.False(EquipmentSlotUiPolicy.CanClearSlot("OffHand", character, false));
            ClassicAssert.True(EquipmentSlotUiPolicy.CanClearSlot("Accessory1", character, false));
            ClassicAssert.True(EquipmentSlotUiPolicy.CanClearSlot("Accessory2", character, false));
        }

        [Test]
        public void GetExpectedCategory_DistinguishesShieldFromAccessories()
        {
            var character = new CharacterData
            {
                Id = "guard",
                Name = "Guard",
                EquippableCategories = new()
                {
                    EquipmentCategory.Sword,
                    EquipmentCategory.GreatShield,
                    EquipmentCategory.Accessory
                }
            };

            ClassicAssert.AreEqual(EquipmentCategory.Sword, EquipmentSlotUiPolicy.GetExpectedCategory("MainHand", character, false));
            ClassicAssert.AreEqual(EquipmentCategory.GreatShield, EquipmentSlotUiPolicy.GetExpectedCategory("OffHand", character, false));
            ClassicAssert.AreEqual(EquipmentCategory.Accessory, EquipmentSlotUiPolicy.GetExpectedCategory("Accessory1", character, false));
        }
    }
}
