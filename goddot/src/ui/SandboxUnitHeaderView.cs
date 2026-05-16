using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public static class SandboxUnitHeaderView
    {
        private static int BodyFontSize => Math.Max(12, TestSandboxView.CurrentBodyFontSize - 2);
        private static int TitleFontSize => TestSandboxView.CurrentBodyFontSize;

        public static Control Build(BattleUnit unit)
        {
            var root = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 70)
            };
            root.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.12f, 0.13f, 0.16f), new Color(0.28f, 0.3f, 0.36f)));

            var margin = new MarginContainer();
            AddMargins(margin, 7, 6, 7, 6);
            root.AddChild(margin);

            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 8);
            margin.AddChild(row);

            row.AddChild(BuildAvatar());
            row.AddChild(BuildMainInfo(unit));
            row.AddChild(BuildResourceInfo(unit));

            return root;
        }

        public static void Render(Container parent, BattleUnit unit)
        {
            if (parent == null)
                return;

            parent.AddChild(Build(unit));
        }

        private static Control BuildAvatar()
        {
            var frame = new PanelContainer
            {
                CustomMinimumSize = new Vector2(46, 46)
            };
            frame.AddThemeStyleboxOverride("panel", CreateStyle(new Color(0.2f, 0.23f, 0.28f), new Color(0.38f, 0.42f, 0.5f)));

            var center = new CenterContainer();
            frame.AddChild(center);

            var label = CreateLabel("头像", BodyFontSize, new Color(0.82f, 0.86f, 0.92f));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            center.AddChild(label);

            return frame;
        }

        private static Control BuildMainInfo(BattleUnit unit)
        {
            var box = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            box.AddThemeConstantOverride("separation", 3);

            box.AddChild(BuildNameRow(unit));
            box.AddChild(BuildClassRow(unit));
            box.AddChild(BuildMetaRow(unit));

            return box;
        }

        private static Control BuildNameRow(BattleUnit unit)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 5);

            var name = CreateLabel(GetUnitName(unit), TitleFontSize, new Color(0.95f, 0.95f, 0.92f));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);

            row.AddChild(BuildChip($"Lv {Math.Max(1, unit?.CurrentLevel ?? 1)}", new Color(0.22f, 0.26f, 0.33f), new Color(0.82f, 0.9f, 1f)));

            return row;
        }

        private static Control BuildClassRow(BattleUnit unit)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 5);

            var classes = GetClasses(unit);
            var classText = classes.Count == 0
                ? "兵种 未设置"
                : "兵种 " + string.Join(" / ", classes.Select(GetClassName));

            var label = CreateLabel(classText, BodyFontSize, new Color(0.84f, 0.88f, 0.92f));
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);

            var iconCount = Math.Max(1, Math.Min(3, classes.Count));
            for (var i = 0; i < iconCount; i++)
            {
                var cls = i < classes.Count ? classes[i] : (UnitClass?)null;
                row.AddChild(BuildClassIcon(cls));
            }

            return row;
        }

        private static Control BuildMetaRow(BattleUnit unit)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 4);

            row.AddChild(BuildChip(unit?.IsPlayer == true ? "我方" : "敌方", new Color(0.14f, 0.24f, 0.22f), new Color(0.78f, 0.95f, 0.88f)));
            row.AddChild(BuildChip(GetPositionText(unit), new Color(0.21f, 0.19f, 0.13f), new Color(0.95f, 0.88f, 0.68f)));
            row.AddChild(BuildChip(unit?.IsCc == true ? "CC 已转职" : "CC 未转职", new Color(0.22f, 0.2f, 0.28f), new Color(0.9f, 0.84f, 1f)));

            return row;
        }

        private static Control BuildResourceInfo(BattleUnit unit)
        {
            var box = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(116, 0)
            };
            box.AddThemeConstantOverride("separation", 2);

            var maxHp = Math.Max(1, unit?.GetCurrentStat("HP") ?? 1);
            box.AddChild(BuildResourceLine("HP", unit?.CurrentHp ?? 0, maxHp, new Color(0.78f, 0.22f, 0.22f)));
            box.AddChild(BuildResourceLine("AP", unit?.CurrentAp ?? 0, Math.Max(0, unit?.MaxAp ?? 0), new Color(0.86f, 0.58f, 0.18f)));
            box.AddChild(BuildResourceLine("PP", unit?.CurrentPp ?? 0, Math.Max(0, unit?.MaxPp ?? 0), new Color(0.28f, 0.5f, 0.9f)));

            return box;
        }

        private static Control BuildResourceLine(string labelText, int current, int max, Color color)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 4);

            var tag = new ColorRect
            {
                Color = color,
                CustomMinimumSize = new Vector2(5, 16)
            };
            row.AddChild(tag);

            var label = CreateLabel($"{labelText} {current}/{max}", BodyFontSize, new Color(0.9f, 0.92f, 0.94f));
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);

            return row;
        }

        private static Control BuildClassIcon(UnitClass? unitClass)
        {
            var icon = new PanelContainer
            {
                CustomMinimumSize = new Vector2(22, 22)
            };
            icon.AddThemeStyleboxOverride("panel", CreateStyle(GetClassColor(unitClass), new Color(0.4f, 0.43f, 0.5f)));

            var center = new CenterContainer();
            icon.AddChild(center);

            var text = unitClass.HasValue ? GetClassName(unitClass.Value).Substring(0, 1) : "?";
            var label = CreateLabel(text, Math.Max(10, BodyFontSize - 2), new Color(0.96f, 0.97f, 0.98f));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            center.AddChild(label);

            return icon;
        }

        private static Control BuildChip(string text, Color background, Color foreground)
        {
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", CreateStyle(background, new Color(background.R + 0.08f, background.G + 0.08f, background.B + 0.08f)));

            var margin = new MarginContainer();
            AddMargins(margin, 6, 2, 6, 2);
            chip.AddChild(margin);

            var label = CreateLabel(text, BodyFontSize, foreground);
            label.VerticalAlignment = VerticalAlignment.Center;
            margin.AddChild(label);

            return chip;
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

        private static string GetUnitName(BattleUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit?.Data?.Name))
                return unit.Data.Name;

            return "未命名角色";
        }

        private static string GetPositionText(BattleUnit unit)
        {
            if (unit == null || unit.Position <= 0)
                return "未站位";

            var row = unit.Position <= 3 ? "前排" : "后排";
            return $"{row} {unit.Position}";
        }

        private static IReadOnlyList<UnitClass> GetClasses(BattleUnit unit)
        {
            if (unit == null)
                return Array.Empty<UnitClass>();

            IReadOnlyList<UnitClass> classes = unit.GetEffectiveClasses();
            return classes ?? Array.Empty<UnitClass>();
        }

        private static string GetClassName(UnitClass unitClass)
        {
            return unitClass switch
            {
                UnitClass.Infantry => "步兵",
                UnitClass.Cavalry => "骑兵",
                UnitClass.Flying => "飞行",
                UnitClass.Heavy => "重装",
                UnitClass.Scout => "斥候",
                UnitClass.Archer => "弓兵",
                UnitClass.Mage => "术士",
                UnitClass.Elf => "精灵",
                UnitClass.Beastman => "兽人",
                UnitClass.Winged => "有翼",
                UnitClass.Undead => "不死",
                _ => unitClass.ToString()
            };
        }

        private static Color GetClassColor(UnitClass? unitClass)
        {
            if (!unitClass.HasValue)
                return new Color(0.28f, 0.3f, 0.34f);

            return unitClass.Value switch
            {
                UnitClass.Infantry => new Color(0.42f, 0.44f, 0.48f),
                UnitClass.Cavalry => new Color(0.18f, 0.34f, 0.62f),
                UnitClass.Flying => new Color(0.2f, 0.48f, 0.42f),
                UnitClass.Heavy => new Color(0.58f, 0.34f, 0.18f),
                UnitClass.Scout => new Color(0.5f, 0.24f, 0.52f),
                UnitClass.Archer => new Color(0.34f, 0.5f, 0.22f),
                UnitClass.Mage => new Color(0.36f, 0.28f, 0.62f),
                UnitClass.Elf => new Color(0.18f, 0.5f, 0.38f),
                UnitClass.Beastman => new Color(0.48f, 0.36f, 0.22f),
                UnitClass.Winged => new Color(0.42f, 0.46f, 0.62f),
                UnitClass.Undead => new Color(0.34f, 0.32f, 0.4f),
                _ => new Color(0.32f, 0.34f, 0.38f)
            };
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
    }
}
