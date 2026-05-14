using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class SkillEffectMigrationAuditTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static readonly Regex MultiHitTagPattern = new(
            @"^MultiHit[2-9]\d*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MultiHitDescriptionPattern = new(
            @"(?:^|[^\d])(?:[2-9]\d*)\s*(?:连击|hit)\b|连续|多段",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FixedPowerBonusDescriptionPattern = new(
            @"威力\s*\+\s*\d+|威力[^，。；;]*?最大\s*\+\s*\d+",
            RegexOptions.Compiled);

        private static readonly Regex ApMinusDescriptionPattern = new(
            @"AP\s*-\s*1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PpMinusDescriptionPattern = new(
            @"PP\s*-\s*1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [Test]
        public void RealData_LegacyBehaviorHints_AreBackedByStructuredEffects()
        {
            var failures = new List<string>();

            foreach (var skill in LoadSkillRecords())
            {
                AuditHitAndKillHealing(skill, failures);
                AuditResourceTransferAndDamage(skill, failures);
                AuditIgnoreDefense(skill, failures);
                AuditMultiHit(skill, failures);
                AuditFixedPowerBonus(skill, failures);
                AuditBlockSeal(skill, failures);
                AuditNullifyMarkers(skill, failures);
            }

            Assert.That(failures, Is.Empty,
                "Structured effect migration audit failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        private static void AuditHitAndKillHealing(SkillAuditRecord skill, ICollection<string> failures)
        {
            if (!HasAnyTagContaining(skill, "HitHeal", "KillHeal")
                && !ContainsAny(skill.EffectDescription, "命中时HP回复", "击倒时HP回复"))
            {
                return;
            }

            if (!HasAnyEffectType(skill, "RecoverHp", "HealRatio"))
            {
                failures.Add($"{skill.Key}: HitHeal/KillHeal or HP recovery description requires RecoverHp/HealRatio.");
            }
        }

        private static void AuditResourceTransferAndDamage(SkillAuditRecord skill, ICollection<string> failures)
        {
            var needsPpRecover = HasAnyTagContaining(skill, "KillPp", "HitPp")
                || ContainsAny(skill.EffectDescription, "击倒时PP+", "命中时PP+");
            var needsApRecover = HasAnyTagContaining(skill, "KillAp")
                || ContainsAny(skill.EffectDescription, "击倒时AP+");
            var needsApSteal = HasAnyTagContaining(skill, "StealAp")
                || Regex.IsMatch(skill.EffectDescription, @"夺取.*AP", RegexOptions.IgnoreCase);
            var needsPpSteal = HasAnyTagContaining(skill, "StealPp", "StealAllPp")
                || Regex.IsMatch(skill.EffectDescription, @"夺取.*PP", RegexOptions.IgnoreCase);
            var needsApDamage = HasAnyTagContaining(skill, "ApMinus")
                || ApMinusDescriptionPattern.IsMatch(skill.EffectDescription);
            var needsPpDamage = HasAnyTagContaining(skill, "PpMinus")
                || PpMinusDescriptionPattern.IsMatch(skill.EffectDescription);

            if (needsPpRecover && !HasRecoverOrTransfer(skill, "PP", "RecoverPp"))
            {
                failures.Add($"{skill.Key}: KillPp/HitPp marker requires RecoverPp or PP TransferResource.");
            }

            if (needsApRecover && !HasRecoverOrTransfer(skill, "AP", "RecoverAp"))
            {
                failures.Add($"{skill.Key}: KillAp marker requires RecoverAp or AP TransferResource.");
            }

            if (needsApSteal && !HasStealResourceEffect(skill, "AP", "ApDamage", "RecoverAp"))
            {
                failures.Add($"{skill.Key}: StealAp marker requires AP TransferResource or ApDamage+RecoverAp.");
            }

            if (needsPpSteal && !HasStealResourceEffect(skill, "PP", "PpDamage", "RecoverPp"))
            {
                failures.Add($"{skill.Key}: StealPp marker requires PP TransferResource or PpDamage+RecoverPp.");
            }

            if (needsApDamage && !HasDamageOrTransfer(skill, "AP", "ApDamage"))
            {
                failures.Add($"{skill.Key}: AP-1 marker requires ApDamage or AP TransferResource.");
            }

            if (needsPpDamage && !HasDamageOrTransfer(skill, "PP", "PpDamage"))
            {
                failures.Add($"{skill.Key}: PP-1 marker requires PpDamage or PP TransferResource.");
            }
        }

        private static void AuditIgnoreDefense(SkillAuditRecord skill, ICollection<string> failures)
        {
            if (!HasAnyTagContaining(skill, "IgnoreDef")
                && !ContainsAny(skill.EffectDescription, "无视防御"))
            {
                return;
            }

            if (!HasAnyParameterName(skill.Effects, "IgnoreDefenseRatio"))
            {
                failures.Add($"{skill.Key}: IgnoreDef marker requires IgnoreDefenseRatio.");
            }
        }

        private static void AuditMultiHit(SkillAuditRecord skill, ICollection<string> failures)
        {
            if (!HasMultiHitMarker(skill))
            {
                return;
            }

            if (!HasAnyParameterName(skill.Effects, "HitCount"))
            {
                failures.Add($"{skill.Key}: MultiHit/N-hit marker requires HitCount.");
            }
        }

        private static void AuditFixedPowerBonus(SkillAuditRecord skill, ICollection<string> failures)
        {
            if (!FixedPowerBonusDescriptionPattern.IsMatch(skill.EffectDescription))
            {
                return;
            }

            if (!HasAnyParameterName(skill.Effects, "SkillPowerBonus", "SkillPowerBonusFromTargetHpRatio"))
            {
                failures.Add($"{skill.Key}: 威力+N marker requires SkillPowerBonus or SkillPowerBonusFromTargetHpRatio, not only SkillPowerMultiplier.");
            }
        }

        private static void AuditBlockSeal(SkillAuditRecord skill, ICollection<string> failures)
        {
            if (!HasAnyTagContaining(skill, "BlockSeal")
                && !ContainsAny(skill.EffectDescription, "格挡封印"))
            {
                return;
            }

            if (!HasStatusAilment(skill, "BlockSeal"))
            {
                failures.Add($"{skill.Key}: BlockSeal marker requires StatusAilment(BlockSeal).");
            }
        }

        private static void AuditNullifyMarkers(SkillAuditRecord skill, ICollection<string> failures)
        {
            var needsDamageNullify = HasAnyTagContaining(skill, "DamageNullify")
                || ContainsAny(skill.EffectDescription, "伤害无效", "无效化伤害");
            var needsDebuffNullify = HasAnyTagContaining(skill, "DebuffNullify")
                || ContainsAny(skill.EffectDescription, "debuff无效", "减益无效");

            if (needsDamageNullify
                && !HasTemporalMarkKey(skill, "DamageNullify", "MagicDamageNullify", "MeleeHitNullify")
                && !HasTruthyParameter(skill.Effects, "NullifyPhysicalDamage", "NullifyMagicalDamage"))
            {
                failures.Add($"{skill.Key}: DamageNullify/伤害无效 marker requires TemporalMark or damage-nullify calculation.");
            }

            if (needsDebuffNullify && !HasTemporalMarkKey(skill, "DebuffNullify"))
            {
                failures.Add($"{skill.Key}: DebuffNullify/debuff无效 marker requires TemporalMark(DebuffNullify).");
            }
        }

        private static bool HasRecoverOrTransfer(SkillAuditRecord skill, string resource, string recoverEffect) =>
            HasAnyEffectType(skill, recoverEffect) || HasTransferResource(skill, resource);

        private static bool HasDamageOrTransfer(SkillAuditRecord skill, string resource, string damageEffect) =>
            HasAnyEffectType(skill, damageEffect) || HasTransferResource(skill, resource);

        private static bool HasStealResourceEffect(SkillAuditRecord skill, string resource, string damageEffect, string recoverEffect) =>
            HasTransferResource(skill, resource) || (HasAnyEffectType(skill, damageEffect) && HasAnyEffectType(skill, recoverEffect));

        private static bool HasMultiHitMarker(SkillAuditRecord skill)
        {
            if (skill.Tags.Any(tag => MultiHitTagPattern.IsMatch(tag)))
            {
                return true;
            }

            return MultiHitDescriptionPattern.IsMatch(skill.EffectDescription);
        }

        private static bool HasTransferResource(SkillAuditRecord skill, string resource) =>
            EnumerateEffectObjects(skill.Effects)
                .Where(effect => IsEffectType(effect, "TransferResource"))
                .Any(effect => HasStringPropertyValue(effect, "resource", resource));

        private static bool HasTemporalMarkKey(SkillAuditRecord skill, params string[] keys) =>
            EnumerateEffectObjects(skill.Effects)
                .Where(effect => IsEffectType(effect, "TemporalMark"))
                .Any(effect => HasStringPropertyValue(effect, "key", keys));

        private static bool HasStatusAilment(SkillAuditRecord skill, string ailment) =>
            EnumerateEffectObjects(skill.Effects)
                .Where(effect => IsEffectType(effect, "StatusAilment"))
                .Any(effect => HasStringPropertyValue(effect, "ailment", ailment)
                    || HasStringPropertyValue(effect, "ailments", ailment));

        private static bool HasAnyEffectType(SkillAuditRecord skill, params string[] effectTypes) =>
            EnumerateEffectObjects(skill.Effects).Any(effect => IsEffectType(effect, effectTypes));

        private static bool IsEffectType(JsonElement effect, params string[] effectTypes)
        {
            if (effect.ValueKind != JsonValueKind.Object
                || !effect.TryGetProperty("effectType", out var effectTypeElement)
                || effectTypeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var effectType = effectTypeElement.GetString();
            return effectType != null
                && effectTypes.Any(expected => string.Equals(effectType, expected, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAnyParameterName(JsonElement element, params string[] parameterNames)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (parameterNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                            || HasAnyParameterName(property.Value, parameterNames))
                        {
                            return true;
                        }
                    }

                    return false;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Any(item => HasAnyParameterName(item, parameterNames));
                default:
                    return false;
            }
        }

        private static bool HasTruthyParameter(JsonElement element, params string[] parameterNames)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (parameterNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                            && IsTruthy(property.Value))
                        {
                            return true;
                        }

                        if (HasTruthyParameter(property.Value, parameterNames))
                        {
                            return true;
                        }
                    }

                    return false;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Any(item => HasTruthyParameter(item, parameterNames));
                default:
                    return false;
            }
        }

        private static bool HasStringPropertyValue(JsonElement element, string propertyName, params string[] expectedValues)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                            && ContainsStringValue(property.Value, expectedValues))
                        {
                            return true;
                        }

                        if (HasStringPropertyValue(property.Value, propertyName, expectedValues))
                        {
                            return true;
                        }
                    }

                    return false;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Any(item => HasStringPropertyValue(item, propertyName, expectedValues));
                default:
                    return false;
            }
        }

        private static bool ContainsStringValue(JsonElement element, params string[] expectedValues)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var value = element.GetString();
                    return value != null && expectedValues.Any(expected =>
                        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase));
                case JsonValueKind.Array:
                    return element.EnumerateArray().Any(item => ContainsStringValue(item, expectedValues));
                default:
                    return false;
            }
        }

        private static bool IsTruthy(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => element.TryGetDouble(out var number) && Math.Abs(number) > double.Epsilon,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var value) && value,
                _ => false
            };

        private static IEnumerable<JsonElement> EnumerateEffectObjects(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("effectType", out _))
                    {
                        yield return element;
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        foreach (var nested in EnumerateEffectObjects(property.Value))
                        {
                            yield return nested;
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        foreach (var nested in EnumerateEffectObjects(item))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }

        private static bool HasAnyTagContaining(SkillAuditRecord skill, params string[] markers) =>
            skill.Tags.Any(tag => markers.Any(marker =>
                tag.Contains(marker, StringComparison.OrdinalIgnoreCase)));

        private static bool ContainsAny(string text, params string[] markers) =>
            markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<SkillAuditRecord> LoadSkillRecords() =>
            LoadSkillRecords("active_skills.json", "active")
                .Concat(LoadSkillRecords("passive_skills.json", "passive"))
                .ToList();

        private static IReadOnlyList<SkillAuditRecord> LoadSkillRecords(string fileName, string kind)
        {
            var filePath = Path.Combine(DataPath, fileName);
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            return document.RootElement.EnumerateArray()
                .Select(element => new SkillAuditRecord(
                    kind,
                    GetRequiredString(element, "id"),
                    GetOptionalString(element, "name"),
                    GetOptionalString(element, "effectDescription"),
                    GetStringList(element, "tags"),
                    element.TryGetProperty("effects", out var effects) ? effects.Clone() : default))
                .ToList();
        }

        private static string GetRequiredString(JsonElement element, string propertyName)
        {
            var value = GetOptionalString(element, propertyName);
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Missing required string property '{propertyName}'.")
                : value;
        }

        private static string GetOptionalString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static IReadOnlyList<string> GetStringList(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList();
        }

        private sealed record SkillAuditRecord(
            string Kind,
            string Id,
            string Name,
            string EffectDescription,
            IReadOnlyList<string> Tags,
            JsonElement Effects)
        {
            public string Key => $"{Kind}:{Id}";
        }
    }
}
