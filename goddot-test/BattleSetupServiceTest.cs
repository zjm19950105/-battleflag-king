using BattleKing.Core;
using BattleKing.Data;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;

namespace BattleKing.Tests
{
    [TestFixture]
    public class BattleSetupServiceTest
    {
        private static string DataPath => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "goddot",
            "data"));

        [Test]
        public void CreateUnit_AppliesInitialEquipmentDefaultStrategiesAndAutoPassives()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var unit = service.CreateUnit("swordsman", isPlayer: true, position: 2, day: 1, isCc: false);

            ClassicAssert.AreEqual("swordsman", unit.Data.Id);
            ClassicAssert.IsTrue(unit.IsPlayer);
            ClassicAssert.AreEqual(2, unit.Position);
            ClassicAssert.AreEqual(1, unit.CurrentLevel);
            ClassicAssert.IsFalse(unit.IsCc);
            ClassicAssert.AreEqual("equ_recruit_sword", unit.Equipment.MainHand?.Data.Id);
            ClassicAssert.GreaterOrEqual(unit.Strategies.Count, 1);
            ClassicAssert.AreEqual("act_sharp_slash", unit.Strategies[0].SkillId);
            CollectionAssert.DoesNotContain(unit.Strategies.Select(strategy => strategy.SkillId).ToList(), "act_meteor_slash");
            CollectionAssert.IsSubsetOf(
                unit.Strategies.Select(strategy => strategy.SkillId).Distinct().ToList(),
                unit.GetAvailableActiveSkillIds());
            ClassicAssert.IsNotNull(unit.Strategies[0].Condition1);
            ClassicAssert.IsNull(unit.Strategies[0].Condition2);
            CollectionAssert.AreEqual(new[] { "pas_quick_strike" }, unit.EquippedPassiveSkillIds);
            ClassicAssert.AreEqual(1, unit.PassiveStrategies.Count);
            ClassicAssert.AreEqual("pas_quick_strike", unit.PassiveStrategies[0].SkillId);
            ClassicAssert.IsNull(unit.PassiveStrategies[0].Condition1);
            ClassicAssert.IsNull(unit.PassiveStrategies[0].Condition2);
        }

        [Test]
        public void CreateUnit_FirstThreeCharactersApplyCcOnlyWeaponSlots()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var swordsman = service.CreateUnit("swordsman", isPlayer: true, position: 1, day: 6, isCc: true);
            ClassicAssert.AreEqual("equ_recruit_sword", swordsman.Equipment.MainHand?.Data.Id);
            ClassicAssert.AreEqual("equ_recruit_sword", swordsman.Equipment.OffHand?.Data.Id);
            ClassicAssert.AreEqual(61, swordsman.GetCurrentStat("Str"));

            var mercenary = service.CreateUnit("mercenary", isPlayer: true, position: 1, day: 6, isCc: false);
            ClassicAssert.AreEqual("equ_recruit_sword", mercenary.Equipment.MainHand?.Data.Id);
            ClassicAssert.IsNull(mercenary.Equipment.OffHand);
            CollectionAssert.DoesNotContain(mercenary.GetAvailablePassiveSkillIds(), "pas_vengeance_guard");

            var landsknecht = service.CreateUnit("mercenary", isPlayer: true, position: 1, day: 6, isCc: true);
            ClassicAssert.AreEqual("equ_recruit_sword", landsknecht.Equipment.MainHand?.Data.Id);
            ClassicAssert.AreEqual("equ_mercenary_wood_shield", landsknecht.Equipment.OffHand?.Data.Id);
            CollectionAssert.Contains(landsknecht.GetAvailablePassiveSkillIds(), "pas_vengeance_guard");

            var highLord = service.CreateUnit("lord", isPlayer: true, position: 1, day: 6, isCc: true);
            ClassicAssert.AreEqual("equ_lord_sword", highLord.Equipment.MainHand?.Data.Id);
            ClassicAssert.AreEqual("equ_lord_shield", highLord.Equipment.OffHand?.Data.Id);

            var shieldShooter = service.CreateUnit("shooter", isPlayer: true, position: 1, day: 6, isCc: true);
            ClassicAssert.AreEqual("equ_shooter_bow", shieldShooter.Equipment.MainHand?.Data.Id);
            ClassicAssert.AreEqual("equ_shooter_greatshield", shieldShooter.Equipment.OffHand?.Data.Id);
        }

        [Test]
        public void CharacterDefaultPresetsIncludeCcCoreActiveSkills()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var swordmaster = service.CreateUnit("swordsman", isPlayer: true, position: 1, day: 6, isCc: true);
            service.ApplyStrategyPreset(swordmaster, "char_default_swordsman");
            ClassicAssert.AreEqual("act_meteor_slash", swordmaster.Strategies.First().SkillId);

            var landsknecht = service.CreateUnit("mercenary", isPlayer: true, position: 1, day: 6, isCc: true);
            service.ApplyStrategyPreset(landsknecht, "char_default_mercenary");
            ClassicAssert.AreEqual("act_bastard_cross", landsknecht.Strategies.First().SkillId);

            var highLord = service.CreateUnit("lord", isPlayer: true, position: 1, day: 6, isCc: true);
            service.ApplyStrategyPreset(highLord, "char_default_lord");
            CollectionAssert.Contains(highLord.Strategies.Select(strategy => strategy.SkillId).ToList(), "act_spiral_blade");

            var hoplite = service.CreateUnit("hoplite", isPlayer: true, position: 1, day: 6, isCc: true);
            var hopliteSkillIds = hoplite.Strategies.Select(strategy => strategy.SkillId).ToList();
            CollectionAssert.Contains(hopliteSkillIds, "act_great_shield");
            CollectionAssert.Contains(hopliteSkillIds, "act_line_defense");

            var berserker = service.CreateUnit("gladiator", isPlayer: true, position: 1, day: 6, isCc: true);
            CollectionAssert.Contains(berserker.Strategies.Select(strategy => strategy.SkillId).ToList(), "act_accumulate");

            var breaker = service.CreateUnit("warrior", isPlayer: true, position: 1, day: 6, isCc: true);
            CollectionAssert.Contains(breaker.Strategies.Select(strategy => strategy.SkillId).ToList(), "act_line_destruction");
        }

        [Test]
        public void EquipmentPpBonus_UpdatesPassiveBudgetForTwoPpPassive()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);
            var unit = service.CreateUnit("fighter", isPlayer: true, position: 1, day: 6, isCc: true);
            unit.EquippedPassiveSkillIds.Clear();
            unit.PassiveStrategies.Clear();

            int previousMaxHp = unit.GetCurrentStat("HP");
            unit.Equipment.EquipToSlot("Accessory1", repository.Equipments["equ_pp_crystal_pendant"]);
            unit.SyncResourceCapsFromStats(previousMaxHp);

            ClassicAssert.GreaterOrEqual(unit.PassivePpBudget, 3);
            ClassicAssert.AreEqual(BattleUnit.ResourceCap, unit.MaxPp);
            ClassicAssert.AreEqual(unit.PassivePpBudget, unit.CurrentPp);
            ClassicAssert.IsTrue(unit.CanEquipPassive("pas_hundred_crit"));
        }

        [Test]
        public void FighterSoldierHuskarl_InitialEquipmentMatchesDeclaredSlots()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var fighter = service.CreateUnit("fighter", isPlayer: true, position: 1, day: 1);
            ClassicAssert.AreEqual(EquipmentCategory.Sword, fighter.Equipment.MainHand?.Data.Category);
            ClassicAssert.AreEqual(EquipmentCategory.Shield, fighter.Equipment.OffHand?.Data.Category);

            var soldier = service.CreateUnit("soldier", isPlayer: true, position: 1, day: 1);
            ClassicAssert.AreEqual(EquipmentCategory.Spear, soldier.Equipment.MainHand?.Data.Category);
            ClassicAssert.IsNull(soldier.Equipment.OffHand);

            var huskarl = service.CreateUnit("huskarl", isPlayer: true, position: 1, day: 1);
            ClassicAssert.AreEqual(EquipmentCategory.Axe, huskarl.Equipment.MainHand?.Data.Category);
            ClassicAssert.IsNull(huskarl.Equipment.OffHand);

            var viking = service.CreateUnit("huskarl", isPlayer: true, position: 1, day: 6, isCc: true);
            ClassicAssert.AreEqual(EquipmentCategory.Axe, viking.Equipment.MainHand?.Data.Category);
            ClassicAssert.AreEqual(EquipmentCategory.Shield, viking.Equipment.OffHand?.Data.Category);
        }

        [Test]
        public void FighterSoldierHuskarl_DefaultPresetsIncludeCcCoreSkillsFirst()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);

            ClassicAssert.AreEqual("act_attract_attention", repository.GetStrategyPreset("char_default_fighter").Strategies.First().SkillId);
            ClassicAssert.AreEqual("act_throwing_spear", repository.GetStrategyPreset("char_default_soldier").Strategies.First().SkillId);
            ClassicAssert.AreEqual("act_break_formation", repository.GetStrategyPreset("char_default_huskarl").Strategies.First().SkillId);
        }

        [Test]
        public void GladiatorAndBerserker_UseResearchConfirmedSkillPoolsAndLevels()
        {
            var repository = new GameDataRepository();
            repository.LoadAll(DataPath);
            var service = new BattleSetupService(repository);

            var gladiator = service.CreateUnit("gladiator", isPlayer: true, position: 1, day: 3, isCc: false);
            var berserker = service.CreateUnit("gladiator", isPlayer: true, position: 1, day: 6, isCc: true);

            CollectionAssert.Contains(gladiator.GetAvailableActiveSkillIds(), "act_full_assault");
            CollectionAssert.DoesNotContain(gladiator.GetAvailableActiveSkillIds(), "act_formation_breaker");
            CollectionAssert.DoesNotContain(gladiator.GetAvailableActiveSkillIds(), "act_accumulate");
            CollectionAssert.Contains(gladiator.GetAvailablePassiveSkillIds(), "pas_wide_counter");
            CollectionAssert.DoesNotContain(gladiator.GetAvailablePassiveSkillIds(), "pas_bounce");
            ClassicAssert.AreEqual("pas_wide_counter", gladiator.PassiveStrategies.Single().SkillId);

            CollectionAssert.Contains(berserker.GetAvailableActiveSkillIds(), "act_formation_breaker");
            CollectionAssert.Contains(berserker.GetAvailableActiveSkillIds(), "act_accumulate");
            CollectionAssert.Contains(berserker.GetAvailablePassiveSkillIds(), "pas_bounce");
            CollectionAssert.Contains(berserker.GetAvailablePassiveSkillIds(), "pas_berserk");
        }
    }
}
