using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mapper.Gui
{
    public class SeparatorHook
    {
        public Panel SeparatorPanel { get; }
        public Border Separator { get; }

        public ToolButtonHook LeftHook { get; }
        public ToolButtonHook RightHook { get; }

        public IBrush TurnedOffColor { get; } = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        public IBrush TurnedOnColor { get; } = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        public IBrush DisabledTurnedOnColor { get; } = new SolidColorBrush(Color.FromRgb(115, 115, 115));

        public SeparatorHook(Panel separatorPanel, Border separator, ToolButtonHook leftHook, ToolButtonHook rightHook)
        {
            SeparatorPanel = separatorPanel;
            Separator = separator;

            LeftHook = leftHook;
            RightHook = rightHook;


            StartHook();
            Tool_StateChanged(false);
        }

        private void StartHook()
        {
            LeftHook.ToolButton.Tool.OnTurnedOn += Tool_StateChanged;
            RightHook.ToolButton.Tool.OnTurnedOn += Tool_StateChanged;
            LeftHook.ToolButton.Tool.OnEnabled += Tool_StateChanged;
            RightHook.ToolButton.Tool.OnEnabled += Tool_StateChanged;
        }

        private void Tool_StateChanged(bool isOn)
        {
            GetColor(out IBrush panelBackground, out IBrush separatorBrush);
            SetBackground(panelBackground, separatorBrush);
        }

        private void GetColor(out IBrush panelBackground, out IBrush separatorBrush)
        {
            if (LeftHook.ToolButton.Button is null || RightHook.ToolButton.Button is null)
            {
                panelBackground = Brushes.Transparent;
                separatorBrush = Brushes.Transparent;
                return;
            }

            int levelLeft = GetColors(LeftHook, out panelBackground, out separatorBrush);
            int levelRight = GetColors(RightHook, out _, out _);
            if (levelLeft == levelRight) return;

            if (levelLeft == 2 || levelRight == 2) 
            {
                panelBackground = LeftHook.TurnedOn.Default;
                separatorBrush = panelBackground;
                return;
            }

            if (levelLeft == 1 || levelRight == 1) 
            {
                panelBackground = LeftHook.DisabledTurnedOn.Default;
                separatorBrush = panelBackground;
            }
        }
        private int GetColors(ToolButtonHook hook, out IBrush panelBackground, out IBrush separatorBrush)
        {
            if (hook.ToolButton.Tool.IsTurnedOn && hook.ToolButton.Tool.Enabled) 
            {
                panelBackground = hook.TurnedOn.Default;
                separatorBrush = TurnedOnColor;
                return 2;
            }

            if (hook.ToolButton.Tool.IsTurnedOn && !hook.ToolButton.Tool.Enabled) 
            {
                panelBackground = hook.DisabledTurnedOn.Default;
                separatorBrush = DisabledTurnedOnColor;
                return 1;
            }

            panelBackground = hook.TurnedOff.Default;
            separatorBrush = TurnedOffColor;
            return 0;
        }

        private void SetBackground(IBrush panelBackground, IBrush separatorBrush)
        {
            SeparatorPanel.Background = panelBackground;
            Separator.BorderBrush = separatorBrush;
        }
    }
}
