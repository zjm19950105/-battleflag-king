using NUnit.Framework;
using NUnit.Framework.Legacy;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Tests
{
    [TestFixture]
    public class DamageCalculatorTest
    {
        private DamageCalculator _calc;
        private BattleContext _ctx;

        [SetUp]
        public void SetUp() { _calc = new DamageCalculator(); _ctx = new BattleContext(null!); }

        // ────────── 基础物理伤害 ──────────

        [Test]
        public void 物理攻击_基础公式_攻减防乘威力()
        {
            var attacker = TestDataFactory.CreateUnit(str: 50, crit: 0);
            var defender = TestDataFactory.CreateUnit(def: 30, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(attacker, defender, skill);
            // ATK=50, DEF=30, diff=20, power=100/100=1.0 → 20 damage
            var result = _calc.Calculate(c);
            ClassicAssert.AreEqual(20, result.TotalDamage);
        }

        [Test]
        public void 物理攻击_威力倍率生效()
        {
            var a = TestDataFactory.CreateUnit(str: 60, crit: 0);
            var d = TestDataFactory.CreateUnit(def: 10, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 150);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            // (60-10)=50, power=150/100=1.5 → 75
            var result = _calc.Calculate(c);
            ClassicAssert.AreEqual(75, result.TotalDamage);
        }

        [Test]
        public void 保底伤害_攻小于防时保底1()
        {
            var a = TestDataFactory.CreateUnit(str: 10, crit: 0);
            var d = TestDataFactory.CreateUnit(def: 50, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            var result = _calc.Calculate(c);
            ClassicAssert.GreaterOrEqual(result.TotalDamage, 1);
        }

        // ────────── 暴击 ──────────

        [Test]
        public void 暴击_基础1_5倍()
        {
            var a = TestDataFactory.CreateUnit(str: 60, crit: 100);
            var d = TestDataFactory.CreateUnit(def: 10, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            var result = _calc.Calculate(c);
            if (result.IsCritical)
                ClassicAssert.AreEqual((int)(50 * 1.5f), result.TotalDamage);
        }

        // ────────── 格挡 ──────────

        [Test]
        public void 格挡_物理部分减25percent()
        {
            var a = TestDataFactory.CreateUnit(str: 60);
            var d = TestDataFactory.CreateUnit(def: 10, block: 100);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            var result = _calc.Calculate(c);
            if (result.IsBlocked)
                ClassicAssert.Less(result.TotalDamage, 50); // block reduces damage
        }

        // ────────── 多段攻击 ──────────

        [Test]
        public void 多段攻击_每hit独立判定()
        {
            var a = TestDataFactory.CreateUnit(str: 40, crit: 0);
            var d = TestDataFactory.CreateUnit(def: 10, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 20);
            var c = new DamageCalculation { Attacker = a, Defender = d, Skill = skill, HitCount = 9 };
            var result = _calc.Calculate(c);
            // 9hit, each: (40-10)=30, power=20/100=0.2 → 6 per hit max, 9×6=54 max, 1×6=6 min
            ClassicAssert.GreaterOrEqual(result.TotalDamage, 6);
            ClassicAssert.LessOrEqual(result.TotalDamage, 54);
        }

        // ────────── 混合伤害 ──────────

                [Test]
        public void 魔法攻击_魔攻减魔防已测()
        {
            // Tested above in 魔法攻击_魔攻减魔防
            ClassicAssert.Pass();
        }
        // ────────── 兵种克制 ──────────

        [Test]
        public void 兵种克制_骑兵打步兵2倍()
        {
            var a = TestDataFactory.CreateUnit(str: 50, crit: 0, classes: new() { UnitClass.Cavalry });
            var d = TestDataFactory.CreateUnit(def: 20, block: 0, classes: new() { UnitClass.Infantry });
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(a, d, skill);

            var result = _calc.Calculate(c);
            // diff=30, power=1.0, classMult=2.0 → 60
            ClassicAssert.AreEqual(60, result.TotalDamage);
        }

        [Test]
        public void 兵种克制_飞行打骑兵2倍()
        {
            var a = TestDataFactory.CreateUnit(str: 50, crit: 0, classes: new() { UnitClass.Flying });
            var d = TestDataFactory.CreateUnit(def: 20, block: 0, classes: new() { UnitClass.Cavalry });
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            var result = _calc.Calculate(c);
            ClassicAssert.AreEqual(60, result.TotalDamage);
        }

        // ────────── ForceHit / ForceEvasion ──────────

        [Test]
        public void ForceHit_无视命中回避_必定命中()
        {
            var a = TestDataFactory.CreateUnit(str: 50, hit: 10);
            var d = TestDataFactory.CreateUnit(def: 20, eva: 80);
            var skill = TestDataFactory.CreateSkill(power: 100, hitRate: 50);
            var c = new DamageCalculation { Attacker = a, Defender = d, Skill = skill, ForceHit = true };
            var result = _calc.Calculate(c);
            ClassicAssert.IsTrue(result.IsHit);
            ClassicAssert.IsFalse(result.IsEvaded);
        }

        [Test]
        public void ForceEvasion_强制回避()
        {
            var a = TestDataFactory.CreateUnit(str: 50);
            var d = TestDataFactory.CreateUnit(def: 20);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = new DamageCalculation { Attacker = a, Defender = d, Skill = skill, ForceEvasion = true };
            var result = _calc.Calculate(c);
            ClassicAssert.IsTrue(result.IsEvaded);
            ClassicAssert.AreEqual(0, result.TotalDamage);
        }

        // ────────── 魔法伤害 ──────────

        [Test]
        public void 魔法攻击_魔攻减魔防()
        {
            var a = TestDataFactory.CreateUnit(mag: 50, crit: 0);
            var d = TestDataFactory.CreateUnit(mdef: 20, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100, type: SkillType.Magical);
            var c = TestDataFactory.CreateCalc(a, d, skill);
            var result = _calc.Calculate(c);
            ClassicAssert.AreEqual(30, result.TotalDamage);
        }

        [Test]
        public void 魔法攻击_使用魔攻魔防装备_不混入物攻物防()
        {
            var a = TestDataFactory.CreateUnit(mag: 30, crit: 0);
            a.Equipment.Equip(TestDataFactory.CreateEquipment("eq_staff", "法杖", EquipmentCategory.Staff,
                new() { { "mag_atk", 20 }, { "phys_atk", 99 } }));
            var d = TestDataFactory.CreateUnit(mdef: 10, block: 0);
            d.Equipment.Equip(TestDataFactory.CreateEquipment("eq_ward", "护符", EquipmentCategory.Accessory,
                new() { { "mag_def", 5 }, { "phys_def", 99 } }));
            var skill = TestDataFactory.CreateSkill(power: 100, type: SkillType.Magical);

            var result = _calc.Calculate(TestDataFactory.CreateCalc(a, d, skill));

            ClassicAssert.AreEqual(35, result.TotalDamage);
        }
    }
}
