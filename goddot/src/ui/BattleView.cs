using System;
using BattleKing.Core;
using BattleKing.Data;
using Godot;

namespace BattleKing.Ui
{
    public sealed class BattleView
    {
        private readonly VBoxContainer _leftPanel;
        private readonly RichTextLabel _logLabel;
        private readonly HBoxContainer _buttonBar;
        private readonly Func<string, Action, Button> _createButton;
        private readonly Action _moveLogToRightPanel;
        private BattleEngine _engine;
        private BattleContext _context;
        private RichTextLabel _unitLabel;

        public BattleView(
            VBoxContainer leftPanel,
            RichTextLabel logLabel,
            HBoxContainer buttonBar,
            Func<string, Action, Button> createButton,
            Action moveLogToRightPanel)
        {
            _leftPanel = leftPanel;
            _logLabel = logLabel;
            _buttonBar = buttonBar;
            _createButton = createButton;
            _moveLogToRightPanel = moveLogToRightPanel;
        }

        public void Show(
            BattleEngine engine,
            BattleContext context,
            GameDataRepository gameData,
            Action<BattleResult> onBattleEnd)
        {
            _engine = engine;
            _context = context;

            var unitScroll = new ScrollContainer();
            unitScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            unitScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _leftPanel.AddChild(unitScroll);

            _unitLabel = new RichTextLabel { BbcodeEnabled = true };
            _unitLabel.AddThemeFontSizeOverride("normal_font_size", 16);
            _unitLabel.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
            _unitLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _unitLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _unitLabel.ScrollFollowing = true;
            unitScroll.AddChild(_unitLabel);

            _moveLogToRightPanel();

            ClearLog();
            _engine.OnLog = AppendLog;
            var passiveProc = new BattleKing.Skills.PassiveSkillProcessor(_engine.EventBus, gameData, AppendLog, _engine.EnqueueAction);
            passiveProc.SubscribeAll();
            _engine.InitBattle();

            RefreshBattleStatus();

            _buttonBar.AddChild(_createButton("下一名角色行动", () => StepOneAction(onBattleEnd)));
            _buttonBar.AddChild(_createButton("自动战斗", () => RunAutoBattle(onBattleEnd)));
            _buttonBar.AddChild(_createButton("复制战斗日志", CopyBattleLog));
        }

        private void StepOneAction(Action<BattleResult> onBattleEnd)
        {
            var result = _engine.StepOneAction();
            RefreshBattleStatus();
            if (result == SingleActionResult.PlayerWin || result == SingleActionResult.EnemyWin || result == SingleActionResult.Draw)
                FinishBattle(ToBattleResult(result), onBattleEnd);
        }

        private void RunAutoBattle(Action<BattleResult> onBattleEnd)
        {
            while (true)
            {
                var result = _engine.StepOneAction();
                RefreshBattleStatus();
                if (result == SingleActionResult.PlayerWin || result == SingleActionResult.EnemyWin || result == SingleActionResult.Draw)
                {
                    FinishBattle(ToBattleResult(result), onBattleEnd);
                    return;
                }
            }
        }

        private void FinishBattle(BattleResult result, Action<BattleResult> onBattleEnd)
        {
            ClearButtons();
            AppendLog("\n=== " + result + " ===");
            _buttonBar.AddChild(_createButton("结果", () => onBattleEnd(result)));
            _buttonBar.AddChild(_createButton("复制战斗日志", CopyBattleLog));
        }

        private void RefreshBattleStatus()
        {
            _unitLabel.Clear();
            _unitLabel.AppendText("[color=yellow]=== 战场 ===[/color]\n\n");
            _unitLabel.AppendText("[color=cyan]我方[/color]\n");
            foreach (var u in _context.PlayerUnits) BattleStatusHelper.AppendUnit(_unitLabel, u);
            _unitLabel.AppendText("\n[color=orange]敌方[/color]\n");
            foreach (var u in _context.EnemyUnits) BattleStatusHelper.AppendUnit(_unitLabel, u);
        }

        private void AppendLog(string msg)
        {
            BattleLogTextRenderer.Append(_logLabel, msg);
        }

        private void ClearLog()
        {
            _logLabel.Clear();
        }

        private void CopyBattleLog()
        {
            DisplayServer.ClipboardSet(_logLabel.GetParsedText());
        }

        private void ClearButtons()
        {
            foreach (var child in _buttonBar.GetChildren())
                child.QueueFree();
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
    }
}
