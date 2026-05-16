using BattleKing.Data;
using BattleKing.Ui;
using NUnit.Framework;

namespace BattleKing.Tests
{
    [TestFixture]
    public class SandboxTooltipHelperTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void SkillTooltips_ShowPowerHitRateAndHitCount()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);

            string sharpSlash = SandboxTooltipHelper.BuildActiveSkillDetail(repository.ActiveSkills["act_sharp_slash"]);
            Assert.That(sharpSlash, Does.Contain("威力：100"));
            Assert.That(sharpSlash, Does.Contain("攻击次数：1"));
            Assert.That(sharpSlash, Does.Contain("命中：必中"));

            string meteorSlash = SandboxTooltipHelper.BuildActiveSkillDetail(repository.ActiveSkills["act_meteor_slash"]);
            Assert.That(meteorSlash, Does.Contain("威力：20"));
            Assert.That(meteorSlash, Does.Contain("攻击次数：9"));
            Assert.That(meteorSlash, Does.Contain("命中：100%"));

            string pursuitSlash = SandboxTooltipHelper.BuildPassiveSkillDetail(repository.PassiveSkills["pas_pursuit_slash"]);
            Assert.That(pursuitSlash, Does.Contain("威力：75"));
            Assert.That(pursuitSlash, Does.Contain("攻击次数：1"));
            Assert.That(pursuitSlash, Does.Contain("命中：90%"));

            string quickStrike = SandboxTooltipHelper.BuildPassiveSkillDetail(repository.PassiveSkills["pas_quick_strike"]);
            Assert.That(quickStrike, Does.Contain("威力：150"));
            Assert.That(quickStrike, Does.Contain("命中：必中"));

            Assert.That(sharpSlash, Does.Not.Contain("Tags："));
            Assert.That(sharpSlash, Does.Not.Contain("Effects："));
            Assert.That(pursuitSlash, Does.Not.Contain("Tags："));
            Assert.That(pursuitSlash, Does.Not.Contain("Effects："));
        }
    }
}
