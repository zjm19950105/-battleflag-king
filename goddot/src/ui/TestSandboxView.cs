using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Skills;
using Godot;

namespace BattleKing.Ui
{
    public sealed partial class TestSandboxView
    {
        private const int SlotCount = 6;
        private const int BaseBodyFontSize = 16;
        private static int _fontOffset;
        private static int BodyFontSize => Math.Clamp(BaseBodyFontSize + _fontOffset, 12, 22);
        private static int TitleFontSize => BodyFontSize + 2;
        private static int CompactFontSize => Math.Max(11, BodyFontSize - 2);
        public static int CurrentBodyFontSize => BodyFontSize;
        public static int CurrentTitleFontSize => TitleFontSize;

        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly RichTextLabel _logLabel;
        private readonly IReadOnlyList<CharacterData> _allChars;
        private readonly GameDataRepository _gameData;
        private readonly Func<string, Action, Button> _createButton;
        private readonly Action _moveLogToRightPanel;
        private readonly Action _goBack;
        private readonly BattleSetupService _battleSetup;
        private readonly TestBattleScenarioFactory _scenarioFactory;

        private readonly string[] _playerSlots = new string[SlotCount];
        private readonly string[] _enemySlots = new string[SlotCount];
        private readonly BattleUnit[] _playerDraftUnits = new BattleUnit[SlotCount];
        private readonly BattleUnit[] _enemyDraftUnits = new BattleUnit[SlotCount];

        private VBoxContainer _detailPanel;
        private VBoxContainer _formationPanel;
        private VBoxContainer _abilityPanel;
        private RichTextLabel _battleStatusLabel;

        private bool _selectedIsPlayer = true;
        private int _selectedIndex = -1;
        private int _selectedDay = 1;
        private bool _selectedCc;

        private BattleContext _context;
        private BattleEngine _engine;
        private PassiveSkillProcessor _passiveProcessor;
        private bool _battleStarted;
        private bool _battleFinished;

        public TestSandboxView(
            VBoxContainer leftPanel,
            VBoxContainer rightPanel,
            HBoxContainer buttonBar,
            RichTextLabel logLabel,
            IReadOnlyList<CharacterData> allChars,
            GameDataRepository gameData,
            Func<string, Action, Button> createButton,
            Action moveLogToRightPanel,
            Action goBack)
        {
            _leftPanel = leftPanel;
            _rightPanel = rightPanel;
            _buttonBar = buttonBar;
            _logLabel = logLabel;
            _allChars = allChars;
            _gameData = gameData;
            _createButton = createButton;
            _moveLogToRightPanel = moveLogToRightPanel;
            _goBack = goBack;
            _battleSetup = new BattleSetupService(gameData);
            _scenarioFactory = new TestBattleScenarioFactory(gameData);
        }

        public void Show()
        {
            _battleStarted = false;
            _battleFinished = false;
            _engine = null;
            _context = null;
            _passiveProcessor = null;
            RebuildDraftUnits();

            _battleStatusLabel = BuildBattleStatusLabel();
            _rightPanel.AddChild(_battleStatusLabel);
            RefreshBattleStatusPanel();

            _rightPanel.AddChild(CreateTitle("战斗日志"));
            _moveLogToRightPanel();
            _logLabel.Clear();
            _logLabel.SelectionEnabled = true;
            _logLabel.ContextMenuEnabled = true;
            _logLabel.ScrollFollowing = true;

            BuildLayout();
            RefreshButtons();
            ApplySandboxFontRecursive(_rightPanel);
        }

        private void BuildLayout()
        {
            ClearChildren(_leftPanel);

            var split = new VSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                DraggerVisibility = SplitContainer.DraggerVisibilityEnum.Visible
            };
#pragma warning disable CS0618
            split.SplitOffset = 420;
#pragma warning restore CS0618
            _leftPanel.AddChild(split);

            var detailScroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 300)
            };
            split.AddChild(detailScroll);

            _detailPanel = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 0)
            };
            detailScroll.AddChild(_detailPanel);

            _formationPanel = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 180)
            };
            _formationPanel.AddThemeConstantOverride("separation", 4);
            split.AddChild(_formationPanel);

            RefreshDetailPanel();
            RefreshFormationPanel();
        }

        private void RefreshDetailPanel()
        {
            if (_detailPanel == null)
                return;

            ClearChildren(_detailPanel);

            var selectedUnit = GetSelectedUnit();
            if (selectedUnit == null)
            {
                _detailPanel.AddChild(CreateLabel("从下面角色池拖角色到我方或敌方阵容槽，然后点击阵容里的角色。", BodyFontSize));
                ApplySandboxFontRecursive(_detailPanel);
                return;
            }

            _detailPanel.AddChild(SandboxUnitHeaderView.Build(selectedUnit));

            var tabs = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _detailPanel.AddChild(tabs);

            tabs.AddChild(BuildEquipmentStrategyTab(selectedUnit));
            tabs.SetTabTitle(0, "装备·策略");

            tabs.AddChild(BuildSkillsTab(selectedUnit));
            tabs.SetTabTitle(1, "技能");

            tabs.AddChild(BuildPlaceholderTab("信息", selectedUnit));
            tabs.SetTabTitle(2, "信息");

            tabs.AddChild(BuildPlaceholderTab("能力", selectedUnit));
            tabs.SetTabTitle(3, "能力");

            ApplySandboxFontRecursive(_detailPanel);
        }

        private Control BuildEquipmentStrategyTab(BattleUnit unit)
        {
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            root.AddThemeConstantOverride("separation", 5);

            var top = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 220),
                DraggerVisibility = SplitContainer.DraggerVisibilityEnum.Visible
            };
