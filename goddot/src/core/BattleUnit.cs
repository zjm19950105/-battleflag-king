using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Equipment;
using BattleKing.Skills;

namespace BattleKing.Core
{
    public class BattleUnit
    {
        public const int ResourceCap = 4;

        // Read-only template reference
        public CharacterData Data { get; private set; }
        public GameDataRepository GameData { get; private set; }

        // Runtime mutable state
        public int CurrentHp { get; set; }
        private int _currentAp;
        private int _currentPp;
        public int CurrentAp
        {
            get => _currentAp;
            set => _currentAp = ClampResource(value);
        }
        public int CurrentPp
        {
            get => _currentPp;
            set => _currentPp = ClampResource(value);
        }
        public int MaxAp { get; private set; } = ResourceCap;
        public int MaxPp { get; private set; } = ResourceCap;
        public int InitialAp { get; private set; }
        public int InitialPp { get; private set; }
        public int PassivePpBudget => InitialPp;
        public int Position { get; set; }
        public bool IsFrontRow => Position <= 3;
        public bool IsAlive => CurrentHp > 0;
        public UnitState State { get; set; } = UnitState.Normal;
        public int ActionCount { get; set; } = 0;
        public int ConsecutiveWaitCount { get; set; } = 0;
        public bool IsPlayer { get; set; }
        public bool IsCc { get; private set; }
        public int CurrentLevel { get; set; } = 1;
        public int ActionOrderPriority { get; set; } = 0;
        public string ChargedSkillId { get; set; } = null;  // Module 6: Charge skill tracking  // 天数系统映射的等效等级

        public void SetCcState(bool isCc)
        {
            IsCc = isCc;
        }

        // Runtime collections
        public EquipmentSlot Equipment { get; private set; } = new EquipmentSlot();
        public List<Buff> Buffs { get; private set; } = new List<Buff>();
        public List<StatusAilment> Ailments { get; private set; } = new List<StatusAilment>();
        public List<Strategy> Strategies { get; set; } = new List<Strategy>();
        public List<PassiveStrategy> PassiveStrategies { get; set; } = new List<PassiveStrategy>();
        public List<string> EquippedPassiveSkillIds { get; set; } = new List<string>();
        public List<string> TemporaryActiveSkillIds { get; private set; } = new List<string>();
        public List<string> TemporaryPassiveSkillIds { get; private set; } = new List<string>();

        /// <summary>Per-passive trigger conditions (skillId → Condition). Checked by PassiveSkillProcessor before triggering.</summary>
        public Dictionary<string, Condition> PassiveConditions { get; set; } = new Dictionary<string, Condition>();

        // Module 3: Temporal states (1-time immunities, limited-duration effects)
        public List<TemporalState> TemporalStates { get; private set; } = new List<TemporalState>();

        // Module 4: Custom counters (generic key-value for sprite/rage/combo etc.)
        public Dictionary<string, int> CustomCounters { get; private set; } = new Dictionary<string, int>();

        public BattleUnit(CharacterData data, GameDataRepository gameData, bool isPlayer, bool isCc = false)
        {
            Data = data;
            GameData = gameData;
            IsPlayer = isPlayer;
            IsCc = isCc;
            CurrentHp = data.BaseStats.GetValueOrDefault("HP", 1);
            InitialAp = ClampResource(data.BaseStats.GetValueOrDefault("AP", ResourceCap));
            InitialPp = ClampResource(data.BaseStats.GetValueOrDefault("PP", 0));
            CurrentAp = InitialAp;
            CurrentPp = InitialPp;
            MaxAp = ResourceCap;
            MaxPp = ResourceCap;
        }

        /// <summary>Effective unit classes based on CC state (e.g. Lord→Cavalry after CC)</summary>
        public List<UnitClass> GetEffectiveClasses()
        {
            return IsCc && Data.CcClasses != null && Data.CcClasses.Count > 0
                ? Data.CcClasses
                : Data.Classes;
        }

