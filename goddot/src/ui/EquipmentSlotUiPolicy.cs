using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;

namespace BattleKing.Ui
{
    public static class EquipmentSlotUiPolicy
    {
        public static EquipmentCategory? GetExpectedCategory(string slotName, CharacterData character, bool isCc)
        {
            if (slotName == "Accessory1" || slotName == "Accessory2" || slotName == "Accessory3")
                return EquipmentCategory.Accessory;

            var categories = GetEffectiveCategories(character, isCc);
            var slots = EquipmentSlot.GetSlotNames(character, isCc);
            int slotIndex = slots.IndexOf(slotName);

            if (slotIndex < 0 || slotIndex >= categories.Count)
                return null;

            return categories[slotIndex];
        }

        public static bool CanClearSlot(string slotName, CharacterData character, bool isCc)
        {
            return GetExpectedCategory(slotName, character, isCc) == EquipmentCategory.Accessory;
        }

        public static bool CanClearSlot(BattleUnit unit, string slotName)
        {
            return unit != null && CanClearSlot(slotName, unit.Data, unit.IsCc);
        }

        private static List<EquipmentCategory> GetEffectiveCategories(CharacterData character, bool isCc)
        {
            if (character == null)
                return new List<EquipmentCategory>();

            return isCc && character.CcEquippableCategories?.Count > 0
                ? character.CcEquippableCategories
                : character.EquippableCategories ?? new List<EquipmentCategory>();
        }
    }
}
