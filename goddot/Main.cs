using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public enum GamePhase
{
    PlayerFormation,    // Drag characters to player slots
    EnemyFormation,     // Drag or pick preset for enemy
    PassiveSetup,
    StrategySetup,
    Battle,
    Result
}

public partial class Main : Node2D
{
    // UI containers
    private RichTextLabel _logLabel;
    private ScrollContainer _logScroll;
    private VBoxContainer _leftPanel;
    private VBoxContainer _rightPanel;
    private Label _statusLabel;
    private HBoxContainer _buttonBar;

    // State
    private GamePhase _phase;
    private GameDataRepository _gameData;
    private System.Random _rnd = new();
    private List<CharacterData> _allChars = new();

    // Formation
    private string[] _playerSlots = new string[6];   // characterId per slot 0-5
    private string[] _enemySlots = new string[6];
    private bool _enemyUseDrag;

    // Battle
    private BattleContext _ctx;
    private List<BattleUnit> _playerUnits;
    private List<BattleUnit> _enemyUnits;
    private List<(string charId, int pos, string presetId)> _enemyConfig;
    private int _passiveSetupIdx;
    private int _strategySetupIdx;
    private BattleResult _battleResult;

    public override void _Ready()
    {
        GD.Print("Hello Battle King");
        _gameData = new GameDataRepository();
        _gameData.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        _allChars = _gameData.Characters.Values.ToList();
        SetupUi();
        Log("数据加载完成 — 战旗之王 Phase 1.3");
        EnterPhase(GamePhase.PlayerFormation);
    }

    // ============================================================
    //  UI SETUP — two-panel layout
    // ============================================================

