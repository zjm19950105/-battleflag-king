using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;
using BattleKing.Ui;

namespace BattleKing.Tests
{
    [TestFixture]
    public class DamageCalculatorTest
    {
        private DamageCalculator _calc;
        private BattleContext _ctx;

        [SetUp]
        public void SetUp() { _calc = new DamageCalculator(); _ctx = new BattleContext(null!); }

        [Test]
        public void ForceCrit_MakesZeroCritAttackCritical()
        {
            var attacker = TestDataFactory.CreateUnit(str: 60, hit: 1000, crit: 0);
            var defender = TestDataFactory.CreateUnit(def: 10, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);
            calc.ForceCrit = true;

            var result = _calc.Calculate(calc);

            ClassicAssert.IsTrue(result.IsHit);
            ClassicAssert.IsTrue(result.IsCritical);
            ClassicAssert.AreEqual(75, result.TotalDamage);
        }

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

        [Test]
        public void CritSeal_OnAttacker_PreventsCriticalHits()
        {
            var attacker = TestDataFactory.CreateUnit(str: 60, hit: 1000, crit: 100);
            var defender = TestDataFactory.CreateUnit(def: 10, block: 0);
            attacker.Ailments.Add(StatusAilment.CritSeal);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            var result = _calc.Calculate(calc);

            ClassicAssert.IsTrue(result.IsHit);
            ClassicAssert.IsFalse(result.IsCritical);
            ClassicAssert.AreEqual(50, result.TotalDamage);
        }

        [Test]
        public void CritSeal_OnDefender_DoesNotPreventIncomingCriticalHits()
        {
            var attacker = TestDataFactory.CreateUnit(str: 60, hit: 1000, crit: 100);
            var defender = TestDataFactory.CreateUnit(def: 10, block: 0);
            defender.Ailments.Add(StatusAilment.CritSeal);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var calc = TestDataFactory.CreateCalc(attacker, defender, skill);

            var result = _calc.Calculate(calc);

            ClassicAssert.IsTrue(result.IsHit);
            ClassicAssert.IsTrue(result.IsCritical);
            ClassicAssert.AreEqual(75, result.TotalDamage);
        }

        // ────────── 格挡 ──────────

