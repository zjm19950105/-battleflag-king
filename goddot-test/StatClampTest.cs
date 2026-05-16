using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Skills;
using BattleKing.Ui;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class StatClampTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "goddot", "data"));

        [TestCase("Str")]
        [TestCase("Def")]
        [TestCase("Mag")]
        [TestCase("MDef")]
        [TestCase("Hit")]
        [TestCase("Eva")]
        [TestCase("Crit")]
        [TestCase("Block")]
        [TestCase("Spd")]
        public void GetCurrentStat_RuntimeDebuffsCannotDropStatsBelowZero(string statName)
        {
            var unit = TestDataFactory.CreateUnit(
                str: 10,
                def: 10,
                mag: 10,
                mdef: 10,
                hit: 10,
                eva: 10,
                crit: 10,
                block: 10,
                spd: 10);

            BuffManager.ApplyBuff(unit, new Buff
            {
                SkillId = "test_over_debuff",
                TargetStat = statName,
                Ratio = -2f,
                FlatAmount = -10,
                IsPureBuffOrDebuff = true
            });

            ClassicAssert.AreEqual(0, unit.GetCurrentStat(statName));
        }

        [Test]
        public void GetCurrentStat_ResourcesStillUseResourceCapClamp()
        {
            var unit = TestDataFactory.CreateUnit(ap: 3, pp: 2);
            unit.Buffs.Add(new Buff { TargetStat = "AP", FlatAmount = 10 });
            unit.Buffs.Add(new Buff { TargetStat = "PP", FlatAmount = -10 });

            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.GetCurrentStat("AP"));
            ClassicAssert.AreEqual(0, unit.GetCurrentStat("PP"));
        }

        [Test]
        public void RealQuickCastJson_CritDebuffClampsToZero()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var caster = TestDataFactory.CreateUnit(crit: 20, pp: 2);
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit>()
            };
            var quickCast = repository.PassiveSkills["pas_quick_cast"];
            var executor = new SkillEffectExecutor();

            var logs = executor.ExecuteActionEffects(
                context,
                caster,
                new[] { caster },
                quickCast.Effects,
                quickCast.Id);

            CollectionAssert.AreEqual(
                new[] { "ActionOrderPriority", "AddDebuff" },
                quickCast.Effects.Select(effect => effect.EffectType).ToList());
            ClassicAssert.AreEqual(1000, caster.ActionOrderPriority);
            ClassicAssert.AreEqual(10, caster.GetCurrentCritRate());
            Assert.That(logs, Has.Some.EqualTo("TestUnit.Crit 20->10"));
        }

        [Test]
        public void EquipmentPreview_UsesCurrentStatClampForDebuffs()
        {
            var unit = TestDataFactory.CreateUnit(crit: 10);
            unit.Buffs.Add(new Buff { TargetStat = "Crit", FlatAmount = -50 });

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", null);
            var crit = preview.GetRow("Crit");

            ClassicAssert.AreEqual(0, crit.Current);
            ClassicAssert.AreEqual(0, crit.Preview);
        }

        [Test]
        public void EquipmentPreview_ResourcesStillUseResourceCapClamp()
        {
            var unit = TestDataFactory.CreateUnit(ap: 3);
            var bracelet = TestDataFactory.CreateEquipment(
                "eq_ap_big",
                "Big AP Bracelet",
                EquipmentCategory.Accessory,
                new() { { "AP", 10 } });

            var preview = EquipmentStatPreviewHelper.Build(unit, "Accessory1", bracelet);
            var ap = preview.GetRow("AP");

            ClassicAssert.AreEqual(3, ap.Current);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, ap.Preview);
            ClassicAssert.AreEqual(1, ap.Delta);
        }
    }
}
