using LiveSplit.Model;
using LiveSplit.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace Voxif.AutoSplitter {
    public class ManualDoubleTimer : LiveSplit.UI.Components.IComponent {
        public ManualTimer FirstTimer { get; set; }
        public ManualTimer SecondTimer { get; set; }
        public ManualDoubleTimerSettings Settings { get; set; }
        public GraphicsCache Cache { get; set; }

        public float PaddingTop => 0f;
        public float PaddingLeft => 7f;
        public float PaddingBottom => 0f;
        public float PaddingRight => 7f;

        public float VerticalHeight => Settings.Height;
        public float HorizontalWidth => Settings.Width;

        public float MinimumWidth => 20;
        public float MinimumHeight => 20;

        public IDictionary<string, Action> ContextMenuControls => null;

        public string ComponentName { get; set; }

        public ManualDoubleTimer(LiveSplitState state, string name) {
            ComponentName = name;
            Settings = new ManualDoubleTimerSettings() { CurrentState = state };
            FirstTimer = new ManualTimer();
            SecondTimer = new ManualTimer();
            Cache = new GraphicsCache();
        }

        public void DrawGeneral(Graphics g, float width, float height) {
            ManualTimer.DrawBackground(g, Settings.BackgroundColor, Settings.BackgroundColor2, width, height, Settings.BackgroundGradient);
            
            FirstTimer.Settings.OverrideSplitColors = Settings.OverrideColorFirstTimer;
            FirstTimer.Settings.TimerColor = Settings.ColorFirstTimer;
            FirstTimer.Settings.ShowGradient = Settings.ShowGradientFirstTimer;
            FirstTimer.Settings.DigitsFormat = Settings.DigitsFormatFirstTimer;
            FirstTimer.Settings.Accuracy = Settings.AccuracyFirstTimer;
            FirstTimer.Settings.DecimalsSize = Settings.DecimalsSizeFirstTimer;

            SecondTimer.Settings.OverrideSplitColors = Settings.OverrideColorSecondTimer;
            SecondTimer.Settings.TimerColor = Settings.ColorSecondTimer;
            SecondTimer.Settings.ShowGradient = Settings.ShowGradientSecondTimer;
            SecondTimer.Settings.DigitsFormat = Settings.DigitsFormatSecondTimer;
            SecondTimer.Settings.Accuracy = Settings.AccuracySecondTimer;
            SecondTimer.Settings.DecimalsSize = Settings.DecimalsSizeSecondTimer;
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) {
            DrawGeneral(g, width, VerticalHeight);
            Matrix oldMatrix = g.Transform;
            FirstTimer.Settings.TimerHeight = VerticalHeight * ((100f - Settings.TimerSizeRatio) / 100f);
            FirstTimer.DrawVertical(g, state, width, clipRegion);
            g.Transform = oldMatrix;
            g.TranslateTransform(0, VerticalHeight * ((100f - Settings.TimerSizeRatio) / 100f));
            SecondTimer.Settings.TimerHeight = VerticalHeight * (Settings.TimerSizeRatio / 100f);
            SecondTimer.DrawVertical(g, state, width, clipRegion);
            g.Transform = oldMatrix;
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) {
            DrawGeneral(g, HorizontalWidth, height);
            Matrix oldMatrix = g.Transform;
            FirstTimer.Settings.TimerWidth = HorizontalWidth;
            FirstTimer.DrawHorizontal(g, state, height * ((100f - Settings.TimerSizeRatio) / 100f), clipRegion);
            g.Transform = oldMatrix;
            g.TranslateTransform(0, height * ((100f - Settings.TimerSizeRatio) / 100f));
            SecondTimer.DrawHorizontal(g, state, height * (Settings.TimerSizeRatio / 100f), clipRegion);
            SecondTimer.Settings.TimerWidth = HorizontalWidth;
            g.Transform = oldMatrix;
        }

        public Control GetSettingsControl(LayoutMode mode) {
            Settings.Mode = mode;
            return Settings;
        }

        public void SetSettings(XmlNode settings) => Settings.SetSettings(settings);

        public XmlNode GetSettings(XmlDocument document) => Settings.GetSettings(document);

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) {
            SecondTimer.Update(null, state, width, height, mode);
            FirstTimer.Update(null, state, width, height, mode);

            Cache.Restart();
            Cache["FirstTimerText"] = FirstTimer.BigTextLabel.Text + FirstTimer.SmallTextLabel.Text;
            Cache["SecondTimerText"] = SecondTimer.BigTextLabel.Text + SecondTimer.SmallTextLabel.Text;
            if(FirstTimer.BigTextLabel.Brush != null && invalidator != null) {
                if(FirstTimer.BigTextLabel.Brush is LinearGradientBrush brush) {
                    Cache["TimerColor"] = brush.LinearColors.First().ToArgb();
                } else {
                    Cache["TimerColor"] = FirstTimer.BigTextLabel.ForeColor.ToArgb();
                }
            }

            if(invalidator != null && Cache.HasChanged) {
                invalidator.Invalidate(0, 0, width, height);
            }
        }

        public void Dispose() { }

        public int GetSettingsHashCode() => Settings.GetSettingsHashCode();
    }
}