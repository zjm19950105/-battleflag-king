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
            int total = AllEquipped.Sum(e => e.Data.BaseStats.GetValueOrDefault(statName, 0));

            // Dual-wield rule: Sword + Sword -> mainAtk + offAtk/2 for phys_atk and magic_atk
            if (CanDualWield()
                && MainHand?.Data.Category == EquipmentCategory.Sword
                && OffHand?.Data.Category == EquipmentCategory.Sword)
            {
                if (statName == "phys_atk" || statName == "magic_atk")
                {
                    int mainAtk = MainHand.Data.BaseStats.GetValueOrDefault(statName, 0);
                    int offAtk = OffHand.Data.BaseStats.GetValueOrDefault(statName, 0);
                    // Replace the summed value with dual-wield calculation
                    total = mainAtk + offAtk / 2;
                    // Add accessory stats back
                    total += new[] { Accessory1, Accessory2, Accessory3 }
                        .Where(e => e != null)
                        .Sum(e => e.Data.BaseStats.GetValueOrDefault(statName, 0));
                }
            }

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
            // Stub: will check if class is Swordsman/Swordmaster in the future
            return false;
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
    }
}
