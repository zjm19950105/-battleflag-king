using Godot; // 引入 Godot 引擎的类，比如 Node2D、Label、按钮、布局容器等。
using System; // 引入 C# 基础库，比如 Random、Action、基础类型等。
using System.Collections.Generic; // 引入 List、Dictionary 这类集合工具。
using System.Linq; // 引入 ToList、Where、Select 等方便处理集合的方法。
using BattleKing.Core; // 引入本项目的战斗核心代码，比如 BattleEngine、BattleUnit、BattleContext。
using BattleKing.Data; // 引入本项目的数据读取和数据模型，比如 GameDataRepository、CharacterData。
using BattleKing.Ui; // 引入本项目拆出去的 UI 视图和流程控制类。

public partial class Main : Node2D // 定义 Godot 主场景脚本类；继承 Node2D 表示它是一个 2D 节点。
{
	private RichTextLabel _logLabel; // 战斗日志显示框，可以显示带格式的富文本。
	
	private HSplitContainer _splitter; // 左右分栏容器，用来把界面分成左侧和右侧。
	private VBoxContainer _leftPanel; // 左侧竖向面板，通常放主操作内容。
	private VBoxContainer _rightPanel; // 右侧竖向面板，通常放状态、日志或辅助内容。
	private Label _statusLabel; // 顶部或界面上的状态文字，比如当前阶段提示。
	private HBoxContainer _buttonBar; // 横向按钮栏，用来放下一步、返回等按钮。
	private ScrollContainer _buttonBarScroll; // 底部按钮栏外层滚动容器。
	private float _fontScale = 1.15f; // 字体缩放倍率，1.15 表示比基础字体稍大一点。
	private static readonly int BASE_FONT = 32; // 基础字体大小；readonly 表示运行时不再修改。
	private static readonly Vector2I DefaultWindowSize = new(1280, 720);
	private static readonly Vector2I MinimumWindowSize = new(960, 540);
	private static readonly Vector2I[] ResolutionPresets = // 预设分辨率数组，每个 Vector2I 是宽和高。
	{
		new(1280, 720), // 预设分辨率：1280 x 720。
		new(1600, 900), // 预设分辨率：1600 x 900。
		new(1920, 1080), // 预设分辨率：1920 x 1080。
		new(2048, 1152), // 预设分辨率：2048 x 1152。
		new(2560, 1440), // 预设分辨率：2560 x 1440。
		new(2560, 1600), // 预设分辨率：2560 x 1600。
		new(3440, 1440) // 预设分辨率：3440 x 1440。
	};

	private BattleUiFlowController _flow; // UI 流程控制器，负责在主菜单、布阵、战斗等阶段之间切换。
	private FormationSetupView _formationSetupView; // 布阵界面，负责选择玩家和敌人的上场角色。
	private EquipmentSetupView _equipmentSetupView; // 装备设置界面，负责给角色选择装备。
	private PassiveSetupView _passiveSetupView; // 被动技能设置界面，负责给角色选择被动。
	private StrategySetupView _strategySetupView; // 策略设置界面，负责配置 AI 条件和行动。
	private BattleView _battleView; // 战斗显示界面，负责展示战斗过程、按钮和日志。
	private TestSandboxView _testSandboxView; // 测试场景界面，用来快速拖阵容、调装备策略、跑战斗日志。
	private GameDataRepository _gameData; // 游戏数据仓库，负责从 JSON 读取角色、技能、装备等数据。
	private BattleSetupService _battleSetup; // 战斗创建服务，负责把选择结果组装成真正的战斗单位。
	private System.Random _rnd = new(); // 随机数工具，用于随机敌人、随机配置等。
	private List<CharacterData> _allChars; // 所有角色数据列表，通常从 _gameData 里读取出来。

	private string[] _playerSlots = new string[6]; // 玩家 6 个站位槽，字符串一般存角色 ID。
	private string[] _enemySlots = new string[6]; // 敌人 6 个站位槽，字符串一般存角色 ID。
	private bool _enemyUseDrag; // 敌人是否也使用拖拽方式布阵。
	private int _minSlots = 3; // 最少上场人数，当前默认至少 3 人。
	private int _selectedDay = 1; // 当前选择的天数或关卡日，默认第 1 天。
	private bool _selectedCc = false; // 是否启用 CC 相关规则，默认关闭。
	private bool _fullscreenEnabled = false; // 是否启用全屏，默认关闭，避免小屏幕启动时按钮跑出屏幕。
	private Vector2I _manualResolution = DefaultWindowSize; // 手动窗口分辨率，默认 1280 x 720。
	private string _displayNotice = ""; // 最近一次显示设置应用后的提示。

