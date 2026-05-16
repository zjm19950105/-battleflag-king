using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Tests
{
    /// <summary>Creates minimal test data for unit tests — no JSON loading required.</summary>
    public static class TestDataFactory
    {
        public static BattleUnit CreateUnit(
            int hp = 100, int str = 50, int def = 30, int mag = 20, int mdef = 20,
            int hit = 100, int eva = 0, int crit = 5, int block = 3, int spd = 20,
            int ap = 3, int pp = 2, bool isPlayer = true, List<UnitClass> classes = null)
        {
            var data = new CharacterData
            {
                Id = "test_unit",
                Name = "TestUnit",
                Classes = classes ?? new List<UnitClass> { UnitClass.Infantry },
                BaseStats = new Dictionary<string, int>
                {
                    { "HP", hp }, { "Str", str }, { "Def", def },
                    { "Mag", mag }, { "MDef", mdef },
                    { "Hit", hit }, { "Eva", eva }, { "Crit", crit }, { "Block", block },
                    { "Spd", spd }, { "AP", ap }, { "PP", pp }
                }
            };
            return new BattleUnit(data, null!, isPlayer);
        }

        public static ActiveSkill CreateSkill(
            int power = 100, SkillType type = SkillType.Physical,
            AttackType attackType = AttackType.Melee, TargetType targetType = TargetType.SingleEnemy,
            int? hitRate = 100, int apCost = 1, string name = "TestSkill",
            List<string> tags = null, List<SkillEffectData> effects = null,
            int? physicalPower = null, int? magicalPower = null,
            SkillType? damageType = null)
        {
            var data = new ActiveSkillData
            {
                Id = "test_skill",
                Name = name,
                ApCost = apCost,
                Type = type,
                DamageType = damageType,
                AttackType = attackType,
                Power = power,
                PhysicalPower = physicalPower,
                MagicalPower = magicalPower,
                HitRate = hitRate,
                TargetType = targetType,
                EffectDescription = "Test skill",
                Tags = tags ?? new List<string>(),
                Effects = effects ?? new List<SkillEffectData>()
            };
            return new ActiveSkill(data, null!);
        }

        public static PassiveSkillData CreatePassiveData(
            string id = "test_passive", string name = "TestPassive",
            int ppCost = 1, PassiveTriggerTiming timing = PassiveTriggerTiming.BattleStart,
            SkillType type = SkillType.Physical, int? power = null, int? hitRate = null,
            bool hasSimultaneousLimit = false, List<SkillEffectData> effects = null)
        {
            return new PassiveSkillData
            {
                Id = id, Name = name, PpCost = ppCost,
                TriggerTiming = timing, Type = type,
                Power = power, HitRate = hitRate,
                EffectDescription = "Test passive",
                Tags = new List<string>(),
                Effects = effects ?? new List<SkillEffectData>(),
                HasSimultaneousLimit = hasSimultaneousLimit
            };
        }

        public static DamageCalculation CreateCalc(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            return new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill,
                HitCount = 1
            };
        }

        public static EquipmentData CreateEquipment(
            string id = "test_equip", string name = "TestEquip",
            EquipmentCategory category = EquipmentCategory.Sword,
            Dictionary<string, int> stats = null)
        {
            return new EquipmentData
            {
                Id = id, Name = name, Category = category,
                BaseStats = stats ?? new Dictionary<string, int>()
            };
        }
    }
}
