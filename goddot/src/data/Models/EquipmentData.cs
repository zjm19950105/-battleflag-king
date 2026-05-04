using System.Collections.Generic;

namespace BattleKing.Data
{
    public class EquipmentData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public EquipmentCategory Category { get; set; }
        public Dictionary<string, int> BaseStats { get; set; } = new();
        public List<string> GrantedActiveSkillIds { get; set; } = new();
        public List<string> GrantedPassiveSkillIds { get; set; } = new();
        public List<UnitClass> UsableByClasses { get; set; } = new();
        public List<string> RestrictedClassIds { get; set; } = new();
        public List<string> SpecialEffects { get; set; } = new();
    }
}
