using System.Text.Json;
using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class LocalizationComplianceAuditTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        private static readonly string[] FilesToScan =
        {
            "class_display_names.json",
            "characters.json",
            "active_skills.json",
            "passive_skills.json"
        };

        private static readonly string[] KnownReferenceTerms =
        {
            "女巫",
            "白骑士",
            "狮鹫骑士",
            "圣骑士",
            "剑圣",
            "兰茨克内希特",
            "君主",
            "先锋",
            "军士",
            "维京",
            "甲胄骑兵",
            "狂战士",
            "神射手",
            "盾射手",
            "恶棍",
            "术士",
            "魔女",
            "德鲁伊",
            "精灵女先知",
            "飞龙骑士",
            "羽剑士"
        };

        [Test]
        public void KnownReferenceTerms_AreReportedWithoutBlockingCurrentRuleHardening()
        {
            var findings = new List<string>();

            foreach (var fileName in FilesToScan)
            {
                var filePath = Path.Combine(DataPath, fileName);
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                CollectFindings(document.RootElement, fileName, "$", findings);
            }

            TestContext.Progress.WriteLine(
                $"Localization compliance reference-term findings: {findings.Count}");

            foreach (var finding in findings)
            {
                TestContext.Progress.WriteLine(finding);
            }

            Assert.Pass("Report-only compliance scan; findings are visible in test output.");
        }

        private static void CollectFindings(
            JsonElement element,
            string fileName,
            string jsonPath,
            ICollection<string> findings)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        CollectFindings(
                            property.Value,
                            fileName,
                            $"{jsonPath}.{property.Name}",
                            findings);
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        CollectFindings(item, fileName, $"{jsonPath}[{index}]", findings);
                        index++;
                    }
                    break;

                case JsonValueKind.String:
                    var value = element.GetString() ?? string.Empty;
                    var matchedTerms = KnownReferenceTerms
                        .Where(term => value.Contains(term, StringComparison.Ordinal))
                        .Distinct()
                        .ToList();

                    if (matchedTerms.Count > 0)
                    {
                        findings.Add(
                            $"{fileName} {jsonPath} terms=[{string.Join(", ", matchedTerms)}] text=\"{value}\"");
                    }
                    break;
            }
        }
    }
}
