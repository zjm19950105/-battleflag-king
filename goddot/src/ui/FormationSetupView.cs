using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public sealed class FormationSetupView
    {
        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly IReadOnlyList<CharacterData> _allChars;
        private readonly Func<string, Action, Button> _createButton;

        public FormationSetupView(
            VBoxContainer leftPanel,
            VBoxContainer rightPanel,
            HBoxContainer buttonBar,
            IReadOnlyList<CharacterData> allChars,
            Func<string, Action, Button> createButton)
        {
            _leftPanel = leftPanel;
            _rightPanel = rightPanel;
            _buttonBar = buttonBar;
            _allChars = allChars;
            _createButton = createButton;
        }

        public void BuildDragUI(string teamLabel, string[] slots, int minSlots, Action<string[]> onConfirm, Action onChanged)
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
            for (int i = 0; i < 3; i++) AddSlot(frontRow, slots, i, onChanged);

            _rightPanel.AddChild(new Label { Text = "══ 後 排 ══" });
            var backRow = new HBoxContainer();
            _rightPanel.AddChild(backRow);
            for (int i = 3; i < 6; i++) AddSlot(backRow, slots, i, onChanged);

            // Highlight: confirm button — bold and prominent
            int filled = slots.Count(s => s != null);
            var confirmBtn = _createButton($"★ 确认{teamLabel}阵型 ({filled}/{minSlots}) ★", () => onConfirm(slots));
            confirmBtn.AddThemeFontSizeOverride("font_size", 20);
            if (filled < minSlots)
            {
                confirmBtn.Disabled = true;
                confirmBtn.Text = $"⚠ 还需{minSlots - filled}人 — 确认{teamLabel}阵型 ({filled}/{minSlots})";
            }
            _buttonBar.AddChild(confirmBtn);
        }

        private void AddSlot(HBoxContainer row, string[] slots, int idx, Action onChanged)
        {
            var slot = new DropSlot(slots, idx, onChanged);
            row.AddChild(slot);
            row.AddChild(_createButton("×", () => { slots[idx] = null; onChanged?.Invoke(); }));
        }
    }

    public partial class DraggableChar : Button
    {
        public string CharId;

        public DraggableChar()
        {
            MouseDefaultCursorShape = Control.CursorShape.Drag;
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            SetDragPreview(new Label { Text = Text });
            return CharId;
        }
    }

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
}
