using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BattleKing.Ai;
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

        private static readonly HashSet<string> AttackAttributeConditionValues = new()
        {
            "physical",
            "magical",
            "melee",
            "ranged",
            "row",
            "column",
            "all"
        };

        private static readonly Regex RoleDescriptionTokenRegex = new(@"\{(char|class):([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        private static readonly HashSet<string> UnitClassTokenIds = Enum
            .GetNames<UnitClass>()
            .Select(name => name.ToLowerInvariant())
            .ToHashSet();

        // These are legacy Phase 1 skill records that still carry behavior in Tags
        // without structured Effects. Keep this list explicit so new tag-only combat
        // logic cannot enter data quietly; remove entries as effects are migrated.
        private const string LegacyTagOnlyReason = "Legacy tag-only skill pending structured effects migration.";
        private static readonly Dictionary<string, string> LegacyTagOnlySkillAllowlist = new();

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
                Assert.That(activeSkills, Has.Count.EqualTo(56));
                Assert.That(passiveSkills, Has.Count.EqualTo(54));
                Assert.That(equipments, Has.Count.EqualTo(80));
                Assert.That(enemyFormations, Has.Count.EqualTo(5));
                Assert.That(strategyPresets, Has.Count.EqualTo(21));

                Assert.That(repository.Characters, Has.Count.EqualTo(18));
                Assert.That(repository.ActiveSkills, Has.Count.EqualTo(56));
                Assert.That(repository.PassiveSkills, Has.Count.EqualTo(54));
                Assert.That(repository.Equipments, Has.Count.EqualTo(80));
                Assert.That(repository.EnemyFormations, Has.Count.EqualTo(5));
                Assert.That(repository.StrategyPresets, Has.Count.EqualTo(21));
                Assert.That(repository.ClassDisplayNames, Has.Count.GreaterThanOrEqualTo(32), "all compendium classes");
                Assert.That(repository.CharacterRoleDescriptions, Has.Count.EqualTo(18));
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
                AssertIdsAreUnique(LoadList<CharacterRoleDescriptionData>("character_role_descriptions.json"), x => x.CharacterId, "character_role_descriptions");
            });
        }

        [Test]
        public void RealData_CharacterRoleDescriptions_MatchCharactersAndResolveTokens()
        {
            var repository = LoadRepository();
            var descriptions = LoadList<CharacterRoleDescriptionData>("character_role_descriptions.json");
            var descriptionIds = descriptions.Select(description => description.CharacterId).ToHashSet();
            var characterIds = repository.Characters.Keys.ToHashSet();
            var failures = new List<string>();

            AddSetDifferenceFailures(failures, "character_role_descriptions.characterId", descriptionIds, characterIds);

            foreach (var description in descriptions)
            {
                var textItems = (description.MainRoles ?? new List<string>())
                    .Concat(description.Characteristics ?? new List<string>())
                    .ToList();
                var referencedCharacterIds = new HashSet<string>();
                var referencedClassIds = new HashSet<string>();

                if (string.IsNullOrWhiteSpace(description.DisplayName))
                {
                    failures.Add($"{description.CharacterId}.displayName is required");
                }

                if ((description.UnitClasses ?? new List<string>()).Count == 0)
                {
                    failures.Add($"{description.CharacterId}.unitClasses must not be empty");
                }

                if ((description.MainRoles ?? new List<string>()).Count == 0)
                {
                    failures.Add($"{description.CharacterId}.mainRoles must not be empty");
                }

                foreach (var text in textItems)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        failures.Add($"{description.CharacterId} contains blank role description text");
                        continue;
                    }

                    foreach (Match match in RoleDescriptionTokenRegex.Matches(text))
                    {
                        var kind = match.Groups[1].Value;
                        var id = match.Groups[2].Value;

                        if (kind == "char")
                        {
                            referencedCharacterIds.Add(id);
                            if (!repository.Characters.ContainsKey(id) && !repository.ClassDisplayNames.ContainsKey(id))
                            {
                                failures.Add($"{description.CharacterId} references unknown character token {match.Value}");
                            }
                        }
                        else if (kind == "class")
                        {
                            referencedClassIds.Add(id);
                            if (!repository.ClassDisplayNames.ContainsKey(id) && !UnitClassTokenIds.Contains(id))
                            {
                                failures.Add($"{description.CharacterId} references unknown class token {match.Value}");
                            }
                        }

                        if (!repository.TryResolveDisplayToken(kind, id, out _))
                        {
                            failures.Add($"{description.CharacterId} cannot resolve token {match.Value}");
                        }
                    }

                    var resolvedText = repository.ResolveDisplayTokens(text);
                    if (RoleDescriptionTokenRegex.IsMatch(resolvedText))
                    {
                        failures.Add($"{description.CharacterId} leaves raw token after resolving: {resolvedText}");
                    }
                }

                AddSetDifferenceFailures(
                    failures,
                    $"{description.CharacterId}.referencedCharacterIds",
                    (description.ReferencedCharacterIds ?? new List<string>()).ToHashSet(),
                    referencedCharacterIds);
                AddSetDifferenceFailures(
                    failures,
                    $"{description.CharacterId}.referencedClassIds",
                    (description.ReferencedClassIds ?? new List<string>()).ToHashSet(),
                    referencedClassIds);
            }

            Assert.Multiple(() =>
            {
                Assert.That(descriptions, Has.Count.EqualTo(repository.Characters.Count));
                Assert.That(descriptions, Has.Count.EqualTo(18));
                Assert.That(failures, Is.Empty);
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

        [Test]
        public void RealData_StrategyConditionCanonicalValues_AreParseable()
        {
            var failures = new List<string>();
            var presets = LoadList<StrategyPresetData>("strategy_presets.json");

            foreach (var preset in presets)
            {
                for (var i = 0; i < preset.Strategies.Count; i++)
                {
                    var strategy = preset.Strategies[i];
                    ValidateConditionValue(strategy.Condition1, $"{preset.Id}.strategies[{i}].condition1", failures);
                    ValidateConditionValue(strategy.Condition2, $"{preset.Id}.strategies[{i}].condition2", failures);
                }
            }

            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void RealData_StrategyPresetHpConditionValues_AreRatios()
        {
            var strategyPresets = LoadList<StrategyPresetData>("strategy_presets.json");
            var invalidConditions = new List<string>();

            foreach (var preset in strategyPresets)
            {
                for (var index = 0; index < preset.Strategies.Count; index++)
                {
                    var strategy = preset.Strategies[index];
                    AddInvalidHpConditionValue(invalidConditions, preset.Id, index, "condition1", strategy.Condition1);
                    AddInvalidHpConditionValue(invalidConditions, preset.Id, index, "condition2", strategy.Condition2);
                }
            }

            Assert.That(invalidConditions, Is.Empty);
        }

        [Test]
        public void RealData_SkillEffectsCoverage_IsReportedAndTagOnlySkillsAreExplicit()
        {
            var activeSkills = LoadList<ActiveSkillData>("active_skills.json");
            var passiveSkills = LoadList<PassiveSkillData>("passive_skills.json");
            var records = activeSkills
                .Select(skill => SkillEffectCoverageRecord.FromActive(skill))
                .Concat(passiveSkills.Select(skill => SkillEffectCoverageRecord.FromPassive(skill)))
                .ToList();

            WriteEffectCoverageReport("active", records.Where(record => record.Kind == "active"));
            WriteEffectCoverageReport("passive", records.Where(record => record.Kind == "passive"));

            var tagOnlyKeys = records
                .Where(record => record.Tags.Count > 0 && record.EffectCount == 0)
                .Select(record => record.Key)
                .ToHashSet();
            var tagOnlyMissingAllowlist = tagOnlyKeys
                .Where(key => !LegacyTagOnlySkillAllowlist.ContainsKey(key))
                .OrderBy(key => key)
                .ToList();
            var staleAllowlistEntries = LegacyTagOnlySkillAllowlist.Keys
                .Where(key => !tagOnlyKeys.Contains(key))
                .OrderBy(key => key)
                .ToList();
            var blankReasons = LegacyTagOnlySkillAllowlist
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(key => key)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(tagOnlyMissingAllowlist, Is.Empty,
                    "Tag-only skills must be explicit legacy exceptions or receive structured effects.");
                Assert.That(staleAllowlistEntries, Is.Empty,
                    "Remove allowlist entries when a legacy tag-only skill gains structured effects.");
                Assert.That(blankReasons, Is.Empty,
                    "Each tag-only allowlist entry must state why it is temporarily allowed.");
            });
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

        private static void AddSetDifferenceFailures(
            ICollection<string> failures,
            string label,
            ISet<string> actual,
            ISet<string> expected)
        {
            var missing = expected
                .Where(id => !actual.Contains(id))
                .OrderBy(id => id)
                .ToList();
            var extra = actual
                .Where(id => !expected.Contains(id))
                .OrderBy(id => id)
                .ToList();

            if (missing.Count > 0)
            {
                failures.Add($"{label} missing: {string.Join(", ", missing)}");
            }

            if (extra.Count > 0)
            {
                failures.Add($"{label} extra: {string.Join(", ", extra)}");
            }
        }

        private static void ValidateConditionValue(Condition? condition, string path, List<string> failures)
        {
            if (condition == null)
            {
                return;
            }

            switch (condition.Category)
            {
                case ConditionCategory.UnitClass:
                case ConditionCategory.EnemyClassExists:
                    RequireStringValue(condition, path, failures, value =>
                        Enum.TryParse<UnitClass>(value, ignoreCase: false, out _),
                        "UnitClass enum name");
                    break;
                case ConditionCategory.TeamSize:
                    RequireStringValue(condition, path, failures, value =>
                        Regex.IsMatch(value, "^(enemy|ally):[1-9][0-9]*$"),
                        "enemy:N or ally:N");
                    break;
                case ConditionCategory.Status:
                    RequireStringValue(condition, path, failures, IsCanonicalStatusConditionValue,
                        "buff, debuff, StatusAilment enum name, or not:StatusAilment");
                    break;
                case ConditionCategory.AttackAttribute:
                    RequireStringValue(condition, path, failures, AttackAttributeConditionValues.Contains,
                        "known attack attribute");
                    break;
            }
        }

        private static void RequireStringValue(
            Condition condition,
            string path,
            List<string> failures,
            Func<string, bool> isValid,
            string expected)
        {
            var value = ConditionValueToString(condition.Value);
            if (string.IsNullOrWhiteSpace(value) || !isValid(value))
            {
                failures.Add($"{path} has invalid {condition.Category} value '{value ?? "<null>"}'; expected {expected}");
            }
        }

        private static bool IsCanonicalStatusConditionValue(string value)
        {
            if (value == "buff" || value == "debuff")
            {
                return true;
            }

            if (value.StartsWith("not:", StringComparison.Ordinal))
            {
                value = value["not:".Length..];
            }

            return Enum.TryParse<StatusAilment>(value, ignoreCase: false, out _);
        }

        private static string? ConditionValueToString(object? value)
        {
            return value switch
            {
                null => null,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                JsonElement json => json.ToString(),
                _ => value.ToString()
            };
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

        private static void AddInvalidHpConditionValue(
            ICollection<string> invalidConditions,
            string presetId,
            int strategyIndex,
            string conditionName,
            BattleKing.Ai.Condition? condition)
        {
            if (condition == null)
            {
                return;
            }

            if (condition.Category is not (ConditionCategory.Hp or ConditionCategory.SelfHp))
            {
                return;
            }

            if (condition.Operator is "lowest" or "highest")
            {
                return;
            }

            if (!TryReadNumber(condition.Value, out var value) || value < 0 || value > 1)
            {
                invalidConditions.Add(
                    $"{presetId}.strategies[{strategyIndex}].{conditionName} {condition.Category} {condition.Operator} value={condition.Value}");
            }
        }

        private static bool TryReadNumber(object? value, out double number)
        {
            switch (value)
            {
                case JsonElement { ValueKind: JsonValueKind.Number } json:
                    return json.TryGetDouble(out number);
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    number = Convert.ToDouble(value);
                    return true;
                default:
                    number = default;
                    return false;
            }
        }

        private static void WriteEffectCoverageReport(string kind, IEnumerable<SkillEffectCoverageRecord> records)
        {
            var list = records.ToList();
            var withEffects = list.Count(record => record.EffectCount > 0);
            var tagOnly = list.Count(record => record.Tags.Count > 0 && record.EffectCount == 0);
            var noTagsNoEffects = list.Count(record => record.Tags.Count == 0 && record.EffectCount == 0);

            TestContext.WriteLine(
                $"{kind} skill effects coverage: total={list.Count}, withEffects={withEffects}, tagOnly={tagOnly}, noTagsNoEffects={noTagsNoEffects}");
        }

        private sealed record SkillEffectCoverageRecord(
            string Kind,
            string Id,
            IReadOnlyList<string> Tags,
            int EffectCount)
        {
            public string Key => $"{Kind}:{Id}";

            public static SkillEffectCoverageRecord FromActive(ActiveSkillData skill) =>
                new("active", skill.Id, skill.Tags ?? new List<string>(), skill.Effects?.Count ?? 0);

            public static SkillEffectCoverageRecord FromPassive(PassiveSkillData skill) =>
                new("passive", skill.Id, skill.Tags ?? new List<string>(), skill.Effects?.Count ?? 0);
        }
    }
}
