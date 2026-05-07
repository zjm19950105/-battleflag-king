using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;
using BattleKing.Equipment;

public enum GamePhase
{
	ModeSelect,
	PlayerFormation,
	EnemyChoice,
	EnemyDragFormation,
	EquipmentSetup,
	PassiveSetup,
	StrategySetup,
	Battle,
	Result
}

public partial class Main : Node2D
{
	private TextEdit _logLabel;
	
	private Control _formationArea;  // HBoxContainer: left pool + right grid
	private VBoxContainer _leftPanel;
	private VBoxContainer _rightPanel;
	private Label _statusLabel;
	private HBoxContainer _buttonBar;

	private GamePhase _phase;
	private GameDataRepository _gameData;
	private System.Random _rnd = new();
	private List<CharacterData> _allChars;

	private string[] _playerSlots = new string[6];
	private string[] _enemySlots = new string[6];
	private bool _enemyUseDrag;
	private int _minSlots = 3;
	private int _selectedDay = 1;
	private bool _selectedCc = false;

	private BattleContext _ctx;
	private BattleEngine _engine;
	private BattleKing.Skills.PassiveSkillProcessor _passiveProc;
	private List<BattleUnit> _playerUnits;
	private List<BattleUnit> _enemyUnits;
	private List<(string, int, string)> _enemyConfig;
	private int _equipSetupIdx;
	private int _passiveSetupIdx;
	private int _strategySetupIdx;
	private bool _editingEnemyStrategies;
	private BattleResult _battleResult;
	private Node _logOriginalParent;

	// ── GODOT ────────────────────────────────────────────────

	public override void _Ready()
	{
		GD.Print("Hello Battle King");
		_gameData = new GameDataRepository();
		_gameData.LoadAll(ProjectSettings.GlobalizePath("res://data"));
		_allChars = _gameData.Characters.Values.ToList();
		SetupUi();
		Log("数据加载完成 — 战旗之王 Phase 1.3");
		Go(GamePhase.ModeSelect);
	}

	// ── LAYOUT: status → formation-area → button-bar → log ──

