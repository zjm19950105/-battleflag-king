using System;
using System.Collections.Generic;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Tests
{
    public sealed class GoldenBattleFixture
    {
        public const string MeleeStrikeId = "gold_melee_strike";
        public const string ArrowShotId = "gold_arrow_shot";
        public const string RowSpellId = "gold_row_spell";
        public const string ColumnSpellId = "gold_column_spell";
        public const string HealId = "gold_heal";
        public const string WaitId = "gold_wait";
        public const string CounterPassiveId = "gold_counter";
        public const string CoverPassiveId = "gold_cover";
        public const string BattleEndPassiveId = "gold_battle_end_ap";
        public const string BattleEndAttackPassiveId = "gold_battle_end_attack";
        public const string BattleEndHealPassiveId = "gold_battle_end_heal";
        public const string BattleEndApPassiveId = BattleEndPassiveId;

        public GameDataRepository Repository { get; }
        public BattleUnit Melee { get; }
        public BattleUnit Archer { get; }
        public BattleUnit Flyer { get; }
        public BattleUnit Heavy { get; }
        public BattleUnit Mage { get; }
        public BattleUnit Healer { get; }
        public BattleUnit CounterGuard { get; }
        public BattleUnit CoverGuard { get; }
        public BattleUnit BattleEndAttacker { get; }
        public BattleUnit BattleEndHealer { get; }

        private GoldenBattleFixture(GameDataRepository repository)
        {
            Repository = repository;
            Melee = CreateUnit("gold_melee", "Gold Melee", true, 1, new() { UnitClass.Infantry }, MeleeStrikeId, hp: 240, str: 70, def: 20, mag: 0, mdef: 10, spd: 60, ap: 3, pp: 0);
            Archer = CreateUnit("gold_archer", "Gold Archer", true, 2, new() { UnitClass.Archer }, ArrowShotId, hp: 180, str: 55, def: 10, mag: 0, mdef: 10, spd: 50, ap: 3, pp: 0);
            Flyer = CreateUnit("gold_flyer", "Gold Flyer", true, 3, new() { UnitClass.Flying }, MeleeStrikeId, hp: 170, str: 60, def: 12, mag: 0, mdef: 10, spd: 70, ap: 3, pp: 0);
            Heavy = CreateUnit("gold_heavy", "Gold Heavy", true, 1, new() { UnitClass.Heavy }, WaitId, hp: 320, str: 35, def: 50, mag: 0, mdef: 20, spd: 20, ap: 3, pp: 0);
            Mage = CreateUnit("gold_mage", "Gold Mage", true, 4, new() { UnitClass.Mage }, RowSpellId, hp: 160, str: 10, def: 8, mag: 70, mdef: 20, spd: 45, ap: 3, pp: 0);
            Healer = CreateUnit("gold_healer", "Gold Healer", true, 5, new() { UnitClass.Mage }, HealId, hp: 170, str: 10, def: 10, mag: 50, mdef: 30, spd: 40, ap: 3, pp: 0);
            CounterGuard = CreateUnit("gold_counter_guard", "Gold Counter Guard", false, 1, new() { UnitClass.Infantry }, WaitId, hp: 260, str: 60, def: 15, mag: 0, mdef: 10, spd: 30, ap: 3, pp: 1);
            CoverGuard = CreateUnit("gold_cover_guard", "Gold Cover Guard", false, 2, new() { UnitClass.Heavy }, WaitId, hp: 320, str: 40, def: 35, mag: 0, mdef: 15, spd: 25, ap: 3, pp: 1);
            BattleEndAttacker = CreateUnit("gold_battle_end_attacker", "Gold Battle End Attacker", true, 1, new() { UnitClass.Infantry }, WaitId, hp: 100, str: 120, def: 0, mag: 0, mdef: 0, spd: 30, ap: 1, pp: 0);
            BattleEndHealer = CreateUnit("gold_battle_end_healer", "Gold Battle End Healer", true, 1, new() { UnitClass.Mage }, WaitId, hp: 100, str: 10, def: 0, mag: 0, mdef: 0, spd: 30, ap: 1, pp: 0);
        }

        public static GoldenBattleFixture Create()
        {
            var repository = CreateRepository();
            return new GoldenBattleFixture(repository);
        }

        public BattleContext Context(
            IEnumerable<BattleUnit>? players = null,
            IEnumerable<BattleUnit>? enemies = null)
        {
            return new BattleContext(Repository)
            {
                PlayerUnits = new List<BattleUnit>(players ?? new[] { Melee, Archer, Flyer, Mage, Healer }),
                EnemyUnits = new List<BattleUnit>(enemies ?? new[] { CounterGuard, CoverGuard })
            };
        }

        public BattleUnit Enemy(
            string id,
            int position,
            List<UnitClass>? classes = null,
            int hp = 220,
            int str = 40,
            int def = 10,
            int spd = 20,
            string skillId = WaitId)
        {
            return CreateUnit(id, id, false, position, classes ?? new() { UnitClass.Infantry }, skillId, hp, str, def, 0, 10, spd, ap: 3, pp: 0);
        }

        private BattleUnit CreateUnit(
            string id,
            string name,
            bool isPlayer,
            int position,
            List<UnitClass> classes,
            string activeSkillId,
            int hp,
            int str,
            int def,
            int mag,
            int mdef,
            int spd,
            int ap,
            int pp)
        {
            var unit = new BattleUnit(new CharacterData
            {
                Id = id,
                Name = name,
                Classes = classes,
                InnateActiveSkillIds = new List<string> { activeSkillId },
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
                    { "AP", ap },
                    { "PP", pp }
                }
            }, Repository, isPlayer)
            {
                Position = position,
                CurrentLevel = 20
            };
            unit.Strategies.Add(new Strategy { SkillId = activeSkillId });
            return unit;
        }

        private static GameDataRepository CreateRepository()
        {
            var repository = new GameDataRepository();
            SetRepositoryProperty(repository, nameof(GameDataRepository.Characters), new Dictionary<string, CharacterData>());
            SetRepositoryProperty(repository, nameof(GameDataRepository.ActiveSkills), CreateActiveSkills());
            SetRepositoryProperty(repository, nameof(GameDataRepository.PassiveSkills), CreatePassiveSkills());
            SetRepositoryProperty(repository, nameof(GameDataRepository.Equipments), new Dictionary<string, EquipmentData>());
            SetRepositoryProperty(repository, nameof(GameDataRepository.EnemyFormations), new Dictionary<string, EnemyFormationData>());
            SetRepositoryProperty(repository, nameof(GameDataRepository.StrategyPresets), new Dictionary<string, StrategyPresetData>());
            SetRepositoryProperty(repository, nameof(GameDataRepository.ClassDisplayNames), new Dictionary<string, string>());
            SetRepositoryProperty(repository, nameof(GameDataRepository.CharacterRoleDescriptions), new Dictionary<string, CharacterRoleDescriptionData>());
            return repository;
        }

        private static Dictionary<string, ActiveSkillData> CreateActiveSkills()
        {
            return new()
            {
                [MeleeStrikeId] = Skill(MeleeStrikeId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy, power: 100),
                [ArrowShotId] = Skill(ArrowShotId, SkillType.Physical, AttackType.Ranged, TargetType.SingleEnemy, power: 90),
                [RowSpellId] = Skill(RowSpellId, SkillType.Magical, AttackType.Magic, TargetType.Row, power: 80),
                [ColumnSpellId] = Skill(ColumnSpellId, SkillType.Magical, AttackType.Magic, TargetType.Column, power: 80),
                [HealId] = Skill(HealId, SkillType.Heal, AttackType.Magic, TargetType.SingleAlly, power: 0, effects: new()
                {
                    new SkillEffectData
                    {
                        EffectType = "HealRatio",
                        Parameters = new() { { "target", "Target" }, { "ratio", 0.25 } }
                    }
                }),
                [WaitId] = Skill(WaitId, SkillType.Physical, AttackType.Melee, TargetType.SingleEnemy, power: 1, apCost: 0)
            };
        }

        private static Dictionary<string, PassiveSkillData> CreatePassiveSkills()
        {
            return new()
            {
                [CounterPassiveId] = new PassiveSkillData
                {
                    Id = CounterPassiveId,
                    Name = "Golden Counter",
                    PpCost = 0,
                    TriggerTiming = PassiveTriggerTiming.OnBeingHit,
                    Effects = new List<SkillEffectData>
                    {
                        new()
                        {
                            EffectType = "CounterAttack",
                            Parameters = new()
                            {
                                { "power", 75 },
                                { "hitRate", 100 },
                                { "attackType", "Melee" },
                                { "damageType", "Physical" }
                            }
                        }
                    }
                },
                [CoverPassiveId] = new PassiveSkillData
                {
                    Id = CoverPassiveId,
                    Name = "Golden Cover",
                    PpCost = 0,
                    TriggerTiming = PassiveTriggerTiming.AllyBeforeHit,
                    Effects = new List<SkillEffectData>
                    {
                        new() { EffectType = "CoverAlly", Parameters = new() }
                    }
                },
                [BattleEndPassiveId] = new PassiveSkillData
                {
                    Id = BattleEndPassiveId,
                    Name = "Golden Battle End AP",
                    PpCost = 0,
                    TriggerTiming = PassiveTriggerTiming.BattleEnd,
                    Effects = new List<SkillEffectData>
                    {
                        new()
                        {
                            EffectType = "RecoverAp",
                            Parameters = new() { { "target", "Self" }, { "amount", 1 } }
                        }
                    }
                },
                [BattleEndAttackPassiveId] = new PassiveSkillData
                {
                    Id = BattleEndAttackPassiveId,
                    Name = "Golden Battle End Attack",
                    PpCost = 0,
                    TriggerTiming = PassiveTriggerTiming.BattleEnd,
                    Effects = new List<SkillEffectData>
                    {
                        new()
                        {
                            EffectType = "BattleEndAttack",
                            Parameters = new()
                            {
                                { "target", "AllEnemies" },
                                { "power", 100 },
                                { "hitRate", 1000 },
                                { "damageType", "Physical" },
                                { "attackType", "Melee" },
                                { "targetType", "SingleEnemy" },
                                { "tags", new List<string> { "CannotBeBlocked" } }
                            }
                        }
                    }
                },
                [BattleEndHealPassiveId] = new PassiveSkillData
                {
                    Id = BattleEndHealPassiveId,
                    Name = "Golden Battle End Heal",
                    PpCost = 0,
                    TriggerTiming = PassiveTriggerTiming.BattleEnd,
                    Effects = new List<SkillEffectData>
                    {
                        new()
                        {
                            EffectType = "RecoverHp",
                            Parameters = new()
                            {
                                { "target", "Self" },
                                { "amount", 30 }
                            }
                        }
                    }
                }
            };
        }

        private static ActiveSkillData Skill(
            string id,
            SkillType type,
            AttackType attackType,
            TargetType targetType,
            int power,
            int apCost = 1,
            List<SkillEffectData>? effects = null)
        {
            return new ActiveSkillData
            {
                Id = id,
                Name = id,
                ApCost = apCost,
                Type = type,
                AttackType = attackType,
                Power = power,
                HitRate = 100,
                TargetType = targetType,
                Effects = effects ?? new List<SkillEffectData>()
            };
        }

        private static void SetRepositoryProperty<T>(GameDataRepository repository, string propertyName, T value)
        {
            var property = typeof(GameDataRepository).GetProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"Missing repository property {propertyName}");
            property.SetValue(repository, value);
        }
    }
}
