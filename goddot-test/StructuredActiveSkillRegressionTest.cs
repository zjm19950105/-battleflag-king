using System.Text.Json;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace BattleKing.Tests
{
    [TestFixture]
    public class StructuredActiveSkillRegressionTest
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void RealActiveJson_MissedMigrationSkills_DeclareStructuredEffects()
        {
            var repository = LoadRepository();

            AssertFirstThreeCharacterActiveSkillSourceData(repository);
            AssertLordSlash(repository.ActiveSkills["act_lord_slash"]);
            AssertSpiralBlade(repository.ActiveSkills["act_spiral_blade"]);
            AssertResourceSteal(repository.ActiveSkills["act_passive_steal"], "PP", "All", 2);
            AssertResourceSteal(repository.ActiveSkills["act_active_steal"], "AP", "1", 4);
            AssertHitCount(repository.ActiveSkills["act_poison_throw"], 2);
            AssertPoisonThrowSourceData(repository.ActiveSkills["act_poison_throw"]);
            AssertShadowBiteSourceData(repository.ActiveSkills["act_shadow_bite"]);
            AssertWizardAndWitchMagicSourceData(repository);
            AssertHolyBlade(repository.ActiveSkills["act_holy_blade"]);
            AssertPrimalEdge(repository.ActiveSkills["act_primal_edge"]);
            AssertFairyHeal(repository.ActiveSkills["act_fairy_heal"]);
            AssertElementalRoar(repository.ActiveSkills["act_elemental_roar"]);
        }

        [Test]
        public void RealActiveJson_KnownPowerPlus50Skills_UseFlatSkillPowerBonus()
        {
            var repository = LoadRepository();

            AssertFlatPowerBonus(repository.ActiveSkills["act_enhanced_spear"], 50, ("targetClass", "Cavalry"), ("CannotBeBlocked", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_spear_pierce"], 50, ("targetClass", "Flying"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_throwing_spear"], 50, ("casterHasBuff", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_break_formation"], 50, ("targetHasDebuff", "True"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_spike"], 50, ("casterHpRatioMax", "0.5"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_full_assault"], 50, ("casterHpRatioMin", "1"));
            AssertFlatPowerBonus(repository.ActiveSkills["act_accumulate"], 50, ("casterHpRatioMin", "1"));
        }

        [Test]
        public void RealActiveJson_ResearchConfirmedActiveDataFixes_StayPinned()
        {
            var repository = LoadRepository();

            AssertWhiteKnightAndSaintKnightData(repository);
            AssertGriffinKnightAndRulerData(repository);

            var longThrust = repository.ActiveSkills["act_enhanced_spear"];
            ClassicAssert.AreEqual(1, longThrust.ApCost);
            ClassicAssert.AreEqual(AttackType.Melee, longThrust.AttackType);
            ClassicAssert.AreEqual(TargetType.FrontAndBack, longThrust.TargetType);

            var enhancedSpear = repository.ActiveSkills["act_throwing_spear"];
            ClassicAssert.AreEqual(2, enhancedSpear.ApCost);
            ClassicAssert.AreEqual(AttackType.Melee, enhancedSpear.AttackType);
            ClassicAssert.AreEqual(TargetType.FrontAndBack, enhancedSpear.TargetType);

            var formationBreaker = repository.ActiveSkills["act_formation_breaker"];
            ClassicAssert.AreEqual(1, formationBreaker.ApCost);
            ClassicAssert.AreEqual(20, formationBreaker.UnlockLevel);
            ClassicAssert.AreEqual("狂战士 Lv20", formationBreaker.LearnCondition);

            var accumulate = repository.ActiveSkills["act_accumulate"];
            ClassicAssert.AreEqual(AttackType.Ranged, accumulate.AttackType);
            ClassicAssert.AreEqual(TargetType.AllEnemies, accumulate.TargetType);
            ClassicAssert.AreEqual(30, accumulate.UnlockLevel);
            CollectionAssert.Contains(accumulate.Tags, "Ranged");
            CollectionAssert.Contains(accumulate.Tags, "GroundAttribute");
            var flyingNullify = accumulate.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && GetScalarString(effect.Parameters, "targetClass") == "Flying");
            ClassicAssert.AreEqual("True", GetScalarString(flyingNullify.Parameters, "NullifyPhysicalDamage"));

            AssertRollingAxe(repository.ActiveSkills["act_whirlwind_slash"]);
            AssertWideBreaker(repository.ActiveSkills["act_break_formation"]);
            AssertBattleLongStackingBuff(repository.PassiveSkills["pas_hawk_eye"], ("Def", "ratio", 0.2d), ("Block", "amount", 20d));
            AssertBattleLongStackingBuff(repository.PassiveSkills["pas_emergency_cover"], ("Str", "ratio", 0.2d), ("Hit", "amount", 20d));
            AssertShieldBashGuaranteedStun(repository.ActiveSkills["act_shield_bash"]);
        }

        [Test]
        public void StepOneAction_LordSlash_HealsOnHitAndOnKill()
        {
            const string skillId = "act_lord_slash";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "lord", true, 1, skillId, hp: 200, str: 200, spd: 100);
            caster.CurrentHp = 100;
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var enemy = CreateUnit(repository, "enemy", false, 1, null, hp: 80, def: 0, spd: 1);
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { enemy }
            });

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            ClassicAssert.IsFalse(enemy.IsAlive);
            ClassicAssert.AreEqual(200, caster.CurrentHp);
        }

        [Test]
        public void StepOneAction_IceCoffin_HitsFrontRowAndFreezesOnHit()
        {
            const string skillId = "act_ice_coffin";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "witch", true, 1, skillId, hp: 500, mag: 200, spd: 100);
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var first = CreateUnit(repository, "first", false, 1, null, hp: 1000, mdef: 100, spd: 1);
            var second = CreateUnit(repository, "second", false, 2, null, hp: 1000, mdef: 100, spd: 1);
            var back = CreateUnit(repository, "back", false, 4, null, hp: 1000, mdef: 100, spd: 1);
            var logs = new List<string>();
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { first, second, back }
            }) { OnLog = logs.Add };

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            ClassicAssert.Less(first.CurrentHp, 1000);
            ClassicAssert.Less(second.CurrentHp, 1000);
            ClassicAssert.AreEqual(1000, back.CurrentHp);
            CollectionAssert.Contains(first.Ailments, StatusAilment.Freeze);
            CollectionAssert.Contains(second.Ailments, StatusAilment.Freeze);
            CollectionAssert.DoesNotContain(back.Ailments, StatusAilment.Freeze);
            Assert.That(logs, Has.Some.Contains(repository.ActiveSkills[skillId].Name));
        }

        [Test]
        public void StepOneAction_FairyHeal_HealsTargetRowGrantsDamageNullifyAndSprite()
        {
            const string skillId = "act_fairy_heal";
            var repository = LoadRepository();
            var caster = CreateUnit(repository, "sibyl", true, 4, skillId, hp: 200, mag: 100, spd: 100);
            caster.Strategies.Add(new Strategy { SkillId = skillId });
            var frontLeft = CreateUnit(repository, "front_left", true, 1, null, hp: 200, spd: 1);
            frontLeft.CurrentHp = 80;
            var frontRight = CreateUnit(repository, "front_right", true, 2, null, hp: 200, spd: 1);
            frontRight.CurrentHp = 150;
            var enemy = CreateUnit(repository, "enemy", false, 1, null, hp: 200, spd: 1);
            var engine = new BattleEngine(new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster, frontLeft, frontRight },
                EnemyUnits = new List<BattleUnit> { enemy }
            });

            var result = engine.StepOneAction();

            AssertActionCompleted(result);
            ClassicAssert.AreEqual(180, frontLeft.CurrentHp);
            ClassicAssert.AreEqual(200, frontRight.CurrentHp);
            ClassicAssert.AreEqual(1, caster.GetCounter("Sprite"));
            AssertDamageNullify(frontLeft, skillId);
            AssertDamageNullify(frontRight, skillId);
            ClassicAssert.IsFalse(caster.TemporalStates.Any(state => state.Key == "DamageNullify"));
        }

        [Test]
        public void ExecuteActionEffects_PrimalEdge_GatesPpAndSpriteOnFullHp()
        {
            var fullHp = RunPrimalEdgeActionEffects(currentHp: 100);
            var injured = RunPrimalEdgeActionEffects(currentHp: 99);

            ClassicAssert.AreEqual(1, fullHp.Caster.CurrentPp);
            ClassicAssert.AreEqual(1, fullHp.Caster.GetCounter("Sprite"));
            ClassicAssert.AreEqual(0, injured.Caster.CurrentPp);
            ClassicAssert.AreEqual(0, injured.Caster.GetCounter("Sprite"));
            Assert.That(injured.Logs, Has.None.Contains("Sprite"));
        }

        [TestCase("act_passive_steal", "PP", "All", 2)]
        [TestCase("act_active_steal", "AP", "1", 4)]
        public void ExecutePostDamageEffects_ResourceSteals_RequireOnlyFirstHitUnblocked(
            string skillId,
            string resource,
            string amount,
            int hitCount)
        {
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[skillId];
            AssertResourceSteal(skill, resource, amount, hitCount);

            var firstBlocked = RunStealPostEffects(repository, skill, resource, firstHitBlocked: true);
            ClassicAssert.AreEqual(0, firstBlocked.CasterGained);
            ClassicAssert.AreEqual(3, firstBlocked.TargetRemaining);

            var secondBlockedOnly = RunStealPostEffects(repository, skill, resource, firstHitBlocked: false);
            ClassicAssert.AreEqual(resource == "PP" ? 3 : 1, secondBlockedOnly.CasterGained);
            ClassicAssert.AreEqual(resource == "PP" ? 0 : 2, secondBlockedOnly.TargetRemaining);
        }

        private static void AssertLordSlash(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(100, skill.Power);
            ClassicAssert.AreEqual(100, skill.HitRate);
            CollectionAssert.DoesNotContain(skill.Tags, "SureHit");
            Assert.That(skill.Effects.Select(effect => effect.EffectType), Is.EqualTo(new[] { "OnHitEffect", "OnKillEffect" }));

            AssertNestedHealRatio(EffectAt(skill, "OnHitEffect"), "Self", 0.25);
            AssertNestedHealRatio(EffectAt(skill, "OnKillEffect"), "Self", 0.25);
        }

        private static void AssertSpiralBlade(ActiveSkillData skill)
        {
            var calc = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.AreEqual(2, GetInt(calc.Parameters, "HitCount"));
            ClassicAssert.AreEqual(0.5d, GetDouble(calc.Parameters, "IgnoreDefenseRatio"), 0.0001d);
            ClassicAssert.AreEqual(30, skill.UnlockLevel);

            var recover = NestedEffects(EffectAt(skill, "OnKillEffect")).Single();
            ClassicAssert.AreEqual("RecoverPp", recover.EffectType);
            ClassicAssert.AreEqual("Self", GetString(recover.Parameters, "target"));
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
        }

        private static void AssertResourceSteal(ActiveSkillData skill, string resource, string amount, int hitCount)
        {
            AssertHitCount(skill, hitCount);
            var onHit = EffectAt(skill, "OnHitEffect");
            ClassicAssert.IsTrue(GetBool(onHit.Parameters, "requireFirstHitUnblocked"));
            ClassicAssert.IsFalse(onHit.Parameters.ContainsKey("requireUnblocked"));

            var transfer = NestedEffects(onHit).Single();
            ClassicAssert.AreEqual("TransferResource", transfer.EffectType);
            ClassicAssert.AreEqual(resource, GetString(transfer.Parameters, "resource"));
            ClassicAssert.AreEqual("Target", GetString(transfer.Parameters, "from"));
            ClassicAssert.AreEqual("Self", GetString(transfer.Parameters, "to"));
            ClassicAssert.AreEqual(amount, GetScalarString(transfer.Parameters, "amount"));
        }

        private static void AssertHitCount(ActiveSkillData skill, int expected)
        {
            var calc = EffectAt(skill, "ModifyDamageCalc");
            ClassicAssert.AreEqual(expected, GetInt(calc.Parameters, "HitCount"));
        }

        private static void AssertPoisonThrowSourceData(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(AttackType.Ranged, skill.AttackType);
            CollectionAssert.Contains(skill.Tags, "Ranged");
            CollectionAssert.Contains(skill.Tags, "Poison");
            CollectionAssert.DoesNotContain(skill.Tags, "FlyingNoHitPenalty");
        }

        private static void AssertShadowBiteSourceData(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(2, skill.ApCost);
            ClassicAssert.AreEqual(SkillType.Physical, skill.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, skill.AttackType);
            ClassicAssert.AreEqual(TargetType.Row, skill.TargetType);
            ClassicAssert.AreEqual(15, skill.Power);
            ClassicAssert.AreEqual(100, skill.HitRate);
            AssertHitCount(skill, 2);
            CollectionAssert.Contains(skill.Tags, "Ranged");

            var debuffs = NestedEffects(EffectAt(skill, "OnHitEffect"))
                .Where(effect => effect.EffectType == "AddDebuff")
                .ToList();
            ClassicAssert.AreEqual(2, debuffs.Count);
            AssertFlatDebuff(debuffs, "Spd", 20);
            AssertFlatDebuff(debuffs, "Eva", 20);
        }

        private static void AssertWizardAndWitchMagicSourceData(GameDataRepository repository)
        {
            var fireball = repository.ActiveSkills["act_fireball"];
            ClassicAssert.AreEqual(1, fireball.ApCost);
            ClassicAssert.AreEqual(SkillType.Magical, fireball.Type);
            ClassicAssert.AreEqual(AttackType.Magic, fireball.AttackType);
            ClassicAssert.AreEqual(TargetType.SingleEnemy, fireball.TargetType);
            ClassicAssert.AreEqual(40, fireball.Power);
            ClassicAssert.AreEqual(100, fireball.HitRate);
            AssertHitCount(fireball, 3);
            var burnAilment = NestedEffects(EffectAt(fireball, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("Burn", GetString(burnAilment.Parameters, "ailment"));

            var freezeArrow = repository.ActiveSkills["act_freeze_arrow"];
            ClassicAssert.AreEqual(1, freezeArrow.ApCost);
            ClassicAssert.AreEqual(AttackType.Magic, freezeArrow.AttackType);
            ClassicAssert.AreEqual(TargetType.SingleEnemy, freezeArrow.TargetType);
            ClassicAssert.AreEqual(100, freezeArrow.Power);
            var freezeAilment = NestedEffects(EffectAt(freezeArrow, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("Freeze", GetString(freezeAilment.Parameters, "ailment"));

            var thunderstorm = repository.ActiveSkills["act_thunderstorm"];
            ClassicAssert.AreEqual(1, thunderstorm.ApCost);
            ClassicAssert.AreEqual(TargetType.Row, thunderstorm.TargetType);
            ClassicAssert.AreEqual(100, thunderstorm.Power);
            ClassicAssert.AreEqual(90, thunderstorm.HitRate);
            var stunAilment = NestedEffects(EffectAt(thunderstorm, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("Stun", GetString(stunAilment.Parameters, "ailment"));

            var volcano = repository.ActiveSkills["act_volcano"];
            ClassicAssert.AreEqual(2, volcano.ApCost);
            ClassicAssert.AreEqual(TargetType.FrontAndBack, volcano.TargetType);
            ClassicAssert.AreEqual(150, volcano.Power);
            ClassicAssert.AreEqual(90, volcano.HitRate);
            burnAilment = NestedEffects(EffectAt(volcano, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("Burn", GetString(burnAilment.Parameters, "ailment"));

            var magicMissile = repository.ActiveSkills["act_magic_missile"];
            ClassicAssert.AreEqual(1, magicMissile.ApCost);
            ClassicAssert.AreEqual(AttackType.Magic, magicMissile.AttackType);
            ClassicAssert.AreEqual(TargetType.TwoEnemies, magicMissile.TargetType);
            ClassicAssert.AreEqual(70, magicMissile.Power);
            ClassicAssert.AreEqual(100, magicMissile.HitRate);
            ClassicAssert.IsEmpty(magicMissile.Effects);

            var iceCoffin = repository.ActiveSkills["act_ice_coffin"];
            ClassicAssert.AreEqual(2, iceCoffin.ApCost);
            ClassicAssert.AreEqual(AttackType.Magic, iceCoffin.AttackType);
            ClassicAssert.AreEqual(TargetType.Row, iceCoffin.TargetType);
            ClassicAssert.AreEqual(100, iceCoffin.Power);
            ClassicAssert.AreEqual(100, iceCoffin.HitRate);
            var freezeBonus = EffectAt(iceCoffin, "ModifyDamageCalc");
            ClassicAssert.AreEqual("Freeze", GetString(freezeBonus.Parameters, "targetHasAilment"));
            ClassicAssert.AreEqual(50, GetInt(freezeBonus.Parameters, "SkillPowerBonus"));
            freezeAilment = NestedEffects(EffectAt(iceCoffin, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("Freeze", GetString(freezeAilment.Parameters, "ailment"));
        }

        private static void AssertHolyBlade(ActiveSkillData skill)
        {
            AssertHitCount(skill, 2);
            var bonus = skill.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && effect.Parameters.ContainsKey("SkillPowerBonus"));
            ClassicAssert.AreEqual(1.0d, GetDouble(bonus.Parameters, "casterHpRatioMin"), 0.0001d);
            ClassicAssert.AreEqual(25, GetInt(bonus.Parameters, "SkillPowerBonus"));

            var recover = NestedEffects(EffectAt(skill, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("RecoverPp", recover.EffectType);
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
        }

        private static void AssertWhiteKnightAndSaintKnightData(GameDataRepository repository)
        {
            var hache = repository.ActiveSkills["act_hache"];
            ClassicAssert.AreEqual(1, hache.ApCost);
            ClassicAssert.AreEqual(SkillType.Physical, hache.Type);
            ClassicAssert.AreEqual(AttackType.Melee, hache.AttackType);
            ClassicAssert.AreEqual(TargetType.SingleEnemy, hache.TargetType);
            ClassicAssert.AreEqual(100, hache.Power);
            ClassicAssert.AreEqual(100, hache.HitRate);
            var ppRecover = NestedEffects(EffectAt(hache, "OnHitEffect")).Single();
            ClassicAssert.AreEqual("RecoverPp", ppRecover.EffectType);
            ClassicAssert.AreEqual(1, GetInt(ppRecover.Parameters, "amount"));

            var rowHeal = repository.ActiveSkills["act_row_heal"];
            ClassicAssert.AreEqual(1, rowHeal.ApCost);
            ClassicAssert.AreEqual(SkillType.Heal, rowHeal.Type);
            ClassicAssert.AreEqual(AttackType.Magic, rowHeal.AttackType);
            ClassicAssert.AreEqual(TargetType.Row, rowHeal.TargetType);
            CollectionAssert.AreEqual(new[] { "HealRatio" }, rowHeal.Effects.Select(effect => effect.EffectType).ToArray());
            var healing = EffectAt(rowHeal, "HealRatio");
            ClassicAssert.AreEqual("AllTargets", GetString(healing.Parameters, "target"));
            ClassicAssert.AreEqual(0.5d, GetDouble(healing.Parameters, "ratio"), 0.0001d);
        }

        private static void AssertGriffinKnightAndRulerData(GameDataRepository repository)
        {
            var griffinSlash = repository.ActiveSkills["act_griffin_slash"];
            ClassicAssert.AreEqual(1, griffinSlash.ApCost);
            ClassicAssert.AreEqual("高空挥击", griffinSlash.Name);
            ClassicAssert.AreEqual(SkillType.Physical, griffinSlash.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, griffinSlash.AttackType);
            ClassicAssert.AreEqual(100, griffinSlash.Power);
            ClassicAssert.AreEqual(100, griffinSlash.HitRate);
            ClassicAssert.AreEqual(TargetType.Row, griffinSlash.TargetType);
            var cavalryBonus = EffectAt(griffinSlash, "ModifyDamageCalc");
            ClassicAssert.AreEqual("Cavalry", GetString(cavalryBonus.Parameters, "targetClass"));
            ClassicAssert.AreEqual(50, GetInt(cavalryBonus.Parameters, "SkillPowerBonus"));
            ClassicAssert.IsTrue(GetBool(cavalryBonus.Parameters, "CannotBeBlocked"));

            var fatalDive = repository.ActiveSkills["act_griffin_quick_action"];
            ClassicAssert.AreEqual("致命坠落", fatalDive.Name);
            ClassicAssert.AreEqual(1, fatalDive.ApCost);
            ClassicAssert.AreEqual(SkillType.Physical, fatalDive.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, fatalDive.AttackType);
            ClassicAssert.AreEqual(TargetType.FrontAndBack, fatalDive.TargetType);
            ClassicAssert.AreEqual(100, fatalDive.HitRate);
            var fixedDamage = EffectAt(fatalDive, "ModifyDamageCalc");
            ClassicAssert.AreEqual(0.5d, GetDouble(fixedDamage.Parameters, "FixedDamageFromCasterHpRatio"), 0.0001d);
            ClassicAssert.IsTrue(GetBool(fixedDamage.Parameters, "CannotCrit"));
            ClassicAssert.IsTrue(GetBool(fixedDamage.Parameters, "CannotBeBlocked"));

            var storm = repository.ActiveSkills["act_griffin_storm"];
            ClassicAssert.AreEqual("空中猛击", storm.Name);
            ClassicAssert.AreEqual(2, storm.ApCost);
            ClassicAssert.AreEqual(SkillType.Physical, storm.Type);
            ClassicAssert.AreEqual(AttackType.Ranged, storm.AttackType);
            ClassicAssert.AreEqual(TargetType.Row, storm.TargetType);
            ClassicAssert.AreEqual(150, storm.Power);
            ClassicAssert.AreEqual(100, storm.HitRate);
            var smiteCavalry = EffectAt(storm, "ModifyDamageCalc");
            ClassicAssert.AreEqual("Cavalry", GetString(smiteCavalry.Parameters, "targetClass"));
            ClassicAssert.IsTrue(GetBool(smiteCavalry.Parameters, "CannotBeBlocked"));
            var onHit = EffectAt(storm, "OnHitEffect");
            ClassicAssert.AreEqual(1.0d, GetDouble(onHit.Parameters, "targetHpBeforeRatioMin"), 0.0001d);
            var apDamage = NestedEffects(onHit).Single();
            ClassicAssert.AreEqual("ApDamage", apDamage.EffectType);
            ClassicAssert.AreEqual("Target", GetString(apDamage.Parameters, "target"));
            ClassicAssert.AreEqual(1, GetInt(apDamage.Parameters, "amount"));
        }

        private static void AssertPrimalEdge(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(50, skill.Power);
            ClassicAssert.AreEqual(50, skill.PhysicalPower.GetValueOrDefault());
            ClassicAssert.AreEqual(100, skill.MagicalPower.GetValueOrDefault());

            var recover = EffectAt(skill, "RecoverPp");
            ClassicAssert.AreEqual("Self", GetString(recover.Parameters, "target"));
            ClassicAssert.AreEqual(1, GetInt(recover.Parameters, "amount"));
            ClassicAssert.AreEqual(1.0d, GetDouble(recover.Parameters, "casterHpRatioMin"), 0.0001d);

            var counter = EffectAt(skill, "ModifyCounter");
            ClassicAssert.AreEqual("Sprite", GetString(counter.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(counter.Parameters, "delta"));
            ClassicAssert.AreEqual(1.0d, GetDouble(counter.Parameters, "casterHpRatioMin"), 0.0001d);
        }

        private static void AssertFairyHeal(ActiveSkillData skill)
        {
            var heal = EffectAt(skill, "HealRatio");
            ClassicAssert.AreEqual("AllTargets", GetString(heal.Parameters, "target"));
            ClassicAssert.AreEqual(0.5d, GetDouble(heal.Parameters, "ratio"), 0.0001d);

            var mark = EffectAt(skill, "TemporalMark");
            ClassicAssert.AreEqual("AllTargets", GetString(mark.Parameters, "target"));
            ClassicAssert.AreEqual("DamageNullify", GetString(mark.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(mark.Parameters, "count"));

            var counter = EffectAt(skill, "ModifyCounter");
            ClassicAssert.AreEqual("Sprite", GetString(counter.Parameters, "key"));
            ClassicAssert.AreEqual(1, GetInt(counter.Parameters, "delta"));
        }

        private static void AssertElementalRoar(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(70, skill.Power);
            ClassicAssert.AreEqual(70, skill.PhysicalPower.GetValueOrDefault());
            ClassicAssert.AreEqual(70, skill.MagicalPower.GetValueOrDefault());
        }

        private static void AssertFlatPowerBonus(
            ActiveSkillData skill,
            int expectedBonus,
            params (string Key, string Value)[] preservedParameters)
        {
            string serializedEffects = JsonSerializer.Serialize(skill.Effects);
            Assert.That(serializedEffects, Does.Not.Contain("SkillPowerMultiplier"));

            var bonus = skill.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && effect.Parameters.ContainsKey("SkillPowerBonus"));
            ClassicAssert.AreEqual(expectedBonus, GetInt(bonus.Parameters, "SkillPowerBonus"));

            foreach (var parameter in preservedParameters)
            {
                ClassicAssert.AreEqual(parameter.Value, GetScalarString(bonus.Parameters, parameter.Key));
            }
        }

        private static void AssertShieldBashGuaranteedStun(ActiveSkillData skill)
        {
            CollectionAssert.DoesNotContain(skill.Tags, "StunLowChance");

            var onHit = EffectAt(skill, "OnHitEffect");
            ClassicAssert.IsFalse(onHit.Parameters.ContainsKey("chance"));

            var stun = NestedEffects(onHit).Single();
            ClassicAssert.AreEqual("StatusAilment", stun.EffectType);
            ClassicAssert.AreEqual("Target", GetString(stun.Parameters, "target"));
            ClassicAssert.AreEqual("Stun", GetString(stun.Parameters, "ailment"));
        }

        private static void AssertRollingAxe(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(1, skill.ApCost);
            ClassicAssert.AreEqual(40, skill.Power);
            ClassicAssert.AreEqual(75, skill.HitRate);
            ClassicAssert.AreEqual(TargetType.Row, skill.TargetType);
            AssertHitCount(skill, 3);
            AssertOnHitDefDebuff(skill, 0.15d);
        }

        private static void AssertWideBreaker(ActiveSkillData skill)
        {
            ClassicAssert.AreEqual(2, skill.ApCost);
            ClassicAssert.AreEqual(100, skill.Power);
            ClassicAssert.AreEqual(100, skill.HitRate);
            ClassicAssert.AreEqual(TargetType.Row, skill.TargetType);
            AssertFlatPowerBonus(skill, 50, ("targetHasDebuff", "True"));
            AssertOnHitDefDebuff(skill, 0.3d);
        }

        private static void AssertOnHitDefDebuff(ActiveSkillData skill, double ratio)
        {
            var debuff = NestedEffects(EffectAt(skill, "OnHitEffect")).Single(effect => effect.EffectType == "AddDebuff");
            ClassicAssert.AreEqual("Target", GetString(debuff.Parameters, "target"));
            ClassicAssert.AreEqual("Def", GetString(debuff.Parameters, "stat"));
            ClassicAssert.AreEqual(ratio, GetDouble(debuff.Parameters, "ratio"), 0.0001d);
            ClassicAssert.AreEqual(-1, GetInt(debuff.Parameters, "turns"));
        }

        private static void AssertFlatDebuff(IEnumerable<SkillEffectData> debuffs, string stat, int amount)
        {
            var debuff = debuffs.Single(effect => GetString(effect.Parameters, "stat") == stat);
            ClassicAssert.AreEqual("Target", GetString(debuff.Parameters, "target"));
            ClassicAssert.AreEqual(amount, GetInt(debuff.Parameters, "amount"));
            ClassicAssert.IsFalse(debuff.Parameters.ContainsKey("ratio"));
            ClassicAssert.AreEqual(-1, GetInt(debuff.Parameters, "turns"));
        }

        private static void AssertFirstThreeCharacterActiveSkillSourceData(GameDataRepository repository)
        {
            var sharpSlash = repository.ActiveSkills["act_sharp_slash"];
            ClassicAssert.AreEqual(100, sharpSlash.Power);
            ClassicAssert.IsNull(sharpSlash.HitRate);
            CollectionAssert.Contains(sharpSlash.Tags, "SureHit");
            var sharpCrit = EffectAt(sharpSlash, "AddBuff");
            ClassicAssert.AreEqual("Crit", GetString(sharpCrit.Parameters, "stat"));
            ClassicAssert.AreEqual(0.5d, GetDouble(sharpCrit.Parameters, "ratio"), 0.0001d);
            ClassicAssert.IsFalse(sharpCrit.Parameters.ContainsKey("amount"));

            var pierce = repository.ActiveSkills["act_pierce"];
            ClassicAssert.AreEqual(100, pierce.Power);
            ClassicAssert.AreEqual(100, pierce.HitRate);
            AssertNestedRecover(EffectAt(pierce, "OnKillEffect"), "RecoverPp", "Self", 1);

            var meteorSlash = repository.ActiveSkills["act_meteor_slash"];
            ClassicAssert.AreEqual(20, meteorSlash.Power);
            ClassicAssert.AreEqual(100, meteorSlash.HitRate);
            AssertHitCount(meteorSlash, 9);
            var meteorCrit = EffectAt(meteorSlash, "AddBuff");
            ClassicAssert.AreEqual("Crit", GetString(meteorCrit.Parameters, "stat"));
            ClassicAssert.AreEqual(0.3d, GetDouble(meteorCrit.Parameters, "ratio"), 0.0001d);
            ClassicAssert.IsFalse(meteorCrit.Parameters.ContainsKey("amount"));

            var megaSlash = repository.ActiveSkills["act_mega_slash"];
            ClassicAssert.AreEqual(150, megaSlash.Power);
            ClassicAssert.AreEqual(100, megaSlash.HitRate);
            Assert.That(megaSlash.Effects, Is.Empty);

            var killChain = repository.ActiveSkills["act_kill_chain"];
            ClassicAssert.AreEqual(100, killChain.Power);
            ClassicAssert.AreEqual(100, killChain.HitRate);
            AssertNestedRecover(EffectAt(killChain, "OnKillEffect"), "RecoverAp", "Self", 1);

            var bastardCross = repository.ActiveSkills["act_bastard_cross"];
            ClassicAssert.AreEqual(70, bastardCross.Power);
            ClassicAssert.AreEqual(100, bastardCross.HitRate);
            AssertHitCount(bastardCross, 2);
            var hpBonus = bastardCross.Effects.Single(effect =>
                effect.EffectType == "ModifyDamageCalc"
                && effect.Parameters.ContainsKey("SkillPowerBonusFromTargetHpRatio"));
            ClassicAssert.AreEqual(60, GetInt(hpBonus.Parameters, "SkillPowerBonusFromTargetHpRatio"));

            AssertLordSlash(repository.ActiveSkills["act_lord_slash"]);

            var cavalryBane = repository.ActiveSkills["act_cavalry_bane"];
            ClassicAssert.AreEqual(100, cavalryBane.Power);
            ClassicAssert.AreEqual(100, cavalryBane.HitRate);

            var spiralBlade = repository.ActiveSkills["act_spiral_blade"];
            ClassicAssert.AreEqual(60, spiralBlade.Power);
            ClassicAssert.AreEqual(100, spiralBlade.HitRate);
            AssertHitCount(spiralBlade, 2);
            ClassicAssert.AreEqual(30, spiralBlade.UnlockLevel);
        }

        private static void AssertNestedRecover(SkillEffectData wrapper, string effectType, string target, int amount)
        {
            var recover = NestedEffects(wrapper).Single();
            ClassicAssert.AreEqual(effectType, recover.EffectType);
            ClassicAssert.AreEqual(target, GetString(recover.Parameters, "target"));
            ClassicAssert.AreEqual(amount, GetInt(recover.Parameters, "amount"));
        }

        private static void AssertBattleLongStackingBuff(
            PassiveSkillData skill,
            params (string Stat, string Mode, double Value)[] expectedBuffs)
        {
            foreach (var expected in expectedBuffs)
            {
                var buff = skill.Effects.Single(effect =>
                    effect.EffectType == "AddBuff"
                    && GetString(effect.Parameters, "stat") == expected.Stat);
                ClassicAssert.AreEqual("Self", GetString(buff.Parameters, "target"));
                if (expected.Mode == "ratio")
                {
                    ClassicAssert.AreEqual(expected.Value, GetDouble(buff.Parameters, "ratio"), 0.0001d);
                    ClassicAssert.IsFalse(buff.Parameters.ContainsKey("amount"));
                }
                else
                {
                    ClassicAssert.AreEqual((int)expected.Value, GetInt(buff.Parameters, "amount"));
                    ClassicAssert.IsFalse(buff.Parameters.ContainsKey("ratio"));
                }
                ClassicAssert.AreEqual(-1, GetInt(buff.Parameters, "turns"));
                ClassicAssert.AreEqual("Stack", GetString(buff.Parameters, "stackPolicy"));
            }
        }

        private static void AssertNestedHealRatio(SkillEffectData wrapper, string target, double ratio)
        {
            var heal = NestedEffects(wrapper).Single();
            ClassicAssert.AreEqual("HealRatio", heal.EffectType);
            ClassicAssert.AreEqual(target, GetString(heal.Parameters, "target"));
            ClassicAssert.AreEqual(ratio, GetDouble(heal.Parameters, "ratio"), 0.0001d);
        }

        private static (int CasterGained, int TargetRemaining) RunStealPostEffects(
            GameDataRepository repository,
            ActiveSkillData skill,
            string resource,
            bool firstHitBlocked)
        {
            var caster = CreateUnit(repository, "thief", true, 1, skill.Id, hp: 500);
            var target = CreateUnit(repository, "target", false, 1, null, hp: 500);
            caster.CurrentAp = 0;
            caster.CurrentPp = 0;
            target.CurrentAp = resource == "AP" ? 3 : 0;
            target.CurrentPp = resource == "PP" ? 3 : 0;
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster },
                EnemyUnits = new List<BattleUnit> { target }
            };
            var calculation = new DamageCalculation
            {
                Attacker = caster,
                Defender = target,
                Skill = new ActiveSkill(skill, repository),
                HitCount = GetInt(EffectAt(skill, "ModifyDamageCalc").Parameters, "HitCount")
            };
            var result = new DamageResult(
                physicalDamage: 10,
                magicalDamage: 0,
                isHit: true,
                isCritical: false,
                isBlocked: true,
                isEvaded: false,
                appliedAilments: new List<StatusAilment>(),
                resolvedDefender: target,
                hitResults: new List<DamageHitResult>
                {
                    new() { HitIndex = 1, Landed = true, Blocked = firstHitBlocked },
                    new() { HitIndex = 2, Landed = true, Blocked = !firstHitBlocked }
                });

            _ = new SkillEffectExecutor().ExecutePostDamageEffects(
                context,
                caster,
                target,
                skill.Effects,
                skill.Id,
                calculation,
                result,
                killed: false);

            return resource == "PP"
                ? (caster.CurrentPp, target.CurrentPp)
                : (caster.CurrentAp, target.CurrentAp);
        }

        private static (BattleUnit Caster, List<string> Logs) RunPrimalEdgeActionEffects(int currentHp)
        {
            const string skillId = "act_primal_edge";
            var repository = LoadRepository();
            var skill = repository.ActiveSkills[skillId];
            var caster = CreateUnit(repository, "sibyl", true, 1, skillId, hp: 100, str: 100, spd: 100);
            caster.CurrentHp = currentHp;
            caster.CurrentPp = 0;
            var context = new BattleContext(repository)
            {
                PlayerUnits = new List<BattleUnit> { caster }
            };
            var logs = new SkillEffectExecutor().ExecuteActionEffects(
                context,
                caster,
                Array.Empty<BattleUnit>(),
                skill.Effects,
                skill.Id);

            return (caster, logs);
        }

        private static void AssertDamageNullify(BattleUnit unit, string sourceSkillId)
        {
            Assert.That(unit.TemporalStates, Has.Some.Matches<TemporalState>(state =>
                state.Key == "DamageNullify"
                && state.RemainingCount == 1
                && state.SourceSkillId == sourceSkillId));
        }

        private static void AssertActionCompleted(SingleActionResult result)
        {
            ClassicAssert.IsTrue(
                result == SingleActionResult.ActionDone || result == SingleActionResult.PlayerWin,
                $"Unexpected action result: {result}");
        }

        private static SkillEffectData EffectAt(ActiveSkillData skill, string effectType, int occurrence = 0)
        {
            return skill.Effects
                .Where(effect => effect.EffectType == effectType)
                .ElementAt(occurrence);
        }

        private static List<SkillEffectData> NestedEffects(SkillEffectData effect)
        {
            var json = ((JsonElement)effect.Parameters["effects"]).GetRawText();
            return JsonSerializer.Deserialize<List<SkillEffectData>>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to read nested effects for {effect.EffectType}");
        }

        private static GameDataRepository LoadRepository()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            return repository;
        }

        private static BattleUnit CreateUnit(
            GameDataRepository repository,
            string id,
            bool isPlayer,
            int position,
            string? skillId,
            int hp = 500,
            int str = 120,
            int def = 100,
            int mag = 20,
            int mdef = 20,
            int spd = 20)
        {
            var data = new CharacterData
            {
                Id = id,
                Name = id,
                Classes = new List<UnitClass> { UnitClass.Infantry },
                InnateActiveSkillIds = string.IsNullOrWhiteSpace(skillId)
                    ? new List<string>()
                    : new List<string> { skillId },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp },
                    { "Str", str },
                    { "Def", def },
                    { "Mag", mag },
                    { "MDef", mdef },
                    { "Hit", 1000 },
                    { "Eva", 0 },
                    { "Crit", 0 },
                    { "Block", 0 },
                    { "Spd", spd },
                    { "AP", string.IsNullOrWhiteSpace(skillId) ? 0 : 3 },
                    { "PP", 4 }
                }
            };

            return new BattleUnit(data, repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 99
            };
        }

        private static string GetString(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetString() ?? string.Empty,
                string value => value,
                object value => value.ToString() ?? string.Empty
            };
        }

        private static string GetScalarString(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var raw))
                return string.Empty;

            return raw switch
            {
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
                JsonElement { ValueKind: JsonValueKind.True } => "True",
                JsonElement { ValueKind: JsonValueKind.False } => "False",
                JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble().ToString("0.########"),
                object value => value.ToString() ?? string.Empty
            };
        }

        private static int GetInt(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.ValueKind == JsonValueKind.String
                    ? int.Parse(element.GetString() ?? "0")
                    : element.GetInt32(),
                int value => value,
                long value => (int)value,
                double value => (int)value,
                string value => int.Parse(value),
                object value => Convert.ToInt32(value)
            };
        }

        private static double GetDouble(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetDouble(),
                double value => value,
                float value => value,
                int value => value,
                string value => double.Parse(value),
                object value => Convert.ToDouble(value)
            };
        }

        private static bool GetBool(Dictionary<string, object> parameters, string key)
        {
            return parameters[key] switch
            {
                JsonElement element => element.GetBoolean(),
                bool value => value,
                string value => bool.Parse(value),
                object value => Convert.ToBoolean(value)
            };
        }
    }
}