	private BattleContext _ctx; // 一场战斗的上下文，保存双方单位、回合状态、事件等。
	private BattleEngine _engine; // 战斗引擎，负责推进战斗状态机和执行行动。
	private List<BattleUnit> _playerUnits; // 玩家实际参战单位列表。
	private List<BattleUnit> _enemyUnits; // 敌人实际参战单位列表。
	private List<(string, int, string)> _enemyConfig; // 敌人配置列表；元组里通常放角色 ID、等级或站位、附加配置。
	private int _equipSetupIdx; // 当前正在设置装备的角色下标。
	private int _passiveSetupIdx; // 当前正在设置被动技能的角色下标。
	private int _strategySetupIdx; // 当前正在设置策略的角色下标。
	private bool _editingEnemyStrategies; // 当前是否在编辑敌方策略，而不是玩家策略。
	private BattleResult _battleResult; // 战斗结束后的结果，比如胜利、失败或平局。

	// ── GODOT ────────────────────────────────────────────────

	public override void _Ready()
	{
		GD.Print("Hello Battle King");
		InitializeDisplaySettings();
		_gameData = new GameDataRepository();
		_gameData.LoadAll(ProjectSettings.GlobalizePath("res://data"));
		_battleSetup = new BattleSetupService(_gameData);
		_allChars = _gameData.Characters.Values.ToList();
		SetupUi();
		_flow = new BattleUiFlowController(_leftPanel, _rightPanel, _buttonBar, _logLabel, Btn, RenderPhase);
		_formationSetupView = new FormationSetupView(_leftPanel, _rightPanel, _buttonBar, _allChars, Btn);
		_equipmentSetupView = new EquipmentSetupView(_leftPanel, _rightPanel, _buttonBar, Btn);
		_passiveSetupView = new PassiveSetupView(_leftPanel, _rightPanel, _buttonBar, Btn);
		_strategySetupView = new StrategySetupView(_leftPanel, _rightPanel, _buttonBar, Btn);
		_battleView = new BattleView(_leftPanel, _logLabel, _buttonBar, Btn, _flow.MoveLogToRightPanel);
		_testSandboxView = new TestSandboxView(_leftPanel, _rightPanel, _buttonBar, _logLabel, _allChars, _gameData, Btn, _flow.MoveLogToRightPanel, _flow.GoBack);
		Log("数据加载完成 — 战旗之王 Phase 1.3");
		_flow.Go(GamePhase.MainMenu);
	}

	private void InitializeDisplaySettings()
	{
		DisplayServer.WindowSetMinSize(GetWindowMinimumForCurrentScreen());
		ApplyDisplaySettings(fullscreen: false, DefaultWindowSize);
	}

	private static Vector2I GetCurrentScreenSize()
	{
		return DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
	}

	private static Rect2I GetCurrentScreenUsableRect()
	{
		var screen = DisplayServer.WindowGetCurrentScreen();
		var rect = DisplayServer.ScreenGetUsableRect(screen);
		return rect.Size.X > 0 && rect.Size.Y > 0
			? rect
			: new Rect2I(Vector2I.Zero, DisplayServer.ScreenGetSize(screen));
	}

	private static Vector2I ClampWindowResolution(Vector2I requested, out string notice)
	{
		var usable = GetCurrentScreenUsableRect();
		var max = new Vector2I(Math.Max(1, usable.Size.X), Math.Max(1, usable.Size.Y));
		var min = GetWindowMinimumForCurrentScreen();
		var clamped = new Vector2I(
			Math.Clamp(requested.X, min.X, max.X),
			Math.Clamp(requested.Y, min.Y, max.Y));

		notice = clamped == requested
			? ""
			: $"请求的窗口分辨率 {requested.X} x {requested.Y} 已调整为 {clamped.X} x {clamped.Y}，确保窗口留在当前显示器内。";
		return clamped;
	}

	private static Vector2I GetWindowMinimumForCurrentScreen()
	{
		var usable = GetCurrentScreenUsableRect();
		return new Vector2I(
			Math.Min(MinimumWindowSize.X, Math.Max(1, usable.Size.X)),
			Math.Min(MinimumWindowSize.Y, Math.Max(1, usable.Size.Y)));
	}

