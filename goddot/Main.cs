using Godot;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public partial class Main : Node2D
{
	public override void _Ready()
	{
		GD.Print("Hello Battle King");

		// 1. Load game data
		var gameData = new GameDataRepository();
		string dataPath = ProjectSettings.GlobalizePath("res://data");
		gameData.LoadAll(dataPath);
		GD.Print("数据加载完成");

		// 2. Create battle context
		var ctx = new BattleContext(gameData);

		// 3. Create player unit (swordsman, 未CC状态)
		var playerUnit = CreateUnit(gameData, "swordsman", true, 1, isCc: false);

		// 4. Create enemy unit (mercenary, 未CC状态)
		var enemyUnit = CreateUnit(gameData, "mercenary", false, 1, isCc: false);

		ctx.PlayerUnits.Add(playerUnit);
		ctx.EnemyUnits.Add(enemyUnit);

		// 5. Apply Day1 progression (最小可玩性验证)
		DayProgression.Apply(playerUnit, 1);
		DayProgression.Apply(enemyUnit, 1);

		// 6. Print detailed info with full skill unlock status
		GD.Print("\n=== 角色数据验证（Day1状态）===");
		PrintUnitInfo(gameData, playerUnit, "玩家");
		PrintUnitInfo(gameData, enemyUnit, "敌方");

		// 7. Create BattleEngine and start Day1 battle
		var engine = new BattleEngine(ctx);
		engine.OnLog = msg => GD.Print(msg);
		var result = engine.StartBattle();

		// 8. Print result
		GD.Print($"战斗结果: {result}");
	}

	private BattleUnit CreateUnit(GameDataRepository gameData, string characterId, bool isPlayer, int position, bool isCc = false)
	{
		var charData = gameData.GetCharacter(characterId);
		var unit = new BattleUnit(charData, gameData, isPlayer, isCc)
		{
			IsPlayer = isPlayer,
			Position = position
		};

		// Equip initial equipment based on CC state
		var equipIds = isCc && charData.CcInitialEquipmentIds != null && charData.CcInitialEquipmentIds.Count > 0
			? charData.CcInitialEquipmentIds
			: charData.InitialEquipmentIds;

		if (equipIds != null)
		{
			foreach (var equipId in equipIds)
			{
				var equipData = gameData.GetEquipment(equipId);
				if (equipData != null)
				{
					unit.Equipment.Equip(equipData);
				}
			}
		}

		// Validate: weapon slot must never be empty
		if (!unit.Equipment.ValidateWeaponEquipped())
		{
			GD.PushWarning($"角色 {charData.Name} 武器槽为空，请检查初始装备配置");
		}

		// Set default strategy: use first available active skill on SingleEnemy
		var availableSkillIds = unit.GetAvailableActiveSkillIds();
		var firstSkillId = availableSkillIds.Count > 0
			? availableSkillIds[0]
			: null;

		if (firstSkillId != null)
		{
			unit.Strategies = new List<Strategy>
			{
				new Strategy
				{
					SkillId = firstSkillId,
					Condition1 = null,
					Condition2 = null,
					Mode1 = ConditionMode.Only,
					Mode2 = ConditionMode.Only
				}
			};
		}

		return unit;
	}

	private void PrintUnitInfo(GameDataRepository gameData, BattleUnit unit, string side)
	{
		var data = unit.Data;
		string displayName = unit.IsCc && !string.IsNullOrEmpty(data.CcName)
			? $"{data.Name} → {data.CcName}"
			: data.Name;

		GD.Print($"\n[{side}] {displayName} ({data.Id})");
		GD.Print($"  CC状态: {(unit.IsCc ? "已CC" : "未CC")}");
		GD.Print($"  天数等级: Lv{unit.CurrentLevel}");
		GD.Print($"  兵种: {string.Join(", ", data.Classes)}");

		var equipCats = unit.GetEquippableCategories();
		GD.Print($"  装备槽: {string.Join(", ", equipCats)}");

		// Initial equipment
		GD.Print("  初始装备:");
		var equipped = unit.Equipment.AllEquipped.ToList();
		if (equipped.Count == 0)
		{
			GD.Print("    (无)");
		}
		else
		{
			foreach (var eq in equipped)
			{
				var stats = string.Join(", ", eq.Data.BaseStats.Select(kv => $"{kv.Key}+{kv.Value}"));
				GD.Print($"    - {eq.Data.Name} [{eq.Data.Category}] ({stats})");
			}
		}

		// All active skills with unlock status
		GD.Print("  主动技能池:");
		foreach (var skillId in data.InnateActiveSkillIds)
		{
			var skill = gameData.GetActiveSkill(skillId);
			bool unlocked = skill.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
			string tag = unlocked ? "[已解锁]" : $"[Lv{skill.UnlockLevel}锁定]";
			GD.Print($"    {tag} {skill.Name} (AP{skill.ApCost} 威力{skill.Power}) [{skillId}]");
		}
		if (data.CcInnateActiveSkillIds != null)
		{
			foreach (var skillId in data.CcInnateActiveSkillIds)
			{
				var skill = gameData.GetActiveSkill(skillId);
				bool ccUnlocked = unit.IsCc;
				bool levelUnlocked = skill.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
				string tag;
				if (!ccUnlocked) tag = "[CC未解锁]";
				else if (levelUnlocked) tag = "[已解锁]";
				else tag = $"[Lv{skill.UnlockLevel}锁定]";
				GD.Print($"    {tag} {skill.Name} (AP{skill.ApCost} 威力{skill.Power}) [{skillId}]");
			}
		}

		// All passive skills with unlock status
		GD.Print("  被动技能池:");
		foreach (var skillId in data.InnatePassiveSkillIds)
		{
			var skill = gameData.GetPassiveSkill(skillId);
			bool unlocked = skill.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
			string tag = unlocked ? "[已解锁]" : $"[Lv{skill.UnlockLevel}锁定]";
			GD.Print($"    {tag} {skill.Name} (PP{skill.PpCost} {skill.TriggerTiming}) [{skillId}]");
		}
		if (data.CcInnatePassiveSkillIds != null)
		{
			foreach (var skillId in data.CcInnatePassiveSkillIds)
			{
				var skill = gameData.GetPassiveSkill(skillId);
				bool ccUnlocked = unit.IsCc;
				bool levelUnlocked = skill.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
				string tag;
				if (!ccUnlocked) tag = "[CC未解锁]";
				else if (levelUnlocked) tag = "[已解锁]";
				else tag = $"[Lv{skill.UnlockLevel}锁定]";
				GD.Print($"    {tag} {skill.Name} (PP{skill.PpCost} {skill.TriggerTiming}) [{skillId}]");
			}
		}

		// Equipment-granted skills
		var eqActiveIds = unit.Equipment.GetGrantedActiveSkillIds();
		var eqPassiveIds = unit.Equipment.GetGrantedPassiveSkillIds();
		GD.Print("  装备赋予技能:");
		if (eqActiveIds.Count == 0 && eqPassiveIds.Count == 0)
		{
			GD.Print("    (无)");
		}
		else
		{
			foreach (var skillId in eqActiveIds)
			{
				var skill = gameData.GetActiveSkill(skillId);
				GD.Print($"    [已解锁] [主动] {skill.Name} [{skillId}]");
			}
			foreach (var skillId in eqPassiveIds)
			{
				var skill = gameData.GetPassiveSkill(skillId);
				GD.Print($"    [已解锁] [被动] {skill.Name} [{skillId}]");
			}
		}

		// Currently available skills (after all filters)
		var availActive = unit.GetAvailableActiveSkillIds();
		var availPassive = unit.GetAvailablePassiveSkillIds();
		GD.Print("  当前可用技能:");
		GD.Print($"    主动: {string.Join(", ", availActive)}");
		GD.Print($"    被动: {string.Join(", ", availPassive)}");

		// Stats
		GD.Print("  属性:");
		GD.Print($"    HP:{unit.CurrentHp}/{data.BaseStats.GetValueOrDefault("HP", 0)}  " +
			$"Str:{data.BaseStats.GetValueOrDefault("Str", 0)}  " +
			$"Def:{data.BaseStats.GetValueOrDefault("Def", 0)}  " +
			$"Mag:{data.BaseStats.GetValueOrDefault("Mag", 0)}  " +
			$"MDef:{data.BaseStats.GetValueOrDefault("MDef", 0)}");
		GD.Print($"    Hit:{data.BaseStats.GetValueOrDefault("Hit", 0)}  " +
			$"Eva:{data.BaseStats.GetValueOrDefault("Eva", 0)}  " +
			$"Crit:{data.BaseStats.GetValueOrDefault("Crit", 0)}  " +
			$"Block:{data.BaseStats.GetValueOrDefault("Block", 0)}  " +
			$"Spd:{data.BaseStats.GetValueOrDefault("Spd", 0)}");
		GD.Print($"    AP:{unit.CurrentAp}/{unit.MaxAp}  PP:{unit.CurrentPp}/{unit.MaxPp}");

		// Strategy
		GD.Print("  默认策略:");
		if (unit.Strategies.Count > 0)
		{
			var s = unit.Strategies[0];
			GD.Print($"    技能: {s.SkillId}");
		}
	}
}
