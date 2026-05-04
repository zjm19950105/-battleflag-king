using System.Collections.Generic;

namespace BattleKing.Data
{
    public class CharacterData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<UnitClass> Classes { get; set; } = new();
        public List<EquipmentCategory> EquippableCategories { get; set; } = new();
        public List<string> InnateActiveSkillIds { get; set; } = new();
        public List<string> InnatePassiveSkillIds { get; set; } = new();
        public List<string> InnateValorSkillIds { get; set; } = new();
        public Dictionary<string, int> BaseStats { get; set; } = new();
        public string GrowthType { get; set; }
        public List<TraitData> Traits { get; set; } = new();
        public List<string> InitialEquipmentIds { get; set; } = new();
        public List<string> CcInitialEquipmentIds { get; set; } = new();

        // CC (Class Change) data
        public string CcClassId { get; set; }
        public string CcName { get; set; }
        public List<EquipmentCategory> CcEquippableCategories { get; set; } = new();
        public List<string> CcInnateActiveSkillIds { get; set; } = new();
        public List<string> CcInnatePassiveSkillIds { get; set; } = new();
    }
}