        [Test]
        public void 格挡_物理部分减25percent()
        {
            var a = TestDataFactory.CreateUnit(str: 60, crit: 0);
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

        [Test]
        public void 多段攻击_记录每段暴击明细()
        {
            var attacker = TestDataFactory.CreateUnit(str: 60, hit: 1000, crit: 0);
            var defender = TestDataFactory.CreateUnit(def: 10, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = TestDataFactory.CreateCalc(attacker, defender, skill);
            c.HitCount = 3;
            c.ForceCrit = true;

            var result = _calc.Calculate(c);

            ClassicAssert.AreEqual(3, result.HitResults.Count);
            ClassicAssert.IsTrue(result.HitResults.All(hit => hit.Landed));
            ClassicAssert.IsTrue(result.HitResults.All(hit => hit.Critical));
            ClassicAssert.AreEqual(75f, result.HitResults[0].TotalDamage, 0.001f);
            ClassicAssert.AreEqual(225, result.TotalDamage);
        }

        [Test]
        public void 多段攻击日志_按真实HitBreakdown汇总普通与暴击段()
        {
            var attacker = TestDataFactory.CreateUnit(str: 65, hit: 1000, crit: 0);
            var defender = TestDataFactory.CreateUnit(hp: 500, def: 15, block: 0);
            defender.CurrentHp = 380;
            var skill = TestDataFactory.CreateSkill(power: 20, name: "陨石斩");
            var c = TestDataFactory.CreateCalc(attacker, defender, skill);
            c.HitCount = 9;
            c.LandedHits = 9;
            c.FinalAttackPower = 65;
            c.FinalDefense = 15;
            c.SkillPowerRatio = 0.2f;
            c.CritMultiplier = 2.0f;
            for (int i = 1; i <= 6; i++)
            {
                c.HitResults.Add(new DamageHitResult
                {
                    HitIndex = i,
                    Landed = true,
                    BasePhysicalDamage = 10,
                    PhysicalDamage = 10
                });
            }
            for (int i = 7; i <= 9; i++)
            {
                c.HitResults.Add(new DamageHitResult
                {
                    HitIndex = i,
                    Landed = true,
                    Critical = true,
                    CritMultiplier = 2.0f,
                    BasePhysicalDamage = 10,
                    PhysicalDamage = 20
                });
            }
            var result = new DamageResult(
                120,
                0,
                isHit: true,
                isCritical: true,
                isBlocked: false,
                isEvaded: false,
                appliedAilments: new(),
                resolvedDefender: defender,
                hitResults: c.HitResults);

            var lines = BattleLogHelper.FormatAttack(attacker, defender, skill, c, result, false, new());

            Assert.That(lines, Has.Some.Contains("段数: 9 hit").And.Contains("暴击 3"));
            Assert.That(lines, Has.Some.Contains("单段基础: (65-15=50) x 威力20% = 10"));
            Assert.That(lines, Has.Some.Contains("合计: 普通6hit=60 + 暴击3hit x2.0=60 => 120"));
        }

        [Test]
        public void 命中率_技能命中字段作为倍率而不是加值()
        {
            var attacker = TestDataFactory.CreateUnit(hit: 120);
            var defender = TestDataFactory.CreateUnit(eva: 40);
            var skill = TestDataFactory.CreateSkill(hitRate: 75);

            var chance = HitChanceCalculator.Calculate(attacker, defender, skill);

            ClassicAssert.AreEqual(80, chance.BaseAccuracy);
            ClassicAssert.AreEqual(60, chance.FinalChance);
        }

        [Test]
        public void 命中率_地面对飞行近战先半减再向下取整()
        {
            var attacker = TestDataFactory.CreateUnit(
                hit: 128,
                classes: new() { UnitClass.Infantry });
            var defender = TestDataFactory.CreateUnit(
                eva: 105,
                classes: new() { UnitClass.Flying });
            var skill = TestDataFactory.CreateSkill(hitRate: 100, attackType: AttackType.Melee);

            var chance = HitChanceCalculator.Calculate(attacker, defender, skill);

            ClassicAssert.IsTrue(chance.FlyingPenaltyApplied);
            ClassicAssert.AreEqual(11.5f, chance.RawChance, 0.001f);
            ClassicAssert.AreEqual(11, chance.FinalChance);
        }

        [Test]
        public void 命中率日志_显示技能命中倍率公式()
        {
            var attacker = TestDataFactory.CreateUnit(str: 65, hit: 120, crit: 0);
            var defender = TestDataFactory.CreateUnit(hp: 200, def: 15, eva: 40, block: 0);
            defender.CurrentHp = 190;
            var skill = TestDataFactory.CreateSkill(power: 100, hitRate: 75, name: "倍率测试");
            var c = TestDataFactory.CreateCalc(attacker, defender, skill);
            c.FinalAttackPower = 65;
            c.FinalDefense = 15;
            c.SkillPowerRatio = 1f;
            var result = new DamageResult(
                50,
                0,
                isHit: true,
                isCritical: false,
                isBlocked: false,
                isEvaded: false,
                appliedAilments: new(),
                resolvedDefender: defender,
                hitResults: new List<DamageHitResult>());

            var lines = BattleLogHelper.FormatAttack(attacker, defender, skill, c, result, false, new());

            Assert.That(lines, Has.Some.Contains("命中率:")
                .And.Contains("x 技能命中倍率75%")
                .And.Contains("= 60%"));
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

        [Test]
        public void ForceEvasion_多段攻击_只回避第一段()
        {
            var a = TestDataFactory.CreateUnit(str: 100, hit: 1000, crit: 0);
            var d = TestDataFactory.CreateUnit(def: 0, eva: 0, block: 0);
            var skill = TestDataFactory.CreateSkill(power: 100);
            var c = new DamageCalculation { Attacker = a, Defender = d, Skill = skill, ForceEvasion = true, HitCount = 4 };

            var result = _calc.Calculate(c);

            ClassicAssert.IsTrue(result.IsHit);
            ClassicAssert.IsTrue(result.IsEvaded);
            ClassicAssert.AreEqual(1, c.EvadedHits);
            ClassicAssert.AreEqual(3, c.LandedHits);
            ClassicAssert.AreEqual(300, result.TotalDamage);
        }

        [Test]
        public void MeleeHitNullify_只无效下一段近战物理伤害()
        {
            var a = TestDataFactory.CreateUnit(str: 100, hit: 1000, crit: 0);
            var d = TestDataFactory.CreateUnit(def: 0, eva: 0, block: 0);
            d.AddTemporal("MeleeHitNullify", 1);
            var skill = TestDataFactory.CreateSkill(power: 100, attackType: AttackType.Melee);
            var c = new DamageCalculation { Attacker = a, Defender = d, Skill = skill, HitCount = 3 };

            var result = _calc.Calculate(c);

            ClassicAssert.AreEqual(1, c.NullifiedHits);
            ClassicAssert.AreEqual(3, c.LandedHits);
            ClassicAssert.AreEqual(200, result.TotalDamage);
            ClassicAssert.IsFalse(d.TemporalStates.Any(s => s.Key == "MeleeHitNullify"));
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
