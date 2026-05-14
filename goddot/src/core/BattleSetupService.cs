using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;

namespace BattleKing.Core
{
    public class BattleSetupService
    {
        private readonly GameDataRepository _gameData;

        public BattleSetupService(GameDataRepository gameData)
        {
            _gameData = gameData;
        }

        public BattleUnit CreateUnit(string charId, bool isPlayer, int position, int day, bool isCc = false)
        {
            var character = _gameData.GetCharacter(charId);
            var unit = new BattleUnit(character, _gameData, isPlayer)
            {
                IsPlayer = isPlayer,
                Position = position
            };

            DayProgression.Apply(unit, day);
            unit.SetCcState(isCc);
            int previousMaxHp = unit.GetCurrentStat("HP");
            ApplyInitialEquipment(unit);
            unit.SyncResourceCapsFromStats(previousMaxHp);
            ApplyDefaultStrategies(unit);
            AutoEquipPassives(unit);

            return unit;
        }

        public void ApplyInitialEquipment(BattleUnit unit)
        {
            var character = unit.Data;
            var equipmentIds = unit.IsCc && character.CcInitialEquipmentIds?.Count > 0
                ? character.CcInitialEquipmentIds
                : character.InitialEquipmentIds;

            if (equipmentIds == null)
                return;

            foreach (var equipmentId in equipmentIds)
            {
                var equipment = _gameData.GetEquipment(equipmentId);
                if (equipment != null)
                    unit.Equipment.Equip(equipment);
            }
        }

        public void ApplyDefaultStrategies(BattleUnit unit)
        {
            var availableActiveSkillIds = unit.GetAvailableActiveSkillIds();
            var skillId = availableActiveSkillIds
                .Select(id => _gameData.GetActiveSkill(id))
                .Where(skill => skill != null)
                .OrderBy(skill => skill.UnlockLevel ?? 0)
                .ThenBy(skill => availableActiveSkillIds.IndexOf(skill.Id))
                .Select(skill => skill.Id)
                .FirstOrDefault();

            unit.Strategies = skillId != null
                ? new List<Strategy> { new Strategy { SkillId = skillId } }
                : new List<Strategy>();
        }

        public void ApplyStrategyPreset(BattleUnit unit, string presetId, bool fillToEight = false)
        {
            var preset = _gameData.GetStrategyPreset(presetId);
            if (preset == null)
                return;

            var activeSkillIds = unit.GetAvailableActiveSkillIds();
            unit.Strategies = preset.Strategies
                .Select(strategy => BuildStrategy(strategy, activeSkillIds))
                .Where(strategy => strategy != null)
                .ToList();

            if (fillToEight && unit.Strategies.Count > 0)
            {
                var firstSkillId = unit.Strategies[0].SkillId;
                while (unit.Strategies.Count < 8)
                    unit.Strategies.Add(new Strategy { SkillId = firstSkillId });
            }
        }

        public void AutoEquipPassives(BattleUnit unit)
        {
            var availablePassiveSkillIds = unit.GetAvailablePassiveSkillIds();
            var passive = availablePassiveSkillIds
                .Select(id => _gameData.GetPassiveSkill(id))
                .Where(skill => skill != null)
                .Where(skill => skill.PpCost <= unit.PassivePpBudget)
                .OrderBy(skill => skill.UnlockLevel ?? 0)
                .ThenBy(skill => skill.PpCost)
                .ThenBy(skill => availablePassiveSkillIds.IndexOf(skill.Id))
                .FirstOrDefault();

            unit.EquippedPassiveSkillIds.Clear();
            unit.PassiveStrategies.Clear();

            if (passive == null)
                return;

            unit.EquippedPassiveSkillIds.Add(passive.Id);
            unit.PassiveStrategies.Add(new PassiveStrategy { SkillId = passive.Id });
        }

        public void AutoEquipAdditionalPassives(BattleUnit unit, Random random)
        {
            int usedPp = unit.GetUsedPp();
            var available = unit.GetAvailablePassiveSkillIds()
                .Select(id => _gameData.GetPassiveSkill(id))
                .Where(skill => skill != null)
                .Where(skill => !unit.EquippedPassiveSkillIds.Contains(skill.Id))
                .OrderBy(_ => random.Next())
                .ToList();

            foreach (var passive in available)
            {
                if (usedPp + passive.PpCost > unit.PassivePpBudget)
                    break;

                unit.EquippedPassiveSkillIds.Add(passive.Id);
                usedPp += passive.PpCost;
            }
        }

        private static Strategy BuildStrategy(PresetStrategyData presetStrategy, List<string> activeSkillIds)
        {
            string skillId = !string.IsNullOrEmpty(presetStrategy.SkillId)
                ? presetStrategy.SkillId
                : presetStrategy.SkillIndex >= 0 && presetStrategy.SkillIndex < activeSkillIds.Count
                    ? activeSkillIds[presetStrategy.SkillIndex]
                    : activeSkillIds.FirstOrDefault();

            return skillId != null
                ? new Strategy
                {
                    SkillId = skillId,
                    Condition1 = presetStrategy.Condition1,
                    Condition2 = presetStrategy.Condition2,
                    Mode1 = presetStrategy.Mode1,
                    Mode2 = presetStrategy.Mode2
                }
                : null;
        }
    }
}