	private void SetupUi()
	{
		var canvas = new CanvasLayer();
		AddChild(canvas);
		var root = new VBoxContainer();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		canvas.AddChild(root);

		// 1) Yellow status bar
		_statusLabel = new Label { Text = "" };
		_statusLabel.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0));
		root.AddChild(_statusLabel);

		// 2) Formation area: left character pool | right grid
		_formationArea = new HBoxContainer();
		_formationArea.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		root.AddChild(_formationArea);

		_leftPanel = new VBoxContainer();
		_leftPanel.CustomMinimumSize = new Vector2(180, 0);
		_formationArea.AddChild(_leftPanel);
		_formationArea.AddChild(new VSeparator());
		_rightPanel = new VBoxContainer();
		_rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_formationArea.AddChild(_rightPanel);

		// 3) Button bar (prominent)
		_buttonBar = new HBoxContainer();
		_buttonBar.AddThemeConstantOverride("separation", 8);
		root.AddChild(_buttonBar);

		// 4) Log: TextEdit (read-only) — larger font, fills available space
		_logLabel = new TextEdit();
		_logLabel.Editable = false;
		_logLabel.WrapMode = TextEdit.LineWrappingMode.Boundary;
		_logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_logLabel.AddThemeColorOverride("background_color", new Color(0.05f, 0.05f, 0.08f));
		_logLabel.AddThemeFontSizeOverride("font_size", 20);
		root.AddChild(_logLabel);
	}

	// ── HELPERS ──────────────────────────────────────────────

	private void Log(string msg) => _logLabel.InsertTextAtCaret(msg + "\n");
	private void ClearLog() => _logLabel.Text = "";
	private void ClearPanel(Control p) { foreach (var c in p.GetChildren()) c.QueueFree(); }
	private void ClearAll() { ClearPanel(_leftPanel); ClearPanel(_rightPanel); ClearButtons(); }

	private static string StarStr(int current, int max)
	{
		if (max <= 0) return "";
		string s = "";
		for (int i = 0; i < current; i++) s += "★";
		for (int i = current; i < max; i++) s += "☆";
		return s;
	}

	private Button Btn(string text, Action onClick)
	{
		var b = new Button { Text = text };
		b.Pressed += () => onClick();
		return b;
	}
	private void AddBtn(string text, Action onClick) => _buttonBar.AddChild(Btn(text, onClick));
	private void AddBackBtn() {
		var backTo = _phaseHistory.Count > 0 ? _phaseHistory.Peek() : GamePhase.ModeSelect;
		_buttonBar.AddChild(Btn("← 上一步", () => GoBack()));
	}
	private void ClearButtons() { foreach (var c in _buttonBar.GetChildren()) c.QueueFree(); }

	// ── STATE MACHINE ────────────────────────────────────────

	private Stack<GamePhase> _phaseHistory = new();
	private bool _suppressHistory;

	private void Go(GamePhase p)
	{
		if (!_suppressHistory && _phase != p)
			_phaseHistory.Push(_phase);
		_suppressHistory = false;
		_phase = p;
		// Before clearing: reparent log if it was moved to right panel
		if (_logOriginalParent != null)
		{
			if (_rightPanel != null && _logLabel.GetParent() == _rightPanel)
				_rightPanel.RemoveChild(_logLabel);
			_logOriginalParent.AddChild(_logLabel);
			_logOriginalParent = null;
		}
		ClearAll();
		switch (p)
		{
			case GamePhase.ModeSelect:        Phase_ModeSelect(); break;
			case GamePhase.PlayerFormation:   Phase_PlayerFormation(); break;
			case GamePhase.PassiveSetup:       Phase_PassiveSetup(); break;
			case GamePhase.StrategySetup:      Phase_StrategySetup(); break;
			case GamePhase.Battle:             Phase_Battle(); break;
			case GamePhase.Result:             Phase_Result(); break;
			case GamePhase.EnemyChoice:       Phase_EnemyChoice(); break;
			case GamePhase.EnemyDragFormation: Phase_EnemyDragFormation(); break;
			case GamePhase.EquipmentSetup:     Phase_EquipmentSetup(); break;

		}
		// Back button for all phases except ModeSelect
		if (p != GamePhase.ModeSelect) AddBackBtn();
	}

	private void GoBack()
	{
		if (_phaseHistory.Count > 0)
		{
			_suppressHistory = true;
			Go(_phaseHistory.Pop());
		}
		else Go(GamePhase.ModeSelect);
	}

	// ── PHASE 0: MODE SELECT ──────────────────────────────

	private void Phase_ModeSelect()
	{
		_statusLabel.Text = "▶  选择对战模式";
		ClearLog();
		_leftPanel.AddChild(new Label { Text = "选择对战模式:\n" });

		// Day & CC conditions
		var dayLabel = new Label { Text = "天数:" };
		_leftPanel.AddChild(dayLabel);
		var dayOpt = new OptionButton();
		for (int d = 1; d <= 6; d++) dayOpt.AddItem($"第{d}天 — Lv{d*10}技能{(d>=5?",可转职":"")}");
		dayOpt.ItemSelected += (long idx) => { int d = (int)idx + 1; _selectedDay = d; };
		_leftPanel.AddChild(dayOpt);

		var ccCheck = new CheckBox { Text = "转职 (第5天起)", Disabled = true };
		ccCheck.Toggled += (on) => { _selectedCc = on; };
		_leftPanel.AddChild(ccCheck);
		dayOpt.ItemSelected += (long idx) => { int d = (int)idx + 1; ccCheck.Disabled = (d < 5); if (d < 5) { ccCheck.ButtonPressed = false; _selectedCc = false; } };
		_leftPanel.AddChild(Btn("[1v1 对战] — 双方各上场1人", () => {
			_minSlots = 1;
			Go(GamePhase.PlayerFormation);
		}));
		_leftPanel.AddChild(Btn("[3v3 对战] — 双方各上场3人", () => {
			_minSlots = 3;
			Go(GamePhase.PlayerFormation);
		}));
	}

	// ── PHASE 1: PLAYER FORMATION ────────────────────────────

	private void Phase_PlayerFormation()
	{
		_statusLabel.Text = $"▶  我方阵型 — 拖拽角色到格子 (至少{_minSlots}人，最多6人)";
		ClearLog();
		BuildDragUI("我方", _playerSlots, () => {
			int n = _playerSlots.Count(s => s != null);
			if (n < _minSlots) { Log($"至少需要{_minSlots}人 (当前{n})"); return; }
			Log($"我方阵型确认 ({n}人) Day{_selectedDay} CC:{_selectedCc}: {string.Join(",", _playerSlots.Where(s => s != null))}");
			Go(GamePhase.EnemyChoice);
		});
	}

	// ── PHASE 2: ENEMY CHOICE ────────────────────────────────

	private void Phase_EnemyChoice()
	{
		_statusLabel.Text = "▶  敌方配置 — 选择预设 或 自定义拖拽";
		ClearLog();

		_leftPanel.AddChild(new Label { Text = "选择敌方配置方式:\n" });

		_leftPanel.AddChild(Btn("[自定义拖拽敌人]", () => {
			Array.Clear(_enemySlots);
			_enemyUseDrag = true;
			Go(GamePhase.EnemyDragFormation);
		}));

		_leftPanel.AddChild(new Label { Text = "\n── 预设敌人 ──" });

		var formations = _gameData.EnemyFormations.Values.ToList();
		for (int i = 0; i < formations.Count; i++)
		{
			int idx = i;
			var f = formations[i];
			_leftPanel.AddChild(Btn($"{f.Name} [难度{f.Difficulty}]  — {f.Description}", () => {
				_enemyUseDrag = false;
				SelectPresetEnemy(idx);
			}));
		}
	}

	private void Phase_EnemyDragFormation()
	{
		_statusLabel.Text = $"▶  敌方阵型 — 拖拽角色到格子 (至少{_minSlots}人)";
		BuildDragUI("敌方", _enemySlots, () => {
			int n = _enemySlots.Count(s => s != null);
			if (n < _minSlots) { Log($"至少需要{_minSlots}人 (当前{n})"); return; }
			Log($"敌方阵型确认 ({n}人)");
			CreateAllUnits(preset: false);
			Go(GamePhase.EquipmentSetup);
		});
	}

	private void SelectPresetEnemy(int idx)
	{
		var formations = _gameData.EnemyFormations.Values.ToList();
		var selected = formations[idx];
		Log($"选择敌人: {selected.Name}");

		if (selected.Id == "fmt_random" || selected.Units.Count == 0)
		{
			var available = _gameData.Characters.Keys.ToList();
			var picked = available.OrderBy(_ => _rnd.Next()).Take(_minSlots).ToList();
			_enemyConfig = picked.Select((p, i) => (p, i + 1, "preset_aggressive")).ToList();
		}
		else
		{
			_enemyConfig = selected.Units.Select(u => (u.CharacterId, u.Position, u.StrategyPresetId ?? "preset_aggressive")).ToList();
		}
		CreateAllUnits(preset: true);
		Go(GamePhase.PassiveSetup);
	}

	// ── SHARED DRAG-AND-DROP UI ──────────────────────────────

	private void BuildDragUI(string teamLabel, string[] slots, Action onConfirm)
	{
		// Left: scrollable character pool
		_leftPanel.AddChild(new Label { Text = $"角色池 ({_allChars.Count}人，拖拽到右侧格子)" });
		var charScroll = new ScrollContainer();
		charScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var charList = new VBoxContainer();
		foreach (var ch in _allChars)
			charList.AddChild(new DraggableChar { Text = ch.Name, CharId = ch.Id });
		charScroll.AddChild(charList);
		_leftPanel.AddChild(charScroll);

		// Right: 6-slot formation grid
		_rightPanel.AddChild(new Label { Text = "══ 前 排 ══" });
		var frontRow = new HBoxContainer();
		_rightPanel.AddChild(frontRow);
		for (int i = 0; i < 3; i++) AddSlot(frontRow, slots, i);

		_rightPanel.AddChild(new Label { Text = "══ 後 排 ══" });
		var backRow = new HBoxContainer();
		_rightPanel.AddChild(backRow);
		for (int i = 3; i < 6; i++) AddSlot(backRow, slots, i);

		// Highlight: confirm button — bold and prominent
		int filled = slots.Count(s => s != null);
		var confirmBtn = Btn($"★ 确认{teamLabel}阵型 ({filled}/{_minSlots}) ★", onConfirm);
		confirmBtn.AddThemeFontSizeOverride("font_size", 20);
		if (filled < _minSlots)
		{
			confirmBtn.Disabled = true;
			confirmBtn.Text = $"⚠ 还需{_minSlots - filled}人 — 确认{teamLabel}阵型 ({filled}/{_minSlots})";
		}
		_buttonBar.AddChild(confirmBtn);
	}

	private void AddSlot(HBoxContainer row, string[] slots, int idx)
	{
		var slot = new DropSlot(slots, idx, () => RebuildDragPhase());
		row.AddChild(slot);
		row.AddChild(Btn("×", () => { slots[idx] = null; RebuildDragPhase(); }));
	}

	private void RebuildDragPhase()
	{
		ClearAll();
		if (_phase == GamePhase.PlayerFormation)
			Phase_PlayerFormation();
		else if (_phase == GamePhase.EnemyDragFormation)
			Phase_EnemyDragFormation();
	}

	// ── UNIT CREATION ────────────────────────────────────────

	private void CreateAllUnits(bool preset)
	{
		_ctx = new BattleContext(_gameData);
		_playerUnits = new();
		_enemyUnits = new();

		for (int i = 0; i < 6; i++)
		{
			if (_playerSlots[i] != null)
			{
				var u = NewUnit(_playerSlots[i], true, i + 1, isCc: _selectedCc);
				DayProgression.Apply(u, _selectedDay);
				_playerUnits.Add(u);
				_ctx.PlayerUnits.Add(u);
			}
			else _ctx.PlayerUnits.Add(null);
		}

		if (preset)
		{
			for (int i = 0; i < _enemyConfig.Count; i++)
			{
				var u = NewUnit(_enemyConfig[i].Item1, false, _enemyConfig[i].Item2, isCc: _selectedCc);
				DayProgression.Apply(u, _selectedDay);
				_enemyUnits.Add(u);
				_ctx.EnemyUnits.Add(u);
			}
			while (_ctx.EnemyUnits.Count < 6) _ctx.EnemyUnits.Add(null);
		}
		else
		{
			for (int i = 0; i < 6; i++)
			{
				if (_enemySlots[i] != null)
				{
					var u = NewUnit(_enemySlots[i], false, i + 1, isCc: _selectedCc);
					DayProgression.Apply(u, _selectedDay);
					_enemyUnits.Add(u);
					_ctx.EnemyUnits.Add(u);
				}
				else _ctx.EnemyUnits.Add(null);
			}
		}

		// Give enemies additional random passives (on top of auto-equipped defaults)
		foreach (var u in _enemyUnits.Where(u => u != null))
		{
			int usedPp = u.GetUsedPp();
			var avail = u.GetAvailablePassiveSkillIds()
				.Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null)
				.Where(s => !u.EquippedPassiveSkillIds.Contains(s.Id))
				.OrderBy(_ => _rnd.Next()).ToList();
			foreach (var s in avail)
			{
				if (usedPp + s.PpCost > u.MaxPp) break;
				u.EquippedPassiveSkillIds.Add(s.Id);
				usedPp += s.PpCost;
			}
		}
	}

	private BattleUnit NewUnit(string charId, bool isPlayer, int pos, bool isCc = false)
	{
		var cd = _gameData.GetCharacter(charId);
		var u = new BattleUnit(cd, _gameData, isPlayer) { IsPlayer = isPlayer, Position = pos };
		DayProgression.Apply(u, _selectedDay);
		u.SetCcState(isCc);
		var eq = u.IsCc && cd.CcInitialEquipmentIds?.Count > 0 ? cd.CcInitialEquipmentIds : cd.InitialEquipmentIds;
		if (eq != null) foreach (var eid in eq) { var ed = _gameData.GetEquipment(eid); if (ed != null) u.Equipment.Equip(ed); }
		var firstId = u.GetAvailableActiveSkillIds().FirstOrDefault();
		if (firstId != null) u.Strategies = Enumerable.Range(0, 8).Select(_ => new Strategy { SkillId = firstId }).ToList();

		// Auto-equip default passives (UnlockLevel <= 1) up to PP cap
		var autoPassives = u.GetAvailablePassiveSkillIds()
			.Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null)
			.Where(s => s.UnlockLevel == null || s.UnlockLevel <= 1)
			.OrderBy(s => s.PpCost).ToList();
		int ppUsed = 0;
		foreach (var s in autoPassives)
		{
			if (ppUsed + s.PpCost > u.MaxPp) continue;
			u.EquippedPassiveSkillIds.Add(s.Id);
			ppUsed += s.PpCost;
		}
		return u;
	}

	// ── PHASE 3: PASSIVE SETUP ───────────────────────────────

	// ── PHASE 3: EQUIPMENT SETUP ──────────────────────────

	private void Phase_EquipmentSetup() { _equipSetupIdx = 0; ShowEquipment(); }

	private void ShowEquipment()
	{
		ClearAll();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_equipSetupIdx >= units.Count) { Go(GamePhase.PassiveSetup); return; }

		var unit = units[_equipSetupIdx];
		var cd = unit.Data;
		bool isCc = unit.IsCc;
		var slots = EquipmentSlot.GetSlotNames(cd, isCc);
		var allEquip = _gameData.GetAllEquipment();

		_statusLabel.Text = $"▶  装备配置 [{cd.Name}] — {slots.Count}槽";

		// Left panel: slot dropdowns
		var slotScroll = new ScrollContainer();
		slotScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var slotList = new VBoxContainer();
		slotScroll.AddChild(slotList);
		slotList.AddChild(new Label { Text = $"{cd.Name} 装备槽:\n" });

		// Build filtered equipment lists per slot
		foreach (var slotName in slots)
		{
			var current = unit.Equipment.GetBySlot(slotName);
			string slotLabel = slotName switch
			{
				"MainHand" => "主手", "OffHand" => "副手", _ => slotName
			};

			var row = new HBoxContainer();
			row.AddChild(new Label { Text = $"[{slotLabel}] " });

			var dropdown = new OptionButton();
			dropdown.AddItem("(空)");
			int selectedIdx = 0;

			// Determine which equipment fits this slot
			string expectedCat = GetExpectedCategory(slotName, cd, isCc);
			var candidates = new List<EquipmentData>();
			if (expectedCat != null)
			{
				foreach (var eq in allEquip)
				{
					if (eq.Category.ToString() == expectedCat && EquipmentSlot.CanEquipCategory(eq.Category, cd, isCc))
						candidates.Add(eq);
				}
			}

			for (int i = 0; i < candidates.Count; i++)
			{
				var eq = candidates[i];
				string desc = eq.Name;
				foreach (var kv in eq.BaseStats)
					desc += $" {kv.Key}+{kv.Value}";
				if (eq.SpecialEffects.Count > 0)
					desc += $" [{string.Join(",", eq.SpecialEffects)}]";
				dropdown.AddItem(desc);
				if (current != null && eq.Id == current.Data.Id)
					selectedIdx = i + 1;
			}
			dropdown.Selected = selectedIdx;

			string slotCapture = slotName;
			var capsCopy = candidates;
			dropdown.ItemSelected += (long sel) => {
				int s = (int)sel;
				if (s == 0) unit.Equipment.Unequip(slotCapture);
				else if (s - 1 < capsCopy.Count)
					unit.Equipment.EquipToSlot(slotCapture, capsCopy[s - 1]);
				UpdateEquipDetail(unit);
			};
			row.AddChild(dropdown);
			slotList.AddChild(row);
		}

		_leftPanel.AddChild(slotScroll);

		// Right panel: stat overview
		UpdateEquipDetail(unit);

		// Bottom buttons
		AddBtn("→ 确认/下一个角色", () => { _equipSetupIdx++; ShowEquipment(); });
		AddBtn("→ 全部默认装备", () => { _equipSetupIdx = units.Count; ShowEquipment(); });
	}

	private static string GetExpectedCategory(string slotName, CharacterData cd, bool isCc)
	{
		var types = isCc && cd.CcEquippableCategories?.Count > 0 ? cd.CcEquippableCategories : cd.EquippableCategories;
		var slots = EquipmentSlot.GetSlotNames(cd, isCc);
		int idx = slots.IndexOf(slotName);
		if (idx < 0 || idx >= types.Count) return null;
		return types[idx].ToString();
	}

	private void UpdateEquipDetail(BattleUnit unit)
	{
		ClearPanel(_rightPanel);
		var cd = unit.Data;

		var detailLabel = new RichTextLabel { BbcodeEnabled = true };
		detailLabel.AddThemeFontSizeOverride("normal_font_size", 16);
		detailLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_rightPanel.AddChild(detailLabel);

		detailLabel.AppendText($"[color=yellow]══ {cd.Name} 属性 ══[/color]\n\n");

		var statNames = new[] { "HP", "Str", "Def", "Mag", "MDef", "Hit", "Eva", "Crit", "Block", "Spd", "AP", "PP" };
		foreach (var sn in statNames)
		{
			if (!cd.BaseStats.ContainsKey(sn)) continue;
			int baseVal = cd.BaseStats[sn];
			int equipVal = unit.Equipment.GetTotalStat(sn);
			int buffVal = (int)(baseVal * BattleKing.Equipment.BuffManager.GetTotalBuffRatio(unit, sn));
			int total = baseVal + equipVal + buffVal;

			string line = $"{sn}: {baseVal}";
			if (equipVal > 0) line += $" [color=#88ff88]+{equipVal}[/color]";
			if (equipVal < 0) line += $" [color=#ff8888]{equipVal}[/color]";
			if (buffVal != 0) line += $" [color=#8888ff]+{buffVal}(buff)[/color]";
			if (equipVal != 0 || buffVal != 0) line += $" [color=yellow]= {total}[/color]";

			detailLabel.AppendText(line + "\n");
		}

		detailLabel.AppendText("\n[color=cyan]══ 当前装备 ══[/color]\n");
		foreach (var e in unit.Equipment.AllEquipped)
		{
			string stats = string.Join(" ", e.Data.BaseStats.Select(kv => $"{kv.Key}+{kv.Value}"));
			detailLabel.AppendText($"{e.Data.Name}: {stats}\n");
		}
	}

	private void Phase_PassiveSetup() { _passiveSetupIdx = 0; ShowPassive(); }

	private void ShowPassive()
	{
		ClearAll();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_passiveSetupIdx >= units.Count)
		{
			Go(GamePhase.StrategySetup);
			return;
		}

		var unit = units[_passiveSetupIdx];
		_statusLabel.Text = $"▶  被动技能 [{unit.Data.Name}] — [color=blue]PP:{StarStr(unit.GetUsedPp(), unit.MaxPp)}[/color]";
		_leftPanel.AddChild(new Label { Text = $"{unit.Data.Name} 可用被动 (可设置发动条件):\n" });

		foreach (var s in unit.GetAvailablePassiveSkillIds().Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null))
		{
			bool on = unit.EquippedPassiveSkillIds.Contains(s.Id);

			// Toggle button
			var toggleRow = new HBoxContainer();
			toggleRow.AddChild(Btn($"{(on ? "[✓]" : "[  ]")} {s.Name} PP{s.PpCost} [{s.TriggerTiming}]", () => {
				if (on) { unit.EquippedPassiveSkillIds.Remove(s.Id); unit.PassiveConditions.Remove(s.Id); Log($"  卸下: {s.Name}"); }
				else if (!unit.CanEquipPassive(s.Id)) { Log("  PP不足!"); return; }
				else { unit.EquippedPassiveSkillIds.Add(s.Id); Log($"  装备: {s.Name}"); }
				ShowPassive();
			}));
			_leftPanel.AddChild(toggleRow);

			// If equipped, show condition selector
			if (on)
			{
				var condRow = new HBoxContainer();
				condRow.AddChild(new Label { Text = "    发动条件:" });

				var cond = unit.PassiveConditions.TryGetValue(s.Id, out var c) ? c : null;
				var catOpt = new OptionButton();
				catOpt.AddItem("(无条件)");
				int catSel = 0;
				var cats = ConditionMeta.AllCategories;
				for (int ci = 0; ci < cats.Count; ci++)
				{
					catOpt.AddItem(ConditionMeta.CategoryLabel(cats[ci]));
					if (cond != null && cats[ci] == cond.Category) catSel = ci + 1;
				}
				catOpt.Selected = catSel;
				catOpt.ItemSelected += (long idx) => {
					int i = (int)idx;
					if (i <= 0) { unit.PassiveConditions.Remove(s.Id); }
					else {
						var cat = cats[i - 1];
						var ops = ConditionMeta.GetOperators(cat);
						var vals = ConditionMeta.GetValues(cat, ops[0]);
						unit.PassiveConditions[s.Id] = ConditionMeta.BuildCondition(cat, ops[0], vals[0], true);
					}
					ShowPassive();
				};
				condRow.AddChild(catOpt);

				// If category selected, show operator + value
				if (catSel > 0)
				{
					var curCat = cats[catSel - 1];
					var ops = ConditionMeta.GetOperators(curCat);
					var opOpt = new OptionButton();
					int opSel = 0;
					for (int oi = 0; oi < ops.Count; oi++)
					{
						opOpt.AddItem(ops[oi]);
						if (cond != null && ops[oi] == (cond.Operator switch { "less_than" => "低于", "greater_than" => "高于", "equals" => "等于", "lowest" => "最低", "highest" => "最高", _ => ops[oi] }))
							opSel = oi;
					}
					opOpt.Selected = opSel;
					opOpt.ItemSelected += (long _) => ShowPassive();
					condRow.AddChild(opOpt);

					var vals = ConditionMeta.GetValues(curCat, ops[opSel]);
					var valOpt = new OptionButton();
					int valSel = 0;
					for (int vi = 0; vi < vals.Count; vi++)
					{
						valOpt.AddItem(vals[vi]);
						if (cond != null && vals[vi] == cond.Value?.ToString()) valSel = vi;
					}
					valOpt.Selected = valSel;
					valOpt.ItemSelected += (long _) => ShowPassive();
					condRow.AddChild(valOpt);

					// Save on each selection
					catOpt.ItemSelected += (long idx) => {
						int ii = (int)idx;
						if (ii <= 0) { unit.PassiveConditions.Remove(s.Id); }
						else {
							var cc = cats[ii - 1];
							var oo = ConditionMeta.GetOperators(cc);
							var vv = ConditionMeta.GetValues(cc, oo[opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? Math.Min(opOpt.Selected, oo.Count - 1) : 0]);
							var vo = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? Math.Min(valOpt.Selected, vv.Count - 1) : 0;
							unit.PassiveConditions[s.Id] = ConditionMeta.BuildCondition(cc, oo[0], vv[vo], true);
						}
					};
				}

				_leftPanel.AddChild(condRow);
			}
		}

		AddBtn("→ 下一个", () => { Log($"  [{unit.Data.Name}] 被动完成"); _passiveSetupIdx++; ShowPassive(); });
		AddBtn("→ 全部跳过", () => { _passiveSetupIdx = units.Count; ShowPassive(); });
	}

	private void ApplyPresetStrat(BattleUnit u, (string, int, string) cfg)
	{
		var pid = string.IsNullOrEmpty(cfg.Item3) || !_gameData.StrategyPresets.ContainsKey(cfg.Item3) ? "preset_aggressive" : cfg.Item3;
		var ps = _gameData.GetStrategyPreset(pid);
		var ids = u.GetAvailableActiveSkillIds();
		u.Strategies = ps.Strategies.Select(s => {
			string sid = s.SkillIndex >= 0 && s.SkillIndex < ids.Count ? ids[s.SkillIndex] : ids.FirstOrDefault();
			return sid != null ? new Strategy { SkillId = sid, Condition1 = s.Condition1, Condition2 = s.Condition2, Mode1 = s.Mode1, Mode2 = s.Mode2 } : null;
		}).Where(s => s != null).ToList();
	}

	// ── PHASE 4: STRATEGY SETUP ──────────────────────────────

	private void Phase_StrategySetup() { _strategySetupIdx = 0; _editingEnemyStrategies = false; ShowStrategy(); }

	private void ShowStrategy()
	{
		ClearAll();
		var units = _editingEnemyStrategies
			? _enemyUnits.Where(u => u != null).ToList()
			: _playerUnits.Where(u => u != null).ToList();

		if (_strategySetupIdx >= units.Count)
		{
			if (!_editingEnemyStrategies)
			{
				// All player units done — now configure enemy strategies
				_strategySetupIdx = 0;
				_editingEnemyStrategies = true;
				ShowStrategy();
				return;
			}
			Go(GamePhase.Battle);
			return;
		}

		var unit = units[_strategySetupIdx];
		string teamLabel = _editingEnemyStrategies ? "[敌方]" : "[我方]";
		var avail = unit.GetAvailableActiveSkillIds().Select(id => _gameData.GetActiveSkill(id)).Where(s => s != null).ToList();
		_statusLabel.Text = $"▶  策略编程 {teamLabel} [{unit.Data.Name}] — 技能条件组合";

		// Left: scrollable strategy editor
		var stratScroll = new ScrollContainer();
		stratScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var stratList = new VBoxContainer();
		stratScroll.AddChild(stratList);

		stratList.AddChild(new Label { Text = $"{unit.Data.Name} — 8条策略栏位 (AP:{unit.CurrentAp}/{unit.MaxAp})\n" });

		for (int i = 0; i < 8; i++)
		{
			int slot = i;
			var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;

			// Separator
			if (i > 0) stratList.AddChild(new HSeparator());

			// Row header: slot number + skill dropdown
			var headerRow = new HBoxContainer();
			headerRow.AddChild(new Label { Text = $"[{slot + 1}]" });
			var skillOpt = new OptionButton();
			skillOpt.AddItem("(空)");
			int skillSel = 0;
			for (int j = 0; j < avail.Count; j++)
			{
				skillOpt.AddItem($"{avail[j].Name} AP{avail[j].ApCost}");
				if (s != null && avail[j].Id == s.SkillId) skillSel = j + 1;
			}
			skillOpt.Selected = skillSel;
			int cap = slot;
			skillOpt.ItemSelected += (long idx) => {
				int si = (int)idx;
				while (unit.Strategies.Count <= cap) unit.Strategies.Add(new Strategy { SkillId = avail[0].Id });
				unit.Strategies[cap].SkillId = si == 0 ? avail[0].Id : avail[si - 1].Id;
			};
			headerRow.AddChild(skillOpt);
			stratList.AddChild(headerRow);

			// Condition 1 row
			BuildConditionRow(stratList, unit, slot, isCond1: true);

			// Condition 2 row
			BuildConditionRow(stratList, unit, slot, isCond1: false);
		}

		_leftPanel.AddChild(stratScroll);

		// Right: skill detail
		UpdateSkillDetail(unit);

		// Bottom
		string nextLabel = _editingEnemyStrategies ? "→ 下一个敌方角色" : "→ 下一个我方角色";
		AddBtn(nextLabel, () => { _strategySetupIdx++; ShowStrategy(); });
		string skipLabel = _editingEnemyStrategies ? "→ 敌方全默认(开始战斗)" : "→ 跳过全部策略配置";
		AddBtn(skipLabel, () => { _strategySetupIdx = units.Count; ShowStrategy(); });
	}

	private void BuildConditionRow(VBoxContainer parent, BattleUnit unit, int slot, bool isCond1)
	{
		var strategy = unit.Strategies.Count > slot ? unit.Strategies[slot] : null;
		var cond = isCond1 ? strategy?.Condition1 : strategy?.Condition2;
		var mode = isCond1 ? strategy?.Mode1 : strategy?.Mode2;
		bool isOnly = mode == ConditionMode.Only;

		// Row 1: category + operator + value + mode buttons
		var row1 = new HBoxContainer();
		string label = isCond1 ? "  条件1:" : "  条件2:";
		row1.AddChild(new Label { Text = label });

		var catOpt = new OptionButton();
		catOpt.AddItem("(无)");
		int catSel = 0;
		for (int c = 0; c < ConditionMeta.AllCategories.Count; c++)
		{
			catOpt.AddItem(ConditionMeta.CategoryLabel(ConditionMeta.AllCategories[c]));
			if (cond != null && ConditionMeta.AllCategories[c] == cond.Category) catSel = c + 1;
		}
		catOpt.Selected = catSel;

		var opOpt = new OptionButton();
		var valOpt = new OptionButton();

		// Mode toggle buttons: [优先] [仅]
		var priBtn = new Button { Text = "优先", Flat = false };
		var onlyBtn = new Button { Text = "仅", Flat = false };
		priBtn.AddThemeColorOverride("font_color", isOnly ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 1.0f, 0.3f));
		onlyBtn.AddThemeColorOverride("font_color", isOnly ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.6f));
		Label previewLabel = new Label();

		// Populate operator & value
		var selCat = catSel > 0 ? ConditionMeta.AllCategories[catSel - 1] : (ConditionCategory?)null;
		RebuildCondDropdowns(opOpt, valOpt, selCat, cond?.Operator, cond?.Value);

		Action refreshUi = () => {
			bool curOnly = onlyBtn.Modulate != null; // use button color state
			// Update button colors
			bool nowOnly = onlyBtn.GetMeta("active", false).AsBool();
			priBtn.AddThemeColorOverride("font_color", nowOnly ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 1.0f, 0.3f));
			onlyBtn.AddThemeColorOverride("font_color", nowOnly ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.6f));
			SaveCondition(unit, slot, isCond1, catOpt, opOpt, valOpt, nowOnly);
			// Update preview
			previewLabel.Text = BuildCondPreview(catOpt, opOpt, valOpt, nowOnly);
		};

		// Use SetMeta to track only/priority state
		onlyBtn.SetMeta("active", Variant.From(isOnly));
		priBtn.SetMeta("active", Variant.From(!isOnly));

		priBtn.Pressed += () => {
			onlyBtn.SetMeta("active", Variant.From(false));
			priBtn.SetMeta("active", Variant.From(true));
			refreshUi();
		};
		onlyBtn.Pressed += () => {
			onlyBtn.SetMeta("active", Variant.From(true));
			priBtn.SetMeta("active", Variant.From(false));
			refreshUi();
		};

		// Cascade handlers
		catOpt.ItemSelected += (long idx) => {
			int ci = (int)idx;
			var newCat = ci > 0 ? ConditionMeta.AllCategories[ci - 1] : (ConditionCategory?)null;
			RebuildCondDropdowns(opOpt, valOpt, newCat, null, null);
			refreshUi();
		};
		opOpt.ItemSelected += (long _) => {
			int ci = catOpt.Selected;
			var curCat = ci > 0 ? ConditionMeta.AllCategories[ci - 1] : (ConditionCategory?)null;
			string curOp = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : null;
			RebuildValueDropdown(valOpt, curCat, curOp);
			refreshUi();
		};
		valOpt.ItemSelected += (_) => refreshUi();

		row1.AddChild(catOpt);
		row1.AddChild(opOpt);
		row1.AddChild(valOpt);
		row1.AddChild(priBtn);
		row1.AddChild(onlyBtn);
		parent.AddChild(row1);

		// Row 2: preview text
		var row2 = new HBoxContainer();
		row2.AddChild(new Label { Text = "       → " });
		previewLabel.Text = BuildCondPreview(catOpt, opOpt, valOpt, isOnly);
		previewLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 1.0f));
		row2.AddChild(previewLabel);
		parent.AddChild(row2);
	}

	private static string BuildCondPreview(OptionButton catOpt, OptionButton opOpt, OptionButton valOpt, bool isOnly)
	{
		int ci = catOpt.Selected;
		if (ci <= 0) return "(无条件)";
		string cat = catOpt.GetItemText(ci);
		string op = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : "";
		string val = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? valOpt.GetItemText(valOpt.Selected) : "";
		string modeStr = isOnly ? "仅" : "优先";
		if (string.IsNullOrEmpty(op) || op == "-") return $"[{modeStr}] {cat}";
		if (string.IsNullOrEmpty(val) || val == "-") return $"[{modeStr}] {cat} {op}";
		return $"[{modeStr}] {cat} {op}{val}";
	}

	private static void RebuildCondDropdowns(OptionButton opOpt, OptionButton valOpt, ConditionCategory? cat, string currentOp, object currentVal)
	{
		opOpt.Clear();
		if (cat == null) { opOpt.AddItem("-"); valOpt.Clear(); valOpt.AddItem("-"); return; }

		var ops = ConditionMeta.GetOperators(cat.Value);
		int opSel = 0;
		for (int o = 0; o < ops.Count; o++)
		{
			opOpt.AddItem(ops[o]);
			if (ops[o] == currentOp) opSel = o;
		}
		opOpt.Selected = opSel;
		string selOp = ops[opSel];
		RebuildValueDropdown(valOpt, cat, selOp);
	}

	private static void RebuildValueDropdown(OptionButton valOpt, ConditionCategory? cat, string op)
	{
		valOpt.Clear();
		if (cat == null || string.IsNullOrEmpty(op)) { valOpt.AddItem("-"); return; }

		var vals = ConditionMeta.GetValues(cat.Value, op);
		foreach (var v in vals) valOpt.AddItem(v);
	}

	private void SaveCondition(BattleUnit unit, int slot, bool isCond1, OptionButton catOpt, OptionButton opOpt, OptionButton valOpt, bool isOnly)
	{
		while (unit.Strategies.Count <= slot)
			unit.Strategies.Add(new Strategy { SkillId = unit.GetAvailableActiveSkillIds().FirstOrDefault() ?? "" });

		int ci = catOpt.Selected;
		if (ci <= 0)
		{
			if (isCond1) { unit.Strategies[slot].Condition1 = null; unit.Strategies[slot].Mode1 = ConditionMode.Priority; }
			else { unit.Strategies[slot].Condition2 = null; unit.Strategies[slot].Mode2 = ConditionMode.Priority; }
			return;
		}

		var cat = ConditionMeta.AllCategories[ci - 1];
		string op = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : "";
		string val = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? valOpt.GetItemText(valOpt.Selected) : "";

		var cond = ConditionMeta.BuildCondition(cat, op, val, isOnly);
		if (isCond1) { unit.Strategies[slot].Condition1 = cond; unit.Strategies[slot].Mode1 = isOnly ? ConditionMode.Only : ConditionMode.Priority; }
		else { unit.Strategies[slot].Condition2 = cond; unit.Strategies[slot].Mode2 = isOnly ? ConditionMode.Only : ConditionMode.Priority; }
	}

	private void UpdateSkillDetail(BattleUnit unit)
	{
		ClearPanel(_rightPanel);
		var detailLabel = new RichTextLabel { BbcodeEnabled = true };
		detailLabel.AddThemeFontSizeOverride("normal_font_size", 15);
		detailLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_rightPanel.AddChild(detailLabel);

		detailLabel.AppendText($"[color=yellow]══ 策略编程说明 ══[/color]\n\n");
		detailLabel.AppendText("每条策略 = 技能 + 条件1 + 条件2\n\n");
		detailLabel.AppendText("[color=cyan]条件组合逻辑:[/color]\n");
		detailLabel.AppendText("• 仅+仅 = AND (两个条件都满足才发动)\n");
		detailLabel.AppendText("• 仅+优先 = 先过滤, 再排序\n");
		detailLabel.AppendText("• 优先+优先 = 条件2优先于条件1!\n");
		detailLabel.AppendText("  顺序: 1+2 > 2 > 1\n\n");
		detailLabel.AppendText("[color=cyan]模式:[/color]\n");
		detailLabel.AppendText("• 仅 = 条件不满足则[color=red]跳过技能[/color]\n");
		detailLabel.AppendText("• 优先(不勾选仅) = 条件不满足[color=green]仍会发动[/color]\n\n");
		detailLabel.AppendText($"[color=orange]{unit.Data.Name} 可用技能:[/color]\n");
		foreach (var sid in unit.GetAvailableActiveSkillIds())
		{
			var sk = _gameData.GetActiveSkill(sid);
			if (sk == null) continue;
			detailLabel.AppendText($"  AP{sk.ApCost} {sk.Name} — {sk.EffectDescription}\n");
		}
	}

