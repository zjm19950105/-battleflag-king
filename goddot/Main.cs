using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public enum GamePhase
{
	ModeSelect,
	PlayerFormation,
	EnemyChoice,
	EnemyDragFormation,
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
	private int _passiveSetupIdx;
	private int _strategySetupIdx;
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

	private Button Btn(string text, Action onClick)
	{
		var b = new Button { Text = text };
		b.Pressed += () => onClick();
		return b;
	}
	private void AddBtn(string text, Action onClick) => _buttonBar.AddChild(Btn(text, onClick));
	private void ClearButtons() { foreach (var c in _buttonBar.GetChildren()) c.QueueFree(); }

	// ── STATE MACHINE ────────────────────────────────────────

	private void Go(GamePhase p)
	{
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

		}
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
			Go(GamePhase.PassiveSetup);
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

	private void Phase_PassiveSetup() { _passiveSetupIdx = 0; ShowPassive(); }

	private void ShowPassive()
	{
		ClearAll();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_passiveSetupIdx >= units.Count)
		{
			// Enemy preset strategies
			var eu = _enemyUnits.Where(u => u != null).ToList();
			for (int i = 0; i < eu.Count; i++)
			{
				var cfg = _enemyConfig != null && i < _enemyConfig.Count ? _enemyConfig[i] : ("", 0, "preset_aggressive");
				ApplyPresetStrat(eu[i], cfg);
			}
			Go(GamePhase.StrategySetup);
			return;
		}

		var unit = units[_passiveSetupIdx];
		_statusLabel.Text = $"▶  被动技能 [{unit.Data.Name}] — PP: {unit.GetUsedPp()}/{unit.MaxPp}";
		_leftPanel.AddChild(new Label { Text = $"{unit.Data.Name} 可用被动:\n" });

		foreach (var s in unit.GetAvailablePassiveSkillIds().Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null))
		{
			bool on = unit.EquippedPassiveSkillIds.Contains(s.Id);
			_leftPanel.AddChild(Btn($"{(on ? "[✓]" : "[  ]")} {s.Name} PP{s.PpCost} [{s.TriggerTiming}]", () => {
				if (on) { unit.EquippedPassiveSkillIds.Remove(s.Id); Log($"  卸下: {s.Name}"); }
				else if (!unit.CanEquipPassive(s.Id)) { Log("  PP不足!"); return; }
				else { unit.EquippedPassiveSkillIds.Add(s.Id); Log($"  装备: {s.Name}"); }
				ShowPassive();
			}));
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

	private void Phase_StrategySetup() { _strategySetupIdx = 0; ShowStrategy(); }

	private void ShowStrategy()
	{
		ClearAll();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_strategySetupIdx >= units.Count) { Go(GamePhase.Battle); return; }

		var unit = units[_strategySetupIdx];
		var avail = unit.GetAvailableActiveSkillIds().Select(id => _gameData.GetActiveSkill(id)).Where(s => s != null).ToList();
		_statusLabel.Text = "▶  策略配置 [" + unit.Data.Name + "] — 下拉选择技能";
		_leftPanel.AddChild(new Label { Text = unit.Data.Name + " 8条策略栏位:" });

		for (int i = 0; i < 8; i++)
		{
			int slot = i;
			var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;

			var row = new HBoxContainer();
			row.AddChild(new Label { Text = "[" + (slot + 1) + "]" });
			var opt = new OptionButton();
			opt.AddItem("(空)");
			int selected = 0;
			for (int j = 0; j < avail.Count; j++)
			{
				opt.AddItem(avail[j].Name + " (AP" + avail[j].ApCost + ")");
				if (s != null && avail[j].Id == s.SkillId) selected = j + 1;
			}
			opt.Selected = selected;
			int cap = slot;
			opt.ItemSelected += (long idx) => {
				while (unit.Strategies.Count <= cap) unit.Strategies.Add(new Strategy { SkillId = avail[0].Id });
				unit.Strategies[cap].SkillId = (int)idx == 0 ? avail[0].Id : avail[(int)idx - 1].Id;
			};
			row.AddChild(opt);
			_leftPanel.AddChild(row);
		}

		AddBtn("→ 确认/下一个", () => { _strategySetupIdx++; ShowStrategy(); });
		AddBtn("→ 全部默认跳过", () => { _strategySetupIdx = units.Count; ShowStrategy(); });
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

	private void RefreshBattleStatus(RichTextLabel label)
	{
		label.Clear();
		label.AppendText("[color=yellow]══ 战场 ══[/color]\n\n");
		label.AppendText("[color=cyan]▸ 己方[/color]\n");
		foreach (var u in _playerUnits)
		{
			if (u == null) continue;
			if (!u.IsAlive) { label.AppendText($"  [s]× {u.Data.Name}[/s]\n"); continue; }
			int hpPct = u.CurrentHp * 100 / Math.Max(1, u.Data.BaseStats.GetValueOrDefault("HP", 1));
			string hpBar = new string('█', Math.Min(10, hpPct / 10)) + new string('░', Math.Max(0, 10 - hpPct / 10));
			var pv = u.GetEquippedPassiveSkills();
			string classStr = u.GetEffectiveClasses()?.Count > 0 ? "(" + string.Join(",", u.GetEffectiveClasses()) + ") " : "";
			string pvStr = pv.Count > 0 ? " [" + string.Join(",", pv.Select(p => p.Name)) + "]" : "";
			label.AppendText($"  [{u.Position}] [color=#88ff88]{hpBar}[/color] {classStr}{u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp} PP:{u.CurrentPp}/{u.MaxPp}{pvStr}\n");
		}
		label.AppendText("\n[color=orange]▸ 敌方[/color]\n");
		foreach (var u in _enemyUnits)
		{
			if (u == null) continue;
			if (!u.IsAlive) { label.AppendText($"  [s]× {u.Data.Name}[/s]\n"); continue; }
			int hpPct = u.CurrentHp * 100 / Math.Max(1, u.Data.BaseStats.GetValueOrDefault("HP", 1));
			string hpBar = new string('█', Math.Min(10, hpPct / 10)) + new string('░', Math.Max(0, 10 - hpPct / 10));
			var pv = u.GetEquippedPassiveSkills();
			string classStr = u.GetEffectiveClasses()?.Count > 0 ? "(" + string.Join(",", u.GetEffectiveClasses()) + ") " : "";
			string pvStr = pv.Count > 0 ? " [" + string.Join(",", pv.Select(p => p.Name)) + "]" : "";
			label.AppendText($"  [{u.Position}] [color=#ff8888]{hpBar}[/color] {classStr}{u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp} PP:{u.CurrentPp}/{u.MaxPp}{pvStr}\n");
		}
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
