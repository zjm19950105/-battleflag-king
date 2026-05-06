using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Ai;

public enum GamePhase
{
    TeamSelect,
    Formation,
    EnemySelect,
    PassiveSetup,
    StrategySetup,
    Battle,
    Result
}

public partial class Main : Node2D
{
    // UI
    private RichTextLabel _logLabel;
    private ScrollContainer _logScroll;
    private HBoxContainer _buttonBar;
    private Label _statusLabel;

    // State
    private GamePhase _phase;
    private GameDataRepository _gameData;
    private System.Random _rnd = new();

    // Selections
    private List<CharacterData> _allChars = new();
    private List<CharacterData> _selectedChars = new();
    private List<int> _selectedPositions = new();
    private int _formationCharIdx;

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
        SetupUi();
        _gameData = new GameDataRepository();
        _gameData.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        _allChars = _gameData.Characters.Values.ToList();
        Log("数据加载完成 — 战旗之王 Phase 1.3");
        EnterPhase(GamePhase.TeamSelect);
    }

    // ============================================================
    //  UI SETUP
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

        _logScroll = new ScrollContainer();
        _logScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        outer.AddChild(_logScroll);

        _logLabel = new RichTextLabel();
        _logLabel.BbcodeEnabled = true;
        _logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _logLabel.ScrollFollowing = true;
        _logScroll.AddChild(_logLabel);

        _buttonBar = new HBoxContainer();
        _buttonBar.AddThemeConstantOverride("separation", 6);
        outer.AddChild(_buttonBar);
    }

    private void Log(string msg)
    {
        _logLabel.AppendText(msg + "\n");
    }

    private void ClearLog() => _logLabel.Clear();

    private void ClearButtons()
    {
        foreach (var child in _buttonBar.GetChildren())
            child.QueueFree();
    }

    private Button AddButton(string text, Action onClick)
    {
        var btn = new Button { Text = text };
        btn.Pressed += () => onClick();
        _buttonBar.AddChild(btn);
        return btn;
    }

    // ============================================================
    //  STATE MACHINE
    // ============================================================

    private void EnterPhase(GamePhase phase)
    {
        _phase = phase;
        ClearButtons();
        switch (phase)
        {
            case GamePhase.TeamSelect:    BuildTeamSelectUI(); break;
            case GamePhase.Formation:     BuildFormationUI(); break;
            case GamePhase.EnemySelect:   BuildEnemySelectUI(); break;
            case GamePhase.PassiveSetup:  BuildPassiveSetupUI(); break;
            case GamePhase.StrategySetup: BuildStrategySetupUI(); break;
            case GamePhase.Battle:        StartBattle(); break;
            case GamePhase.Result:        BuildResultUI(); break;
        }
    }

    // ============================================================
    //  PHASE 1: TEAM SELECT
    // ============================================================

    private void BuildTeamSelectUI()
    {
        _selectedChars.Clear();
        _statusLabel.Text = "选择3个角色 — 点击按钮切换选中/取消";
        ClearLog();
        Log("=== 角色选择 ===\n");

        var charButtons = new List<Button>();
        for (int i = 0; i < _allChars.Count; i++)
        {
            var ch = _allChars[i];
            int idx = i;
            var btn = AddButton(ch.Name, () => ToggleCharacter(idx, charButtons));
            btn.ButtonPressed = false;
            charButtons.Add(btn);
        }
        AddButton("[确认 3人]", () => ConfirmTeamSelect());
    }

    private void ToggleCharacter(int idx, List<Button> buttons)
    {
        var ch = _allChars[idx];
        if (_selectedChars.Contains(ch))
        {
            _selectedChars.Remove(ch);
            buttons[idx].ButtonPressed = false;
        }
        else if (_selectedChars.Count < 3)
        {
            _selectedChars.Add(ch);
            buttons[idx].ButtonPressed = true;
        }
        _statusLabel.Text = $"已选 {_selectedChars.Count}/3: {string.Join(", ", _selectedChars.Select(c => c.Name))}";
    }

    private void ConfirmTeamSelect()
    {
        if (_selectedChars.Count != 3)
        {
            Log("请选择恰好3个角色");
            return;
        }
        Log($"己方队伍: {string.Join(", ", _selectedChars.Select(c => c.Name))}");
        _selectedPositions = Enumerable.Repeat(-1, 3).ToList();
        EnterPhase(GamePhase.Formation);
    }

    // ============================================================
    //  PHASE 2: FORMATION
    // ============================================================

    private void BuildFormationUI()
    {
        _formationCharIdx = 0;
        ShowFormationForChar();
    }

    private void ShowFormationForChar()
    {
        ClearButtons();
        var ch = _selectedChars[_formationCharIdx];
        _statusLabel.Text = $"为 [{ch.Name}] 选择位置 (1-6)";
        Log($"为 [{ch.Name}] 选择位置:");

        for (int i = 1; i <= 6; i++)
        {
            int pos = i;
            var btn = AddButton($"{pos}", () => SelectPosition(pos));
            btn.Disabled = _selectedPositions.Contains(pos);
        }
    }

    private void SelectPosition(int pos)
    {
        _selectedPositions[_formationCharIdx] = pos;
        var ch = _selectedChars[_formationCharIdx];
        Log($"  {ch.Name} → [{pos}]");
        _formationCharIdx++;

        if (_formationCharIdx >= 3)
        {
            Log("阵型布置完成");
            EnterPhase(GamePhase.EnemySelect);
        }
        else
        {
            ShowFormationForChar();
        }
    }

    // ============================================================
    //  PHASE 3: ENEMY SELECT
    // ============================================================

    private void BuildEnemySelectUI()
    {
        _statusLabel.Text = "选择敌人配置";
        Log("\n=== 选择敌人配置 ===");

        var formations = _gameData.EnemyFormations.Values.ToList();
        for (int i = 0; i < formations.Count; i++)
        {
            var f = formations[i];
            int idx = i;
            AddButton($"{f.Name} [难度{f.Difficulty}]", () => SelectEnemy(idx));
        }
    }

    private void SelectEnemy(int idx)
    {
        var formations = _gameData.EnemyFormations.Values.ToList();
        var selected = formations[idx];
        Log($"选择: {selected.Name}");

        if (selected.Id == "fmt_random" || selected.Units.Count == 0)
        {
            var available = _gameData.Characters.Keys.Except(_selectedChars.Select(c => c.Id)).ToList();
            var picked = available.OrderBy(_ => _rnd.Next()).Take(3).ToList();
            _enemyConfig = new() { (picked[0], 1, "preset_aggressive"), (picked[1], 2, "preset_aggressive"), (picked[2], 3, "preset_aggressive") };
        }
        else
        {
            _enemyConfig = selected.Units.Select(u => (u.CharacterId, u.Position, u.StrategyPresetId ?? "preset_aggressive")).ToList();
        }

        CreateAllUnits();
        EnterPhase(GamePhase.PassiveSetup);
    }

    private void CreateAllUnits()
    {
        _ctx = new BattleContext(_gameData);
        _playerUnits = new();
        _enemyUnits = new();

        for (int i = 0; i < 3; i++)
        {
            var unit = CreateUnit(_gameData, _selectedChars[i].Id, true, _selectedPositions[i]);
            DayProgression.Apply(unit, 1);
            _playerUnits.Add(unit);
            _ctx.PlayerUnits.Add(unit);
        }
        for (int i = 0; i < 3; i++)
        {
            var unit = CreateUnit(_gameData, _enemyConfig[i].charId, false, _enemyConfig[i].pos);
            DayProgression.Apply(unit, 1);
            _enemyUnits.Add(unit);
            _ctx.EnemyUnits.Add(unit);
        }
        while (_ctx.PlayerUnits.Count < 6) _ctx.PlayerUnits.Add(null);
        while (_ctx.EnemyUnits.Count < 6) _ctx.EnemyUnits.Add(null);

        // Auto-assign random enemy passives
        foreach (var unit in _enemyUnits)
        {
            var avail = unit.GetAvailablePassiveSkillIds()
                .Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null)
                .OrderBy(_ => _rnd.Next()).ToList();
            var equipped = new List<string>();
            int usedPp = 0;
            foreach (var s in avail)
            {
                if (usedPp + s.PpCost > unit.MaxPp) break;
                equipped.Add(s.Id);
                usedPp += s.PpCost;
            }
            unit.EquippedPassiveSkillIds = equipped;
        }
    }

    // ============================================================
    //  PHASE 4: PASSIVE SETUP
    // ============================================================

    private void BuildPassiveSetupUI()
    {
        _passiveSetupIdx = 0;
        ShowPassiveForCurrent();
    }

    private void ShowPassiveForCurrent()
    {
        ClearButtons();
        if (_passiveSetupIdx >= _playerUnits.Count)
        {
            // Apply enemy preset strategies
            for (int i = 0; i < _enemyUnits.Count; i++)
                ApplyPresetStrategiesSingle(_enemyUnits[i], _enemyConfig[i]);
            EnterPhase(GamePhase.StrategySetup);
            return;
        }

        var unit = _playerUnits[_passiveSetupIdx];
        _statusLabel.Text = $"[{unit.Data.Name}] 被动技能 (PP: {unit.GetUsedPp()}/{unit.MaxPp})";
        Log($"\n--- [{unit.Data.Name}] 被动配置 ---");

        var available = unit.GetAvailablePassiveSkillIds()
            .Select(id => _gameData.GetPassiveSkill(id)).Where(s => s != null).ToList();

        foreach (var skill in available)
        {
            bool on = unit.EquippedPassiveSkillIds.Contains(skill.Id);
            string mark = on ? "[✓]" : "[  ]";
            var btn = AddButton($"{mark} {skill.Name} PP{skill.PpCost}", () => TogglePassive(unit, skill));
        }
        AddButton("→ 下一个", () => {
            Log($"  [{unit.Data.Name}] 被动: {unit.EquippedPassiveSkillIds.Count}个, PP {unit.GetUsedPp()}/{unit.MaxPp}");
            _passiveSetupIdx++;
            ShowPassiveForCurrent();
        });
        AddButton("→ 全部跳过", () => {
            _passiveSetupIdx = _playerUnits.Count;
            ShowPassiveForCurrent();
        });
    }

    private void TogglePassive(BattleUnit unit, PassiveSkillData skill)
    {
        if (unit.EquippedPassiveSkillIds.Contains(skill.Id))
        {
            unit.EquippedPassiveSkillIds.Remove(skill.Id);
            Log($"  卸下: {skill.Name}");
        }
        else
        {
            if (!unit.CanEquipPassive(skill.Id))
            {
                Log($"  PP不足!");
                return;
            }
            unit.EquippedPassiveSkillIds.Add(skill.Id);
            Log($"  装备: {skill.Name}");
        }
        ShowPassiveForCurrent();
    }

    private void ApplyPresetStrategiesSingle(BattleUnit unit, (string charId, int pos, string presetId) config)
    {
        var presetId = string.IsNullOrEmpty(config.presetId) || !_gameData.StrategyPresets.ContainsKey(config.presetId)
            ? "preset_aggressive" : config.presetId;
        var preset = _gameData.GetStrategyPreset(presetId);
        var availIds = unit.GetAvailableActiveSkillIds();
        unit.Strategies = preset.Strategies.Select(ps => {
            string sid = ps.SkillIndex >= 0 && ps.SkillIndex < availIds.Count ? availIds[ps.SkillIndex] : availIds.FirstOrDefault();
            return sid != null ? new Strategy { SkillId = sid, Condition1 = ps.Condition1, Condition2 = ps.Condition2, Mode1 = ps.Mode1, Mode2 = ps.Mode2 } : null;
        }).Where(s => s != null).ToList();
    }

    // ============================================================
    //  PHASE 5: STRATEGY SETUP
    // ============================================================

    private void BuildStrategySetupUI()
    {
        _strategySetupIdx = 0;
        ShowStrategyForCurrent();
    }

    private void ShowStrategyForCurrent()
    {
        ClearButtons();
        if (_strategySetupIdx >= _playerUnits.Count)
        {
            EnterPhase(GamePhase.Battle);
            return;
        }

        var unit = _playerUnits[_strategySetupIdx];
        var available = unit.GetAvailableActiveSkillIds().Select(id => _gameData.GetActiveSkill(id)).Where(s => s != null).ToList();

        _statusLabel.Text = $"[{unit.Data.Name}] 策略 (8条) — 点击栏位切换技能";
        Log($"\n--- [{unit.Data.Name}] 策略配置 ---");

        for (int i = 0; i < 8; i++)
        {
            var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;
            int slot = i;
            string label = s != null ? $"[{slot + 1}] {available.FirstOrDefault(sk => sk.Id == s.SkillId)?.Name ?? s.SkillId}" : $"[{slot + 1}] (空)";
            AddButton(label, () => EditStrategySlot(unit, slot, available));
        }
        AddButton("→ 确认/下一个", () => {
            Log($"  [{unit.Data.Name}] 策略完成");
            _strategySetupIdx++;
            ShowStrategyForCurrent();
        });
        AddButton("→ 全部默认跳过", () => {
            _strategySetupIdx = _playerUnits.Count;
            ShowStrategyForCurrent();
        });
    }

    private void EditStrategySlot(BattleUnit unit, int slot, List<ActiveSkillData> available)
    {
        ClearButtons();
        _statusLabel.Text = $"[{unit.Data.Name}] 栏位[{slot + 1}] — 选技能";
        foreach (var skill in available)
        {
            AddButton($"{skill.Name} AP{skill.ApCost}", () => {
                while (unit.Strategies.Count <= slot)
                    unit.Strategies.Add(new Strategy { SkillId = available[0].Id, Condition1 = null, Condition2 = null, Mode1 = ConditionMode.Only, Mode2 = ConditionMode.Only });
                unit.Strategies[slot].SkillId = skill.Id;
                Log($"  栏位[{slot + 1}] → {skill.Name}");
                ShowStrategyForCurrent();
            });
        }
        AddButton("← 返回", () => ShowStrategyForCurrent());
    }

    // ============================================================
    //  PHASE 6: BATTLE
    // ============================================================

    private void StartBattle()
    {
        ClearLog();
        Log("=== 队伍信息 ===\n己方:");
        foreach (var u in _playerUnits)
            Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        Log("敌方:");
        foreach (var u in _enemyUnits)
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
    //  PHASE 7: RESULT
    // ============================================================

    private void BuildResultUI()
    {
        _statusLabel.Text = $"战斗结果: {_battleResult}";
        Log($"\n=== {_battleResult} ===");
        AddButton("再来一局", () => EnterPhase(GamePhase.TeamSelect));
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
