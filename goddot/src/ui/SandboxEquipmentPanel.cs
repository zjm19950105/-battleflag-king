using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using Godot;

namespace BattleKing.Ui
{
    public sealed class SandboxEquipmentPanel
    {
        private static int BodyFontSize => Math.Max(11, TestSandboxView.CurrentBodyFontSize - 2);
        private static int TitleFontSize => BodyFontSize + 1;
        private static int SmallFontSize => Math.Max(10, BodyFontSize - 1);

        private Control _root;
        private VBoxContainer _slotList;
        private VBoxContainer _equipmentDetailContent;
        private PopupPanel _candidatePopup;
        private EquipmentData _pinnedEquipment;
        private GameDataRepository _gameData;
        private Action<ActiveSkillData> _onActiveSkillSelected;
        private Action<PassiveSkillData> _onPassiveSkillSelected;

        public Control Build(
            BattleUnit unit,
            GameDataRepository gameData,
            Action<EquipmentStatPreview> onPreviewChanged,
            Action onPreviewCleared,
            Action onEquipmentChanged,
            Action<ActiveSkillData> onActiveSkillSelected = null,
            Action<PassiveSkillData> onPassiveSkillSelected = null)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (gameData == null) throw new ArgumentNullException(nameof(gameData));
            _gameData = gameData;
            _onActiveSkillSelected = onActiveSkillSelected;
            _onPassiveSkillSelected = onPassiveSkillSelected;

