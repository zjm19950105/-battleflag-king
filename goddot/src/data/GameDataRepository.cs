using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleKing.Data
{
    public class GameDataRepository
    {
        private static readonly Regex DisplayTokenRegex = new(@"\{(char|class):([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> BuiltInUnitClassDisplayNames = new()
        {
            ["infantry"] = "步兵",
            ["cavalry"] = "骑兵",
            ["flying"] = "飞行",
            ["heavy"] = "重装",
            ["scout"] = "斥候",
            ["archer"] = "弓兵",
            ["mage"] = "术师",
            ["elf"] = "精灵",
            ["beastman"] = "兽人",
            ["winged"] = "翼人",
            ["undead"] = "不死"
        };

        public Dictionary<string, CharacterData> Characters { get; private set; }
        public Dictionary<string, ActiveSkillData> ActiveSkills { get; private set; }
        public Dictionary<string, PassiveSkillData> PassiveSkills { get; private set; }
        public Dictionary<string, EquipmentData> Equipments { get; private set; }
        public Dictionary<string, EnemyFormationData> EnemyFormations { get; private set; }
        public Dictionary<string, StrategyPresetData> StrategyPresets { get; private set; }
        public Dictionary<string, string> ClassDisplayNames { get; private set; }
        public Dictionary<string, CharacterRoleDescriptionData> CharacterRoleDescriptions { get; private set; }

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

            ClassDisplayNames = LoadJsonFile<Dictionary<string, string>>(Path.Combine(dataPath, "class_display_names.json"));

            CharacterRoleDescriptions = LoadJsonFile<List<CharacterRoleDescriptionData>>(Path.Combine(dataPath, "character_role_descriptions.json"))
                .ToDictionary(r => r.CharacterId);
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

        public string ResolveDisplayTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return DisplayTokenRegex.Replace(text, match =>
                TryResolveDisplayToken(match.Groups[1].Value, match.Groups[2].Value, out var displayName)
                    ? displayName
                    : match.Value);
        }

        public bool TryResolveDisplayToken(string tokenKind, string id, out string displayName)
        {
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(tokenKind) || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (tokenKind == "char")
            {
                if (ClassDisplayNames != null && ClassDisplayNames.TryGetValue(id, out displayName))
                {
                    return true;
                }

                if (Characters != null && Characters.TryGetValue(id, out var character) && !string.IsNullOrWhiteSpace(character.Name))
                {
                    displayName = character.Name;
                    return true;
                }

                return false;
            }

            if (tokenKind == "class")
            {
                if (ClassDisplayNames != null && ClassDisplayNames.TryGetValue(id, out displayName))
                {
                    return true;
                }

                return BuiltInUnitClassDisplayNames.TryGetValue(id, out displayName);
            }

            return false;
        }
    }
}