        /// <summary>Effective traits based on CC state</summary>
        public List<TraitData> GetEffectiveTraits()
        {
            var traits = new List<TraitData>(Data.Traits ?? new());
            if (IsCc && Data.CcTraits != null)
                traits.AddRange(Data.CcTraits);
            return traits;
        }

        // CC-aware equippable categories
        public List<EquipmentCategory> GetEquippableCategories()
        {
            if (IsCc && Data.CcEquippableCategories != null && Data.CcEquippableCategories.Count > 0)
                return Data.CcEquippableCategories;
            return Data.EquippableCategories;
        }

        public readonly struct StatBreakdown
        {
            public StatBreakdown(string statName, int baseValue, int equipmentValue, int buffDelta, int current)
            {
                StatName = statName;
                BaseValue = baseValue;
                EquipmentValue = equipmentValue;
                BuffDelta = buffDelta;
                Current = current;
            }

            public string StatName { get; }
            public int BaseValue { get; }
            public int EquipmentValue { get; }
            public int EquippedBaseline => BaseValue + EquipmentValue;
            public int BuffDelta { get; }
            public int Current { get; }
        }

        // Dynamic stat calculation (base + equipment + buff)
        public int GetCurrentStat(string statName)
        {
            return GetStatBreakdown(statName).Current;
        }

        public int GetCurrentStat(string statName, EquipmentSlot equipment)
        {
            return GetStatBreakdown(statName, equipment).Current;
        }

        public StatBreakdown GetStatBreakdown(string statName)
        {
            return GetStatBreakdown(statName, Equipment);
        }

        public StatBreakdown GetStatBreakdown(string statName, EquipmentSlot equipment)
        {
            int baseValue = Data.BaseStats.GetValueOrDefault(statName, 0);
            int equipValue = GetDisplayEquipmentStat(equipment ?? Equipment, statName);
            float buffRatio = Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
            int flatBuff = Buffs.Where(b => b.TargetStat == statName).Sum(b => b.FlatAmount);
            int value = (int)((baseValue + equipValue) * (1 + buffRatio)) + flatBuff;
            int current = ClampCalculatedStat(statName, value);
            return new StatBreakdown(statName, baseValue, equipValue, current - (baseValue + equipValue), current);
        }

        public int GetCurrentAttackPower(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "Mag" : "Str";
            return GetCurrentCombatStat(stat);
        }

        public int GetCurrentDefense(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "MDef" : "Def";
            return GetCurrentCombatStat(stat);
        }

        private int GetCurrentCombatStat(string statName)
        {
            return GetStatBreakdown(statName).Current;
        }

        private static int GetDisplayEquipmentStat(EquipmentSlot equipment, string statName)
        {
            int value = equipment?.GetTotalStat(statName) ?? 0;
            value += statName switch
            {
                "Str" => equipment?.GetTotalStat("phys_atk") ?? 0,
                "Def" => equipment?.GetTotalStat("phys_def") ?? 0,
                "Mag" => equipment?.GetTotalStat("mag_atk") ?? 0,
                "MDef" => equipment?.GetTotalStat("mag_def") ?? 0,
                _ => 0
            };
            return value;
        }

        public int GetCurrentSpeed() => GetCurrentStat("Spd");
        public int GetCurrentHitRate() => GetCurrentStat("Hit");
        public int GetCurrentEvasion() => GetCurrentStat("Eva");
        public int GetCurrentCritRate() => GetCurrentStat("Crit");
        public int GetCurrentBlockRate() => GetCurrentStat("Block");

        public float GetBlockReduction()
        {
            if (Equipment?.OffHand?.Data?.Category == EquipmentCategory.GreatShield)
                return 0.75f;
            if (Equipment?.OffHand?.Data?.Category == EquipmentCategory.Shield)
                return 0.50f;
            return 0.25f;
        }

