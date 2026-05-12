using System;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public sealed class PassiveSetupView
    {
        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly Func<string, Action, Button> _createButton;

        public PassiveSetupView(
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

        public string BuildStatusText(BattleUnit unit)
        {
            return $"▶  被动技能 [{unit.Data.Name}] — [color=blue]PP:{BattleStatusHelper.StarStr(unit.GetUsedPp(), unit.MaxPp)}[/color]";
        }

        public void Show(
            BattleUnit unit,
            GameDataRepository gameData,
            Action<string> log,
            Action refresh,
            Action onNext,
            Action onSkipAll,
            Action onBack)
        {
            _leftPanel.AddChild(new Label { Text = $"{unit.Data.Name} 可用被动 (可设置发动条件):\n" });

            foreach (var s in unit.GetAvailablePassiveSkillIds().Select(id => gameData.GetPassiveSkill(id)).Where(s => s != null))
            {
                bool on = unit.EquippedPassiveSkillIds.Contains(s.Id);

                // Toggle button
                var toggleRow = new HBoxContainer();
                toggleRow.AddChild(_createButton($"{(on ? "[✓]" : "[  ]")} {s.Name} PP{s.PpCost} [{s.TriggerTiming}]", () => {
                    if (on) { unit.EquippedPassiveSkillIds.Remove(s.Id); unit.PassiveConditions.Remove(s.Id); log($"  卸下: {s.Name}"); }
                    else if (!unit.CanEquipPassive(s.Id)) { log("  PP不足!"); return; }
                    else { unit.EquippedPassiveSkillIds.Add(s.Id); log($"  装备: {s.Name}"); }
                    refresh();
                }));
                _leftPanel.AddChild(toggleRow);

                // If equipped, show condition selector
                if (on)
                {
                    var condRow = new HBoxContainer();
                    condRow.AddChild(new Label { Text = "    发动条件:" });

                    var cond = unit.PassiveConditions.TryGetValue(s.Id, out var c) ? c : null;
                    var catOpt = new OptionButton();
                    catOpt.AddItem("(无条件)");
                    int catSel = 0;
                    var cats = ConditionMeta.AllCategories;
                    for (int ci = 0; ci < cats.Count; ci++)
                    {
                        catOpt.AddItem(ConditionMeta.CategoryLabel(cats[ci]));
                        if (cond != null && cats[ci] == cond.Category) catSel = ci + 1;
                    }
                    catOpt.Selected = catSel;
                    catOpt.ItemSelected += (long idx) => {
                        int i = (int)idx;
                        if (i <= 0) { unit.PassiveConditions.Remove(s.Id); }
                        else {
                            var cat = cats[i - 1];
                            var ops = ConditionMeta.GetOperators(cat);
                            var vals = ConditionMeta.GetValues(cat, ops[0]);
                            unit.PassiveConditions[s.Id] = ConditionMeta.BuildCondition(cat, ops[0], vals[0], true);
                        }
                        refresh();
                    };
                    condRow.AddChild(catOpt);

                    // If category selected, show operator + value
                    if (catSel > 0)
                    {
                        var curCat = cats[catSel - 1];
                        var ops = ConditionMeta.GetOperators(curCat);
                        var opOpt = new OptionButton();
                        int opSel = 0;
                        for (int oi = 0; oi < ops.Count; oi++)
                        {
                            opOpt.AddItem(ops[oi]);
                            if (cond != null && ops[oi] == (cond.Operator switch { "less_than" => "低于", "greater_than" => "高于", "equals" => "等于", "lowest" => "最低", "highest" => "最高", _ => ops[oi] }))
                                opSel = oi;
                        }
                        opOpt.Selected = opSel;
                        opOpt.ItemSelected += (long _) => refresh();
                        condRow.AddChild(opOpt);

                        var vals = ConditionMeta.GetValues(curCat, ops[opSel]);
                        var valOpt = new OptionButton();
                        int valSel = 0;
                        for (int vi = 0; vi < vals.Count; vi++)
                        {
                            valOpt.AddItem(vals[vi]);
                            if (cond != null && vals[vi] == cond.Value?.ToString()) valSel = vi;
                        }
                        valOpt.Selected = valSel;
                        valOpt.ItemSelected += (long _) => refresh();
                        condRow.AddChild(valOpt);

                        // Save on each selection
                        catOpt.ItemSelected += (long idx) => {
                            int ii = (int)idx;
                            if (ii <= 0) { unit.PassiveConditions.Remove(s.Id); }
                            else {
                                var cc = cats[ii - 1];
                                var oo = ConditionMeta.GetOperators(cc);
                                var vv = ConditionMeta.GetValues(cc, oo[opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? Math.Min(opOpt.Selected, oo.Count - 1) : 0]);
                                var vo = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? Math.Min(valOpt.Selected, vv.Count - 1) : 0;
                                unit.PassiveConditions[s.Id] = ConditionMeta.BuildCondition(cc, oo[0], vv[vo], true);
                            }
                        };
                    }

                    _leftPanel.AddChild(condRow);
                }
            }

            // Show first equipped passive detail in right panel
            var firstEquippedId = unit.EquippedPassiveSkillIds.FirstOrDefault();
            if (firstEquippedId != null) {
                var ps = gameData.GetPassiveSkill(firstEquippedId);
                if (ps != null) PassiveDetailHelper.Show(_rightPanel, ps, true);
            }

            _buttonBar.AddChild(_createButton("→ 下一个", onNext));
            _buttonBar.AddChild(_createButton("→ 全部跳过", onSkipAll));
            _buttonBar.AddChild(_createButton("← 上一步", onBack));
        }
    }
}
