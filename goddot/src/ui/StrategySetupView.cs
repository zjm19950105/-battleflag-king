using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Core;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public sealed class StrategySetupView
    {
        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly Func<string, Action, Button> _createButton;

        public StrategySetupView(
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

        public void Show(
            BattleUnit unit,
            GameDataRepository gameData,
            bool editingEnemyStrategies,
            Action onNext,
            Action onSkipAll,
            Action onBack)
        {
            var avail = unit.GetAvailableActiveSkillIds().Select(id => gameData.GetActiveSkill(id)).Where(s => s != null).ToList();

            // Left: scrollable strategy editor
            var stratScroll = new ScrollContainer();
            stratScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            var stratList = new VBoxContainer();
            stratScroll.AddChild(stratList);

            stratList.AddChild(new Label { Text = $"{unit.Data.Name} — 8条策略栏位 (AP:{unit.CurrentAp}/{unit.MaxAp})\n" });

            for (int i = 0; i < 8; i++)
            {
                int slot = i;
                var s = unit.Strategies.Count > i ? unit.Strategies[i] : null;

                // Separator
                if (i > 0) stratList.AddChild(new HSeparator());

                // Row header: slot number + skill dropdown
                var headerRow = new HBoxContainer();
                headerRow.AddChild(new Label { Text = $"[{slot + 1}]" });
                var skillOpt = new OptionButton();
                skillOpt.AddItem("(空)");
                int skillSel = 0;
                for (int j = 0; j < avail.Count; j++)
                {
                    skillOpt.AddItem($"{avail[j].Name} AP{avail[j].ApCost}");
                    if (s != null && avail[j].Id == s.SkillId) skillSel = j + 1;
                }
                skillOpt.Selected = skillSel;
                UpdateSkillOptionTooltip(skillOpt, avail, skillSel);
                int cap = slot;
                skillOpt.ItemSelected += (long idx) => {
                    int si = (int)idx;
                    while (unit.Strategies.Count <= cap) unit.Strategies.Add(new Strategy { SkillId = avail[0].Id });
                    unit.Strategies[cap].SkillId = si == 0 ? avail[0].Id : avail[si - 1].Id;
                    UpdateSkillOptionTooltip(skillOpt, avail, si);
                };
                headerRow.AddChild(skillOpt);
                stratList.AddChild(headerRow);

                // Condition 1 row
                BuildConditionRow(stratList, unit, slot, isCond1: true);

                // Condition 2 row
                BuildConditionRow(stratList, unit, slot, isCond1: false);
            }

            _leftPanel.AddChild(stratScroll);

            // Right: skill detail
            UpdateSkillDetail(unit, gameData);

            // Bottom
            string nextLabel = editingEnemyStrategies ? "→ 下一个敌方角色" : "→ 下一个我方角色";
            _buttonBar.AddChild(_createButton(nextLabel, onNext));
            string skipLabel = editingEnemyStrategies ? "→ 敌方全默认(开始战斗)" : "→ 跳过全部策略配置";
            _buttonBar.AddChild(_createButton(skipLabel, onSkipAll));
            _buttonBar.AddChild(_createButton("← 上一步", onBack));
        }

        private void BuildConditionRow(VBoxContainer parent, BattleUnit unit, int slot, bool isCond1)
        {
            var strategy = unit.Strategies.Count > slot ? unit.Strategies[slot] : null;
            var cond = isCond1 ? strategy?.Condition1 : strategy?.Condition2;
            var mode = isCond1 ? strategy?.Mode1 : strategy?.Mode2;
            bool isOnly = cond != null && mode == ConditionMode.Only;
            var selection = StrategyConditionUiMapper.FindSelection(cond);

            // Row 1: category + operator + value + mode buttons
            var row1 = new HBoxContainer();
            string label = isCond1 ? "  条件1:" : "  条件2:";
            row1.AddChild(new Label { Text = label });

            var catOpt = new OptionButton();
            catOpt.AddItem("(无)");
            for (int c = 0; c < ConditionMeta.AllCategories.Count; c++)
            {
                catOpt.AddItem(ConditionMeta.CategoryLabel(ConditionMeta.AllCategories[c]));
            }
            catOpt.Selected = selection.CategoryIndex;

            var opOpt = new OptionButton();
            var valOpt = new OptionButton();

            // Mode toggle buttons: [优先] [仅]
            var priBtn = new Button { Text = "优先", Flat = false };
            var onlyBtn = new Button { Text = "仅", Flat = false };
            priBtn.AddThemeColorOverride("font_color", isOnly ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 1.0f, 0.3f));
            onlyBtn.AddThemeColorOverride("font_color", isOnly ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.6f));
            Label previewLabel = new Label();

            // Populate operator & value
            RebuildCondDropdowns(opOpt, valOpt, selection);

            Action refreshUi = () => {
                bool nowOnly = onlyBtn.GetMeta("active", false).AsBool();
                priBtn.AddThemeColorOverride("font_color", nowOnly ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 1.0f, 0.3f));
                onlyBtn.AddThemeColorOverride("font_color", nowOnly ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.6f));
                SaveCondition(unit, slot, isCond1, catOpt, opOpt, valOpt, nowOnly);
                // Update preview
                previewLabel.Text = BuildCondPreview(catOpt, opOpt, valOpt, nowOnly);
            };

            // Use SetMeta to track only/priority state
            onlyBtn.SetMeta("active", Variant.From(isOnly));
            priBtn.SetMeta("active", Variant.From(!isOnly));

            priBtn.Pressed += () => {
                onlyBtn.SetMeta("active", Variant.From(false));
                priBtn.SetMeta("active", Variant.From(true));
                refreshUi();
            };
            onlyBtn.Pressed += () => {
                onlyBtn.SetMeta("active", Variant.From(true));
                priBtn.SetMeta("active", Variant.From(false));
                refreshUi();
            };

            // Cascade handlers
            catOpt.ItemSelected += (long idx) => {
                int ci = (int)idx;
                RebuildCondDropdowns(opOpt, valOpt, new ConditionEditorSelection(ci, 0, 0));
                refreshUi();
            };
            opOpt.ItemSelected += (long _) => {
                int ci = catOpt.Selected;
                var curCat = ci > 0 ? ConditionMeta.AllCategories[ci - 1] : (ConditionCategory?)null;
                string curOp = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : null;
                RebuildValueDropdown(valOpt, curCat, curOp, 0);
                refreshUi();
            };
            valOpt.ItemSelected += (_) => refreshUi();

            row1.AddChild(catOpt);
            row1.AddChild(opOpt);
            row1.AddChild(valOpt);
            row1.AddChild(priBtn);
            row1.AddChild(onlyBtn);
            parent.AddChild(row1);

            // Row 2: preview text
            var row2 = new HBoxContainer();
            row2.AddChild(new Label { Text = "       → " });
            previewLabel.Text = BuildCondPreview(catOpt, opOpt, valOpt, isOnly);
            previewLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 1.0f));
            row2.AddChild(previewLabel);
            parent.AddChild(row2);
        }

        private static string BuildCondPreview(OptionButton catOpt, OptionButton opOpt, OptionButton valOpt, bool isOnly)
        {
            int ci = catOpt.Selected;
            if (ci <= 0) return "(无条件)";
            string cat = catOpt.GetItemText(ci);
            string op = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : "";
            string val = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? valOpt.GetItemText(valOpt.Selected) : "";
            string modeStr = isOnly ? "仅" : "优先";
            if (string.IsNullOrEmpty(op) || op == "-") return $"[{modeStr}] {cat}";
            if (string.IsNullOrEmpty(val) || val == "-") return $"[{modeStr}] {cat} {op}";
            return $"[{modeStr}] {cat} {op}{val}";
        }

        private static void RebuildCondDropdowns(OptionButton opOpt, OptionButton valOpt, ConditionEditorSelection selection)
        {
            opOpt.Clear();
            if (selection.CategoryIndex <= 0)
            {
                opOpt.AddItem("-");
                valOpt.Clear();
                valOpt.AddItem("-");
                return;
            }

            var cat = ConditionMeta.AllCategories[selection.CategoryIndex - 1];
            var ops = ConditionMeta.GetOperators(cat);
            int opSel = Math.Clamp(selection.OperatorIndex, 0, ops.Count - 1);
            for (int o = 0; o < ops.Count; o++)
                opOpt.AddItem(ops[o]);

            opOpt.Selected = opSel;
            string selOp = ops[opSel];
            RebuildValueDropdown(valOpt, cat, selOp, selection.ValueIndex);
        }

        private static void RebuildValueDropdown(OptionButton valOpt, ConditionCategory? cat, string op, int selectedValueIndex)
        {
            valOpt.Clear();
            if (cat == null || string.IsNullOrEmpty(op)) { valOpt.AddItem("-"); return; }

            var vals = ConditionMeta.GetValues(cat.Value, op);
            foreach (var v in vals) valOpt.AddItem(v);
            valOpt.Selected = Math.Clamp(selectedValueIndex, 0, vals.Count - 1);
        }

        private static void SaveCondition(BattleUnit unit, int slot, bool isCond1, OptionButton catOpt, OptionButton opOpt, OptionButton valOpt, bool isOnly)
        {
            while (unit.Strategies.Count <= slot)
                unit.Strategies.Add(new Strategy { SkillId = unit.GetAvailableActiveSkillIds().FirstOrDefault() ?? "" });

            int ci = catOpt.Selected;
            var mode = isOnly ? ConditionMode.Only : ConditionMode.Priority;
            if (ci <= 0)
            {
                StrategyConditionUiMapper.SaveSelection(unit.Strategies[slot], isCond1, ci, "", "", mode);
                return;
            }

            string op = opOpt.Selected >= 0 && opOpt.ItemCount > 0 ? opOpt.GetItemText(opOpt.Selected) : "";
            string val = valOpt.Selected >= 0 && valOpt.ItemCount > 0 ? valOpt.GetItemText(valOpt.Selected) : "";
            StrategyConditionUiMapper.SaveSelection(
                unit.Strategies[slot],
                isCond1,
                ci,
                op,
                val,
                mode);
        }

        private void UpdateSkillDetail(BattleUnit unit, GameDataRepository gameData)
        {
            ClearPanel(_rightPanel);
            var detailLabel = new RichTextLabel { BbcodeEnabled = true };
            detailLabel.AddThemeFontSizeOverride("normal_font_size", 15);
            detailLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _rightPanel.AddChild(detailLabel);

            detailLabel.AppendText($"[color=yellow]══ 策略编程说明 ══[/color]\n\n");
            detailLabel.AppendText("每条策略 = 技能 + 条件1 + 条件2\n\n");
            detailLabel.AppendText("[color=cyan]条件组合逻辑:[/color]\n");
            detailLabel.AppendText("• 仅+仅 = AND (两个条件都满足才发动)\n");
            detailLabel.AppendText("• 仅+优先 = 先过滤, 再排序\n");
            detailLabel.AppendText("• 优先+优先 = 条件2优先于条件1!\n");
            detailLabel.AppendText("  顺序: 1+2 > 2 > 1\n\n");
            detailLabel.AppendText("[color=cyan]模式:[/color]\n");
            detailLabel.AppendText("• 仅 = 条件不满足则[color=red]跳过技能[/color]\n");
            detailLabel.AppendText("• 优先(不勾选仅) = 条件不满足[color=green]仍会发动[/color]\n\n");
            detailLabel.AppendText($"[color=orange]{unit.Data.Name} 可用技能:[/color]\n");
            foreach (var sid in unit.GetAvailableActiveSkillIds())
            {
                var sk = gameData.GetActiveSkill(sid);
                if (sk == null) continue;
                detailLabel.AppendText($"  AP{sk.ApCost} {sk.Name} — 威力{FormatPowerSummary(sk)} / 命中{FormatHitSummary(sk)} — {sk.EffectDescription}\n");
            }
        }

        private static void UpdateSkillOptionTooltip(OptionButton skillOpt, List<ActiveSkillData> skills, int selectedIndex)
        {
            if (selectedIndex <= 0 || selectedIndex > skills.Count)
            {
                skillOpt.TooltipText = "";
                return;
            }

            skillOpt.TooltipText = SandboxTooltipHelper.BuildActiveSkillDetail(skills[selectedIndex - 1]);
        }

        private static string FormatPowerSummary(ActiveSkillData skill)
        {
            if (skill.PhysicalPower.HasValue || skill.MagicalPower.HasValue)
            {
                var parts = new List<string>();
                if (skill.PhysicalPower.HasValue)
                    parts.Add("物理" + skill.PhysicalPower.Value);
                if (skill.MagicalPower.HasValue)
                    parts.Add("魔法" + skill.MagicalPower.Value);
                return string.Join("/", parts);
            }

            return skill.Power > 0 ? skill.Power.ToString() : "无";
        }

        private static string FormatHitSummary(ActiveSkillData skill)
        {
            bool sureHit = skill.Tags?.Any(tag => string.Equals(tag, "SureHit", StringComparison.OrdinalIgnoreCase)) == true;
            return sureHit ? "必中" : (skill.HitRate.HasValue ? skill.HitRate.Value + "%" : "100%");
        }

        private static void ClearPanel(Control panel)
        {
            foreach (var child in panel.GetChildren())
                child.QueueFree();
        }
    }
}
