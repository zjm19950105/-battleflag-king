using NUnit.Framework;
using NUnit.Framework.Legacy;
using BattleKing.Core;
using BattleKing.Equipment;

namespace BattleKing.Tests
{
    [TestFixture]
    public class BuffManagerTest
    {
        private BattleUnit _unit;

        [SetUp]
        public void SetUp() { _unit = TestDataFactory.CreateUnit(); }

        [Test]
        public void 同技能纯buff_重复施加_被跳过()
        {
            var b1 = new Buff { SkillId = "act_shield_bash", TargetStat = "Def", Ratio = 0.2f, IsPureBuffOrDebuff = true };
            BuffManager.ApplyBuff(_unit, b1);
            BuffManager.ApplyBuff(_unit, b1);
            ClassicAssert.AreEqual(1, _unit.Buffs.Count);
        }

        [Test]
        public void 不同技能同名效果_可叠加()
        {
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "act_smash", TargetStat = "Def", Ratio = -0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "act_def_curse", TargetStat = "Def", Ratio = -0.5f });
            ClassicAssert.AreEqual(2, _unit.Buffs.Count);
            ClassicAssert.That(BuffManager.GetTotalBuffRatio(_unit, "Def"), Is.EqualTo(-0.7f).Within(0.01f));
        }

        [Test]
        public void 一次性buff_行动后清除()
        {
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "act_magic_blade", TargetStat = "Str", Ratio = 0.3f, IsOneTime = true });
            ClassicAssert.AreEqual(1, _unit.Buffs.Count);
            BuffManager.CleanupAfterAction(_unit);
            ClassicAssert.AreEqual(0, _unit.Buffs.Count);
        }

        [Test]
        public void 永久buff_RemainingTurns为负1_不清除()
        {
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "act_warcry", TargetStat = "Str", Ratio = 0.2f, RemainingTurns = -1 });
            BuffManager.CleanupEndOfTurn(_unit);
            ClassicAssert.AreEqual(1, _unit.Buffs.Count);
        }

        [Test]
        public void __1回合buff_回合末减至0_自动清除()
        {
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "act_temp", TargetStat = "Spd", Ratio = 0.1f, RemainingTurns = 1 });
            ClassicAssert.AreEqual(1, _unit.Buffs.Count);
            BuffManager.CleanupEndOfTurn(_unit);
            ClassicAssert.AreEqual(0, _unit.Buffs.Count);
        }

        [Test]
        public void GetTotalBuffRatio_多个buff某属性_正确求和()
        {
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "a", TargetStat = "Str", Ratio = 0.3f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "b", TargetStat = "Str", Ratio = -0.1f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "c", TargetStat = "Def", Ratio = 0.5f });
            ClassicAssert.That(BuffManager.GetTotalBuffRatio(_unit, "Str"), Is.EqualTo(0.2f).Within(0.01f));
            ClassicAssert.That(BuffManager.GetTotalBuffRatio(_unit, "Def"), Is.EqualTo(0.5f).Within(0.01f));
        }
    }
}
