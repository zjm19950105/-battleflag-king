using NUnit.Framework;
using NUnit.Framework.Legacy;
using BattleKing.Core;
using BattleKing.Data;
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

        [Test]
        public void StackablePureBuff_SameSkillAndStat_CanStack()
        {
            var b1 = new Buff
            {
                SkillId = "pas_hawk_eye",
                TargetStat = "Def",
                Ratio = 0.2f,
                IsPureBuffOrDebuff = true,
                CanStackWithSameSkill = true
            };
            var b2 = new Buff
            {
                SkillId = "pas_hawk_eye",
                TargetStat = "Def",
                Ratio = 0.2f,
                IsPureBuffOrDebuff = true,
                CanStackWithSameSkill = true
            };

            BuffManager.ApplyBuff(_unit, b1);
            BuffManager.ApplyBuff(_unit, b2);

            ClassicAssert.AreEqual(2, _unit.Buffs.Count);
            ClassicAssert.That(BuffManager.GetTotalBuffRatio(_unit, "Def"), Is.EqualTo(0.4f).Within(0.01f));
        }

        [Test]
        public void RatioBuffs_AreAdditiveAgainstEquippedSameStatBaseline()
        {
            _unit = TestDataFactory.CreateUnit(str: 30, def: 30);
            _unit.Equipment.Equip(TestDataFactory.CreateEquipment(
                "eq_stat_ring",
                "Stat Ring",
                EquipmentCategory.Accessory,
                new() { { "Str", 10 }, { "Def", 10 } }));

            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_a", TargetStat = "Str", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_b", TargetStat = "Str", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_a", TargetStat = "Def", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_b", TargetStat = "Def", Ratio = 0.2f });

            ClassicAssert.AreEqual(56, _unit.GetCurrentStat("Str"));
            ClassicAssert.AreEqual(56, _unit.GetCurrentStat("Def"));
        }

        [Test]
        public void RatioBuffs_PhysicalAttackAndDefenseUseEquippedCombatBaseline()
        {
            _unit = TestDataFactory.CreateUnit(str: 30, def: 30);
            _unit.Equipment.Equip(TestDataFactory.CreateEquipment(
                "eq_combat_gear",
                "Combat Gear",
                EquipmentCategory.Accessory,
                new() { { "phys_atk", 10 }, { "phys_def", 10 } }));

            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_a", TargetStat = "Str", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_b", TargetStat = "Str", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_a", TargetStat = "Def", Ratio = 0.2f });
            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "buff_b", TargetStat = "Def", Ratio = 0.2f });

            Assert.Multiple(() =>
            {
                ClassicAssert.AreEqual(
                    56,
                    _unit.GetCurrentAttackPower(SkillType.Physical),
                    "Physical attack buffs should use Str + phys_atk as the additive ratio baseline.");
                ClassicAssert.AreEqual(
                    56,
                    _unit.GetCurrentDefense(SkillType.Physical),
                    "Physical defense buffs should use Def + phys_def as the additive ratio baseline.");
            });
        }

        [Test]
        public void RatioDebuffs_CurrentStatAndBreakdownUseEquippedCombatBaseline()
        {
            _unit = TestDataFactory.CreateUnit(def: 30);
            _unit.Equipment.Equip(TestDataFactory.CreateEquipment(
                "eq_def_shield",
                "Def Shield",
                EquipmentCategory.Accessory,
                new() { { "phys_def", 12 } }));

            BuffManager.ApplyBuff(_unit, new Buff { SkillId = "debuff_def", TargetStat = "Def", Ratio = -0.2f });

            var breakdown = _unit.GetStatBreakdown("Def");
            ClassicAssert.AreEqual(42, breakdown.EquippedBaseline);
            ClassicAssert.AreEqual(-9, breakdown.BuffDelta);
            ClassicAssert.AreEqual(33, breakdown.Current);
            ClassicAssert.AreEqual(33, _unit.GetCurrentStat("Def"));
            ClassicAssert.AreEqual(33, _unit.GetCurrentDefense(SkillType.Physical));
        }
    }
}
