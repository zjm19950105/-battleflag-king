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
        public Dictionary<string, EnemyFormationData> EnemyFormations { get; private set; }
        public Dictionary<string, StrategyPresetData> StrategyPresets { get; private set; }

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

            EnemyFormations = LoadJsonFile<List<EnemyFormationData>>(Path.Combine(dataPath, "enemy_formations.json"))
                .ToDictionary(f => f.Id);

            StrategyPresets = LoadJsonFile<List<StrategyPresetData>>(Path.Combine(dataPath, "strategy_presets.json"))
                .ToDictionary(p => p.Id);
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

        public CharacterData GetCharacter(string id) => Characters.TryGetValue(id, out var v) ? v : null;
        public ActiveSkillData GetActiveSkill(string id) => ActiveSkills.TryGetValue(id, out var v) ? v : null;
        public PassiveSkillData GetPassiveSkill(string id) => PassiveSkills.TryGetValue(id, out var v) ? v : null;
        public EquipmentData GetEquipment(string id) => Equipments.TryGetValue(id, out var v) ? v : null;
        public List<EquipmentData> GetAllEquipment() => Equipments.Values.ToList();
        public EnemyFormationData GetEnemyFormation(string id) => EnemyFormations.TryGetValue(id, out var v) ? v : null;
        public StrategyPresetData GetStrategyPreset(string id) => StrategyPresets.TryGetValue(id, out var v) ? v : null;
    }
}
