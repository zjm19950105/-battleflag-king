using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public partial class Main : Node2D
{
	private LineEdit _lineEdit;
	private Label _promptLabel;
	private System.Random _rnd = new System.Random();

	public override void _Ready()
	{
		GD.Print("Hello Battle King");
		SetupUi();
		_ = RunGameLoopWithErrorHandlingAsync();
	}

	private async Task RunGameLoopWithErrorHandlingAsync()
	{
		try
		{
			await RunGameLoopAsync();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"游戏循环异常: {ex.GetType().Name}: {ex.Message}");
			GD.PrintErr(ex.StackTrace);
			_promptLabel.Text = $"错误: {ex.GetType().Name}: {ex.Message}";
		}
	}

	private void SetupUi()
	{
		var canvas = new CanvasLayer();
		AddChild(canvas);

		var panel = new Panel();
		panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		panel.OffsetLeft = 10;
		panel.OffsetRight = -10;
		panel.OffsetTop = -100;
		panel.OffsetBottom = -10;
		canvas.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 4);
		panel.AddChild(vbox);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		vbox.AddChild(margin);

		var inner = new VBoxContainer();
		margin.AddChild(inner);

		_promptLabel = new Label();
		_promptLabel.Text = "等待输入...";
		inner.AddChild(_promptLabel);

		_lineEdit = new LineEdit();
		_lineEdit.CustomMinimumSize = new Vector2(580, 30);
		inner.AddChild(_lineEdit);
	}

	private async Task RunGameLoopAsync()
	{
		var gameData = new GameDataRepository();
		string dataPath = ProjectSettings.GlobalizePath("res://data");
		gameData.LoadAll(dataPath);
		GD.Print("数据加载完成");

		while (true)
		{
			await RunPreBattleSetupAsync(gameData);
			var again = await ReadInputAsync("\n是否再来一局? (y/n): ", "n");
			if (again.ToLower() != "y")
				break;
		}

		_promptLabel.Text = "游戏结束";
		_lineEdit.Editable = false;
	}

	private async Task RunPreBattleSetupAsync(GameDataRepository gameData)
	{
		var playerCharIds = await SelectPlayerCharactersAsync(gameData);
		var playerPositions = await SetupFormationAsync(playerCharIds, gameData, "己方");
		var enemyConfig = await SelectEnemyFormationAsync(gameData, playerCharIds);

		var ctx = new BattleContext(gameData);

		var playerUnits = new List<BattleUnit>();
		foreach (var (charId, pos) in playerPositions)
		{
			var unit = CreateUnit(gameData, charId, true, pos, isCc: false);
			DayProgression.Apply(unit, 1);
			playerUnits.Add(unit);
			ctx.PlayerUnits.Add(unit);
		}

		var enemyUnits = new List<BattleUnit>();
		foreach (var (charId, pos, presetId) in enemyConfig)
		{
			var unit = CreateUnit(gameData, charId, false, pos, isCc: false);
			DayProgression.Apply(unit, 1);
			enemyUnits.Add(unit);
			ctx.EnemyUnits.Add(unit);
		}

		while (ctx.PlayerUnits.Count < 6) ctx.PlayerUnits.Add(null);
		while (ctx.EnemyUnits.Count < 6) ctx.EnemyUnits.Add(null);

		await SetupPassiveSkillsAsync(playerUnits, gameData, "己方");
		SetupEnemyPassiveSkills(enemyUnits, gameData);

		await SetupStrategiesAsync(playerUnits, gameData, "己方");
		ApplyPresetStrategies(enemyUnits, enemyConfig, gameData);

		GD.Print("\n=== 队伍信息 ===");
		GD.Print("己方队伍:");
		foreach (var unit in ctx.PlayerUnits.Where(u => u != null))
			PrintUnitDetail(unit);
		GD.Print("敌方队伍:");
		foreach (var unit in ctx.EnemyUnits.Where(u => u != null))
			PrintUnitDetail(unit);

		var engine = new BattleEngine(ctx);
		engine.OnLog = msg => GD.Print(msg);

		var passiveProcessor = new BattleKing.Skills.PassiveSkillProcessor(engine.EventBus, gameData, msg => GD.Print(msg));
		passiveProcessor.SubscribeAll();

		var result = engine.StartBattle();
		GD.Print($"战斗结果: {result}");
	}

	private async Task<string> ReadInputAsync(string prompt, string defaultValue = "")
	{
		_promptLabel.Text = prompt;
		_lineEdit.Text = "";
		_lineEdit.Editable = true;
		_lineEdit.GrabFocus();

		var result = await ToSignal(_lineEdit, LineEdit.SignalName.TextSubmitted);
		string input = (string)result[0];

		_lineEdit.Editable = false;

		if (string.IsNullOrWhiteSpace(input))
		{
			if (!string.IsNullOrEmpty(defaultValue))
			{
				GD.Print($"(使用默认值: {defaultValue})");
				return defaultValue;
			}
			return "";
		}
		GD.Print(input.Trim());
		return input.Trim();
	}

	private async Task<List<string>> SelectPlayerCharactersAsync(GameDataRepository gameData)
	{
		var allChars = gameData.Characters.Values.ToList();

		GD.Print("\n=== 角色选择 ===");
		GD.Print(string.Format("{0,-4} {1,-6} {2,-10} {3,-4} {4,-4} {5,-4} {6,-4} 特性", "编号", "名称", "兵种", "HP", "Str", "Def", "Spd"));
		for (int i = 0; i < allChars.Count; i++)
		{
			var c = allChars[i];
			var classes = string.Join(",", c.Classes);
			var desc = string.IsNullOrEmpty(c.Description) ? "[待补充]" : c.Description;
			GD.Print($"{i + 1,-4} {c.Name,-6} {classes,-10} {c.BaseStats.GetValueOrDefault("HP", 0),-4} {c.BaseStats.GetValueOrDefault("Str", 0),-4} {c.BaseStats.GetValueOrDefault("Def", 0),-4} {c.BaseStats.GetValueOrDefault("Spd", 0),-4} {desc}");
		}

		while (true)
		{
			var input = await ReadInputAsync("请选择3个角色（输入编号，空格分隔，如 1 3 5）: ", "1 2 3");
			var indices = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(s => int.TryParse(s, out int n) ? n - 1 : -1)
				.Where(i => i >= 0 && i < allChars.Count)
				.Distinct()
				.ToList();

			if (indices.Count == 3)
				return indices.Select(i => allChars[i].Id).ToList();

			GD.Print($"输入无效，已选择 {indices.Count} 个角色，需要恰好3个。");
		}
	}

	private async Task<List<(string charId, int position)>> SetupFormationAsync(List<string> charIds, GameDataRepository gameData, string side)
	{
		var result = new List<(string, int)>();
		var occupied = new HashSet<int>();

		GD.Print($"\n=== {side}阵型布置 ===");
		PrintFormationGrid(result, occupied, gameData);

		for (int i = 0; i < charIds.Count; i++)
		{
			var charData = gameData.GetCharacter(charIds[i]);
			while (true)
			{
				var input = await ReadInputAsync($"请为 {charData.Name} 选择位置 (1-6): ", (i + 1).ToString());
				if (int.TryParse(input, out int pos) && pos >= 1 && pos <= 6 && !occupied.Contains(pos))
				{
					result.Add((charIds[i], pos));
					occupied.Add(pos);
					break;
				}
				GD.Print("位置无效或已被占用，请重新选择。");
			}
			PrintFormationGrid(result, occupied, gameData);
		}

		return result;
	}

	private void PrintFormationGrid(List<(string charId, int position)> placed, HashSet<int> occupied, GameDataRepository gameData)
	{
		var nameAt = new Dictionary<int, string>();
		foreach (var (cid, pos) in placed)
			nameAt[pos] = gameData.GetCharacter(cid).Name;

		GD.Print("前排: [1] " + (nameAt.GetValueOrDefault(1) ?? "空") + "  [2] " + (nameAt.GetValueOrDefault(2) ?? "空") + "  [3] " + (nameAt.GetValueOrDefault(3) ?? "空"));
		GD.Print("后排: [4] " + (nameAt.GetValueOrDefault(4) ?? "空") + "  [5] " + (nameAt.GetValueOrDefault(5) ?? "空") + "  [6] " + (nameAt.GetValueOrDefault(6) ?? "空"));
	}

	private async Task<List<(string charId, int position, string presetId)>> SelectEnemyFormationAsync(GameDataRepository gameData, List<string> playerCharIds)
	{
		var formations = gameData.EnemyFormations.Values.ToList();

		GD.Print("\n=== 选择敌人配置 ===");
		for (int i = 0; i < formations.Count; i++)
		{
			var f = formations[i];
			GD.Print($"  {i + 1}. [难度{f.Difficulty}] {f.Name} - {f.Description}");
		}

		var input = await ReadInputAsync("请选择: ", "1");
		if (!int.TryParse(input, out int choice) || choice < 1 || choice > formations.Count)
			choice = 1;

		var selected = formations[choice - 1];

		if (selected.Id == "fmt_random" || selected.Units.Count == 0)
		{
			var available = gameData.Characters.Keys.Except(playerCharIds).ToList();
			var picked = available.OrderBy(_ => _rnd.Next()).Take(3).ToList();
			return new List<(string, int, string)>
			{
				(picked[0], 1, "preset_aggressive"), (picked[1], 2, "preset_aggressive"), (picked[2], 3, "preset_aggressive")
			};
		}

		return selected.Units.Select(u => (u.CharacterId, u.Position, u.StrategyPresetId ?? "preset_aggressive")).ToList();
	}

	private async Task SetupPassiveSkillsAsync(List<BattleUnit> units, GameDataRepository gameData, string side)
	{
		GD.Print($"\n=== {side}被动技能配置 ===");
		foreach (var unit in units)
		{
			var availableIds = unit.GetAvailablePassiveSkillIds();
			if (availableIds.Count == 0)
			{
				GD.Print($"  [{unit.Data.Name}] 无可用被动技能");
				continue;
			}

			GD.Print($"\n为 [{unit.Data.Name}] 选择被动技能 (PP上限: {unit.MaxPp})");
			var availableSkills = availableIds
				.Select(id => gameData.GetPassiveSkill(id))
				.Where(s => s != null)
				.ToList();

			for (int i = 0; i < availableSkills.Count; i++)
			{
				var s = availableSkills[i];
				GD.Print($"  {i + 1}. {s.Name} (PP{s.PpCost}) [{s.TriggerTiming}] {s.EffectDescription}");
			}

			var equipped = new List<string>();
			int usedPp = 0;
			while (true)
			{
				int remaining = unit.MaxPp - usedPp;
				if (remaining <= 0) break;

				var affordable = availableSkills
					.Where(s => !equipped.Contains(s.Id) && s.PpCost <= remaining)
					.ToList();
				if (affordable.Count == 0) break;

				var input = await ReadInputAsync($"  请选择要装备的被动技能编号 (剩余PP: {remaining}, 直接回车结束): ", "");
				if (string.IsNullOrWhiteSpace(input)) break;

				if (!int.TryParse(input, out int choice) || choice < 1 || choice > availableSkills.Count)
				{
					GD.Print("  无效输入，请重新选择。");
					continue;
				}

				var selected = availableSkills[choice - 1];
				if (equipped.Contains(selected.Id))
				{
					GD.Print($"  {selected.Name} 已装备，不能重复选择。");
					continue;
				}
				if (selected.PpCost > remaining)
				{
					GD.Print($"  PP不足！需要 {selected.PpCost}，剩余 {remaining}。");
					continue;
				}

				equipped.Add(selected.Id);
				usedPp += selected.PpCost;
				GD.Print($"  已装备: {selected.Name} (PP{selected.PpCost})");
			}

			unit.EquippedPassiveSkillIds = equipped;
			GD.Print($"  [{unit.Data.Name}] 最终配置: {equipped.Count}个被动技能, 消耗PP {usedPp}/{unit.MaxPp}");
		}
	}

	private void SetupEnemyPassiveSkills(List<BattleUnit> units, GameDataRepository gameData)
	{
		foreach (var unit in units)
		{
			var availableIds = unit.GetAvailablePassiveSkillIds();
			var availableSkills = availableIds
				.Select(id => gameData.GetPassiveSkill(id))
				.Where(s => s != null)
				.OrderBy(_ => _rnd.Next())
				.ToList();

			var equipped = new List<string>();
			int usedPp = 0;
			foreach (var skill in availableSkills)
			{
				if (usedPp + skill.PpCost > unit.MaxPp) break;
				equipped.Add(skill.Id);
				usedPp += skill.PpCost;
			}

			unit.EquippedPassiveSkillIds = equipped;
		}
	}

	private readonly List<(string name, Condition condition)> _onlyConditions = new()
	{
		("无条件", null),
		("自身HP > 50%", new Condition { Category = ConditionCategory.SelfHp, Operator = "greater_than", Value = 0.5f }),
		("自身AP >= 2", new Condition { Category = ConditionCategory.SelfApPp, Operator = "greater_than", Value = 2 }),
		("目标在前排", new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" }),
		("目标是步兵", new Condition { Category = ConditionCategory.UnitClass, Operator = "equals", Value = "Infantry" })
	};

	private readonly List<(string name, Condition condition)> _priorityConditions = new()
	{
		("无", null),
		("优先HP最低", new Condition { Category = ConditionCategory.Hp, Operator = "lowest", Value = null }),
		("优先前排", new Condition { Category = ConditionCategory.Position, Operator = "equals", Value = "front" })
	};

	private async Task SetupStrategiesAsync(List<BattleUnit> units, GameDataRepository gameData, string side)
	{
		GD.Print($"\n=== {side}策略配置 (8条策略栏位) ===");
		foreach (var unit in units)
		{
			GD.Print($"\n--- 为 [{unit.Data.Name}] 配置策略 ---");
			var availableSkillIds = unit.GetAvailableActiveSkillIds();
			var availableSkills = availableSkillIds
				.Select(id => gameData.GetActiveSkill(id))
				.Where(s => s != null)
				.ToList();

			if (availableSkills.Count == 0)
			{
				GD.Print("  无可用主动技能");
				continue;
			}

			// Default: fill all 8 slots with first skill + no conditions
			var firstSkill = availableSkills[0];
			var strategies = Enumerable.Range(0, 8)
				.Select(_ => new Strategy
				{
					SkillId = firstSkill.Id,
					Condition1 = null,
					Condition2 = null,
					Mode1 = ConditionMode.Only,
					Mode2 = ConditionMode.Only
				})
				.ToList();

			while (true)
			{
				GD.Print("\n当前策略配置:");
				for (int i = 0; i < strategies.Count; i++)
				{
					var s = strategies[i];
					var skillName = availableSkills.FirstOrDefault(sk => sk.Id == s.SkillId)?.Name ?? "未知";
					var c1Name = _onlyConditions.FirstOrDefault(c => c.condition?.Category == s.Condition1?.Category && c.condition?.Operator == s.Condition1?.Operator).name ?? "无条件";
					var c2Name = _priorityConditions.FirstOrDefault(c => c.condition?.Category == s.Condition2?.Category && c.condition?.Operator == s.Condition2?.Operator).name ?? "无";
					GD.Print($"  [{i + 1}] {skillName} | {c1Name} | {c2Name}");
				}

				var editInput = await ReadInputAsync("编辑策略(1-8), 0=确认, d=全部默认: ", "0");
				if (editInput == "0") break;
				if (editInput.ToLower() == "d")
				{
					strategies = Enumerable.Range(0, 8)
						.Select(_ => new Strategy { SkillId = firstSkill.Id, Condition1 = null, Condition2 = null, Mode1 = ConditionMode.Only, Mode2 = ConditionMode.Only })
						.ToList();
					continue;
				}
				if (!int.TryParse(editInput, out int slot) || slot < 1 || slot > 8)
				{
					GD.Print("无效输入，请输入 1-8 或 0");
					continue;
				}

				// Edit specific slot
				GD.Print($"\n编辑策略栏位 [{slot}/8]");
				for (int i = 0; i < availableSkills.Count; i++)
				{
					var s = availableSkills[i];
					GD.Print($"    {i + 1}. {s.Name} (AP{s.ApCost} {s.AttackType} 威力{s.Power})");
				}
				var skillInput = await ReadInputAsync("    请选择技能编号: ", "1");
				if (!int.TryParse(skillInput, out int skillChoice) || skillChoice < 1 || skillChoice > availableSkills.Count)
					skillChoice = 1;
				var selectedSkill = availableSkills[skillChoice - 1];

				for (int i = 0; i < _onlyConditions.Count; i++)
					GD.Print($"    {i + 1}. {_onlyConditions[i].name}");
				var c1Input = await ReadInputAsync("    条件1 (过滤): ", "1");
				if (!int.TryParse(c1Input, out int c1Choice) || c1Choice < 1 || c1Choice > _onlyConditions.Count)
					c1Choice = 1;
				var cond1 = _onlyConditions[c1Choice - 1].condition;

				for (int i = 0; i < _priorityConditions.Count; i++)
					GD.Print($"    {i + 1}. {_priorityConditions[i].name}");
				var c2Input = await ReadInputAsync("    条件2 (优先): ", "1");
				if (!int.TryParse(c2Input, out int c2Choice) || c2Choice < 1 || c2Choice > _priorityConditions.Count)
					c2Choice = 1;
				var cond2 = _priorityConditions[c2Choice - 1].condition;

				strategies[slot - 1] = new Strategy
				{
					SkillId = selectedSkill.Id,
					Condition1 = cond1,
					Condition2 = cond2,
					Mode1 = ConditionMode.Only,
					Mode2 = cond2 != null ? ConditionMode.Priority : ConditionMode.Only
				};
			}

			unit.Strategies = strategies;
			GD.Print($"  [{unit.Data.Name}] 策略配置完成");
		}
	}

	private void ApplyPresetStrategies(List<BattleUnit> units, List<(string charId, int position, string presetId)> enemyConfig, GameDataRepository gameData)
	{
		for (int i = 0; i < units.Count; i++)
		{
			var unit = units[i];
			var presetId = enemyConfig[i].presetId;
			if (string.IsNullOrEmpty(presetId) || !gameData.StrategyPresets.ContainsKey(presetId))
				presetId = "preset_aggressive";

			var preset = gameData.GetStrategyPreset(presetId);
			var availableSkillIds = unit.GetAvailableActiveSkillIds();

			var strategies = new List<Strategy>();
			foreach (var ps in preset.Strategies)
			{
				string skillId = null;
				if (ps.SkillIndex >= 0 && ps.SkillIndex < availableSkillIds.Count)
					skillId = availableSkillIds[ps.SkillIndex];
				else if (availableSkillIds.Count > 0)
					skillId = availableSkillIds[0];

				if (skillId == null) continue;

				strategies.Add(new Strategy
				{
					SkillId = skillId,
					Condition1 = ps.Condition1,
					Condition2 = ps.Condition2,
					Mode1 = ps.Mode1,
					Mode2 = ps.Mode2
				});
			}

			unit.Strategies = strategies;
		}
	}

	private void PrintUnitDetail(BattleUnit unit)
	{
		var passives = unit.GetEquippedPassiveSkills();
		var passiveStr = passives.Count > 0 ? string.Join(", ", passives.Select(p => p.Name)) : "无";
		var stats = unit.Data.BaseStats;
		GD.Print($"  [{unit.Data.Name}] 位置{unit.Position} HP:{unit.CurrentHp}/{stats.GetValueOrDefault("HP",0)} AP:{unit.CurrentAp}/{stats.GetValueOrDefault("AP",0)} PP:{unit.GetUsedPp()}/{unit.MaxPp}");
		GD.Print($"      Str:{unit.GetCurrentStat("Str")} Def:{unit.GetCurrentStat("Def")} Spd:{unit.GetCurrentStat("Spd")} Mag:{unit.GetCurrentStat("Mag")} MDef:{unit.GetCurrentStat("MDef")} Hit:{unit.GetCurrentStat("Hit")} Eva:{unit.GetCurrentStat("Eva")} Crit:{unit.GetCurrentStat("Crit")} Block:{unit.GetCurrentStat("Block")}");
		GD.Print($"      被动:[{passiveStr}]");
	}

	private BattleUnit CreateUnit(GameDataRepository gameData, string characterId, bool isPlayer, int position, bool isCc = false)
	{
		var charData = gameData.GetCharacter(characterId);
		var unit = new BattleUnit(charData, gameData, isPlayer, isCc)
		{
			IsPlayer = isPlayer,
			Position = position
		};

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

		if (!unit.Equipment.ValidateWeaponEquipped())
		{
			GD.PushWarning($"角色 {charData.Name} 武器槽为空，请检查初始装备配置");
		}

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
}