    private void SetupUi()
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);

        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(outer);

        _statusLabel = new Label { Text = "等待操作..." };
        _statusLabel.AddThemeColorOverride("font_color", new Color(1, 1, 0));
        outer.AddChild(_statusLabel);

        // Middle: left (pool) + right (grid)
        var mid = new HBoxContainer();
        mid.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        outer.AddChild(mid);

        _leftPanel = new VBoxContainer();
        _leftPanel.CustomMinimumSize = new Vector2(160, 0);
        mid.AddChild(_leftPanel);

        var separator = new VSeparator();
        mid.AddChild(separator);

        _rightPanel = new VBoxContainer();
        _rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mid.AddChild(_rightPanel);

        // Bottom: log
        _logScroll = new ScrollContainer();
        _logScroll.CustomMinimumSize = new Vector2(0, 180);
        outer.AddChild(_logScroll);

        _logLabel = new RichTextLabel();
        _logLabel.BbcodeEnabled = true;
        _logLabel.ScrollFollowing = true;
        _logScroll.AddChild(_logLabel);

        _buttonBar = new HBoxContainer();
        _buttonBar.AddThemeConstantOverride("separation", 6);
        outer.AddChild(_buttonBar);
    }

    private void Log(string msg) => _logLabel.AppendText(msg + "\n");
    private void ClearLog() => _logLabel.Clear();

    private void ClearPanel(Control panel)
    {
        foreach (var c in panel.GetChildren()) c.QueueFree();
    }

    private Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text };
        b.Pressed += () => onClick();
        return b;
    }

    private void AddButton(string text, Action onClick) => _buttonBar.AddChild(MakeButton(text, onClick));
    private void ClearButtons() { foreach (var c in _buttonBar.GetChildren()) c.QueueFree(); }

    // ============================================================
    //  STATE MACHINE
    // ============================================================

    private void EnterPhase(GamePhase phase)
    {
        _phase = phase;
        ClearPanel(_leftPanel);
        ClearPanel(_rightPanel);
        ClearButtons();
        switch (phase)
        {
            case GamePhase.PlayerFormation: BuildFormationUI("我方", _playerSlots, GamePhase.EnemyFormation); break;
            case GamePhase.EnemyFormation:  BuildEnemyChoiceUI(); break;
            case GamePhase.PassiveSetup:    BuildPassiveSetupUI(); break;
            case GamePhase.StrategySetup:   BuildStrategySetupUI(); break;
            case GamePhase.Battle:          StartBattle(); break;
            case GamePhase.Result:          BuildResultUI(); break;
        }
    }

    // ============================================================
    //  DRAG-AND-DROP FORMATION UI
    // ============================================================

    private void BuildFormationUI(string label, string[] slots, GamePhase nextPhase)
    {
        _statusLabel.Text = $"{label}阵型 — 从左侧角色池拖拽到阵型格子 (重复角色可用)";
        ClearLog();

        // Left: character pool
        var poolLabel = new Label { Text = "角色池 (可重复拖拽)" };
        _leftPanel.AddChild(poolLabel);

        foreach (var ch in _allChars)
        {
            var dragBtn = new DraggableChar { Text = ch.Name, CharId = ch.Id };
            _leftPanel.AddChild(dragBtn);
        }

        // Right: formation grid
        var gridLabel = new Label { Text = $"前 排" };
        _rightPanel.AddChild(gridLabel);
        var frontRow = new HBoxContainer();
        _rightPanel.AddChild(frontRow);
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var slot = new DropSlot(slots, idx, () => RefreshFormationUI(slots));
            frontRow.AddChild(slot);
            _rightPanel.AddChild(MakeButton("×", () => { slots[idx] = null; RefreshFormationUI(slots); }));
        }

        var backLabel = new Label { Text = $"後 排" };
        _rightPanel.AddChild(backLabel);
        var backRow = new HBoxContainer();
        _rightPanel.AddChild(backRow);
        for (int i = 3; i < 6; i++)
        {
            int idx = i;
            var slot = new DropSlot(slots, idx, () => RefreshFormationUI(slots));
            backRow.AddChild(slot);
            _rightPanel.AddChild(MakeButton("×", () => { slots[idx] = null; RefreshFormationUI(slots); }));
        }

        int filled = slots.Count(s => s != null);
        if (filled >= 3)
            AddButton($"[确认 {label}阵型 ({filled}人)]", () => EnterPhase(nextPhase));
        else
            AddButton($"[需要至少3人 (当前{filled})]", () => { });
    }

    private void RefreshFormationUI(string[] slots)
    {
        // Just redraw the current phase
        if (_phase == GamePhase.PlayerFormation)
            BuildFormationUI("我方", _playerSlots, GamePhase.EnemyFormation);
        else if (_phase == GamePhase.EnemyFormation && _enemyUseDrag)
            BuildFormationUI("敌方", _enemySlots, GamePhase.PassiveSetup);
    }

    // DraggableChar and DropSlot are defined at namespace level below (Godot 4 requires
    // classes deriving from GodotObject to be top-level or partial). See bottom of file.
    // ============================================================

    // ============================================================
    //  ENEMY CHOICE: preset OR drag
    // ============================================================

    private void BuildEnemyChoiceUI()
    {
        _statusLabel.Text = "选择敌方配置方式";
        ClearLog();

        var modeLabel = new Label { Text = "选择敌人配置方式:" };
        _leftPanel.AddChild(modeLabel);
        var dragBtn = MakeButton("自定义拖拽敌人 (同角色池)", () => {
            _enemyUseDrag = true;
            Array.Clear(_enemySlots);
            EnterDragEnemyFormation();
        });
        _leftPanel.AddChild(dragBtn);

        var formations = _gameData.EnemyFormations.Values.ToList();
        var presetLabel = new Label { Text = "\n预设敌人:" };
        _leftPanel.AddChild(presetLabel);
        for (int i = 0; i < formations.Count; i++)
        {
            var f = formations[i];
            int idx = i;
            _leftPanel.AddChild(MakeButton($"{f.Name} [难度{f.Difficulty}]", () => {
                _enemyUseDrag = false;
                SelectPresetEnemy(idx);
            }));
        }
    }

    private void EnterDragEnemyFormation()
    {
        _enemyUseDrag = true;
        BuildFormationUI("敌方", _enemySlots, GamePhase.PassiveSetup);
    }

    private void SelectPresetEnemy(int idx)
    {
        var formations = _gameData.EnemyFormations.Values.ToList();
        var selected = formations[idx];
        Log($"选择敌人: {selected.Name}");

        if (selected.Id == "fmt_random" || selected.Units.Count == 0)
        {
            var used = _playerSlots.Where(s => s != null).Distinct().ToList();
            var available = _gameData.Characters.Keys.ToList();  // Allow overlap with player
            var picked = available.OrderBy(_ => _rnd.Next()).Take(3).ToList();
            _enemyConfig = new() { (picked[0], 1, "preset_aggressive"), (picked[1], 2, "preset_aggressive"), (picked[2], 3, "preset_aggressive") };
        }
        else
        {
            _enemyConfig = selected.Units.Select(u => (u.CharacterId, u.Position, u.StrategyPresetId ?? "preset_aggressive")).ToList();
        }

        CreateAllUnits(preset: true);
        EnterPhase(GamePhase.PassiveSetup);
    }

    // ============================================================
    //  UNIT CREATION
    // ============================================================

    private void CreateAllUnits(bool preset = false)
    {
        _ctx = new BattleContext(_gameData);
        _playerUnits = new();
        _enemyUnits = new();

        // Player: from slots 0-5
        for (int i = 0; i < 6; i++)
        {
            if (_playerSlots[i] != null)
            {
                var unit = CreateUnit(_gameData, _playerSlots[i], true, i + 1);
                DayProgression.Apply(unit, 1);
                _playerUnits.Add(unit);
                _ctx.PlayerUnits.Add(unit);
            }
            else
            {
                _ctx.PlayerUnits.Add(null);
            }
        }

        // Enemy: from slots (drag) or config (preset)
        if (preset)
        {
            for (int i = 0; i < 3; i++)
            {
                var unit = CreateUnit(_gameData, _enemyConfig[i].charId, false, _enemyConfig[i].pos);
                DayProgression.Apply(unit, 1);
                _enemyUnits.Add(unit);
                _ctx.EnemyUnits.Add(unit);
            }
            while (_ctx.EnemyUnits.Count < 6) _ctx.EnemyUnits.Add(null);
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                if (_enemySlots[i] != null)
                {
                    var unit = CreateUnit(_gameData, _enemySlots[i], false, i + 1);
                    DayProgression.Apply(unit, 1);
                    _enemyUnits.Add(unit);
                    _ctx.EnemyUnits.Add(unit);
                }
                else
                {
                    _ctx.EnemyUnits.Add(null);
                }
            }
        }

        // Auto enemy passives
        foreach (var unit in _enemyUnits.Where(u => u != null))
        {
            var avail = unit.GetAvailablePassiveSkillIds()
                .Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null)
                .OrderBy(_ => _rnd.Next()).ToList();
            var equipped = new List<string>();
            int usedPp = 0;
            foreach (var s in avail) { if (usedPp + s.PpCost > unit.MaxPp) break; equipped.Add(s.Id); usedPp += s.PpCost; }
            unit.EquippedPassiveSkillIds = equipped;
        }
    }

    // ============================================================
    //  PHASE 3: PASSIVE SETUP
    // ============================================================

    private void BuildPassiveSetupUI()
    {
        _passiveSetupIdx = 0;
        ShowPassiveForCurrent();
    }

    private void ShowPassiveForCurrent()
    {
        ClearPanel(_leftPanel); ClearPanel(_rightPanel); ClearButtons();
        if (_passiveSetupIdx >= _playerUnits.Count(u => u != null))
        {
            var eu = _enemyUnits.Where(u => u != null).ToList();
            for (int i = 0; i < eu.Count; i++)
            {
                var config = _enemyConfig != null && i < _enemyConfig.Count ? _enemyConfig[i] : ("", 0, "preset_aggressive");
                ApplyPresetStrategiesSingle(eu[i], config);
            }
            EnterPhase(GamePhase.StrategySetup);
            return;
        }

        var unit = _playerUnits.Where(u => u != null).ElementAt(_passiveSetupIdx);
        _statusLabel.Text = $"[{unit.Data.Name}] 被动技能 (PP: {unit.GetUsedPp()}/{unit.MaxPp})";
        Log($"\n--- [{unit.Data.Name}] 被动配置 ---");

        var available = unit.GetAvailablePassiveSkillIds()
            .Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null).ToList();

        var label = new Label { Text = $"{unit.Data.Name} — 可用被动:" };
        _leftPanel.AddChild(label);

        foreach (var skill in available)
        {
            bool on = unit.EquippedPassiveSkillIds.Contains(skill.Id);
            string mark = on ? "[✓]" : "[  ]";
            _leftPanel.AddChild(MakeButton($"{mark} {skill.Name} PP{skill.PpCost}", () => TogglePassive(unit, skill)));
        }

        AddButton("→ 下一个", () => { Log($"  [{unit.Data.Name}] 被动完成"); _passiveSetupIdx++; ShowPassiveForCurrent(); });
        AddButton("→ 全部跳过", () => { _passiveSetupIdx = _playerUnits.Count(u => u != null); ShowPassiveForCurrent(); });
    }

    private void TogglePassive(BattleUnit unit, PassiveSkillData skill)
    {
        if (unit.EquippedPassiveSkillIds.Contains(skill.Id))
        { unit.EquippedPassiveSkillIds.Remove(skill.Id); Log($"  卸下: {skill.Name}"); }
        else
        {
            if (!unit.CanEquipPassive(skill.Id)) { Log("  PP不足!"); return; }
            unit.EquippedPassiveSkillIds.Add(skill.Id); Log($"  装备: {skill.Name}");
        }
        ShowPassiveForCurrent();
    }

    private void ApplyPresetStrategiesSingle(BattleUnit unit, (string charId, int pos, string presetId) config)
    {
        var pid = string.IsNullOrEmpty(config.presetId) || !_gameData.StrategyPresets.ContainsKey(config.presetId)
            ? "preset_aggressive" : config.presetId;
        var preset = _gameData.GetStrategyPreset(pid);
        var ids = unit.GetAvailableActiveSkillIds();
        unit.Strategies = preset.Strategies.Select(ps => {
            string sid = ps.SkillIndex >= 0 && ps.SkillIndex < ids.Count ? ids[ps.SkillIndex] : ids.FirstOrDefault();
            return sid != null ? new Strategy { SkillId = sid, Condition1 = ps.Condition1, Condition2 = ps.Condition2, Mode1 = ps.Mode1, Mode2 = ps.Mode2 } : null;
        }).Where(s => s != null).ToList();
    }

    // ============================================================
    //  PHASE 4: STRATEGY SETUP
    // ============================================================

    private void BuildStrategySetupUI()
    {
        _strategySetupIdx = 0;
        ShowStrategyForCurrent();
    }

    private void ShowStrategyForCurrent()
    {
        ClearPanel(_leftPanel); ClearPanel(_rightPanel); ClearButtons();
        var units = _playerUnits.Where(u => u != null).ToList();
        if (_strategySetupIdx >= units.Count) { EnterPhase(GamePhase.Battle); return; }

        var unit = units[_strategySetupIdx];
        var available = unit.GetAvailableActiveSkillIds().Select(id => _gameData.GetActiveSkill(id)).Where(s => s != null).ToList();
        _statusLabel.Text = $"[{unit.Data.Name}] 策略 (8条)";
        Log($"\n--- [{unit.Data.Name}] 策略配置 ---");

        var label = new Label { Text = $"{unit.Data.Name} — 8条策略栏位:" };
        _leftPanel.AddChild(label);

        for (int i = 0; i < 8; i++)
        {
            var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;
            int slot = i;
            string name = s != null ? available.FirstOrDefault(sk => sk.Id == s.SkillId)?.Name ?? s.SkillId : "(空)";
            _leftPanel.AddChild(MakeButton($"[{slot + 1}] {name}", () => EditStrategySlot(unit, slot, available)));
        }

        AddButton("→ 确认/下一个", () => { Log($"  [{unit.Data.Name}] 策略完成"); _strategySetupIdx++; ShowStrategyForCurrent(); });
        AddButton("→ 全部默认跳过", () => { _strategySetupIdx = units.Count; ShowStrategyForCurrent(); });
    }

    private void EditStrategySlot(BattleUnit unit, int slot, List<ActiveSkillData> available)
    {
        ClearPanel(_leftPanel); ClearPanel(_rightPanel); ClearButtons();
        _statusLabel.Text = $"[{unit.Data.Name}] 栏位[{slot + 1}] — 选技能";
        foreach (var skill in available)
        {
            _leftPanel.AddChild(MakeButton($"{skill.Name} AP{skill.ApCost}", () => {
                while (unit.Strategies.Count <= slot)
                    unit.Strategies.Add(new Strategy { SkillId = available[0].Id, Condition1 = null, Condition2 = null, Mode1 = ConditionMode.Only, Mode2 = ConditionMode.Only });
                unit.Strategies[slot].SkillId = skill.Id;
                ShowStrategyForCurrent();
            }));
        }
        AddButton("← 返回", () => ShowStrategyForCurrent());
    }

    // ============================================================
    //  PHASE 5: BATTLE
    // ============================================================

    private void StartBattle()
    {
        ClearLog();
        Log("=== 队伍信息 ===\n己方:");
        foreach (var u in _playerUnits.Where(u => u != null))
            Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        Log("敌方:");
        foreach (var u in _enemyUnits.Where(u => u != null))
            Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        Log("\n=== 战斗开始 ===\n");

        var engine = new BattleEngine(_ctx);
        engine.OnLog = msg => Log(msg);
        var proc = new BattleKing.Skills.PassiveSkillProcessor(engine.EventBus, _gameData, msg => Log(msg), engine.EnqueueAction);
        proc.SubscribeAll();

        _battleResult = engine.StartBattle();
        EnterPhase(GamePhase.Result);
    }

    // ============================================================
    //  PHASE 6: RESULT
    // ============================================================

    private void BuildResultUI()
    {
        _statusLabel.Text = $"战斗结果: {_battleResult}";
        Log($"\n=== {_battleResult} ===");
        AddButton("再来一局", () => EnterPhase(GamePhase.PlayerFormation));
        AddButton("结束", () => { _statusLabel.Text = "游戏结束"; ClearButtons(); });
    }

    // ============================================================
    //  UNIT FACTORY
    // ============================================================

    private BattleUnit CreateUnit(GameDataRepository gameData, string characterId, bool isPlayer, int position, bool isCc = false)
    {
        var charData = gameData.GetCharacter(characterId);
        var unit = new BattleUnit(charData, gameData, isPlayer, isCc) { IsPlayer = isPlayer, Position = position };
        var equipIds = isCc && charData.CcInitialEquipmentIds != null && charData.CcInitialEquipmentIds.Count > 0
            ? charData.CcInitialEquipmentIds : charData.InitialEquipmentIds;
        if (equipIds != null)
            foreach (var eid in equipIds) { var ed = gameData.GetEquipment(eid); if (ed != null) unit.Equipment.Equip(ed); }
        if (!unit.Equipment.ValidateWeaponEquipped())
            GD.PushWarning($"角色 {charData.Name} 武器槽为空");
        var availIds = unit.GetAvailableActiveSkillIds();
        var firstId = availIds.Count > 0 ? availIds[0] : null;
        if (firstId != null)
            unit.Strategies = Enumerable.Range(0, 8).Select(_ => new Strategy { SkillId = firstId, Condition1 = null, Condition2 = null, Mode1 = ConditionMode.Only, Mode2 = ConditionMode.Only }).ToList();
        return unit;
    }
}

// ============================================================
//  DRAG-AND-DROP: must be top-level for Godot 4 source generators
// ============================================================

public partial class DraggableChar : Button
{
    public string CharId;
    public DraggableChar()
    {
        MouseDefaultCursorShape = Control.CursorShape.Drag;
    }
    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Label { Text = Text };
        SetDragPreview(preview);
        return CharId;
    }
}

public partial class DropSlot : Panel
{
    private string[] _slots;
    private int _idx;
    private Action _onChanged;
    private Label _label;

    public DropSlot(string[] slots, int index, Action onChanged)
    {
        _slots = slots;
        _idx = index;
        _onChanged = onChanged;
        CustomMinimumSize = new Vector2(80, 50);
        var style = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f) };
        AddThemeStyleboxOverride("panel", style);
        _label = new Label { Text = $"[{index + 1}] 空", HorizontalAlignment = HorizontalAlignment.Center };
        _label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_label);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_slots[_idx] != null)
            _label.Text = $"[{_idx + 1}] {_slots[_idx]}";
        else
            _label.Text = $"[{_idx + 1}] 空";
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
        => data.VariantType == Variant.Type.String;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _slots[_idx] = (string)data;
        UpdateDisplay();
        _onChanged?.Invoke();
    }
}
