using Godot;
using BattleKing.Data;

namespace BattleKing.Ui
{
    public static class PassiveDetailHelper
    {
        public static void Show(Control panel, PassiveSkillData s, bool equipped)
        {
            var pd = new RichTextLabel { BbcodeEnabled = true };
            pd.AddThemeFontSizeOverride("normal_font_size", 15);
            pd.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

            string st = equipped ? "[color=green][已装备][/color]" : "[color=gray][未装备][/color]";
            pd.AppendText("[color=yellow]== " + s.Name + " ==[/color] " + st + "\n\n");
            pd.AppendText("[color=cyan]PP消耗:[/color] " + s.PpCost + "\n");
            pd.AppendText("[color=cyan]触发时机:[/color] " + s.TriggerTiming + "\n");
            pd.AppendText("[color=cyan]类型:[/color] " + s.Type + "\n");
            if (s.Power != null) pd.AppendText("[color=cyan]威力:[/color] " + s.Power.Value + "\n");
            if (s.HitRate != null) pd.AppendText("[color=cyan]命中:[/color] " + s.HitRate.Value + "%\n");
            pd.AppendText("\n[color=#88aaff]" + s.EffectDescription + "[/color]\n");
            if (s.Tags.Count > 0)
                pd.AppendText("\n标签: " + string.Join(", ", s.Tags) + "\n");
            if (s.HasSimultaneousLimit)
                pd.AppendText("[color=#ffaa44]同时发动限制[/color]\n");
            if (s.LearnCondition != null)
                pd.AppendText("\n习得: " + s.LearnCondition + "\n");
            panel.AddChild(pd);
        }
    }
}
