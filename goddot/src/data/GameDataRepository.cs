using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BattleKing.Data
{
    public class GameDataRepository
    {
        public Dictionary<string, CharacterData> Characters { get; private set; }
        public Dictionary<string, ActiveSkillData> ActiveSkills { get; private set; }
        public Dictionary<string, PassiveSkillData> PassiveSkills { get; private set; }
        public Dictionary<string, EquipmentData> Equipments { get; private set; }

        public void LoadAll(string dataPath)
        {
            Characters = LoadJsonFile<List<CharacterData>>(Path.Combine(dataPath, "characters.json"))
                .ToDictionary(c => c.Id);

            ActiveSkills = LoadJsonFile<List<ActiveSkillData>>(Path.Combine(dataPath, "active_skills.json"))
                .ToDictionary(s => s.Id);

            PassiveSkills = LoadJsonFile<List<PassiveSkillData>>(Path.Combine(dataPath, "passive_skills.json"))
                .ToDictionary(s => s.Id);

            Equipments = LoadJsonFile<List<EquipmentData>>(Path.Combine(dataPath, "equipments.json"))
                .ToDictionary(e => e.Id);
        }

        private static T LoadJsonFile<T>(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        }

        public CharacterData GetCharacter(string id) => Characters[id];
        public ActiveSkillData GetActiveSkill(string id) => ActiveSkills[id];
        public PassiveSkillData GetPassiveSkill(string id) => PassiveSkills[id];
        public EquipmentData GetEquipment(string id) => Equipments[id];
    }
}