// ── PHASE 5: BATTLE ──────────────────────────────────────

	// ── PHASE 5: BATTLE (step-by-step) ─────────────────────

	private void Phase_Battle()
	{
		ClearAll();
		_statusLabel.Text = "▶  战斗 — 点击「下一步」逐行动推进";

		// Left: unit status — fill panel height
		var unitScroll = new ScrollContainer();
		unitScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		unitScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_leftPanel.AddChild(unitScroll);
		var unitLabel = new RichTextLabel { BbcodeEnabled = true };
		unitLabel.AddThemeFontSizeOverride("normal_font_size", 16);
		unitLabel.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
		unitLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		unitLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		unitLabel.ScrollFollowing = true;
		unitScroll.AddChild(unitLabel);

		// Right: reparent log TextEdit — full height
		_logOriginalParent = _logLabel.GetParent();
		if (_logOriginalParent != null) _logOriginalParent.RemoveChild(_logLabel);
		_logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_rightPanel.AddChild(_logLabel);

		// Init engine
		_engine = new BattleEngine(_ctx);
		_engine.OnLog = msg => { _logLabel.InsertTextAtCaret(msg + "\n"); };
		_passiveProc = new BattleKing.Skills.PassiveSkillProcessor(_engine.EventBus, _gameData,
			msg => { _logLabel.InsertTextAtCaret(msg + "\n"); },
			_engine.EnqueueAction);
		_passiveProc.SubscribeAll();
		_engine.InitBattle();

		ClearLog();
		RefreshBattleStatus(unitLabel);

		AddBtn("▶ 下一步", () => StepOneAction(unitLabel));
	}

	private static string ClassColor(UnitClass c) => c switch
	{
		UnitClass.Infantry => "#aaaaaa", UnitClass.Cavalry => "#4488ff",
		UnitClass.Flying => "#44ff88", UnitClass.Heavy => "#ff8844",
		UnitClass.Scout => "#ff44ff", UnitClass.Archer => "#88ff44",
		UnitClass.Mage => "#8844ff", _ => "#cccccc"
	};

	private void AppendUnitStatus(RichTextLabel label, BattleUnit u)
	{
		if (u == null) return;
		if (!u.IsAlive) { label.AppendText($"  [s]× {u.Data.Name}[/s]\n"); return; }

		int maxHp = Math.Max(1, u.Data.BaseStats.GetValueOrDefault("HP", 1));
		int hpPct = u.CurrentHp * 100 / maxHp;
		string hpBar = new string('█', Math.Min(10, hpPct / 10)) + new string('░', Math.Max(0, 10 - hpPct / 10));

		// Class tags with color
		var classes = u.GetEffectiveClasses();
		string classStr = "";
		if (classes?.Count > 0)
			classStr = "[" + string.Join(" ", classes.Select(c => $"[color={ClassColor(c)}]{c}[/color]")) + "] ";

		// CC indicator
		string ccStr = u.IsCc ? " [color=yellow]CC[/color]" : "";

		label.AppendText($"  [{u.Position}] [color=#88ff88]{hpBar}[/color] {classStr}[color=white]{u.Data.Name}[/color]{ccStr} HP:{u.CurrentHp}/{maxHp} [color=red]AP:{StarStr(u.CurrentAp, u.MaxAp)}[/color] [color=blue]PP:{StarStr(u.CurrentPp, u.MaxPp)}[/color]\n");

		// Buffs
		var buffs = u.Buffs.Where(b => b.Ratio > 0).ToList();
		if (buffs.Count > 0)
		{
			string buffStr = string.Join(" ", buffs.Select(b => {
				string turns = b.RemainingTurns == -1 ? "∞" : b.RemainingTurns.ToString();
				return $"[color=#88ff88]{b.TargetStat}+{(int)(b.Ratio*100)}%[{turns}回合][/color]";
			}));
			label.AppendText($"    Buff: {buffStr}\n");
		}

		// Debuffs
		var debuffs = u.Buffs.Where(b => b.Ratio < 0).ToList();
		if (debuffs.Count > 0)
		{
			string debuffStr = string.Join(" ", debuffs.Select(b => {
				string turns = b.RemainingTurns == -1 ? "∞" : b.RemainingTurns.ToString();
				return $"[color=#ff8888]{b.TargetStat}{(int)(b.Ratio*100)}%[{turns}回合][/color]";
			}));
			label.AppendText($"    Debuff: {debuffStr}\n");
		}

		// Ailments
		if (u.Ailments.Count > 0)
		{
			string ailStr = string.Join(" ", u.Ailments.Select(a => $"[color=#ff4444]{a}[/color]"));
			label.AppendText($"    异常: {ailStr}\n");
		}

		// Temporal states
		if (u.TemporalStates.Count > 0)
		{
			string tmpStr = string.Join(" ", u.TemporalStates.Select(t => $"[color=#ffaa44]{t.Key}({t.RemainingCount})[/color]"));
			label.AppendText($"    标记: {tmpStr}\n");
		}

		// Equipped passives
		var pv = u.GetEquippedPassiveSkills();
		if (pv.Count > 0)
			label.AppendText($"    被动: [color=#888888]{string.Join(", ", pv.Select(p => p.Name))}[/color]\n");
	}

	private void RefreshBattleStatus(RichTextLabel label)
	{
		label.Clear();
		label.AppendText("[color=yellow]══ 战场 ══[/color]\n\n");
		label.AppendText("[color=cyan]▸ 己方[/color]\n");
		foreach (var u in _playerUnits) AppendUnitStatus(label, u);
		label.AppendText("\n[color=orange]▸ 敌方[/color]\n");
		foreach (var u in _enemyUnits) AppendUnitStatus(label, u);
	}

	private void StepOneAction(RichTextLabel label)
	{
		var result = _engine.StepOneAction();
		RefreshBattleStatus(label);
		if (result == SingleActionResult.PlayerWin || result == SingleActionResult.EnemyWin || result == SingleActionResult.Draw)
		{
			ClearButtons();
			_battleResult = result switch
			{
				SingleActionResult.PlayerWin => BattleResult.PlayerWin,
				SingleActionResult.EnemyWin => BattleResult.EnemyWin,
				_ => BattleResult.Draw
			};
			Log("\n=== " + _battleResult + " ===");
			AddBtn("结果", () => Go(GamePhase.Result));
		}
	}

	// ── PHASE 6: RESULT ──────────────────────────────────────

	private void Phase_Result()
	{
		_statusLabel.Text = $"战斗结果: {_battleResult}";
		Log($"\n=== {_battleResult} ===");
		AddBtn("再来一局", () => { Array.Clear(_playerSlots); Array.Clear(_enemySlots); Go(GamePhase.ModeSelect); });
		AddBtn("结束", () => { _statusLabel.Text = "游戏结束"; });
	}
}

