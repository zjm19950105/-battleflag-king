using System.Text.Json;
using System.Text.Json.Serialization;
using BattleKing.Data;
using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class DataContractTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly HashSet<string> EquipmentStatKeyWhitelist = new()
        {
            "HP",
            "Str",
            "Def",
            "Mag",
            "MDef",
            "Hit",
            "hit",
            "Eva",
            "Crit",
            "Block",
            "Spd",
            "AP",
            "PP",
            "phys_atk",
            "mag_atk",
            "phys_def",
            "mag_def",
            "block_rate"
        };

        [Test]
        public void RealData_LoadAll_DeserializesAndMatchesCurrentCounts()
        {
            var characters = LoadList<CharacterData>("characters.json");
            var activeSkills = LoadList<ActiveSkillData>("active_skills.json");
            var passiveSkills = LoadList<PassiveSkillData>("passive_skills.json");
            var equipments = LoadList<EquipmentData>("equipments.json");
            var enemyFormations = LoadList<EnemyFormationData>("enemy_formations.json");
            var strategyPresets = LoadList<StrategyPresetData>("strategy_presets.json");

            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);

            Assert.Multiple(() =>
            {
                Assert.That(characters, Has.Count.EqualTo(18));
                Assert.That(activeSkills, Has.Count.EqualTo(55));
                Assert.That(passiveSkills, Has.Count.EqualTo(50));
                Assert.That(equipments, Has.Count.EqualTo(80));
                Assert.That(enemyFormations, Has.Count.EqualTo(5));
                Assert.That(strategyPresets, Has.Count.EqualTo(3));

                Assert.That(repository.Characters, Has.Count.EqualTo(18));
                Assert.That(repository.ActiveSkills, Has.Count.EqualTo(55));
                Assert.That(repository.PassiveSkills, Has.Count.EqualTo(50));
                Assert.That(repository.Equipments, Has.Count.EqualTo(80));
                Assert.That(repository.EnemyFormations, Has.Count.EqualTo(5));
                Assert.That(repository.StrategyPresets, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void RealData_Ids_AreUnique()
        {
            Assert.Multiple(() =>
            {
                AssertIdsAreUnique(LoadList<CharacterData>("characters.json"), x => x.Id, "characters");
                AssertIdsAreUnique(LoadList<ActiveSkillData>("active_skills.json"), x => x.Id, "active_skills");
                AssertIdsAreUnique(LoadList<PassiveSkillData>("passive_skills.json"), x => x.Id, "passive_skills");
                AssertIdsAreUnique(LoadList<EquipmentData>("equipments.json"), x => x.Id, "equipments");
                AssertIdsAreUnique(LoadList<EnemyFormationData>("enemy_formations.json"), x => x.Id, "enemy_formations");
                AssertIdsAreUnique(LoadList<StrategyPresetData>("strategy_presets.json"), x => x.Id, "strategy_presets");
            });
        }

        [Test]
        public void RealData_CharacterAndEquipmentSkillReferences_Exist()
        {
            var repository = LoadRepository();
            var missingActiveSkillRefs = new List<string>();
            var missingPassiveSkillRefs = new List<string>();

            foreach (var character in repository.Characters.Values)
            {
                AddMissingRefs(
                    missingActiveSkillRefs,
                    $"character {character.Id}.innateActiveSkillIds",
                    character.InnateActiveSkillIds,
                    repository.ActiveSkills);
                AddMissingRefs(
                    missingActiveSkillRefs,
                    $"character {character.Id}.ccInnateActiveSkillIds",
                    character.CcInnateActiveSkillIds,
                    repository.ActiveSkills);
                AddMissingRefs(
                    missingPassiveSkillRefs,
                    $"character {character.Id}.innatePassiveSkillIds",
                    character.InnatePassiveSkillIds,
                    repository.PassiveSkills);
                AddMissingRefs(
                    missingPassiveSkillRefs,
                    $"character {character.Id}.ccInnatePassiveSkillIds",
                    character.CcInnatePassiveSkillIds,
                    repository.PassiveSkills);
            }

            foreach (var equipment in repository.Equipments.Values)
            {
                AddMissingRefs(
                    missingActiveSkillRefs,
                    $"equipment {equipment.Id}.grantedActiveSkillIds",
                    equipment.GrantedActiveSkillIds,
                    repository.ActiveSkills);
                AddMissingRefs(
                    missingPassiveSkillRefs,
                    $"equipment {equipment.Id}.grantedPassiveSkillIds",
                    equipment.GrantedPassiveSkillIds,
                    repository.PassiveSkills);
            }

            Assert.Multiple(() =>
            {
                Assert.That(missingActiveSkillRefs, Is.Empty);
                Assert.That(missingPassiveSkillRefs, Is.Empty);
            });
        }

        [Test]
        public void RealData_EnemyFormationReferences_Exist()
        {
            var repository = LoadRepository();
            var missingCharacterRefs = new List<string>();
            var missingStrategyPresetRefs = new List<string>();

            foreach (var formation in repository.EnemyFormations.Values)
            {
                foreach (var unit in formation.Units ?? Enumerable.Empty<FormationUnitData>())
                {
                    if (!repository.Characters.ContainsKey(unit.CharacterId))
                    {
                        missingCharacterRefs.Add($"formation {formation.Id} position {unit.Position} characterId={unit.CharacterId}");
                    }

                    if (!repository.StrategyPresets.ContainsKey(unit.StrategyPresetId))
                    {
                        missingStrategyPresetRefs.Add($"formation {formation.Id} position {unit.Position} strategyPresetId={unit.StrategyPresetId}");
                    }
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(missingCharacterRefs, Is.Empty);
                Assert.That(missingStrategyPresetRefs, Is.Empty);
            });
        }

        [Test]
        public void RealData_EquipmentStatKeys_AreWhitelisted()
        {
            var repository = LoadRepository();
            var unknownKeys = repository.Equipments.Values
                .SelectMany(equipment => (equipment.BaseStats ?? new Dictionary<string, int>())
                    .Keys
                    .Where(key => !EquipmentStatKeyWhitelist.Contains(key))
                    .Select(key => $"{equipment.Id}.{key}"))
                .ToList();

            Assert.That(unknownKeys, Is.Empty);
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static List<T> LoadList<T>(string fileName)
        {
            var filePath = Path.Combine(DataPath, fileName);
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize {filePath}");
        }

        private static void AssertIdsAreUnique<T>(IEnumerable<T> items, Func<T, string> idSelector, string label)
        {
            var duplicateIds = items
                .Select(idSelector)
                .Where(id => !string.IsNullOrEmpty(id))
                .GroupBy(id => id)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            Assert.That(duplicateIds, Is.Empty, $"{label} contains duplicate ids");
        }

        private static void AddMissingRefs<T>(
            ICollection<string> missingRefs,
            string owner,
            IEnumerable<string>? refs,
            IReadOnlyDictionary<string, T> knownItems)
        {
            foreach (var id in refs ?? Enumerable.Empty<string>())
            {
                if (!knownItems.ContainsKey(id))
                {
                    missingRefs.Add($"{owner}: {id}");
                }
            }
        }
    }
}
