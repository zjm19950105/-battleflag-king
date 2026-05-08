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
        // Read-only template reference
        public CharacterData Data { get; private set; }
        public GameDataRepository GameData { get; private set; }

        // Runtime mutable state
        public int CurrentHp { get; set; }
        public int CurrentAp { get; set; }
        public int CurrentPp { get; set; }
        public int MaxAp { get; private set; }
        public int MaxPp { get; private set; }
        public int Position { get; set; }
        public bool IsFrontRow => Position <= 3;
        public bool IsAlive => CurrentHp > 0;
        public UnitState State { get; set; } = UnitState.Normal;
        public int ActionCount { get; set; } = 0;
        public int ConsecutiveWaitCount { get; set; } = 0;
        public bool IsPlayer { get; set; }
        public bool IsCc { get; private set; }
        public int CurrentLevel { get; set; } = 1;
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
            CurrentAp = data.BaseStats.GetValueOrDefault("AP", 5);
            CurrentPp = data.BaseStats.GetValueOrDefault("PP", 0);
            MaxAp = CurrentAp;
            MaxPp = CurrentPp;
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

        // Dynamic stat calculation (base + equipment + buff)
        public int GetCurrentStat(string statName)
        {
            int baseValue = Data.BaseStats.GetValueOrDefault(statName, 0);
            int equipValue = Equipment.GetTotalStat(statName);
            float buffRatio = Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
            int flatBuff = Buffs.Where(b => b.TargetStat == statName).Sum(b => b.FlatAmount);
            return (int)((baseValue + equipValue) * (1 + buffRatio)) + flatBuff;
        }

        public int GetCurrentAttackPower(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "Mag" : "Str";
            string equipmentStat = damageType == SkillType.Magical ? "mag_atk" : "phys_atk";
            return GetCurrentStat(stat) + Equipment.GetTotalStat(equipmentStat);
        }

        public int GetCurrentDefense(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "MDef" : "Def";
            string equipmentStat = damageType == SkillType.Magical ? "mag_def" : "phys_def";
            return GetCurrentStat(stat) + Equipment.GetTotalStat(equipmentStat);
        }

        public int GetCurrentSpeed() => GetCurrentStat("Spd");
        public int GetCurrentHitRate() => GetCurrentStat("Hit");
        public int GetCurrentEvasion() => GetCurrentStat("Eva");
        public int GetCurrentCritRate() => GetCurrentStat("Crit");
        public int GetCurrentBlockRate() => GetCurrentStat("Block");

        public float GetBlockReduction()
        {
            if (Equipment?.OffHand?.Data?.Category == EquipmentCategory.GreatShield)
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
            return GetUsedPp() + skill.PpCost <= MaxPp;
        }

        public void ConsumeAp(int amount) => CurrentAp = Math.Max(0, CurrentAp - amount);
        public void ConsumePp(int amount) => CurrentPp = Math.Max(0, CurrentPp - amount);
        public void RecoverAp(int amount) => CurrentAp = Math.Min(MaxAp, CurrentAp + amount);
        public void RecoverPp(int amount) => CurrentPp = Math.Min(MaxPp, CurrentPp + amount);

        public void TakeDamage(int damage)
        {
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