        // Available skill pool = innate + CC skills (filtered by CurrentLevel) + equipment granted
        public List<string> GetAvailableActiveSkillIds()
        {
            var ids = new List<string>(Data.InnateActiveSkillIds);
            if (IsCc && Data.CcInnateActiveSkillIds != null)
                ids.AddRange(Data.CcInnateActiveSkillIds);

            // Level unlock filter: null = no level requirement (e.g. equipment-granted are added later)
            ids = ids.Where(id =>
            {
                var skill = GameData.GetActiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= CurrentLevel;
            }).ToList();

            ids.AddRange(Equipment.GetGrantedActiveSkillIds());
            ids.AddRange(TemporaryActiveSkillIds);
            return ids.Distinct().ToList();
        }

        public List<string> GetAvailablePassiveSkillIds()
        {
            var ids = new List<string>(Data.InnatePassiveSkillIds);
            if (IsCc && Data.CcInnatePassiveSkillIds != null)
                ids.AddRange(Data.CcInnatePassiveSkillIds);

            ids = ids.Where(id =>
            {
                var skill = GameData.GetPassiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= CurrentLevel;
            }).ToList();

            ids.AddRange(Equipment.GetGrantedPassiveSkillIds());
            ids.AddRange(TemporaryPassiveSkillIds);
            return ids.Distinct().ToList();
        }

        public void GrantTemporarySkill(string skillId, bool isPassive)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return;

            var targetList = isPassive ? TemporaryPassiveSkillIds : TemporaryActiveSkillIds;
            if (!targetList.Contains(skillId))
                targetList.Add(skillId);
        }

        public bool CanUseActiveSkill(ActiveSkill skill) => CurrentAp >= skill.Data.ApCost;
        public bool CanUsePassiveSkill(PassiveSkill skill) => CurrentPp >= skill.Data.PpCost && State != UnitState.Charging;

        public List<PassiveSkillData> GetEquippedPassiveSkills()
        {
            var result = new List<PassiveSkillData>();
            foreach (var id in EquippedPassiveSkillIds)
            {
                if (GameData.PassiveSkills.TryGetValue(id, out var skill))
                    result.Add(skill);
            }
            return result;
        }

        public List<PassiveStrategy> GetPassiveStrategiesInOrder()
        {
            if (PassiveStrategies.Count == 0)
            {
                return EquippedPassiveSkillIds
                    .Select(id => new PassiveStrategy
                    {
                        SkillId = id,
                        Condition1 = PassiveConditions.TryGetValue(id, out var condition) ? condition : null
                    })
                    .ToList();
            }

            var equipped = EquippedPassiveSkillIds.ToHashSet();
            var result = PassiveStrategies
                .Where(row => row != null
                    && !string.IsNullOrWhiteSpace(row.SkillId)
                    && equipped.Contains(row.SkillId))
                .ToList();

            var represented = result.Select(row => row.SkillId).ToHashSet();
            foreach (var id in EquippedPassiveSkillIds.Where(id => !represented.Contains(id)))
            {
                result.Add(new PassiveStrategy
                {
                    SkillId = id,
                    Condition1 = PassiveConditions.TryGetValue(id, out var condition) ? condition : null
                });
            }

            return result;
        }

        public int GetUsedPp()
        {
            int total = 0;
            foreach (var id in EquippedPassiveSkillIds)
            {
                if (GameData.PassiveSkills.TryGetValue(id, out var skill))
                    total += skill.PpCost;
            }
            return total;
        }

        public bool CanEquipPassive(string skillId)
        {
            if (EquippedPassiveSkillIds.Contains(skillId)) return false;
            if (!GameData.PassiveSkills.TryGetValue(skillId, out var skill)) return false;
            return GetUsedPp() + skill.PpCost <= PassivePpBudget;
        }