#pragma warning disable CS0618
            top.SplitOffset = 240;
#pragma warning restore CS0618
            root.AddChild(top);

            var equipment = new SandboxEquipmentPanel().Build(
                unit,
                _gameData,
                preview => RefreshAbilityPanel(unit, preview),
                () => RefreshAbilityPanel(unit, null),
                () =>
                {
                    RefreshAbilityPanel(unit, null);
                    RefreshDetailPanel();
                    RefreshBattleStatusPanel();
                },
                ShowActiveSkillDetail,
                ShowPassiveSkillDetail);
            equipment.CustomMinimumSize = new Vector2(215, 210);
            top.AddChild(equipment);

            _abilityPanel = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(170, 0)
            };
            top.AddChild(_abilityPanel);
            RefreshAbilityPanel(unit, null);

            root.AddChild(CreateTitle("策略"));
            root.AddChild(new SandboxStrategyTableView(unit, _gameData)
            {
                CustomMinimumSize = new Vector2(0, 165)
            });

            return root;
        }

        private Control BuildSkillsTab(BattleUnit unit)
        {
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            root.AddThemeConstantOverride("separation", 8);
            root.AddChild(CreateTitle("技能"));

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            root.AddChild(scroll);

            var list = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            list.AddThemeConstantOverride("separation", 10);
            scroll.AddChild(list);

            AddActiveSkillSection(list, "原始主动", GetInnateActiveSkillIds(unit));
            AddPassiveSkillSection(list, "原始被动", GetInnatePassiveSkillIds(unit));
            AddActiveSkillSection(list, "装备主动", unit.Equipment.GetGrantedActiveSkillIds());
            AddPassiveSkillSection(list, "装备被动", unit.Equipment.GetGrantedPassiveSkillIds());

            return root;
        }

        private void AddActiveSkillSection(VBoxContainer list, string title, IEnumerable<string> skillIds)
        {
            list.AddChild(CreateLabel(title, BodyFontSize));
            var skills = skillIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .Select(id => _gameData.GetActiveSkill(id))
                .Where(skill => skill != null)
                .ToList() ?? new List<ActiveSkillData>();

            if (skills.Count == 0)
            {
                list.AddChild(CreateLabel("无", CompactFontSize));
                return;
            }

            foreach (var skill in skills)
                list.AddChild(BuildActiveSkillRow(skill));
        }

        private void AddPassiveSkillSection(VBoxContainer list, string title, IEnumerable<string> skillIds)
        {
            list.AddChild(CreateLabel(title, BodyFontSize));
            var skills = skillIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .Select(id => _gameData.GetPassiveSkill(id))
                .Where(skill => skill != null)
                .ToList() ?? new List<PassiveSkillData>();

            if (skills.Count == 0)
            {
                list.AddChild(CreateLabel("无", CompactFontSize));
                return;
            }

            foreach (var skill in skills)
                list.AddChild(BuildPassiveSkillRow(skill));
        }

        private Button BuildActiveSkillRow(ActiveSkillData skill)
        {
            var button = CreateSkillRowButton($"{skill.Name}  AP{skill.ApCost}  {SkillSummary(skill.EffectDescription)}");
            SandboxTooltipHelper.AttachActiveSkillTooltip(button, skill);
            button.Pressed += () => ShowActiveSkillDetail(skill);
            return button;
        }

        private Button BuildPassiveSkillRow(PassiveSkillData skill)
        {
            var button = CreateSkillRowButton($"{skill.Name}  PP{skill.PpCost}  {SkillSummary(skill.EffectDescription)}");
            SandboxTooltipHelper.AttachPassiveSkillTooltip(button, skill);
            button.Pressed += () => ShowPassiveSkillDetail(skill);
            return button;
        }

        private static Button CreateSkillRowButton(string text)
        {
            var button = new Button
            {
                Text = text ?? string.Empty,
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(0, 40),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            button.AddThemeFontSizeOverride("font_size", BodyFontSize);
            return button;
        }

        private IEnumerable<string> GetInnateActiveSkillIds(BattleUnit unit)
        {
            var ids = new List<string>(unit.Data.InnateActiveSkillIds ?? new List<string>());
            if (unit.IsCc && unit.Data.CcInnateActiveSkillIds != null)
                ids.AddRange(unit.Data.CcInnateActiveSkillIds);

            return ids.Where(id =>
            {
                var skill = _gameData.GetActiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
            });
        }

        private IEnumerable<string> GetInnatePassiveSkillIds(BattleUnit unit)
        {
            var ids = new List<string>(unit.Data.InnatePassiveSkillIds ?? new List<string>());
            if (unit.IsCc && unit.Data.CcInnatePassiveSkillIds != null)
                ids.AddRange(unit.Data.CcInnatePassiveSkillIds);

            return ids.Where(id =>
            {
                var skill = _gameData.GetPassiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= unit.CurrentLevel;
            });
        }

        private Control BuildPlaceholderTab(string title, BattleUnit unit)
        {
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            root.AddChild(CreateTitle(title));
            root.AddChild(CreateLabel($"{unit.Data.Name} 的“{title}”页先占位，后续再接完整内容。", BodyFontSize));
            return root;
        }

        private void RefreshAbilityPanel(BattleUnit unit, EquipmentStatPreview preview)
        {
            if (_abilityPanel == null)
                return;

            ClearChildren(_abilityPanel);

            var frame = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            frame.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.12f, 0.11f, 0.1f), new Color(0.38f, 0.32f, 0.22f)));
            _abilityPanel.AddChild(frame);

            var margin = new MarginContainer();
            AddMargins(margin, 12, 10, 12, 10);
            frame.AddChild(margin);

            var content = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            content.AddThemeConstantOverride("separation", 8);
            margin.AddChild(content);

            content.AddChild(CreateTitle("能力"));
            var grid = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 4);
            content.AddChild(grid);

            foreach (var stat in StatOrder())
                grid.AddChild(BuildStatCell(unit, preview, stat));

            ApplySandboxFontRecursive(_abilityPanel);
        }

        private static Control BuildStatCell(BattleUnit unit, EquipmentStatPreview preview, string statName)
        {
            var text = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(150, 30)
            };
            text.AddThemeFontSizeOverride("normal_font_size", BodyFontSize);

            if (preview != null)
            {
                var row = preview.Rows.First(r => r.StatName == statName);
                text.AppendText($"{StatLabel(statName)}  {row.Current}{row.DeltaBbcode}");
                return text;
            }

            text.AppendText($"{StatLabel(statName)}  {GetDisplayStat(unit, statName)}");
            return text;
        }

        private static IEnumerable<string> StatOrder()
        {
            yield return "AP";
            yield return "PP";
            yield return "HP";
            yield return "Str";
            yield return "Def";
            yield return "Mag";
            yield return "MDef";
            yield return "Hit";
            yield return "Eva";
            yield return "Crit";
            yield return "Block";
            yield return "Spd";
        }

        private static int GetDisplayStat(BattleUnit unit, string statName)
        {
            return statName switch
            {
                "Str" => unit.GetCurrentStat("Str") + unit.Equipment.GetTotalStat("phys_atk"),
                "Def" => unit.GetCurrentStat("Def") + unit.Equipment.GetTotalStat("phys_def"),
                "Mag" => unit.GetCurrentStat("Mag") + unit.Equipment.GetTotalStat("mag_atk"),
                "MDef" => unit.GetCurrentStat("MDef") + unit.Equipment.GetTotalStat("mag_def"),
                _ => unit.GetCurrentStat(statName)
            };
        }

        private void RefreshFormationPanel()
        {
            if (_formationPanel == null)
                return;

            ClearChildren(_formationPanel);

            var configRow = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 24)
            };
            configRow.AddThemeConstantOverride("separation", 5);
            _formationPanel.AddChild(configRow);

            configRow.AddChild(CreateLabel("设置", CompactFontSize));

            var day = new OptionButton
            {
                CustomMinimumSize = new Vector2(82, 24)
            };
            day.AddThemeFontSizeOverride("font_size", CompactFontSize);
            for (var d = 1; d <= 6; d++)
                day.AddItem($"Day {d}");
            day.Selected = _selectedDay - 1;
            day.ItemSelected += index =>
            {
                _selectedDay = (int)index + 1;
                ResetBattleRuntime();
                RebuildDraftUnits();
                RefreshDetailPanel();
                RefreshFormationPanel();
                RefreshBattleStatusPanel();
                RefreshButtons();
            };
            configRow.AddChild(day);

            var cc = new CheckBox
            {
                Text = "转职/CC",
                ButtonPressed = _selectedCc,
                CustomMinimumSize = new Vector2(88, 24)
            };
            cc.AddThemeFontSizeOverride("font_size", CompactFontSize);
            cc.Toggled += on =>
            {
                _selectedCc = on;
                ResetBattleRuntime();
                RebuildDraftUnits();
                RefreshDetailPanel();
                RefreshFormationPanel();
                RefreshBattleStatusPanel();
                RefreshButtons();
            };
            configRow.AddChild(cc);

            var teams = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 82)
            };
            teams.AddThemeConstantOverride("separation", 6);
            _formationPanel.AddChild(teams);

            teams.AddChild(BuildTeamPanel("我方阵容", true));
            teams.AddChild(BuildTeamPanel("敌方阵容", false));

            var poolHeader = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 24)
            };
            poolHeader.AddThemeConstantOverride("separation", 6);
            var poolTitle = CreateTitle("角色池");
            poolTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            poolHeader.AddChild(poolTitle);
            var removeSelected = CreateSmallButton("退下选中", () =>
            {
                if (_selectedIndex >= 0)
                    ClearSlot(_selectedIsPlayer, _selectedIndex);
            });
            removeSelected.CustomMinimumSize = new Vector2(72, 24);
            removeSelected.TooltipText = "退下当前选中的我方或敌方阵容角色。";
            poolHeader.AddChild(removeSelected);
            _formationPanel.AddChild(poolHeader);

            var poolScroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 78)
            };
            _formationPanel.AddChild(poolScroll);

            var pool = new SandboxCharacterPool(this)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            pool.AddThemeConstantOverride("h_separation", 6);
            pool.AddThemeConstantOverride("v_separation", 4);
            poolScroll.AddChild(pool);

            foreach (var character in _allChars)
            {
                var button = new DraggableChar
                {
                    Text = character.Name,
                    CharId = character.Id,
                    CustomMinimumSize = new Vector2(98, 26)
                };
                button.AddThemeFontSizeOverride("font_size", CompactFontSize);
                button.TooltipText = $"{character.Name}\n拖到我方或敌方阵容槽。";
                pool.AddChild(button);
            }

            ApplySandboxFontRecursive(_formationPanel);
        }

        private Control BuildTeamPanel(string title, bool isPlayer)
        {
            var panel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 76)
            };
            panel.AddThemeStyleboxOverride("panel", CreateStyle(
                isPlayer ? new Color(0.11f, 0.16f, 0.14f) : new Color(0.16f, 0.12f, 0.12f),
                new Color(0.3f, 0.34f, 0.36f)));

            var margin = new MarginContainer();
            AddMargins(margin, 5, 4, 5, 4);
            panel.AddChild(margin);

            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 3);
            margin.AddChild(box);

            box.AddChild(CreateLabel(title, CompactFontSize));
            box.AddChild(BuildSlotRow(isPlayer, start: 0));
            box.AddChild(BuildSlotRow(isPlayer, start: 3));

            return panel;
        }

        private Control BuildSlotRow(bool isPlayer, int start)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 3);

            for (var i = start; i < start + 3; i++)
            {
                var slotIndex = i;
                row.AddChild(new SandboxFormationSlot(this, isPlayer, slotIndex));

                var clear = CreateSmallButton("x", () =>
                {
                    ClearSlot(isPlayer, slotIndex);
                });
                clear.Disabled = GetSlots(isPlayer)[slotIndex] == null;
                row.AddChild(clear);
            }

            return row;
        }

        private Control BuildBattleStatusPanel()
        {
            var label = BuildBattleStatusLabel();
            RenderBattleStatus(label);
            return label;
        }

        private RichTextLabel BuildBattleStatusLabel()
        {
            var label = new RichTextLabel
            {
                BbcodeEnabled = true,
                SelectionEnabled = true,
                ContextMenuEnabled = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 170),
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            label.AddThemeFontSizeOverride("normal_font_size", BodyFontSize);
            return label;
        }

        private void RefreshBattleStatusPanel()
        {
            if (_battleStatusLabel == null)
                return;

            RenderBattleStatus(_battleStatusLabel);
            ApplySandboxFontRecursive(_battleStatusLabel);
        }

        private void RenderBattleStatus(RichTextLabel label)
        {
            if (label == null)
                return;

            label.Clear();
            label.AppendText("[color=cyan]我方[/color]\n");
            foreach (var unit in GetVisibleUnits(true))
                BattleStatusHelper.AppendUnit(label, unit);

            label.AppendText("\n[color=orange]敌方[/color]\n");
            foreach (var unit in GetVisibleUnits(false))
                BattleStatusHelper.AppendUnit(label, unit);
        }

        private void HandleSlotDrop(bool targetIsPlayer, int targetIndex, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var targetSlots = GetSlots(targetIsPlayer);

            if (raw.StartsWith("SANDBOX_SLOT:", StringComparison.Ordinal))
            {
                if (!TryParseSandboxSlotDragData(raw, out var sourceIsPlayer, out var sourceIndex))
                    return;

                var sourceSlots = GetSlots(sourceIsPlayer);
                var sourceUnits = GetDraftUnits(sourceIsPlayer);
                var targetUnits = GetDraftUnits(targetIsPlayer);
                (sourceSlots[sourceIndex], targetSlots[targetIndex]) = (targetSlots[targetIndex], sourceSlots[sourceIndex]);
                (sourceUnits[sourceIndex], targetUnits[targetIndex]) = (targetUnits[targetIndex], sourceUnits[sourceIndex]);
                SandboxDraftUnitState.MoveToSlot(sourceUnits[sourceIndex], sourceIsPlayer, sourceIndex);
                SandboxDraftUnitState.MoveToSlot(targetUnits[targetIndex], targetIsPlayer, targetIndex);
            }
            else
            {
                targetSlots[targetIndex] = raw;
            }

            ResetBattleRuntime();
            RebuildDraftUnits();
            SelectSlot(targetIsPlayer, targetIndex);
            RefreshFormationPanel();
            RefreshBattleStatusPanel();
            RefreshButtons();
        }

        private void ClearSlot(bool isPlayer, int index)
        {
            if (index < 0 || index >= SlotCount)
                return;

            var slots = GetSlots(isPlayer);
            if (string.IsNullOrWhiteSpace(slots[index]))
                return;

            slots[index] = null;
            ResetBattleRuntime();
            RebuildDraftUnits();
            if (_selectedIsPlayer == isPlayer && _selectedIndex == index)
                _selectedIndex = -1;
            RefreshDetailPanel();
            RefreshFormationPanel();
            RefreshBattleStatusPanel();
            RefreshButtons();
        }

        private void ApplySlotDebugAction(bool isPlayer, int index, SlotDebugAction action)
        {
            if (index < 0 || index >= SlotCount)
                return;

            var unit = GetUnitForSlot(isPlayer, index);
            if (unit == null)
                return;

            switch (action)
            {
                case SlotDebugAction.Damage10:
                    ApplyManualDamage(unit, 10, "扣减10 HP");
                    break;
                case SlotDebugAction.DamageHalf:
                    ApplyManualDamage(unit, Math.Max(1, GetMaxHp(unit) / 2), "扣减50% HP");
                    break;
                case SlotDebugAction.HealHalf:
                    ApplyManualHeal(unit, Math.Max(1, GetMaxHp(unit) / 2), "恢复50% HP");
                    break;
                case SlotDebugAction.Stun:
                    unit.State = UnitState.Stunned;
                    AppendLog($"[测试] {BattleLogHelper.FormatUnitName(unit)} 陷入气绝，下次行动跳过。");
                    break;
            }

            SelectSlot(isPlayer, index);
            RefreshFormationPanel();
            RefreshBattleStatusPanel();
            RefreshButtons();
        }

        private void ApplyManualDamage(BattleUnit unit, int amount, string label)
        {
            var before = unit.CurrentHp;
            unit.CurrentHp = Math.Max(0, unit.CurrentHp - Math.Max(0, amount));
            AppendLog($"[测试] {BattleLogHelper.FormatUnitName(unit)} {label}: {before} -> {unit.CurrentHp}");
        }

        private void ApplyManualHeal(BattleUnit unit, int amount, string label)
        {
            var before = unit.CurrentHp;
            unit.CurrentHp = Math.Min(GetMaxHp(unit), unit.CurrentHp + Math.Max(0, amount));
            AppendLog($"[测试] {BattleLogHelper.FormatUnitName(unit)} {label}: {before} -> {unit.CurrentHp}");
        }

        private BattleUnit GetUnitForSlot(bool isPlayer, int index)
        {
            if (_battleStarted && _context != null)
                return _context.GetUnitAtPosition(isPlayer, index + 1);

            return GetDraftUnits(isPlayer)[index];
        }

        private static int GetMaxHp(BattleUnit unit)
        {
            if (unit == null)
                return 1;

            return Math.Max(1, unit.GetCurrentStat("HP"));
        }

        private void SelectSlot(bool isPlayer, int index)
        {
            _selectedIsPlayer = isPlayer;
            _selectedIndex = index;
            RefreshDetailPanel();
        }

        private void RebuildDraftUnits()
        {
            RebuildDraftSide(true);
            RebuildDraftSide(false);
        }

        private void RebuildDraftSide(bool isPlayer)
        {
            var slots = GetSlots(isPlayer);
            var units = GetDraftUnits(isPlayer);

            for (var i = 0; i < SlotCount; i++)
            {
                var characterId = slots[i];
                var existing = units[i];
                if (string.IsNullOrWhiteSpace(characterId))
                {
                    units[i] = null;
                    continue;
                }

                if (SandboxDraftUnitState.MatchesSelection(existing, characterId, i + 1, isPlayer, _selectedCc, _selectedDay))
                {
                    continue;
                }

                units[i] = _battleSetup.CreateUnit(characterId, isPlayer, i + 1, _selectedDay, _selectedCc);
            }
        }

        private void StartBattle()
        {
            if (!HasAnyUnit(true) || !HasAnyUnit(false))
            {
                AppendLog("测试战斗需要我方和敌方都至少 1 人。");
                return;
            }

            try
            {
                var slots = BuildScenarioSlots();
                _context = _scenarioFactory.CreateContext(slots);
                CopyDraftConfigurationToBattleUnits(_context.PlayerUnits, true);
                CopyDraftConfigurationToBattleUnits(_context.EnemyUnits, false);

                _engine = new BattleEngine(_context);
                _engine.OnLog = AppendLog;
                _passiveProcessor = new PassiveSkillProcessor(_engine.EventBus, _gameData, AppendLog, _engine.EnqueueAction);
                _passiveProcessor.SubscribeAll();

                _battleStarted = true;
                _battleFinished = false;
                _logLabel.Clear();
                _engine.InitBattle();
                RefreshDetailPanel();
                RefreshBattleStatusPanel();
                RefreshButtons();
            }
            catch (Exception ex)
            {
                AppendLog("无法开始测试战斗：" + ex.Message);
            }
        }

        private List<TestBattleScenarioSlot> BuildScenarioSlots()
        {
            var result = new List<TestBattleScenarioSlot>();
            AddScenarioSlots(result, true);
            AddScenarioSlots(result, false);
            return result;
        }

        private void AddScenarioSlots(List<TestBattleScenarioSlot> result, bool isPlayer)
        {
            var slots = GetSlots(isPlayer);
            for (var i = 0; i < SlotCount; i++)
            {
                if (!string.IsNullOrWhiteSpace(slots[i]))
                    result.Add(new TestBattleScenarioSlot(slots[i], i + 1, isPlayer, _selectedDay, _selectedCc));
            }
        }

        private void CopyDraftConfigurationToBattleUnits(IEnumerable<BattleUnit> battleUnits, bool isPlayer)
        {
            var drafts = GetDraftUnits(isPlayer);
            foreach (var target in battleUnits)
            {
                var source = drafts[target.Position - 1];
                if (source == null)
                    continue;

                int previousMaxHp = Math.Max(1, target.GetCurrentStat("HP"));
                foreach (var slotName in BattleKing.Equipment.EquipmentSlot.GetSlotNames(target.Data, target.IsCc))
                    target.Equipment.EquipToSlot(slotName, source.Equipment.GetBySlot(slotName)?.Data);
                target.SyncResourceCapsFromStats(previousMaxHp);

                target.Strategies = source.Strategies
                    .Select(CloneStrategy)
                    .ToList();
                target.EquippedPassiveSkillIds = source.EquippedPassiveSkillIds.ToList();
                target.PassiveStrategies = source.PassiveStrategies
                    .Select(ClonePassiveStrategy)
                    .ToList();
                target.PassiveConditions = source.PassiveConditions.ToDictionary(kv => kv.Key, kv => CloneCondition(kv.Value));
            }
        }

        private static Strategy CloneStrategy(Strategy source)
        {
            if (source == null)
                return new Strategy { SkillId = "" };

            return new Strategy
            {
                SkillId = source.SkillId,
                Condition1 = CloneCondition(source.Condition1),
                Condition2 = CloneCondition(source.Condition2),
                Mode1 = source.Mode1,
                Mode2 = source.Mode2
            };
        }

        private static PassiveStrategy ClonePassiveStrategy(PassiveStrategy source)
        {
            if (source == null)
                return new PassiveStrategy { SkillId = "" };

            return new PassiveStrategy
            {
                SkillId = source.SkillId,
                Condition1 = CloneCondition(source.Condition1),
                Condition2 = CloneCondition(source.Condition2),
                Mode1 = source.Mode1,
                Mode2 = source.Mode2
            };
        }

        private static Condition CloneCondition(Condition source)
        {
            if (source == null)
                return null;

            return new Condition
            {
                Category = source.Category,
                Operator = source.Operator,
                Value = source.Value
            };
        }

        private void StepOneAction()
        {
            if (_engine == null || _battleFinished)
                return;

            var result = _engine.StepOneAction();
            RefreshDetailPanel();
            RefreshBattleStatusPanel();
            if (IsTerminal(result))
                FinishBattle(ToBattleResult(result));
        }

        private void RunAutoBattle()
        {
            if (_engine == null || _battleFinished)
                return;

            for (var guard = 0; guard < 500; guard++)
            {
                var result = _engine.StepOneAction();
                if (IsTerminal(result))
                {
                    RefreshDetailPanel();
                    RefreshBattleStatusPanel();
                    FinishBattle(ToBattleResult(result));
                    return;
                }
            }

            AppendLog("自动战斗超过 500 步，已停止。");
            RefreshDetailPanel();
            RefreshBattleStatusPanel();
        }

        private void FinishBattle(BattleResult result)
        {
            _battleFinished = true;
            AppendLog($"=== {result} ===");
            RefreshButtons();
        }

        private void ResetBattleRuntime()
        {
            _battleStarted = false;
            _battleFinished = false;
            _context = null;
            _engine = null;
            _passiveProcessor = null;
        }

        private void RefreshButtons()
        {
            ClearChildren(_buttonBar);

            var start = BuildBarButton(_battleStarted ? "重新开始测试战斗" : "开始测试战斗", 120, StartBattle);
            start.Disabled = !HasAnyUnit(true) || !HasAnyUnit(false);
            _buttonBar.AddChild(start);

            var step = BuildBarButton("下一名行动", 92, StepOneAction);
            step.Disabled = !_battleStarted || _battleFinished;
            _buttonBar.AddChild(step);

            var auto = BuildBarButton("自动战斗", 82, RunAutoBattle);
            auto.Disabled = !_battleStarted || _battleFinished;
            _buttonBar.AddChild(auto);

            _buttonBar.AddChild(BuildFontButton("字号-", -1));
            _buttonBar.AddChild(BuildFontButton("字号+", 1));

            _buttonBar.AddChild(BuildBarButton("重置战斗", 82, () =>
            {
                ResetBattleRuntime();
                RebuildDraftUnits();
                _logLabel.Clear();
                RefreshDetailPanel();
                RefreshFormationPanel();
                RefreshBattleStatusPanel();
                RefreshButtons();
            }));

            _buttonBar.AddChild(BuildBarButton("复制日志", 76, () => DisplayServer.ClipboardSet(_logLabel.GetParsedText())));
            _buttonBar.AddChild(BuildBarButton("返回", 52, _goBack));
            ApplySandboxFontRecursive(_buttonBar);
        }

        private Button BuildBarButton(string text, float width, Action onClick)
        {
            var button = _createButton(text, onClick);
            button.CustomMinimumSize = new Vector2(width, 28);
            button.AddThemeFontSizeOverride("font_size", CompactFontSize);
            return button;
        }

        private Button BuildFontButton(string text, int delta)
        {
            var button = BuildBarButton(text, 58, () => ChangeSandboxFont(delta));
            button.TooltipText = delta < 0 ? "缩小测试场景字号" : "放大测试场景字号";
            return button;
        }

        private void ChangeSandboxFont(int delta)
        {
            _fontOffset = Math.Clamp(_fontOffset + delta, -4, 6);
            RefreshDetailPanel();
            RefreshFormationPanel();
            RefreshBattleStatusPanel();
            RefreshButtons();
            ApplySandboxFontRecursive(_rightPanel);
        }

        private BattleUnit GetSelectedUnit()
        {
            if (_selectedIndex < 0 || _selectedIndex >= SlotCount)
                return null;

            if (_battleStarted && _context != null)
                return _context.GetUnitAtPosition(_selectedIsPlayer, _selectedIndex + 1);

            return GetDraftUnits(_selectedIsPlayer)[_selectedIndex];
        }

        private IEnumerable<BattleUnit> GetVisibleUnits(bool isPlayer)
        {
            if (_battleStarted && _context != null)
                return isPlayer ? _context.PlayerUnits : _context.EnemyUnits;

            return GetDraftUnits(isPlayer).Where(unit => unit != null);
        }

        private string[] GetSlots(bool isPlayer) => isPlayer ? _playerSlots : _enemySlots;
        private BattleUnit[] GetDraftUnits(bool isPlayer) => isPlayer ? _playerDraftUnits : _enemyDraftUnits;
        private bool HasAnyUnit(bool isPlayer) => GetSlots(isPlayer).Any(id => !string.IsNullOrWhiteSpace(id));

        private static bool TryParseSandboxSlotDragData(string raw, out bool isPlayer, out int index)
        {
            isPlayer = true;
            index = -1;

            if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("SANDBOX_SLOT:", StringComparison.Ordinal))
                return false;

            var parts = raw.Split(':', 4);
            if (parts.Length != 4)
                return false;

            isPlayer = parts[1] == "P";
            return int.TryParse(parts[2], out index)
                && index >= 0
                && index < SlotCount;
        }

        private enum SlotDebugAction
        {
            Damage10 = 1,
            DamageHalf = 2,
            HealHalf = 3,
            Stun = 4
        }

        private void AppendLog(string message)
        {
            BattleLogTextRenderer.Append(_logLabel, message);
        }

        private void ShowActiveSkillDetail(ActiveSkillData skill)
        {
            if (skill == null)
                return;

            SetDescription(skill.Name, SandboxTooltipHelper.BuildActiveSkillDetail(skill));
        }

        private void ShowPassiveSkillDetail(PassiveSkillData skill)
        {
            if (skill == null)
                return;

            SetDescription(skill.Name, SandboxTooltipHelper.BuildPassiveSkillDetail(skill));
        }

        private void SetDescription(string title, string body)
        {
            // Right panel space is reserved for battlefield state; skill details stay available via tooltips.
        }

        private static string SkillSummary(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "无效果描述";

            var text = description.Trim().Replace("\r", " ").Replace("\n", " ");
            return text.Length <= 58 ? text : text[..58] + "...";
        }

        private static bool IsTerminal(SingleActionResult result)
        {
            return result == SingleActionResult.PlayerWin
                || result == SingleActionResult.EnemyWin
                || result == SingleActionResult.Draw;
        }

        private static BattleResult ToBattleResult(SingleActionResult result)
        {
            return result switch
            {
                SingleActionResult.PlayerWin => BattleResult.PlayerWin,
                SingleActionResult.EnemyWin => BattleResult.EnemyWin,
                _ => BattleResult.Draw
            };
        }

        private Button CreateSmallButton(string text, Action onClick)
        {
            var button = _createButton(text, onClick);
            button.CustomMinimumSize = new Vector2(24, 26);
            button.AddThemeFontSizeOverride("font_size", CompactFontSize);
            return button;
        }

        private static Label CreateTitle(string text)
        {
            var label = CreateLabel(text, TitleFontSize);
            label.SetMeta("sandbox_font_role", "title");
            label.AddThemeColorOverride("font_color", new Color(1f, 0.91f, 0.58f));
            return label;
        }

        private static Label CreateLabel(string text, int fontSize)
        {
            var label = new Label
            {
                Text = text ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            if (fontSize <= CompactFontSize)
                label.SetMeta("sandbox_font_role", "compact");
            else if (fontSize >= TitleFontSize)
                label.SetMeta("sandbox_font_role", "title");
            else
                label.SetMeta("sandbox_font_role", "body");
            label.AddThemeFontSizeOverride("font_size", fontSize);
            return label;
        }

        private static string StatLabel(string statName) => statName switch
        {
            "HP" => "HP",
            "AP" => "AP",
            "PP" => "PP",
            "Str" => "物攻",
            "Def" => "物防",
            "Mag" => "魔攻",
            "MDef" => "魔防",
            "Hit" => "命中",
            "Eva" => "闪避",
            "Crit" => "暴击",
            "Block" => "格挡",
            "Spd" => "速度",
            _ => statName
        };

        private static StyleBoxFlat CreateStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BgColor = background,
                BorderColor = border,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6
            };
        }

        private static void AddMargins(MarginContainer margin, int left, int top, int right, int bottom)
        {
            margin.AddThemeConstantOverride("margin_left", left);
            margin.AddThemeConstantOverride("margin_top", top);
            margin.AddThemeConstantOverride("margin_right", right);
            margin.AddThemeConstantOverride("margin_bottom", bottom);
        }

        private static void ApplySandboxFontRecursive(Node node)
        {
            if (node == null)
                return;

            if (node is Control control)
            {
                if (control is RichTextLabel richText)
                    richText.AddThemeFontSizeOverride("normal_font_size", BodyFontSize);
                else if (control is Button or OptionButton or CheckBox or TabContainer)
                    control.AddThemeFontSizeOverride("font_size", BodyFontSize);
                else if (control is Label)
                    control.AddThemeFontSizeOverride("font_size", ResolveLabelFontSize(control));

                if (control is OptionButton optionButton)
                    SetPopupFontSize(optionButton.GetPopup(), BodyFontSize);
            }

            foreach (var child in node.GetChildren())
                ApplySandboxFontRecursive(child);
        }

        private static void SetFontSizeIfMissing(Control control, string themeType, int size)
        {
            if (control == null || control.HasThemeFontSizeOverride(themeType))
                return;

            control.AddThemeFontSizeOverride(themeType, size);
        }

        private static void SetPopupFontSize(PopupMenu popup, int size)
        {
            if (popup == null)
                return;

            popup.AddThemeFontSizeOverride("font_size", size);
        }

        private static int ResolveLabelFontSize(Control control)
        {
            if (control != null && control.HasMeta("sandbox_font_role"))
            {
                return control.GetMeta("sandbox_font_role").ToString() switch
                {
                    "title" => TitleFontSize,
                    "compact" => CompactFontSize,
                    _ => BodyFontSize
                };
            }

            return BodyFontSize;
        }

        private static void ClearChildren(Node node)
        {
            foreach (var child in node.GetChildren())
                child.QueueFree();
        }

        private sealed partial class SandboxFormationSlot : Button
        {
            private readonly TestSandboxView _owner;
            private readonly bool _isPlayer;
            private readonly int _index;
            private readonly PopupMenu _debugMenu;

            public SandboxFormationSlot(TestSandboxView owner, bool isPlayer, int index)
            {
                _owner = owner;
                _isPlayer = isPlayer;
                _index = index;
                CustomMinimumSize = new Vector2(74, 26);
                AddThemeFontSizeOverride("font_size", CompactFontSize);
                ClipText = true;
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                Pressed += () => _owner.SelectSlot(_isPlayer, _index);

                _debugMenu = new PopupMenu();
                _debugMenu.AddItem("扣减 10 HP", (int)SlotDebugAction.Damage10);
                _debugMenu.AddItem("扣减 50% HP", (int)SlotDebugAction.DamageHalf);
                _debugMenu.AddItem("恢复 50% HP", (int)SlotDebugAction.HealHalf);
                _debugMenu.AddItem("气绝：跳过下次行动", (int)SlotDebugAction.Stun);
                _debugMenu.IdPressed += id => _owner.ApplySlotDebugAction(_isPlayer, _index, (SlotDebugAction)id);
                AddChild(_debugMenu);

                UpdateText();
            }

            public override void _GuiInput(InputEvent @event)
            {
                if (@event is not InputEventMouseButton mouse
                    || mouse.ButtonIndex != MouseButton.Right
                    || !mouse.Pressed)
                {
                    return;
                }

                AcceptEvent();
                if (string.IsNullOrWhiteSpace(_owner.GetSlots(_isPlayer)[_index]))
                    return;

                _owner.SelectSlot(_isPlayer, _index);
                _debugMenu.Position = DisplayServer.MouseGetPosition();
                _debugMenu.Popup();
            }

            public override Variant _GetDragData(Vector2 atPosition)
            {
                var id = _owner.GetSlots(_isPlayer)[_index];
                if (string.IsNullOrWhiteSpace(id))
                    return default;

                var preview = new Label { Text = Text };
                preview.AddThemeFontSizeOverride("font_size", CompactFontSize);
                SetDragPreview(preview);
                return $"SANDBOX_SLOT:{(_isPlayer ? "P" : "E")}:{_index.ToString(CultureInfo.InvariantCulture)}:{id}";
            }

            public override bool _CanDropData(Vector2 atPosition, Variant data)
            {
                return data.VariantType == Variant.Type.String;
            }

            public override void _DropData(Vector2 atPosition, Variant data)
            {
                _owner.HandleSlotDrop(_isPlayer, _index, (string)data);
            }

            private void UpdateText()
            {
                var id = _owner.GetSlots(_isPlayer)[_index];
                var name = id != null && _owner._gameData.Characters.TryGetValue(id, out var character)
                    ? character.Name
                    : "空";
                Text = $"{_index + 1} {name}";
            }
        }

        private sealed partial class SandboxCharacterPool : HFlowContainer
        {
            private readonly TestSandboxView _owner;

            public SandboxCharacterPool(TestSandboxView owner)
            {
                _owner = owner;
                TooltipText = "把阵容里的角色拖回这里可以退下。";
            }

            public override bool _CanDropData(Vector2 atPosition, Variant data)
            {
                return data.VariantType == Variant.Type.String
                    && TryParseSandboxSlotDragData((string)data, out _, out _);
            }

            public override void _DropData(Vector2 atPosition, Variant data)
            {
                if (data.VariantType != Variant.Type.String)
                    return;

                if (TryParseSandboxSlotDragData((string)data, out var isPlayer, out var index))
                    _owner.ClearSlot(isPlayer, index);
            }
        }
    }
}