	private static void CenterWindow(Vector2I resolution)
	{
		var usable = GetCurrentScreenUsableRect();
		var position = new Vector2I(
			usable.Position.X + Math.Max(0, (usable.Size.X - resolution.X) / 2),
			usable.Position.Y + Math.Max(0, (usable.Size.Y - resolution.Y) / 2));
		DisplayServer.WindowSetPosition(position);
	}

	private void ApplyDisplaySettings(bool fullscreen, Vector2I resolution)
	{
		_fullscreenEnabled = fullscreen;
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);

		if (fullscreen)
		{
			var screen = GetCurrentScreenSize();
			DisplayServer.WindowSetSize(screen);
			DisplayServer.WindowSetPosition(GetCurrentScreenUsableRect().Position);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			_displayNotice = "";
			ApplyResponsiveLayout();
			return;
		}

		DisplayServer.WindowSetMinSize(GetWindowMinimumForCurrentScreen());
		var clamped = ClampWindowResolution(resolution, out var notice);
		_manualResolution = clamped;
		DisplayServer.WindowSetSize(clamped);
		CenterWindow(clamped);
		_displayNotice = notice;
		ApplyResponsiveLayout();
	}

	private void ApplyResponsiveLayout()
	{
		if (_splitter == null)
			return;

		var width = GetViewportRect().Size.X;
		_leftPanel.CustomMinimumSize = width <= 1400 ? new Vector2(420, 0) : new Vector2(520, 0);
#pragma warning disable CS0618
		_splitter.SplitOffset = width <= 1400 ? 680 : 760;
#pragma warning restore CS0618
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
		_splitter = new HSplitContainer();
		_splitter.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
#pragma warning disable CS0618
			_splitter.SplitOffset = 680;
#pragma warning restore CS0618
			_splitter.DraggerVisibility = Godot.SplitContainer.DraggerVisibilityEnum.Visible;
		root.AddChild(_splitter);

		_leftPanel = new VBoxContainer();
		_leftPanel.CustomMinimumSize = new Vector2(420, 0);
		_splitter.AddChild(_leftPanel);
		
		_rightPanel = new VBoxContainer();
		_rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_splitter.AddChild(_rightPanel);

		// 3) Button bar (prominent)
		_buttonBarScroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 64)
		};
		root.AddChild(_buttonBarScroll);
		_buttonBar = new HBoxContainer();
		_buttonBar.AddThemeConstantOverride("separation", 8);
		_buttonBarScroll.AddChild(_buttonBar);

		// 4) Log: RichTextLabel — larger font, fills available space
		_logLabel = new RichTextLabel();
		_logLabel.BbcodeEnabled = false;
		_logLabel.SelectionEnabled = true;
		_logLabel.ContextMenuEnabled = true;
		_logLabel.ScrollFollowing = true;
		_logLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_logLabel.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
		_logLabel.AddThemeFontSizeOverride("normal_font_size", 36);
		root.AddChild(_logLabel);
		ApplyResponsiveLayout();
	}

	// ── HELPERS ──────────────────────────────────────────────

	private void Log(string msg) => BattleLogTextRenderer.Append(_logLabel, msg);
	private void ClearLog() => _logLabel.Clear();

	private Button Btn(string text, Action onClick)
	{
		var b = new Button
		{
			Text = text,
			ClipText = true,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
		};
		b.CustomMinimumSize = new Vector2(0, 56);
		b.AddThemeFontSizeOverride("font_size", Math.Max(24, (int)(BASE_FONT * _fontScale)));
		b.Pressed += () => onClick();
		return b;
	}

	private void ReapplyFontScale()
	{
		int size = Math.Max(10, (int)(BASE_FONT * _fontScale));
		_statusLabel.AddThemeFontSizeOverride("font_size", size + 4);
		_logLabel.AddThemeFontSizeOverride("normal_font_size", size + 2);
		if (GetTree() != null)
			ApplyFontSizeRecursive(GetTree().Root, size);
	}

	private void ApplyFontSizeRecursive(Node node, int size)
	{
		if (node is Label l) l.AddThemeFontSizeOverride("font_size", size);
		if (node is Button b) b.AddThemeFontSizeOverride("font_size", size);
		if (node is CheckBox cb) cb.AddThemeFontSizeOverride("font_size", size);
		if (node is OptionButton ob) ob.AddThemeFontSizeOverride("font_size", size);
		if (node is RichTextLabel rl) rl.AddThemeFontSizeOverride("normal_font_size", size);
		if (node is TextEdit te) { te.AddThemeFontSizeOverride("font_size", size); }
		foreach (Node child in node.GetChildren())
			ApplyFontSizeRecursive(child, size);
	}

	// ── STATE MACHINE ────────────────────────────────────────

	private void RenderPhase(GamePhase p)
	{
		if (_buttonBarScroll != null)
			_buttonBarScroll.CustomMinimumSize = p == GamePhase.TestSandbox ? new Vector2(0, 34) : new Vector2(0, 64);

		switch (p)
		{
			case GamePhase.MainMenu:          Phase_MainMenu(); break;
			case GamePhase.DisplaySettings:   Phase_DisplaySettings(); break;
			case GamePhase.ModeSelect:        Phase_ModeSelect(); break;
			case GamePhase.PlayerFormation:   Phase_PlayerFormation(); break;
			case GamePhase.PassiveSetup:       Phase_PassiveSetup(); break;
			case GamePhase.StrategySetup:      Phase_StrategySetup(); break;
			case GamePhase.TestSandbox:        Phase_TestSandbox(); break;
			case GamePhase.Battle:             Phase_Battle(); break;
			case GamePhase.Result:             Phase_Result(); break;
			case GamePhase.EnemyChoice:       Phase_EnemyChoice(); break;
			case GamePhase.EnemyDragFormation: Phase_EnemyDragFormation(); break;
			case GamePhase.EquipmentSetup:     Phase_EquipmentSetup(); break;

		}
		if (p != GamePhase.TestSandbox)
			ReapplyFontScale();
	}

	// ── START MENU ───────────────────────────────────────────

	private void Phase_MainMenu()
	{
		_statusLabel.Text = "战旗之王";
		ClearLog();

		var title = new Label { Text = "战旗之王\n" };
		title.AddThemeFontSizeOverride("font_size", 56);
		_leftPanel.AddChild(title);
		_leftPanel.AddChild(new Label { Text = "战前编程策略 -> 自动战斗\n" });
		_leftPanel.AddChild(Btn("开始游戏", () => _flow.Go(GamePhase.ModeSelect)));
		_leftPanel.AddChild(Btn("测试场景", () => _flow.Go(GamePhase.TestSandbox)));
		_leftPanel.AddChild(Btn("显示设置", () => _flow.Go(GamePhase.DisplaySettings)));
		_leftPanel.AddChild(Btn("退出", () => GetTree().Quit()));

		var screen = GetCurrentScreenSize();
		_rightPanel.AddChild(new Label
		{
			Text = $"当前显示器: {screen.X} x {screen.Y}\n当前模式: {(_fullscreenEnabled ? "全屏" : "窗口")}\n\n如果窗口没有铺满屏幕，请进入显示设置，勾选全屏。"
		});
	}

	private void Phase_TestSandbox()
	{
		_statusLabel.Text = "测试场景";
		_statusLabel.AddThemeFontSizeOverride("font_size", TestSandboxView.CurrentTitleFontSize);
		_testSandboxView.Show();
	}

	private void Phase_DisplaySettings()
	{
		_statusLabel.Text = "显示设置";
		ClearLog();

		var screen = GetCurrentScreenSize();
		var usable = GetCurrentScreenUsableRect();
		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_leftPanel.AddChild(scroll);

		var settingsPanel = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		scroll.AddChild(settingsPanel);

		settingsPanel.AddChild(new Label
		{
			Text = $"当前显示器分辨率: {screen.X} x {screen.Y}\n可用窗口区域: {usable.Size.X} x {usable.Size.Y}\n当前窗口设置: {(_fullscreenEnabled ? "全屏" : $"{_manualResolution.X} x {_manualResolution.Y}")}\n"
		});

		var fullscreenCheck = new CheckBox
		{
			Text = "全屏（自动使用当前显示器分辨率）",
			ButtonPressed = _fullscreenEnabled
		};
		fullscreenCheck.Toggled += on => {
			if (on)
				ApplyDisplaySettings(true, GetCurrentScreenSize());
			else
				ApplyDisplaySettings(false, _manualResolution);
			_flow.Go(GamePhase.DisplaySettings);
		};
		settingsPanel.AddChild(fullscreenCheck);

		settingsPanel.AddChild(new Label { Text = "\n常用窗口分辨率:" });
		foreach (var preset in ResolutionPresets)
		{
			var captured = preset;
			settingsPanel.AddChild(Btn($"{captured.X} x {captured.Y}", () => {
				ApplyDisplaySettings(false, captured);
				_flow.Go(GamePhase.DisplaySettings);
			}));
		}

		settingsPanel.AddChild(new Label { Text = "\n手动输入窗口分辨率:" });
		var row = new HBoxContainer();
		var widthEdit = new LineEdit { Text = _manualResolution.X.ToString(), PlaceholderText = "宽度" };
		var heightEdit = new LineEdit { Text = _manualResolution.Y.ToString(), PlaceholderText = "高度" };
		widthEdit.CustomMinimumSize = new Vector2(180, 56);
		heightEdit.CustomMinimumSize = new Vector2(180, 56);
		row.AddChild(widthEdit);
		row.AddChild(new Label { Text = " x " });
		row.AddChild(heightEdit);
		settingsPanel.AddChild(row);

		settingsPanel.AddChild(Btn("应用手动分辨率", () => {
			if (!int.TryParse(widthEdit.Text, out var width) || !int.TryParse(heightEdit.Text, out var height))
			{
				Log("请输入数字宽度和高度。");
				return;
			}

			width = Math.Clamp(width, 800, 7680);
			height = Math.Clamp(height, 450, 4320);
			ApplyDisplaySettings(false, new Vector2I(width, height));
			_flow.Go(GamePhase.DisplaySettings);
		}));

		_rightPanel.AddChild(new Label
		{
			Text = $"说明:\n- 勾选全屏会自动读取你的显示器分辨率。\n- 应用窗口分辨率会自动退出全屏。\n- 超出当前显示器可用区域的窗口分辨率会自动调整。\n{(string.IsNullOrWhiteSpace(_displayNotice) ? "" : "\n提示:\n" + _displayNotice + "\n")}"
		});
		_flow.AddBackButton();
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
			_flow.Go(GamePhase.PlayerFormation);
		}));
		_leftPanel.AddChild(Btn("[3v3 对战] — 双方各上场3人", () => {
			_minSlots = 3;
			_flow.Go(GamePhase.PlayerFormation);
		}));
		_flow.AddBackButton();
	}

	// ── PHASE 1: PLAYER FORMATION ────────────────────────────

	private void Phase_PlayerFormation()
	{
		_statusLabel.Text = $"▶  我方阵型 — 拖拽角色到格子 (至少{_minSlots}人，最多6人)";
		ClearLog();
		_formationSetupView.BuildDragUI("我方", _playerSlots, _minSlots, slots => {
			int n = slots.Count(s => s != null);
			if (n < _minSlots) { Log($"至少需要{_minSlots}人 (当前{n})"); return; }
			Log($"我方阵型确认 ({n}人) Day{_selectedDay} CC:{_selectedCc}: {string.Join(",", slots.Where(s => s != null))}");
			_flow.Go(GamePhase.EnemyChoice);
		}, RebuildDragPhase);
		_flow.AddBackButton();
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
			_flow.Go(GamePhase.EnemyDragFormation);
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
		_flow.AddBackButton();
	}

	private void Phase_EnemyDragFormation()
	{
		_statusLabel.Text = $"▶  敌方阵型 — 拖拽角色到格子 (至少{_minSlots}人)";
		_formationSetupView.BuildDragUI("敌方", _enemySlots, _minSlots, slots => {
			int n = slots.Count(s => s != null);
			if (n < _minSlots) { Log($"至少需要{_minSlots}人 (当前{n})"); return; }
			Log($"敌方阵型确认 ({n}人)");
			CreateAllUnits(preset: false);
			_flow.Go(GamePhase.EquipmentSetup);
		}, RebuildDragPhase);
		_flow.AddBackButton();
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
		_flow.Go(GamePhase.PassiveSetup);
	}

	private void RebuildDragPhase()
	{
		_flow.ClearPanelsAndButtons();
		if (_flow.CurrentPhase == GamePhase.PlayerFormation)
			Phase_PlayerFormation();
		else if (_flow.CurrentPhase == GamePhase.EnemyDragFormation)
			Phase_EnemyDragFormation();
		ReapplyFontScale();
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
				var u = _battleSetup.CreateUnit(_playerSlots[i], true, i + 1, _selectedDay, isCc: _selectedCc);
				_playerUnits.Add(u);
				_ctx.PlayerUnits.Add(u);
			}
			else _ctx.PlayerUnits.Add(null);
		}

		if (preset)
		{
			for (int i = 0; i < _enemyConfig.Count; i++)
			{
				var u = _battleSetup.CreateUnit(_enemyConfig[i].Item1, false, _enemyConfig[i].Item2, _selectedDay, isCc: _selectedCc);
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
					var u = _battleSetup.CreateUnit(_enemySlots[i], false, i + 1, _selectedDay, isCc: _selectedCc);
					_enemyUnits.Add(u);
					_ctx.EnemyUnits.Add(u);
				}
				else _ctx.EnemyUnits.Add(null);
			}
		}

		// Give enemies additional random passives (on top of auto-equipped defaults)
		foreach (var u in _enemyUnits.Where(u => u != null))
			_battleSetup.AutoEquipAdditionalPassives(u, _rnd);
	}

	// ── PHASE 3: PASSIVE SETUP ───────────────────────────────

	// ── PHASE 3: EQUIPMENT SETUP ──────────────────────────

	private void Phase_EquipmentSetup() { _equipSetupIdx = 0; ShowEquipment(); }

	private void ShowEquipment()
	{
		_flow.ClearPanelsAndButtons();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_equipSetupIdx >= units.Count) { _flow.Go(GamePhase.PassiveSetup); return; }

		var unit = units[_equipSetupIdx];
		var cd = unit.Data;
		_statusLabel.Text = $"▶  装备配置 [{cd.Name}] — {EquipmentSetupView.GetSlotCount(unit)}槽";
		_equipmentSetupView.Show(
			unit,
			_gameData,
			() => { _equipSetupIdx++; ShowEquipment(); },
			() => { _equipSetupIdx = units.Count; ShowEquipment(); },
			_flow.GoBack);
		ReapplyFontScale();
	}

	private void Phase_PassiveSetup() { _passiveSetupIdx = 0; ShowPassive(); }

	private void ShowPassive()
	{
		_flow.ClearPanelsAndButtons();
		var units = _playerUnits.Where(u => u != null).ToList();
		if (_passiveSetupIdx >= units.Count)
		{
			_flow.Go(GamePhase.StrategySetup);
			return;
		}

		var unit = units[_passiveSetupIdx];
		_statusLabel.Text = _passiveSetupView.BuildStatusText(unit);
		_passiveSetupView.Show(
			unit,
			_gameData,
			Log,
			ShowPassive,
			() => { Log($"  [{unit.Data.Name}] 被动完成"); _passiveSetupIdx++; ShowPassive(); },
			() => { _passiveSetupIdx = units.Count; ShowPassive(); },
			_flow.GoBack);
		ReapplyFontScale();
	}

	// ── PHASE 4: STRATEGY SETUP ──────────────────────────────

	private void Phase_StrategySetup() { _strategySetupIdx = 0; _editingEnemyStrategies = false; ShowStrategy(); }

	private void ShowStrategy()
	{
		_flow.ClearPanelsAndButtons();
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
			_flow.Go(GamePhase.Battle);
			return;
		}

		var unit = units[_strategySetupIdx];
		string teamLabel = _editingEnemyStrategies ? "[敌方]" : "[我方]";
		_statusLabel.Text = $"▶  策略编程 {teamLabel} [{unit.Data.Name}] — 技能条件组合";
		_strategySetupView.Show(
			unit,
			_gameData,
			_editingEnemyStrategies,
			() => { _strategySetupIdx++; ShowStrategy(); },
			() => { _strategySetupIdx = units.Count; ShowStrategy(); },
			_flow.GoBack);
		ReapplyFontScale();
	}

// ── PHASE 5: BATTLE ──────────────────────────────────────

	// ── PHASE 5: BATTLE (step-by-step) ─────────────────────

	private void Phase_Battle()
	{
		_flow.ClearPanelsAndButtons();
		_statusLabel.Text = "▶  战斗 — 点击「下一名角色行动」逐个角色推进";
		_engine = new BattleEngine(_ctx);
		_battleView.Show(_engine, _ctx, _gameData, result => {
			_battleResult = result;
			_flow.Go(GamePhase.Result);
		});
	}

	// ── PHASE 6: RESULT ──────────────────────────────────────

	private void Phase_Result()
	{
		_statusLabel.Text = $"战斗结果: {_battleResult}";
		Log($"\n=== {_battleResult} ===");
		_flow.AddButton("再来一局", () => { Array.Clear(_playerSlots); Array.Clear(_enemySlots); _flow.Go(GamePhase.ModeSelect); });
		_flow.AddButton("结束", () => { _statusLabel.Text = "游戏结束"; });
	}
}