// ── DRAG SOURCE ─────────────────────────────────────────────

public partial class DraggableChar : Button
{
	public string CharId;
	public DraggableChar() { MouseDefaultCursorShape = Control.CursorShape.Drag; }
	public override Variant _GetDragData(Vector2 atPosition)
	{
		SetDragPreview(new Label { Text = Text });
		return CharId;
	}
}

// ── DROP TARGET ─────────────────────────────────────────────

public partial class DropSlot : Panel
{
	private string[] _slots;
	private int _idx;
	private Action _onChanged;
	private Label _label;

	public DropSlot(string[] slots, int idx, Action onChanged)
	{
		_slots = slots; _idx = idx; _onChanged = onChanged;
		CustomMinimumSize = new Vector2(90, 52);
		AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.18f, 0.18f, 0.22f) });
		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_label);
		UpdateDisplay();
	}

	private void UpdateDisplay()
	{
		_label.Text = _slots[_idx] != null ? $"[{_idx + 1}]\n{_slots[_idx]}" : $"[{_idx + 1}]\n空";
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		if (_slots[_idx] == null) return default;
		SetDragPreview(new Label { Text = $"[{_idx + 1}] {_slots[_idx]}" });
		return $"SLOT:{_idx}:{_slots[_idx]}";
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
		=> data.VariantType == Variant.Type.String;

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		string raw = (string)data;
		if (raw.StartsWith("SLOT:"))
		{
			var parts = raw.Split(':', 3);
			int srcIdx = int.Parse(parts[1]);
			string srcChar = parts[2];
			string myChar = _slots[_idx];
			_slots[srcIdx] = myChar;
			_slots[_idx] = srcChar;
		}
		else
		{
			_slots[_idx] = raw;
		}
		UpdateDisplay();
		_onChanged?.Invoke();
	}
}
