using System;
using System.Collections.Generic;
using Godot;

namespace BattleKing.Ui
{
    public enum GamePhase
    {
        MainMenu,
        DisplaySettings,
        ModeSelect,
        PlayerFormation,
        EnemyChoice,
        EnemyDragFormation,
        EquipmentSetup,
        PassiveSetup,
        StrategySetup,
        TestSandbox,
        Battle,
        Result
    }

    public sealed class BattleUiFlowController
    {
        private readonly VBoxContainer _leftPanel;
        private readonly VBoxContainer _rightPanel;
        private readonly HBoxContainer _buttonBar;
        private readonly RichTextLabel _logLabel;
        private readonly Func<string, Action, Button> _createButton;
        private readonly Action<GamePhase> _renderPhase;
        private readonly Stack<GamePhase> _phaseHistory = new();

        private bool _suppressHistory;
        private Node _logOriginalParent;

        public GamePhase CurrentPhase { get; private set; }

        public BattleUiFlowController(
            VBoxContainer leftPanel,
            VBoxContainer rightPanel,
            HBoxContainer buttonBar,
            RichTextLabel logLabel,
            Func<string, Action, Button> createButton,
            Action<GamePhase> renderPhase)
        {
            _leftPanel = leftPanel;
            _rightPanel = rightPanel;
            _buttonBar = buttonBar;
            _logLabel = logLabel;
            _createButton = createButton;
            _renderPhase = renderPhase;
        }

        public void Go(GamePhase phase)
        {
            if (!_suppressHistory && CurrentPhase != phase)
                _phaseHistory.Push(CurrentPhase);
            _suppressHistory = false;
            CurrentPhase = phase;

            RestoreLogParent();
            ClearPanelsAndButtons();
            _renderPhase(phase);

        }

        public void GoBack()
        {
            if (_phaseHistory.Count > 0)
            {
                _suppressHistory = true;
                Go(_phaseHistory.Pop());
            }
            else
            {
                Go(GamePhase.MainMenu);
            }
        }

        public void ClearPanel(Control panel)
        {
            foreach (var child in panel.GetChildren())
                child.QueueFree();
        }

        public void ClearPanelsAndButtons()
        {
            ClearPanel(_leftPanel);
            ClearPanel(_rightPanel);
            ClearButtons();
        }

        public void ClearButtons()
        {
            foreach (var child in _buttonBar.GetChildren())
                child.QueueFree();
        }

        public void AddButton(string text, Action onClick)
        {
            _buttonBar.AddChild(_createButton(text, onClick));
        }

        public void AddBackButton()
        {
            _buttonBar.AddChild(_createButton("← 上一步", GoBack));
        }

        public void MoveLogToRightPanel()
        {
            _logOriginalParent = _logLabel.GetParent();
            if (_logOriginalParent != null)
                _logOriginalParent.RemoveChild(_logLabel);

            _logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _rightPanel.AddChild(_logLabel);
        }

        private void RestoreLogParent()
        {
            if (_logOriginalParent == null)
                return;

            if (_rightPanel != null && _logLabel.GetParent() == _rightPanel)
                _rightPanel.RemoveChild(_logLabel);

            _logOriginalParent.AddChild(_logLabel);
            _logOriginalParent = null;
        }
    }
}
