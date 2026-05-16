using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;

namespace BattleKing.Equipment
{
    public class EquipmentSlot
    {
        public Equipment MainHand { get; set; }
        public Equipment OffHand { get; set; }
        public Equipment Accessory1 { get; set; }
        public Equipment Accessory2 { get; set; }
        public Equipment Accessory3 { get; set; }

        public IEnumerable<Equipment> AllEquipped =>
            new[] { MainHand, OffHand, Accessory1, Accessory2, Accessory3 }
            .Where(e => e != null);

        public int GetTotalStat(string statName)
        {
            int total = AllEquipped.Sum(e => GetEquipmentStat(e, statName));

            // Dual-wield rule: Sword + Sword -> mainAtk + offAtk/2 for phys_atk and magic_atk
            if (CanDualWield()
                && MainHand?.Data.Category == EquipmentCategory.Sword
                && OffHand?.Data.Category == EquipmentCategory.Sword)
            {
                if (statName == "phys_atk" || statName == "mag_atk")
                {
                    int mainAtk = GetEquipmentStat(MainHand, statName);
                    int offAtk = GetEquipmentStat(OffHand, statName);
                    // Replace the summed value with dual-wield calculation
                    total = mainAtk + offAtk / 2;
                    // Add accessory stats back
                    total += new[] { Accessory1, Accessory2, Accessory3 }
                        .Where(e => e != null)
                        .Sum(e => GetEquipmentStat(e, statName));
                }
            }

            return total;
        }

        private static int GetEquipmentStat(Equipment equipment, string statName)
        {
            if (equipment?.Data?.BaseStats == null) return 0;

            int total = equipment.Data.BaseStats.GetValueOrDefault(statName, 0);
            if (statName == "Hit")
                total += equipment.Data.BaseStats.GetValueOrDefault("hit", 0);
            if (statName == "Block")
                total += equipment.Data.BaseStats.GetValueOrDefault("block_rate", 0);
            return total;
        }

        public List<string> GetGrantedActiveSkillIds()
        {
            return AllEquipped.SelectMany(e => e.Data.GrantedActiveSkillIds).ToList();
        }

        public List<string> GetGrantedPassiveSkillIds()
        {
            return AllEquipped.SelectMany(e => e.Data.GrantedPassiveSkillIds).ToList();
        }

        public bool CanDualWield()
        {
            return MainHand?.Data.Category == EquipmentCategory.Sword
                && OffHand?.Data.Category == EquipmentCategory.Sword;
        }

        public void Equip(EquipmentData data)
        {
            var equipment = new Equipment(data);
            switch (data.Category)
            {
                case EquipmentCategory.Sword:
                case EquipmentCategory.Axe:
                case EquipmentCategory.Spear:
                case EquipmentCategory.Bow:
                case EquipmentCategory.Staff:
                    if (MainHand == null)
                        MainHand = equipment;
                    else
                        OffHand = equipment;
                    break;
                case EquipmentCategory.Shield:
                case EquipmentCategory.GreatShield:
                    OffHand = equipment;
                    break;
                case EquipmentCategory.Accessory:
                    if (Accessory1 == null) Accessory1 = equipment;
                    else if (Accessory2 == null) Accessory2 = equipment;
                    else if (Accessory3 == null) Accessory3 = equipment;
                    break;
            }
        }

        public bool ValidateWeaponEquipped()
        {
            return MainHand != null;
        }

        /// <summary>Get equipment in a named slot (for UI display).</summary>
        public Equipment GetBySlot(string slotName) => slotName switch
        {
            "MainHand" => MainHand,
            "OffHand" => OffHand,
            "Accessory1" => Accessory1,
            "Accessory2" => Accessory2,
            "Accessory3" => Accessory3,
            _ => null
        };

        /// <summary>Unequip (clear) a named slot.</summary>
        public void Unequip(string slotName)
        {
            switch (slotName)
            {
                case "MainHand": MainHand = null; break;
                case "OffHand": OffHand = null; break;
                case "Accessory1": Accessory1 = null; break;
                case "Accessory2": Accessory2 = null; break;
                case "Accessory3": Accessory3 = null; break;
            }
        }

        /// <summary>Check if a character can equip this type of equipment.</summary>
        public static bool CanEquipCategory(EquipmentCategory cat, CharacterData cd, bool isCc)
        {
            if (cat == EquipmentCategory.Accessory) return true;
            var types = isCc && cd.CcEquippableCategories?.Count > 0 ? cd.CcEquippableCategories : cd.EquippableCategories;
            return types.Contains(cat);
        }

        /// <summary>Get ordered slot names for a character based on equippableCategories.</summary>
        public static List<string> GetSlotNames(CharacterData cd, bool isCc)
        {
            var types = isCc && cd.CcEquippableCategories?.Count > 0 ? cd.CcEquippableCategories : cd.EquippableCategories;
            var slots = new List<string>();
            int accIdx = 0;
            foreach (var t in types)
            {
                if (t == EquipmentCategory.Accessory) { accIdx++; slots.Add("Accessory" + accIdx); }
                else if (t == EquipmentCategory.Shield || t == EquipmentCategory.GreatShield) slots.Add("OffHand");
                else if (slots.Count == 0) slots.Add("MainHand");  // first weapon = MainHand
                else slots.Add("OffHand");  // second weapon = OffHand (dual-wield)
            }
            return slots;
        }

        /// <summary>Equip into a specific named slot (for UI-driven equipment changes).</summary>
        public void EquipToSlot(string slotName, EquipmentData data)
        {
            var equipment = data != null ? new Equipment(data) : null;
            switch (slotName)
            {
                case "MainHand": MainHand = equipment; break;
                case "OffHand": OffHand = equipment; break;
                case "Accessory1": Accessory1 = equipment; break;
                case "Accessory2": Accessory2 = equipment; break;
                case "Accessory3": Accessory3 = equipment; break;
            }
        }
    }
}