            var root = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(260, 205)
            };
            root.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.12f, 0.14f, 0.16f), new Color(0.3f, 0.34f, 0.4f)));
            _root = root;

            var margin = new MarginContainer();
            AddMargins(margin, 6, 5, 6, 5);
            root.AddChild(margin);

            var layout = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            layout.AddThemeConstantOverride("separation", 3);
            margin.AddChild(layout);

            layout.AddChild(BuildHeader(unit));
            layout.AddChild(BuildEquipmentDetailPanel());

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            layout.AddChild(scroll);

            _slotList = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _slotList.AddThemeConstantOverride("separation", 4);
            scroll.AddChild(_slotList);

            _candidatePopup = new PopupPanel
            {
                Exclusive = false,
                Unresizable = false
            };
            _candidatePopup.CloseRequested += () =>
            {
                onPreviewCleared?.Invoke();
                _candidatePopup.Hide();
            };
            root.AddChild(_candidatePopup);

            RebuildSlots(unit, gameData, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            ShowEquipmentDetail(null);
            ApplyPanelFontRecursive(root);
            return root;
        }

        private Control BuildHeader(BattleUnit unit)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 8);

            var title = CreateLabel($"{GetUnitName(unit)} 装备", TitleFontSize, new Color(1f, 0.93f, 0.68f));
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(title);

            var slotCount = EquipmentSlot.GetSlotNames(unit.Data, unit.IsCc).Count;
            row.AddChild(BuildChip($"{slotCount}槽", new Color(0.18f, 0.23f, 0.3f), new Color(0.82f, 0.9f, 1f)));

            return row;
        }

        private Control BuildEquipmentDetailPanel()
        {
            var panel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50)
            };
            panel.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.10f, 0.11f, 0.13f), new Color(0.34f, 0.38f, 0.44f)));

            var margin = new MarginContainer();
            AddMargins(margin, 6, 3, 6, 3);
            panel.AddChild(margin);

            _equipmentDetailContent = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _equipmentDetailContent.AddThemeConstantOverride("separation", 2);
            margin.AddChild(_equipmentDetailContent);

            return panel;
        }

        private void RebuildSlots(
            BattleUnit unit,
            GameDataRepository gameData,
            Action<EquipmentStatPreview> onPreviewChanged,
            Action onPreviewCleared,
            Action onEquipmentChanged)
        {
            ClearChildren(_slotList);

            var slots = EquipmentSlot.GetSlotNames(unit.Data, unit.IsCc);
            if (slots.Count == 0)
            {
                _slotList.AddChild(CreateLabel("没有可用装备槽", BodyFontSize, new Color(0.84f, 0.86f, 0.88f)));
                return;
            }

            foreach (var slotName in slots)
            {
                _slotList.AddChild(BuildSlotRow(unit, gameData, slotName, onPreviewChanged, onPreviewCleared, onEquipmentChanged));
            }
        }

        private Control BuildSlotRow(
            BattleUnit unit,
            GameDataRepository gameData,
            string slotName,
            Action<EquipmentStatPreview> onPreviewChanged,
            Action onPreviewCleared,
            Action onEquipmentChanged)
        {
            var current = unit.Equipment.GetBySlot(slotName)?.Data;

            var rowFrame = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34)
            };
            rowFrame.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.17f, 0.19f, 0.22f), new Color(0.32f, 0.36f, 0.42f)));

            var margin = new MarginContainer();
            AddMargins(margin, 5, 3, 5, 3);
            rowFrame.AddChild(margin);

            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 5);
            margin.AddChild(row);

            var slotButton = CreateButton(GetSlotLabel(slotName), new Vector2(76, 28), new Color(0.22f, 0.27f, 0.34f));
            slotButton.TooltipText = "打开候选装备";
            slotButton.Pressed += () => ShowCandidates(unit, gameData, slotName, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            row.AddChild(slotButton);

            var equipmentButton = CreateButton(GetEquipmentName(current), new Vector2(140, 28), new Color(0.2f, 0.22f, 0.26f));
            equipmentButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            equipmentButton.Alignment = HorizontalAlignment.Left;
            equipmentButton.MouseEntered += () =>
            {
                ShowEquipmentDetail(current);
                if (current != null)
                    onPreviewChanged?.Invoke(EquipmentStatPreviewHelper.Build(unit, slotName, current));
            };
            equipmentButton.MouseExited += () =>
            {
                RestorePinnedEquipmentDetail();
                onPreviewCleared?.Invoke();
            };
            equipmentButton.Pressed += () =>
            {
                PinEquipment(current);
                ShowCandidates(unit, gameData, slotName, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            };
            SandboxTooltipHelper.AttachEquipmentTooltip(equipmentButton, current, gameData);
            row.AddChild(equipmentButton);

            var changeButton = CreateButton("更换", new Vector2(54, 28), new Color(0.2f, 0.3f, 0.36f));
            changeButton.Pressed += () => ShowCandidates(unit, gameData, slotName, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            row.AddChild(changeButton);

            bool canClear = EquipmentSlotUiPolicy.CanClearSlot(unit, slotName);
            var clearButton = CreateButton("清空", new Vector2(52, 28), new Color(0.32f, 0.22f, 0.23f));
            clearButton.Disabled = current == null || !canClear;
            clearButton.TooltipText = canClear ? "卸下当前装备" : "武器和盾槽不可清空";
            clearButton.MouseEntered += () =>
            {
                if (!canClear)
                    return;
                ShowEquipmentDetail(null);
                onPreviewChanged?.Invoke(EquipmentStatPreviewHelper.Build(unit, slotName, null));
            };
            clearButton.MouseExited += () =>
            {
                RestorePinnedEquipmentDetail();
                onPreviewCleared?.Invoke();
            };
            clearButton.Pressed += () =>
            {
                if (!canClear)
                    return;
                EquipAndRefresh(unit, gameData, slotName, null, onPreviewCleared, onEquipmentChanged);
                RebuildSlots(unit, gameData, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            };
            row.AddChild(clearButton);

            return rowFrame;
        }

        private void ShowCandidates(
            BattleUnit unit,
            GameDataRepository gameData,
            string slotName,
            Action<EquipmentStatPreview> onPreviewChanged,
            Action onPreviewCleared,
            Action onEquipmentChanged)
        {
            if (_candidatePopup == null || _root == null)
                return;

            _candidatePopup.Hide();
            onPreviewCleared?.Invoke();
            ClearChildren(_candidatePopup);

            var frame = new MarginContainer
            {
                CustomMinimumSize = new Vector2(440, 0)
            };
            AddMargins(frame, 8, 8, 8, 8);
            _candidatePopup.AddChild(frame);

            var popupHeight = Math.Min(470, 72 + Math.Max(1, GetCandidates(unit, gameData, slotName).Count + 1) * 36);
            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(420, popupHeight)
            };
            frame.AddChild(scroll);

            var list = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            list.AddThemeConstantOverride("separation", 4);
            scroll.AddChild(list);

            list.AddChild(CreateLabel($"{GetSlotLabel(slotName)} 候选装备", TitleFontSize, new Color(1f, 0.93f, 0.68f)));
            if (EquipmentSlotUiPolicy.CanClearSlot(unit, slotName))
                list.AddChild(BuildCandidateButton(unit, gameData, slotName, null, onPreviewChanged, onPreviewCleared, onEquipmentChanged));

            var candidates = GetCandidates(unit, gameData, slotName);
            if (candidates.Count == 0)
            {
                var empty = CreateLabel("没有可装备候选", BodyFontSize, new Color(0.78f, 0.82f, 0.86f));
                list.AddChild(empty);
            }
            else
            {
                foreach (var equipment in candidates)
                    list.AddChild(BuildCandidateButton(unit, gameData, slotName, equipment, onPreviewChanged, onPreviewCleared, onEquipmentChanged));
            }

            ApplyPanelFontRecursive(_candidatePopup);
            _candidatePopup.PopupCentered(new Vector2I(460, popupHeight + 28));
        }

        private Control BuildCandidateButton(
            BattleUnit unit,
            GameDataRepository gameData,
            string slotName,
            EquipmentData equipment,
            Action<EquipmentStatPreview> onPreviewChanged,
            Action onPreviewCleared,
            Action onEquipmentChanged)
        {
            var button = CreateButton(GetCandidateText(equipment), new Vector2(360, 32), equipment == null
                ? new Color(0.24f, 0.22f, 0.22f)
                : new Color(0.18f, 0.22f, 0.27f));
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
            button.MouseEntered += () =>
            {
                ShowEquipmentDetail(equipment);
                onPreviewChanged?.Invoke(EquipmentStatPreviewHelper.Build(unit, slotName, equipment));
            };
            button.MouseExited += () =>
            {
                RestorePinnedEquipmentDetail();
                onPreviewCleared?.Invoke();
            };
            button.Pressed += () =>
            {
                PinEquipment(equipment);
                EquipAndRefresh(unit, gameData, slotName, equipment, onPreviewCleared, onEquipmentChanged);
                RebuildSlots(unit, gameData, onPreviewChanged, onPreviewCleared, onEquipmentChanged);
            };

            SandboxTooltipHelper.AttachEquipmentTooltip(button, equipment, gameData);
            return button;
        }

        private void PinEquipment(EquipmentData equipment)
        {
            _pinnedEquipment = equipment;
            ShowEquipmentDetail(equipment);
        }

        private void RestorePinnedEquipmentDetail()
        {
            ShowEquipmentDetail(_pinnedEquipment);
        }

        private void ShowEquipmentDetail(EquipmentData equipment)
        {
            if (_equipmentDetailContent == null)
                return;

            ClearChildren(_equipmentDetailContent);

            if (equipment == null)
            {
                var empty = CreateLabel("悬停或点击装备查看属性、特殊效果和附带技能。", BodyFontSize, new Color(0.78f, 0.82f, 0.86f));
                empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                _equipmentDetailContent.AddChild(empty);
                return;
            }

            var title = CreateLabel(GetEquipmentName(equipment), TitleFontSize, new Color(1f, 0.93f, 0.68f));
            title.TooltipText = SandboxTooltipHelper.BuildEquipmentDetail(equipment, _gameData);
            _equipmentDetailContent.AddChild(title);

            var detail = new RichTextLabel
            {
                BbcodeEnabled = false,
                FitContent = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 42)
            };
            detail.AddThemeFontSizeOverride("normal_font_size", SmallFontSize);
            detail.AddText(BuildEquipmentSummary(equipment));
            _equipmentDetailContent.AddChild(detail);

            AddGrantedSkillButtons(equipment);
            ApplyPanelFontRecursive(_equipmentDetailContent);
        }

        private void AddGrantedSkillButtons(EquipmentData equipment)
        {
            var activeIds = equipment.GrantedActiveSkillIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList() ?? new List<string>();
            var passiveIds = equipment.GrantedPassiveSkillIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList() ?? new List<string>();

            if (activeIds.Count == 0 && passiveIds.Count == 0)
                return;

            var row = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("h_separation", 6);
            row.AddThemeConstantOverride("v_separation", 6);
            _equipmentDetailContent.AddChild(row);

            foreach (var id in activeIds)
            {
                var skill = _gameData?.GetActiveSkill(id);
                var button = CreateButton("主动 " + SkillName(skill, id), new Vector2(116, 28), new Color(0.18f, 0.25f, 0.32f));
                SandboxTooltipHelper.AttachActiveSkillTooltip(button, skill);
                button.Pressed += () => _onActiveSkillSelected?.Invoke(skill);
                row.AddChild(button);
            }

            foreach (var id in passiveIds)
            {
                var skill = _gameData?.GetPassiveSkill(id);
                var button = CreateButton("被动 " + SkillName(skill, id), new Vector2(116, 28), new Color(0.22f, 0.22f, 0.32f));
                SandboxTooltipHelper.AttachPassiveSkillTooltip(button, skill);
                button.Pressed += () => _onPassiveSkillSelected?.Invoke(skill);
                row.AddChild(button);
            }
        }

        private static string BuildEquipmentSummary(EquipmentData equipment)
        {
            var lines = new List<string> { $"类别：{EquipmentCategoryLabel(equipment.Category)}" };
            var stats = FormatStats(equipment.BaseStats);
            lines.Add("属性：" + (string.IsNullOrWhiteSpace(stats) ? "无" : stats));

            var effects = equipment.SpecialEffects?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();
            lines.Add("特殊效果：" + (effects == null || effects.Count == 0 ? "无" : string.Join("；", effects)));
            return string.Join("\n", lines);
        }

        private static string SkillName(ActiveSkillData skill, string fallback)
        {
            return string.IsNullOrWhiteSpace(skill?.Name) ? fallback : skill.Name;
        }

        private static string SkillName(PassiveSkillData skill, string fallback)
        {
            return string.IsNullOrWhiteSpace(skill?.Name) ? fallback : skill.Name;
        }

        private void EquipAndRefresh(
            BattleUnit unit,
            GameDataRepository gameData,
            string slotName,
            EquipmentData equipment,
            Action onPreviewCleared,
            Action onEquipmentChanged)
        {
            if (equipment == null && !EquipmentSlotUiPolicy.CanClearSlot(unit, slotName))
                return;

            int previousMaxHp = Math.Max(1, unit.GetCurrentStat("HP"));
            unit.Equipment.EquipToSlot(slotName, equipment);
            unit.SyncResourceCapsFromStats(previousMaxHp);
            onPreviewCleared?.Invoke();
            _candidatePopup?.Hide();
            onEquipmentChanged?.Invoke();
        }

        private static List<EquipmentData> GetCandidates(BattleUnit unit, GameDataRepository gameData, string slotName)
        {
            var expectedCategory = EquipmentSlotUiPolicy.GetExpectedCategory(slotName, unit.Data, unit.IsCc);
            if (!expectedCategory.HasValue)
                return new List<EquipmentData>();

            return gameData.GetAllEquipment()
                .Where(e => e != null)
                .Where(e => e.Category == expectedCategory.Value)
                .Where(e => EquipmentSlot.CanEquipCategory(e.Category, unit.Data, unit.IsCc))
                .OrderBy(e => e.Name ?? e.Id)
                .ToList();
        }

        private static string GetCandidateText(EquipmentData equipment)
        {
            if (equipment == null)
                return "卸下 / 空";

            var parts = new List<string> { equipment.Name ?? equipment.Id ?? "(未命名装备)" };
            var stats = FormatStats(equipment.BaseStats);
            if (!string.IsNullOrWhiteSpace(stats))
                parts.Add(stats);
            if (equipment.SpecialEffects != null && equipment.SpecialEffects.Count > 0)
                parts.Add("[" + string.Join(", ", equipment.SpecialEffects.Where(s => !string.IsNullOrWhiteSpace(s))) + "]");
            return string.Join("  ", parts);
        }

        private static string FormatStats(Dictionary<string, int> stats)
        {
            if (stats == null || stats.Count == 0)
                return "";

            return string.Join(" ", stats
                .Where(kv => kv.Value != 0)
                .Select(kv => $"{StatLabel(kv.Key)}{FormatSigned(kv.Value)}"));
        }

        private static string FormatSigned(int value)
        {
            return value > 0
                ? "+" + value.ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetSlotLabel(string slotName)
        {
            return slotName switch
            {
                "MainHand" => "主手",
                "OffHand" => "副手",
                "Accessory1" => "饰品 1",
                "Accessory2" => "饰品 2",
                "Accessory3" => "饰品 3",
                _ => slotName
            };
        }

        private static string GetEquipmentName(EquipmentData equipment)
        {
            if (!string.IsNullOrWhiteSpace(equipment?.Name))
                return equipment.Name;

            if (!string.IsNullOrWhiteSpace(equipment?.Id))
                return equipment.Id;

            return "(空)";
        }

        private static string GetUnitName(BattleUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit?.Data?.Name))
                return unit.Data.Name;

            return "未命名角色";
        }

        private static string EquipmentCategoryLabel(EquipmentCategory category) => category switch
        {
            EquipmentCategory.Sword => "剑",
            EquipmentCategory.Axe => "斧",
            EquipmentCategory.Spear => "枪",
            EquipmentCategory.Bow => "弓",
            EquipmentCategory.Staff => "杖",
            EquipmentCategory.Shield => "盾",
            EquipmentCategory.GreatShield => "大盾",
            EquipmentCategory.Accessory => "饰品",
            _ => category.ToString()
        };

        private static string StatLabel(string stat) => stat switch
        {
            "HP" => "HP",
            "Str" => "物攻",
            "Def" => "物防",
            "Mag" => "魔攻",
            "MDef" => "魔防",
            "Hit" or "hit" or "hit_rate" => "命中",
            "Eva" or "eva" => "回避",
            "Crit" or "crit" => "会心",
            "CritDmg" or "crit_dmg" => "会伤",
            "Block" or "block" or "block_rate" => "格挡",
            "Spd" or "spd" => "速度",
            "AP" => "AP",
            "PP" => "PP",
            "phys_atk" => "物攻",
            "phys_def" => "物防",
            "mag_atk" => "魔攻",
            "mag_def" => "魔防",
            _ => stat
        };

        private static Button CreateButton(string text, Vector2 minSize, Color background)
        {
            var button = new Button
            {
                Text = text ?? string.Empty,
                CustomMinimumSize = minSize,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            button.AddThemeFontSizeOverride("font_size", BodyFontSize);
            button.AddThemeStyleboxOverride("normal", CreateStyle(background, new Color(background.R + 0.08f, background.G + 0.08f, background.B + 0.08f)));
            button.AddThemeStyleboxOverride("hover", CreateStyle(new Color(background.R + 0.04f, background.G + 0.04f, background.B + 0.04f), new Color(0.55f, 0.62f, 0.68f)));
            button.AddThemeStyleboxOverride("pressed", CreateStyle(new Color(background.R - 0.03f, background.G - 0.03f, background.B - 0.03f), new Color(0.75f, 0.7f, 0.48f)));
            return button;
        }

        private static Label CreateLabel(string text, int fontSize, Color color)
        {
            var label = new Label
            {
                Text = text ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        private static Control BuildChip(string text, Color background, Color foreground)
        {
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", CreateStyle(background, new Color(background.R + 0.08f, background.G + 0.08f, background.B + 0.08f)));

            var margin = new MarginContainer();
            AddMargins(margin, 6, 2, 6, 2);
            chip.AddChild(margin);

            var label = CreateLabel(text, SmallFontSize, foreground);
            margin.AddChild(label);

            return chip;
        }

        private static StyleBoxFlat CreateStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BgColor = ClampColor(background),
                BorderColor = ClampColor(border),
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

        private static Color ClampColor(Color color)
        {
            return new Color(
                Math.Clamp(color.R, 0f, 1f),
                Math.Clamp(color.G, 0f, 1f),
                Math.Clamp(color.B, 0f, 1f),
                Math.Clamp(color.A, 0f, 1f));
        }

        private static void AddMargins(MarginContainer margin, int left, int top, int right, int bottom)
        {
            margin.AddThemeConstantOverride("margin_left", left);
            margin.AddThemeConstantOverride("margin_top", top);
            margin.AddThemeConstantOverride("margin_right", right);
            margin.AddThemeConstantOverride("margin_bottom", bottom);
        }

        private static void ClearChildren(Node node)
        {
            if (node == null)
                return;

            foreach (var child in node.GetChildren())
                child.QueueFree();
        }

        private static void ApplyPanelFontRecursive(Node node)
        {
            if (node is Control control)
            {
                if (control is RichTextLabel richText)
                    SetFontSizeIfMissing(richText, "normal_font_size", BodyFontSize);
                else if (control is Label or Button or OptionButton or CheckBox)
                    SetFontSizeIfMissing(control, "font_size", BodyFontSize);

                if (control is OptionButton optionButton)
                    SetPopupFontSize(optionButton.GetPopup(), BodyFontSize);
            }

            foreach (var child in node.GetChildren())
                ApplyPanelFontRecursive(child);
        }

        private static void SetFontSizeIfMissing(Control control, string themeType, int size)
        {
            if (control == null || control.HasThemeFontSizeOverride(themeType))
                return;

            control.AddThemeFontSizeOverride(themeType, size);
        }

        private static void SetPopupFontSize(PopupMenu popup, int size)
        {
            if (popup == null || popup.HasThemeFontSizeOverride("font_size"))
                return;

            popup.AddThemeFontSizeOverride("font_size", size);
        }
    }
}