        public void SyncResourceCapsFromStats(int previousMaxHp, bool preserveMissingHp = true)
        {
            int newMaxHp = Math.Max(1, GetCurrentStat("HP"));
            int hpDelta = newMaxHp - Math.Max(1, previousMaxHp);
            int oldInitialAp = InitialAp;
            int oldInitialPp = InitialPp;

            if (hpDelta > 0)
            {
                if (CurrentHp > 0)
                    CurrentHp = Math.Min(newMaxHp, CurrentHp + hpDelta);
            }
            else if (hpDelta < 0)
            {
                CurrentHp = Math.Min(CurrentHp, newMaxHp);
            }
            else if (!preserveMissingHp)
            {
                CurrentHp = Math.Min(CurrentHp, newMaxHp);
            }

            MaxAp = ResourceCap;
            MaxPp = ResourceCap;
            InitialAp = GetCurrentStat("AP");
            InitialPp = GetCurrentStat("PP");
            if (InitialAp > oldInitialAp)
                CurrentAp += InitialAp - oldInitialAp;
            else if (InitialAp < oldInitialAp)
                CurrentAp = Math.Min(CurrentAp, InitialAp);
            if (InitialPp > oldInitialPp)
                CurrentPp += InitialPp - oldInitialPp;
            else if (InitialPp < oldInitialPp)
                CurrentPp = Math.Min(CurrentPp, InitialPp);
            CurrentAp = Math.Min(CurrentAp, MaxAp);
            CurrentPp = Math.Min(CurrentPp, MaxPp);
        }

        public void ConsumeAp(int amount) => CurrentAp = Math.Max(0, CurrentAp - amount);
        public void ConsumePp(int amount) => CurrentPp = Math.Max(0, CurrentPp - amount);
        public void RecoverAp(int amount) => CurrentAp = Math.Min(MaxAp, CurrentAp + amount);
        public void RecoverPp(int amount) => CurrentPp = Math.Min(MaxPp, CurrentPp + amount);

        private static bool IsResourceStat(string statName)
        {
            return string.Equals(statName, "AP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statName, "PP", StringComparison.OrdinalIgnoreCase);
        }

        private static int ClampResource(int value) => Math.Clamp(value, 0, ResourceCap);

        public static int ClampCalculatedStat(string statName, int value)
        {
            return IsResourceStat(statName)
                ? ClampResource(value)
                : Math.Max(0, value);
        }

        public void TakeDamage(int damage)
        {
            if (damage >= CurrentHp && CurrentHp > 0 && TryConsumeTemporal("DeathResist"))
            {
                CurrentHp = 1;
                return;
            }

            CurrentHp = Math.Max(0, CurrentHp - damage);
            if (CurrentHp == 0) { /* OnKnockdownEvent published by BattleEngine */ }
        }

        // === Module 3: Temporal states ===

        public bool TryConsumeTemporal(string key)
        {
            var state = TemporalStates.Find(s => s.Key == key);
            if (state == null) return false;
            if (state.RemainingCount > 0) state.RemainingCount--;
            if (state.RemainingCount == 0) TemporalStates.Remove(state);
            return true;
        }

        public void AddTemporal(string key, int count = 1, int turns = -1, string source = null)
        {
            TemporalStates.Add(new TemporalState
            {
                Key = key,
                RemainingCount = count,
                RemainingTurns = turns,
                SourceSkillId = source
            });
        }

        // === Module 4: Custom counters ===

        public int GetCounter(string key) => CustomCounters.GetValueOrDefault(key, 0);

        public void ModifyCounter(string key, int delta)
        {
            if (!CustomCounters.ContainsKey(key)) CustomCounters[key] = 0;
            CustomCounters[key] = Math.Max(0, CustomCounters[key] + delta);
        }

        public int ConsumeCounter(string key)
        {
            if (!CustomCounters.TryGetValue(key, out int val)) return 0;
            CustomCounters.Remove(key);
            return val;
        }
    }
}
