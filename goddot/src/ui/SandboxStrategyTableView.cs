using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public partial class SandboxStrategyTableView : VBoxContainer
    {
        private const int MinimumVisibleRows = 8;
        private static int BodyFontSize => TestSandboxView.CurrentBodyFontSize;
        private static int HeaderFontSize => TestSandboxView.CurrentBodyFontSize;
        private static int PopupTitleFontSize => TestSandboxView.CurrentTitleFontSize;

        private BattleUnit _unit;
        private GameDataRepository _gameData;
        private List<ActiveSkillData> _activeSkills = new();
        private List<PassiveSkillData> _passiveSkills = new();
        private PopupPanel _skillPopup;
        private PopupPanel _conditionPopup;
        private RowKind _selectedKind = RowKind.None;
        private int _selectedSourceIndex = -1;

        public SandboxStrategyTableView()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public SandboxStrategyTableView(BattleUnit unit, GameDataRepository gameData)
            : this()
        {
            Bind(unit, gameData);
        }

        public void Bind(BattleUnit unit, GameDataRepository gameData)
        {
            _unit = unit ?? throw new ArgumentNullException(nameof(unit));
            _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            ReloadSkillPools();
            NormalizePassiveStrategies();
            Rebuild();
        }

        private void ReloadSkillPools()
        {
            _activeSkills = _unit.GetAvailableActiveSkillIds()
                .Select(id => _gameData.GetActiveSkill(id))
                .Where(skill => skill != null)
                .ToList();
            _passiveSkills = _unit.GetAvailablePassiveSkillIds()
                .Select(id => _gameData.GetPassiveSkill(id))
                .Where(skill => skill != null)
                .ToList();
        }

        private void Rebuild()
        {
            ClearChildren(this);
            NormalizePassiveStrategies();

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 200)
            };

            var grid = new GridContainer
            {
                Columns = 4,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            grid.AddThemeConstantOverride("h_separation", 3);
            grid.AddThemeConstantOverride("v_separation", 4);

            AddHeader(grid, "优先顺序", 92);
            AddHeader(grid, "行动", 210);
            AddHeader(grid, "条件 1", 280);
            AddHeader(grid, "条件 2", 280);

            foreach (var row in BuildRows())
                AddStrategyRow(grid, row);

            scroll.AddChild(grid);
            AddChild(scroll);

            _skillPopup = new PopupPanel { Exclusive = false, Unresizable = false };
            _skillPopup.CloseRequested += () => _skillPopup.Hide();
            AddChild(_skillPopup);

            _conditionPopup = new PopupPanel { Exclusive = false, Unresizable = false };
            _conditionPopup.CloseRequested += () => _conditionPopup.Hide();
            AddChild(_conditionPopup);
        }

        private List<RowModel> BuildRows()
        {
            var rows = new List<RowModel>();
            int displayOrder = 1;
            for (int i = 0; i < _unit.Strategies.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(_unit.Strategies[i]?.SkillId))
                    continue;

                rows.Add(new RowModel(RowKind.Active, i, displayOrder++));
            }

            for (int i = 0; i < _unit.PassiveStrategies.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(_unit.PassiveStrategies[i]?.SkillId))
                    continue;

                rows.Add(new RowModel(RowKind.Passive, i, displayOrder++));
            }

            int emptyRows = Math.Max(1, MinimumVisibleRows - rows.Count);
            for (int i = 0; i < emptyRows; i++)
                rows.Add(new RowModel(RowKind.Empty, i, 0));

            return rows;
        }

        private void AddStrategyRow(GridContainer grid, RowModel row)
        {
            bool selected = IsSelected(row);
            var palette = GetPalette(row.Kind, selected);

            var orderText = row.Kind == RowKind.Empty
                ? ""
                : row.DisplayOrder.ToString(CultureInfo.InvariantCulture);
            var order = CreateRowCellButton(row, orderText, new Vector2(92, 36), palette.OrderBackground, palette.TextColor);
            order.Alignment = HorizontalAlignment.Center;
            order.Pressed += () => SelectRow(row);
            grid.AddChild(order);

            var action = CreateRowCellButton(row, GetActionText(row), new Vector2(210, 36), palette.ActionBackground, palette.TextColor);
            action.Alignment = row.Kind == RowKind.Empty ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            action.Pressed += () =>
            {
                SelectRow(row);
                ShowSkillPopup(row);
            };
            grid.AddChild(action);

            grid.AddChild(CreateConditionCell(row, isCondition1: true, palette));
            grid.AddChild(CreateConditionCell(row, isCondition1: false, palette));
        }

        private Button CreateConditionCell(RowModel row, bool isCondition1, RowPalette palette)
        {
            var render = RenderCondition(row, isCondition1);
            var button = CreateRowCellButton(
                row,
                render.Text,
                new Vector2(280, 36),
                palette.ConditionBackground,
                render.Color);
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = row.Kind == RowKind.Empty;
            button.TooltipText = row.Kind == RowKind.Empty ? "先新增策略" : "打开条件列表";
            button.Pressed += () =>
            {
                SelectRow(row);
                ShowConditionPopup(row, isCondition1);
            };
            return button;
        }

        private string GetActionText(RowModel row)
        {
            return row.Kind switch
            {
                RowKind.Active => GetActiveSkill(row.SourceIndex)?.Name ?? $"(不可用) {GetActiveStrategy(row.SourceIndex)?.SkillId}",
                RowKind.Passive => GetPassiveSkill(row.SourceIndex)?.Name ?? $"(不可用) {GetPassiveStrategy(row.SourceIndex)?.SkillId}",
                _ => "+ 新增策略"
            };
        }

        private ConditionRender RenderCondition(RowModel row, bool isCondition1)
        {
            if (row.Kind == RowKind.Empty)
                return new ConditionRender("", new Color(0.45f, 0.45f, 0.45f));

            var condition = GetCondition(row, isCondition1);
            if (condition == null)
                return new ConditionRender("", new Color(0.62f, 0.57f, 0.47f));

            var mode = GetConditionMode(row, isCondition1);
            var item = FindCatalogItem(condition, mode);
            if (item == null)
                return new ConditionRender($"{condition.Category} {condition.Operator} {condition.Value}", new Color(0.88f, 0.86f, 0.78f));

            var activeSkill = row.Kind == RowKind.Active ? GetActiveSkill(row.SourceIndex) : null;
            string arrow = item.Arrow switch
            {
                StrategyConditionArrow.Up => " ▲",
                StrategyConditionArrow.Down => " ▼",
                _ => ""
            };
            return new ConditionRender(item.RenderLabel(activeSkill) + arrow, ToColor(item.ResolveTextColor(activeSkill)));
        }

        private void ShowSkillPopup(RowModel row)
        {
            if (_skillPopup == null)
                return;

            PlayUiSelect();
            _skillPopup.Hide();
            ClearChildren(_skillPopup);

            bool isAdding = row.Kind == RowKind.Empty;
            var frame = new MarginContainer
            {
                CustomMinimumSize = isAdding ? new Vector2(720, 0) : new Vector2(500, 0)
            };
            AddMargins(frame, 12, 12, 12, 12);
            _skillPopup.AddChild(frame);

            if (isAdding)
            {
                var root = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                root.AddThemeConstantOverride("separation", 8);
                frame.AddChild(root);

                root.AddChild(CreatePopupTitle("新增策略"));

                var columns = new HBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(700, 430)
                };
                columns.AddThemeConstantOverride("separation", 10);
                root.AddChild(columns);

                columns.AddChild(BuildSkillChoiceColumn(list => AddActiveSkillChoices(list, row)));
                columns.AddChild(BuildSkillChoiceColumn(list => AddPassiveSkillChoices(list, row)));

                ApplyFontRecursive(_skillPopup);
                _skillPopup.PopupCentered(new Vector2I(760, 500));
                return;
            }

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(480, 480)
            };
            frame.AddChild(scroll);

            var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 7);
            scroll.AddChild(list);

            list.AddChild(CreatePopupTitle("选择行动"));
            list.AddChild(BuildDeleteButton(row));

            if (row.Kind == RowKind.Empty || row.Kind == RowKind.Active)
                AddActiveSkillChoices(list, row);
            if (row.Kind == RowKind.Empty || row.Kind == RowKind.Passive)
                AddPassiveSkillChoices(list, row);

            ApplyFontRecursive(_skillPopup);
            _skillPopup.PopupCentered(new Vector2I(520, 530));
        }

        private static Control BuildSkillChoiceColumn(Action<VBoxContainer> fill)
        {
            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(340, 420)
            };

            var list = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            list.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(list);
            fill?.Invoke(list);
            return scroll;
        }

        private void AddActiveSkillChoices(VBoxContainer list, RowModel row)
        {
            list.AddChild(CreateSectionLabel("主动技能", new Color(1f, 0.78f, 0.65f)));
            if (_activeSkills.Count == 0)
            {
                list.AddChild(CreateSectionLabel("无可用主动技能", new Color(0.62f, 0.62f, 0.62f)));
                return;
            }

            foreach (var skill in _activeSkills)
            {
                var button = CreateChoiceButton($"{skill.Name}  AP{skill.ApCost}", ActiveActionColor());
                button.TooltipText = skill.EffectDescription ?? "";
                button.Pressed += () =>
                {
                    PlayUiConfirm();
                    SetActiveSkill(row, skill.Id);
                    _skillPopup.Hide();
                    Rebuild();
                };
                list.AddChild(button);
            }
        }

        private void AddPassiveSkillChoices(VBoxContainer list, RowModel row)
        {
            list.AddChild(CreateSectionLabel("被动技能", new Color(0.7f, 1f, 1f)));
            if (_passiveSkills.Count == 0)
            {
                list.AddChild(CreateSectionLabel("无可用被动技能", new Color(0.62f, 0.62f, 0.62f)));
                return;
            }

            foreach (var skill in _passiveSkills)
            {
                var button = CreateChoiceButton($"{skill.Name}  PP{skill.PpCost}", PassiveActionColor());
                string reason = GetPassiveUnavailableReason(row, skill);
                button.Disabled = reason != null;
                button.TooltipText = reason ?? skill.EffectDescription ?? "";
                button.Pressed += () =>
                {
                    PlayUiConfirm();
                    SetPassiveSkill(row, skill.Id);
                    _skillPopup.Hide();
                    Rebuild();
                };
                list.AddChild(button);
            }
        }

        private Button BuildDeleteButton(RowModel row)
        {
            var button = CreateChoiceButton("删除此策略", new Color(0.34f, 0.16f, 0.16f));
            button.Pressed += () =>
            {
                PlayUiConfirm();
                DeleteRow(row);
                _skillPopup.Hide();
                _selectedKind = RowKind.None;
                _selectedSourceIndex = -1;
                Rebuild();
            };
            return button;
        }

        private void ShowConditionPopup(RowModel row, bool isCondition1)
        {
            if (_conditionPopup == null || row.Kind == RowKind.Empty)
                return;

            PlayUiSelect();
            _conditionPopup.Hide();
            ClearChildren(_conditionPopup);

            var activeSkill = row.Kind == RowKind.Active ? GetActiveSkill(row.SourceIndex) : null;
            var currentItem = FindCatalogItem(GetCondition(row, isCondition1), GetConditionMode(row, isCondition1));
            var selectedCategory = currentItem?.Category
                ?? StrategyConditionUiMapper.GetCatalogCategories().First().Id;

            var frame = new MarginContainer
            {
                CustomMinimumSize = new Vector2(760, 0)
            };
            AddMargins(frame, 12, 10, 12, 12);
            _conditionPopup.AddChild(frame);

            var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("separation", 8);
            frame.AddChild(root);

            var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            header.AddThemeConstantOverride("separation", 8);
            var title = CreatePopupTitle(isCondition1 ? "条件 1" : "条件 2");
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddChild(title);
            var clear = CreateChoiceButton("清空条件", new Color(0.28f, 0.22f, 0.18f));
            clear.CustomMinimumSize = new Vector2(110, 34);
            clear.Pressed += () =>
            {
                PlayUiCancel();
                SetCondition(row, isCondition1, null);
                _conditionPopup.Hide();
                Rebuild();
            };
            header.AddChild(clear);
            root.AddChild(header);

            var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            columns.AddThemeConstantOverride("separation", 10);
            root.AddChild(columns);

            var categoryScroll = new ScrollContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(245, 455)
            };
            var conditionScroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(485, 455)
            };
            columns.AddChild(categoryScroll);
            columns.AddChild(conditionScroll);

            var categoryList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            categoryList.AddThemeConstantOverride("separation", 5);
            categoryScroll.AddChild(categoryList);

            var conditionList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            conditionList.AddThemeConstantOverride("separation", 5);
            conditionScroll.AddChild(conditionList);

            void RebuildConditionList()
            {
                ClearChildren(conditionList);
                foreach (var rendered in StrategyConditionUiMapper.GetCatalogItems(selectedCategory, activeSkill))
                {
                    string arrow = rendered.Item.Arrow switch
                    {
                        StrategyConditionArrow.Up => " ▲",
                        StrategyConditionArrow.Down => " ▼",
                        _ => ""
                    };
                    var button = CreateChoiceButton(rendered.Label + arrow, ConditionButtonBackground(rendered.Item.Kind));
                    button.Alignment = HorizontalAlignment.Left;
                    button.AddThemeColorOverride("font_color", ToColor(rendered.TextColor));
                    button.AddThemeColorOverride("font_hover_color", ToColor(rendered.TextColor));
                    button.Pressed += () =>
                    {
                        PlayUiConfirm();
                        SetCondition(row, isCondition1, rendered.Item);
                        _conditionPopup.Hide();
                        Rebuild();
                    };
                    conditionList.AddChild(button);
                }
            }

            void RebuildCategoryList()
            {
                ClearChildren(categoryList);
                foreach (var category in StrategyConditionUiMapper.GetCatalogCategories())
                {
                    bool selected = category.Id == selectedCategory;
                    var button = CreateChoiceButton(category.Label, selected
                        ? new Color(0.45f, 0.36f, 0.15f)
                        : new Color(0.12f, 0.13f, 0.15f));
                    button.Alignment = HorizontalAlignment.Center;
                    button.Pressed += () =>
                    {
                        selectedCategory = category.Id;
                        RebuildCategoryList();
                        RebuildConditionList();
                    };
                    categoryList.AddChild(button);
                }
            }

            RebuildCategoryList();
            RebuildConditionList();

            ApplyFontRecursive(_conditionPopup);
            _conditionPopup.PopupCentered(new Vector2I(790, 540));
        }

        private void SelectRow(RowModel row)
        {
            _selectedKind = row.Kind;
            _selectedSourceIndex = row.SourceIndex;
            Rebuild();
        }

        private void MoveRow(RowKind kind, int sourceIndex, int targetIndex)
        {
            if (kind != RowKind.Active && kind != RowKind.Passive)
                return;
            if (sourceIndex == targetIndex)
                return;

            if (kind == RowKind.Active)
            {
                var newIndex = MoveItem(_unit.Strategies, sourceIndex, targetIndex);
                _selectedKind = RowKind.Active;
                _selectedSourceIndex = newIndex;
            }
            else
            {
                var newIndex = MoveItem(_unit.PassiveStrategies, sourceIndex, targetIndex);
                _selectedKind = RowKind.Passive;
                _selectedSourceIndex = newIndex;
                SyncEquippedPassivesFromRows();
            }

            Rebuild();
        }

        private static int MoveItem<T>(List<T> list, int sourceIndex, int targetIndex)
        {
            if (list == null
                || sourceIndex < 0
                || sourceIndex >= list.Count
                || targetIndex < 0
                || targetIndex >= list.Count)
            {
                return sourceIndex;
            }

            var item = list[sourceIndex];
            list.RemoveAt(sourceIndex);
            var insertIndex = Math.Clamp(targetIndex, 0, list.Count);
            list.Insert(insertIndex, item);
            return insertIndex;
        }

        private bool IsSelected(RowModel row)
        {
            return row.Kind == _selectedKind
                && row.SourceIndex == _selectedSourceIndex
                && row.Kind != RowKind.Empty;
        }

        private void SetActiveSkill(RowModel row, string skillId)
        {
            if (row.Kind == RowKind.Active && row.SourceIndex >= 0 && row.SourceIndex < _unit.Strategies.Count)
            {
                _unit.Strategies[row.SourceIndex].SkillId = skillId;
                _selectedKind = RowKind.Active;
                _selectedSourceIndex = row.SourceIndex;
                return;
            }

            _unit.Strategies.Add(new Strategy { SkillId = skillId });
            _selectedKind = RowKind.Active;
            _selectedSourceIndex = _unit.Strategies.Count - 1;
        }

        private void SetPassiveSkill(RowModel row, string skillId)
        {
            if (row.Kind == RowKind.Passive && row.SourceIndex >= 0 && row.SourceIndex < _unit.PassiveStrategies.Count)
            {
                var oldId = _unit.PassiveStrategies[row.SourceIndex].SkillId;
                _unit.PassiveStrategies[row.SourceIndex].SkillId = skillId;
                _unit.PassiveStrategies[row.SourceIndex].Condition1 = null;
                _unit.PassiveStrategies[row.SourceIndex].Condition2 = null;
                if (!string.IsNullOrWhiteSpace(oldId))
                    _unit.PassiveConditions.Remove(oldId);
                _selectedKind = RowKind.Passive;
                _selectedSourceIndex = row.SourceIndex;
            }
            else
            {
                _unit.PassiveStrategies.Add(new PassiveStrategy { SkillId = skillId });
                _selectedKind = RowKind.Passive;
                _selectedSourceIndex = _unit.PassiveStrategies.Count - 1;
            }

            SyncEquippedPassivesFromRows();
        }

        private void DeleteRow(RowModel row)
        {
            if (row.Kind == RowKind.Active && row.SourceIndex >= 0 && row.SourceIndex < _unit.Strategies.Count)
            {
                _unit.Strategies.RemoveAt(row.SourceIndex);
                return;
            }

            if (row.Kind == RowKind.Passive && row.SourceIndex >= 0 && row.SourceIndex < _unit.PassiveStrategies.Count)
            {
                var skillId = _unit.PassiveStrategies[row.SourceIndex].SkillId;
                _unit.PassiveStrategies.RemoveAt(row.SourceIndex);
                if (!string.IsNullOrWhiteSpace(skillId))
                    _unit.PassiveConditions.Remove(skillId);
                SyncEquippedPassivesFromRows();
            }
        }

        private void SetCondition(RowModel row, bool isCondition1, StrategyConditionCatalogItem item)
        {
            var condition = item?.BuildCondition();
            var mode = item?.Kind == StrategyConditionKind.Only ? ConditionMode.Only : ConditionMode.Priority;

            if (row.Kind == RowKind.Active && row.SourceIndex >= 0 && row.SourceIndex < _unit.Strategies.Count)
            {
                var strategy = _unit.Strategies[row.SourceIndex];
                if (isCondition1)
                {
                    strategy.Condition1 = condition;
                    strategy.Mode1 = mode;
                }
                else
                {
                    strategy.Condition2 = condition;
                    strategy.Mode2 = mode;
                }
                return;
            }

            if (row.Kind == RowKind.Passive && row.SourceIndex >= 0 && row.SourceIndex < _unit.PassiveStrategies.Count)
            {
                var strategy = _unit.PassiveStrategies[row.SourceIndex];
                if (isCondition1)
                {
                    strategy.Condition1 = condition;
                    strategy.Mode1 = mode;
                }
                else
                {
                    strategy.Condition2 = condition;
                    strategy.Mode2 = mode;
                }

                if (!string.IsNullOrWhiteSpace(strategy.SkillId))
                    _unit.PassiveConditions.Remove(strategy.SkillId);
            }
        }

        private Condition GetCondition(RowModel row, bool isCondition1)
        {
            return row.Kind switch
            {
                RowKind.Active => isCondition1
                    ? GetActiveStrategy(row.SourceIndex)?.Condition1
                    : GetActiveStrategy(row.SourceIndex)?.Condition2,
                RowKind.Passive => isCondition1
                    ? GetPassiveStrategy(row.SourceIndex)?.Condition1
                    : GetPassiveStrategy(row.SourceIndex)?.Condition2,
                _ => null
            };
        }

        private ConditionMode GetConditionMode(RowModel row, bool isCondition1)
        {
            return row.Kind switch
            {
                RowKind.Active => isCondition1
                    ? GetActiveStrategy(row.SourceIndex)?.Mode1 ?? ConditionMode.Priority
                    : GetActiveStrategy(row.SourceIndex)?.Mode2 ?? ConditionMode.Priority,
                RowKind.Passive => isCondition1
                    ? GetPassiveStrategy(row.SourceIndex)?.Mode1 ?? ConditionMode.Priority
                    : GetPassiveStrategy(row.SourceIndex)?.Mode2 ?? ConditionMode.Priority,
                _ => ConditionMode.Priority
            };
        }

        private Strategy GetActiveStrategy(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < _unit.Strategies.Count
                ? _unit.Strategies[sourceIndex]
                : null;
        }

        private PassiveStrategy GetPassiveStrategy(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < _unit.PassiveStrategies.Count
                ? _unit.PassiveStrategies[sourceIndex]
                : null;
        }

        private ActiveSkillData GetActiveSkill(int sourceIndex)
        {
            var strategy = GetActiveStrategy(sourceIndex);
            return string.IsNullOrWhiteSpace(strategy?.SkillId)
                ? null
                : _gameData.GetActiveSkill(strategy.SkillId);
        }

        private PassiveSkillData GetPassiveSkill(int sourceIndex)
        {
            var strategy = GetPassiveStrategy(sourceIndex);
            return string.IsNullOrWhiteSpace(strategy?.SkillId)
                ? null
                : _gameData.GetPassiveSkill(strategy.SkillId);
        }

        private StrategyConditionCatalogItem FindCatalogItem(Condition condition, ConditionMode mode)
        {
            return StrategyConditionUiMapper.FindCatalogItem(condition, mode)
                ?? StrategyConditionUiMapper.FindCatalogItem(condition,
                    mode == ConditionMode.Only ? ConditionMode.Priority : ConditionMode.Only);
        }

        private string GetPassiveUnavailableReason(RowModel row, PassiveSkillData skill)
        {
            if (skill == null)
                return "不可用";

            string replacingId = row.Kind == RowKind.Passive ? GetPassiveStrategy(row.SourceIndex)?.SkillId : null;
            if (!string.Equals(replacingId, skill.Id, StringComparison.OrdinalIgnoreCase)
                && _unit.PassiveStrategies.Any(strategy => string.Equals(strategy?.SkillId, skill.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return "已在被动策略中";
            }

            int usedPp = _unit.PassiveStrategies
                .Where(strategy => strategy != null
                    && !string.IsNullOrWhiteSpace(strategy.SkillId)
                    && !string.Equals(strategy.SkillId, replacingId, StringComparison.OrdinalIgnoreCase))
                .Select(strategy => _gameData.GetPassiveSkill(strategy.SkillId)?.PpCost ?? 0)
                .Sum();
            return usedPp + skill.PpCost <= _unit.MaxPp
                ? null
                : $"PP不足：需要 {usedPp + skill.PpCost}/{_unit.MaxPp}";
        }

        private void NormalizePassiveStrategies()
        {
            if (_unit == null)
                return;

            var represented = _unit.PassiveStrategies
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.SkillId))
                .Select(row => row.SkillId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var skillId in _unit.EquippedPassiveSkillIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList())
            {
                if (represented.Contains(skillId))
                    continue;

                _unit.PassiveStrategies.Add(new PassiveStrategy
                {
                    SkillId = skillId,
                    Condition1 = _unit.PassiveConditions.TryGetValue(skillId, out var condition) ? condition : null
                });
                represented.Add(skillId);
            }
        }

        private void SyncEquippedPassivesFromRows()
        {
            var ids = _unit.PassiveStrategies
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.SkillId))
                .Select(row => row.SkillId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _unit.EquippedPassiveSkillIds = ids;
        }

        private static RowPalette GetPalette(RowKind kind, bool selected)
        {
            if (selected)
            {
                return new RowPalette(
                    new Color(0.48f, 0.54f, 0.28f),
                    new Color(0.42f, 0.56f, 0.32f),
                    new Color(0.38f, 0.34f, 0.20f),
                    new Color(1f, 1f, 0.82f));
            }

            return kind switch
            {
                RowKind.Active => new RowPalette(
                    new Color(0.30f, 0.06f, 0.05f),
                    ActiveActionColor(),
                    new Color(0.26f, 0.22f, 0.13f),
                    new Color(1f, 0.94f, 0.82f)),
                RowKind.Passive => new RowPalette(
                    new Color(0.05f, 0.22f, 0.24f),
                    PassiveActionColor(),
                    new Color(0.20f, 0.24f, 0.16f),
                    new Color(0.80f, 1f, 1f)),
                _ => new RowPalette(
                    new Color(0.10f, 0.10f, 0.12f),
                    new Color(0.12f, 0.12f, 0.14f),
                    new Color(0.10f, 0.10f, 0.12f),
                    new Color(0.45f, 0.45f, 0.48f))
            };
        }

        private static Color ActiveActionColor() => new(0.52f, 0.08f, 0.06f);
        private static Color PassiveActionColor() => new(0.05f, 0.42f, 0.42f);

        private static Color ConditionButtonBackground(StrategyConditionKind kind)
        {
            return kind == StrategyConditionKind.Priority
                ? new Color(0.30f, 0.24f, 0.12f)
                : new Color(0.22f, 0.18f, 0.11f);
        }

        private static Color ToColor(StrategyConditionTextColor color)
        {
            return color switch
            {
                StrategyConditionTextColor.EnemyRed => new Color(1f, 0.42f, 0.42f),
                StrategyConditionTextColor.AllyCyanGreen => new Color(0.45f, 1f, 0.96f),
                StrategyConditionTextColor.NeutralGold => new Color(1f, 0.88f, 0.46f),
                _ => new Color(0.95f, 0.92f, 0.84f)
            };
        }

        private static Button CreateCellButton(string text, Vector2 minSize, Color background, Color textColor)
        {
            var button = new Button
            {
                Text = text ?? "",
                CustomMinimumSize = minSize,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            ApplyCellButtonTheme(button, background, textColor);
            return button;
        }

        private StrategyRowCellButton CreateRowCellButton(RowModel row, string text, Vector2 minSize, Color background, Color textColor)
        {
            var button = new StrategyRowCellButton(this, row)
            {
                Text = text ?? "",
                CustomMinimumSize = minSize,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            if (row.Kind != RowKind.Empty)
            {
                button.MouseDefaultCursorShape = Control.CursorShape.Drag;
                button.TooltipText = "拖拽调整同类技能优先级；点击编辑。";
            }
            ApplyCellButtonTheme(button, background, textColor);
            return button;
        }

        private static void ApplyCellButtonTheme(Button button, Color background, Color textColor)
        {
            button.AddThemeFontSizeOverride("font_size", BodyFontSize);
            button.AddThemeColorOverride("font_color", textColor);
            button.AddThemeColorOverride("font_hover_color", textColor);
            button.AddThemeStyleboxOverride("normal", CreateStyle(background, Brighten(background, 0.08f)));
            button.AddThemeStyleboxOverride("hover", CreateStyle(Brighten(background, 0.04f), new Color(0.76f, 0.66f, 0.36f)));
            button.AddThemeStyleboxOverride("pressed", CreateStyle(Darken(background, 0.03f), new Color(0.96f, 0.86f, 0.52f)));
            button.AddThemeStyleboxOverride("disabled", CreateStyle(background, Brighten(background, 0.04f)));
        }

        private static Button CreateChoiceButton(string text, Color background)
        {
            var button = CreateCellButton(text, new Vector2(0, 38), background, new Color(0.95f, 0.92f, 0.84f));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
            return button;
        }

        private static Label CreatePopupTitle(string text)
        {
            var label = new Label
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center
            };
            label.AddThemeFontSizeOverride("font_size", PopupTitleFontSize);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.91f, 0.58f));
            return label;
        }

        private static Label CreateSectionLabel(string text, Color color)
        {
            var label = new Label
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center
            };
            label.AddThemeFontSizeOverride("font_size", BodyFontSize);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        private static void AddHeader(GridContainer grid, string text, float width)
        {
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(width, 34)
            };
            label.AddThemeFontSizeOverride("font_size", HeaderFontSize);
            label.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.45f));
            grid.AddChild(label);
        }

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
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3
            };
        }

        private static Color Brighten(Color color, float amount)
        {
            return new Color(
                Math.Min(1f, color.R + amount),
                Math.Min(1f, color.G + amount),
                Math.Min(1f, color.B + amount),
                color.A);
        }

        private static Color Darken(Color color, float amount)
        {
            return new Color(
                Math.Max(0f, color.R - amount),
                Math.Max(0f, color.G - amount),
                Math.Max(0f, color.B - amount),
                color.A);
        }

        private static void AddMargins(MarginContainer margin, int left, int top, int right, int bottom)
        {
            margin.AddThemeConstantOverride("margin_left", left);
            margin.AddThemeConstantOverride("margin_top", top);
            margin.AddThemeConstantOverride("margin_right", right);
            margin.AddThemeConstantOverride("margin_bottom", bottom);
        }

        private static void PlayUiSelect()
        {
            // Reserved for UI sound wiring once the strategy flow is settled.
        }

        private static void PlayUiConfirm()
        {
            // Reserved for UI sound wiring once the strategy flow is settled.
        }

        private static void PlayUiCancel()
        {
            // Reserved for UI sound wiring once the strategy flow is settled.
        }

        private static void ApplyFontRecursive(Node node)
        {
            if (node == null)
                return;

            if (node is Control control
                && node is Label or Button
                && !control.HasThemeFontSizeOverride("font_size"))
            {
                control.AddThemeFontSizeOverride("font_size", BodyFontSize);
            }

            foreach (var child in node.GetChildren())
                ApplyFontRecursive(child);
        }

        private static void ClearChildren(Node node)
        {
            foreach (var child in node.GetChildren())
                child.QueueFree();
        }

        private enum RowKind
        {
            None,
            Active,
            Passive,
            Empty
        }

        private readonly record struct RowModel(RowKind Kind, int SourceIndex, int DisplayOrder);

        private readonly record struct RowPalette(
            Color OrderBackground,
            Color ActionBackground,
            Color ConditionBackground,
            Color TextColor);

        private readonly record struct ConditionRender(string Text, Color Color);

        private sealed partial class StrategyRowCellButton : Button
        {
            private const string DragPrefix = "SANDBOX_STRATEGY_ROW:";

            private readonly SandboxStrategyTableView _owner;
            private readonly RowModel _row;

            public StrategyRowCellButton(SandboxStrategyTableView owner, RowModel row)
            {
                _owner = owner;
                _row = row;
            }

            public override Variant _GetDragData(Vector2 atPosition)
            {
                if (_row.Kind != RowKind.Active && _row.Kind != RowKind.Passive)
                    return default;

                var preview = new Label { Text = Text };
                preview.AddThemeFontSizeOverride("font_size", BodyFontSize);
                SetDragPreview(preview);
                return $"{DragPrefix}{_row.Kind}:{_row.SourceIndex.ToString(CultureInfo.InvariantCulture)}";
            }

            public override bool _CanDropData(Vector2 atPosition, Variant data)
            {
                return _row.Kind != RowKind.Empty
                    && data.VariantType == Variant.Type.String
                    && TryParseDragData((string)data, out var kind, out var sourceIndex)
                    && kind == _row.Kind
                    && sourceIndex != _row.SourceIndex;
            }

            public override void _DropData(Vector2 atPosition, Variant data)
            {
                if (data.VariantType != Variant.Type.String)
                    return;

                if (TryParseDragData((string)data, out var kind, out var sourceIndex))
                    _owner.MoveRow(kind, sourceIndex, _row.SourceIndex);
            }

            private static bool TryParseDragData(string raw, out RowKind kind, out int sourceIndex)
            {
                kind = RowKind.None;
                sourceIndex = -1;

                if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(DragPrefix, StringComparison.Ordinal))
                    return false;

                var parts = raw.Split(':', 3);
                return parts.Length == 3
                    && Enum.TryParse(parts[1], out kind)
                    && int.TryParse(parts[2], out sourceIndex)
                    && sourceIndex >= 0;
            }
        }
    }
}
