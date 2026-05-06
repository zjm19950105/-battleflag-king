using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public enum GamePhase
{
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
	private RichTextLabel _logLabel;
	private ScrollContainer _logScroll;
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

	private BattleContext _ctx;
	private List<BattleUnit> _playerUnits;
	private List<BattleUnit> _enemyUnits;
	private List<(string, int, string)> _enemyConfig;
	private int _passiveSetupIdx;
	private int _strategySetupIdx;
	private BattleResult _battleResult;

	// ── GODOT ────────────────────────────────────────────────

	public override void _Ready()
	{
		GD.Print("Hello Battle King");
		_gameData = new GameDataRepository();
		_gameData.LoadAll(ProjectSettings.GlobalizePath("res://data"));
		_allChars = _gameData.Characters.Values.ToList();
		SetupUi();
		Log("数据加载完成 — 战旗之王 Phase 1.3");
		Go(GamePhase.PlayerFormation);
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

		// 4) Scrollable log — dark background for visibility
		_logScroll = new ScrollContainer();
		_logScroll.CustomMinimumSize = new Vector2(0, 200);
		var logBg = new Panel();
		logBg.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.08f) });
		logBg.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		logBg.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_logScroll.AddChild(logBg);
		_logLabel = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true };
		_logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		logBg.AddChild(_logLabel);
		root.AddChild(_logScroll);
	}

	// ── HELPERS ──────────────────────────────────────────────

	private void Log(string msg) => _logLabel.AppendText(msg + "\n");
	private void ClearLog() => _logLabel.Clear();
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
		ClearAll();
		switch (p)
		{
			case GamePhase.PlayerFormation:   Phase_PlayerFormation(); break;
			case GamePhase.EnemyChoice:       Phase_EnemyChoice(); break;
			case GamePhase.EnemyDragFormation: Phase_EnemyDragFormation(); break;
			case GamePhase.PassiveSetup:       Phase_PassiveSetup(); break;
			case GamePhase.StrategySetup:      Phase_StrategySetup(); break;
			case GamePhase.Battle:             Phase_Battle(); break;
			case GamePhase.Result:             Phase_Result(); break;
		}
	}

	// ── PHASE 1: PLAYER FORMATION ────────────────────────────

	private void Phase_PlayerFormation()
	{
		_statusLabel.Text = "▶  我方阵型 — 拖拽角色到格子 (至少3人，最多6人)";
		ClearLog();
		BuildDragUI("我方", _playerSlots, () => {
			int n = _playerSlots.Count(s => s != null);
			if (n < 3) { Log($"至少需要3人 (当前{n})"); return; }
			Log($"我方阵型确认 ({n}人): {string.Join(",", _playerSlots.Where(s => s != null))}");
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
		_statusLabel.Text = "▶  敌方阵型 — 拖拽角色到格子 (至少3人)";
		BuildDragUI("敌方", _enemySlots, () => {
			int n = _enemySlots.Count(s => s != null);
			if (n < 3) { Log($"至少需要3人 (当前{n})"); return; }
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
			var picked = available.OrderBy(_ => _rnd.Next()).Take(3).ToList();
			_enemyConfig = new() { (picked[0], 1, "preset_aggressive"), (picked[1], 2, "preset_aggressive"), (picked[2], 3, "preset_aggressive") };
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
		// Left: character pool
		_leftPanel.AddChild(new Label { Text = "角色池 (可重复拖拽)\n" });
		foreach (var ch in _allChars)
			_leftPanel.AddChild(new DraggableChar { Text = ch.Name, CharId = ch.Id });

		// Right: 6-slot formation grid
		_rightPanel.AddChild(new Label { Text = "前 排" });
		var frontRow = new HBoxContainer();
		_rightPanel.AddChild(frontRow);
		for (int i = 0; i < 3; i++) AddSlot(frontRow, slots, i);

		_rightPanel.AddChild(new Label { Text = "\n後 排" });
		var backRow = new HBoxContainer();
		_rightPanel.AddChild(backRow);
		for (int i = 3; i < 6; i++) AddSlot(backRow, slots, i);

		// Bottom: confirm
		int filled = slots.Count(s => s != null);
		var confirmBtn = Btn($"✓ 确认{teamLabel}阵型 ({filled}人)", onConfirm);
		if (filled < 3) confirmBtn.Disabled = true;
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
				var u = NewUnit(_playerSlots[i], true, i + 1);
				_playerUnits.Add(u);
				_ctx.PlayerUnits.Add(u);
			}
			else _ctx.PlayerUnits.Add(null);
		}

		if (preset)
		{
			for (int i = 0; i < _enemyConfig.Count; i++)
			{
				var u = NewUnit(_enemyConfig[i].Item1, false, _enemyConfig[i].Item2);
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
					var u = NewUnit(_enemySlots[i], false, i + 1);
					_enemyUnits.Add(u);
					_ctx.EnemyUnits.Add(u);
				}
				else _ctx.EnemyUnits.Add(null);
			}
		}

		// Give enemies random passives
		foreach (var u in _enemyUnits.Where(u => u != null))
		{
			var avail = u.GetAvailablePassiveSkillIds().Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null).OrderBy(_ => _rnd.Next()).ToList();
			var eq = new List<string>(); int pp = 0;
			foreach (var s in avail) { if (pp + s.PpCost > u.MaxPp) break; eq.Add(s.Id); pp += s.PpCost; }
			u.EquippedPassiveSkillIds = eq;
		}
	}

	private BattleUnit NewUnit(string charId, bool isPlayer, int pos)
	{
		var cd = _gameData.GetCharacter(charId);
		var u = new BattleUnit(cd, _gameData, isPlayer) { IsPlayer = isPlayer, Position = pos };
		DayProgression.Apply(u, 1);
		var eq = u.IsCc && cd.CcInitialEquipmentIds?.Count > 0 ? cd.CcInitialEquipmentIds : cd.InitialEquipmentIds;
		if (eq != null) foreach (var eid in eq) { var ed = _gameData.GetEquipment(eid); if (ed != null) u.Equipment.Equip(ed); }
		var firstId = u.GetAvailableActiveSkillIds().FirstOrDefault();
		if (firstId != null) u.Strategies = Enumerable.Range(0, 8).Select(_ => new Strategy { SkillId = firstId }).ToList();
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
		_statusLabel.Text = $"▶  策略配置 [{unit.Data.Name}] — 点击栏位选择技能";
		_leftPanel.AddChild(new Label { Text = $"{unit.Data.Name} 8条策略栏位:\n" });

		for (int i = 0; i < 8; i++)
		{
			int slot = i;
			var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;
			string name = s != null ? avail.FirstOrDefault(sk => sk.Id == s.SkillId)?.Name ?? s.SkillId : "(空)";
			_leftPanel.AddChild(Btn($"[{slot + 1}] {name}", () => EditSlot(unit, slot, avail)));
		}

		AddBtn("→ 确认/下一个", () => { _strategySetupIdx++; ShowStrategy(); });
		AddBtn("→ 全部默认跳过", () => { _strategySetupIdx = units.Count; ShowStrategy(); });
	}

	private void EditSlot(BattleUnit unit, int slot, List<ActiveSkillData> avail)
	{
		ClearAll();
		_statusLabel.Text = $"[{unit.Data.Name}] 栏位[{slot + 1}] — 选择技能";
		foreach (var s in avail)
			_leftPanel.AddChild(Btn($"{s.Name} AP{s.ApCost} 威力{s.Power}", () => {
				while (unit.Strategies.Count <= slot) unit.Strategies.Add(new Strategy { SkillId = avail[0].Id });
				unit.Strategies[slot].SkillId = s.Id;
				ShowStrategy();
			}));
		AddBtn("← 返回", () => ShowStrategy());
	}

	// ── PHASE 5: BATTLE ──────────────────────────────────────

	private void Phase_Battle()
	{
		// Hide formation area during battle — log takes full screen
		_formationArea.Visible = false;

		// Expand log to fill available space
		_logScroll.CustomMinimumSize = new Vector2(0, 0);
		_logScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		ClearLog();
		Log("=== 队伍信息 ===\n[己方]");
		foreach (var u in _playerUnits.Where(u => u != null))
			Log($"  [{u.Position}] {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
		Log("[敌方]");
		foreach (var u in _enemyUnits.Where(u => u != null))
			Log($"  [{u.Position}] {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
		Log("");

		var engine = new BattleEngine(_ctx);
		engine.OnLog = msg => { _logLabel.AppendText(msg + "\n"); ScrollToBottom(); };
		var proc = new BattleKing.Skills.PassiveSkillProcessor(engine.EventBus, _gameData,
			msg => { _logLabel.AppendText(msg + "\n"); ScrollToBottom(); },
			engine.EnqueueAction);
		proc.SubscribeAll();
		_battleResult = engine.StartBattle();

		// Restore layout for result phase
		_formationArea.Visible = true;
		_logScroll.CustomMinimumSize = new Vector2(0, 200);
		_logScroll.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

		Go(GamePhase.Result);
	}

	/// <summary>Force scroll log to the latest line</summary>
	private void ScrollToBottom()
	{
		var vbar = _logScroll.GetVScrollBar();
		if (vbar != null)
		{
			// Defer by one frame so RichTextLabel has updated its content height
			CallDeferred(nameof(ScrollToBottomDeferred));
		}
	}
	private void ScrollToBottomDeferred()
	{
		var vbar = _logScroll.GetVScrollBar();
		if (vbar != null) vbar.Value = vbar.MaxValue;
	}

	// ── PHASE 6: RESULT ──────────────────────────────────────

	private void Phase_Result()
	{
		_statusLabel.Text = $"战斗结果: {_battleResult}";
		Log($"\n=== {_battleResult} ===");
		AddBtn("再来一局", () => { Array.Clear(_playerSlots); Array.Clear(_enemySlots); Go(GamePhase.PlayerFormation); });
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
