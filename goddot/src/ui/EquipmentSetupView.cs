using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;
using Godot;

namespace BattleKing.Ui
{
    public sealed class EquipmentSetupView
    {
        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly Func<string, Action, Button> _createButton;

        public EquipmentSetupView(
            VBoxContainer leftPanel,
            VBoxContainer rightPanel,
            HBoxContainer buttonBar,
            Func<string, Action, Button> createButton)
        {
            _leftPanel = leftPanel;
            _rightPanel = rightPanel;
            _buttonBar = buttonBar;
            _createButton = createButton;
        }

        public void Show(BattleUnit unit, GameDataRepository gameData, Action onConfirmNext, Action onSkipAll, Action onBack)
        {
            var cd = unit.Data;
            bool isCc = unit.IsCc;
            var slots = EquipmentSlot.GetSlotNames(cd, isCc);
            var allEquip = gameData.GetAllEquipment();

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
                    int previousMaxHp = Math.Max(1, unit.GetCurrentStat("HP"));
                    if (s == 0) unit.Equipment.Unequip(slotCapture);
                    else if (s - 1 < capsCopy.Count)
                        unit.Equipment.EquipToSlot(slotCapture, capsCopy[s - 1]);
                    unit.SyncResourceCapsFromStats(previousMaxHp);
                    UpdateEquipDetail(unit);
                };
                row.AddChild(dropdown);
                slotList.AddChild(row);
            }

            _leftPanel.AddChild(slotScroll);

            // Right panel: stat overview
            UpdateEquipDetail(unit);

            // Bottom buttons
            _buttonBar.AddChild(_createButton("→ 确认/下一个角色", onConfirmNext));
            _buttonBar.AddChild(_createButton("→ 全部默认装备", onSkipAll));
            _buttonBar.AddChild(_createButton("← 上一步", onBack));
        }

        public static int GetSlotCount(BattleUnit unit)
        {
            return EquipmentSlot.GetSlotNames(unit.Data, unit.IsCc).Count;
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
                int buffVal = (int)(baseVal * BuffManager.GetTotalBuffRatio(unit, sn));
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

        private static void ClearPanel(Control panel)
        {
            foreach (var child in panel.GetChildren())
                child.QueueFree();
        }
    }
}
