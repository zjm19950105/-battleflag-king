using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class SkillEffectArchitectureTest
    {
        private static string SourcePath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "src"));

        [Test]
        public void ProductionCode_DoesNotExposeLegacyISkillEffectExecutionPath()
        {
            var forbiddenMatches = new List<string>();
            var forbiddenTerms = new[]
            {
                "ISkillEffect",
                "SkillEffectFactory",
                "PassiveOnlyEffect"
            };

            foreach (var filePath in Directory.EnumerateFiles(SourcePath, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(filePath);
                foreach (var term in forbiddenTerms)
                {
                    if (text.Contains(term, StringComparison.Ordinal))
                    {
                        forbiddenMatches.Add($"{Path.GetRelativePath(SourcePath, filePath)} contains {term}");
                    }
                }

                if (Path.GetRelativePath(SourcePath, filePath)
                        .Replace('\\', '/')
                        .StartsWith("Skills/Effects/", StringComparison.Ordinal))
                {
                    forbiddenMatches.Add($"{Path.GetRelativePath(SourcePath, filePath)} is under legacy Skills/Effects");
                }
            }

            Assert.That(forbiddenMatches, Is.Empty);
        }
    }
}
