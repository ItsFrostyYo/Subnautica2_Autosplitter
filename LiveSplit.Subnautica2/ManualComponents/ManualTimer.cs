using LiveSplit.Model;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Voxif.AutoSplitter {
    public class ManualTimer : LiveSplit.UI.Components.IComponent {
        public SimpleLabel BigTextLabel { get; set; }
        public SimpleLabel SmallTextLabel { get; set; }
        protected SimpleLabel BigMeasureLabel { get; set; }
        protected RegularTimeFormatter Formatter { get; set; }
        public TimeSpan? Value { get; set; }

        protected Font TimerDecimalPlacesFont { get; set; }
        protected Font TimerFont { get; set; }
        protected float PreviousDecimalsSize { get; set; }

        public GraphicsCache Cache { get; set; }

        public ManualTimerSettings Settings { get; set; }
        public float ActualWidth { get; set; }
        public string ComponentName { get; }

        public float VerticalHeight => Settings.TimerHeight;

        public float MinimumWidth => 20;

        public float HorizontalWidth => Settings.TimerWidth;

        public float MinimumHeight => 20;

        public float PaddingTop => 0f;
        public float PaddingLeft => 7f;
        public float PaddingBottom => 0f;
        public float PaddingRight => 7f;

        public IDictionary<string, Action> ContextMenuControls => null;

        public ManualTimer(string name = "") {
            ComponentName = name;

            BigTextLabel = new SimpleLabel() {
                HorizontalAlignment = StringAlignment.Far,
                VerticalAlignment = StringAlignment.Near,
                Width = 493,
                Text = "0",
            };

            SmallTextLabel = new SimpleLabel() {
                HorizontalAlignment = StringAlignment.Near,
                VerticalAlignment = StringAlignment.Near,
                Width = 257,
                Text = "0",
            };

            BigMeasureLabel = new SimpleLabel() {
                Text = "88:88:88",
                IsMonospaced = true
            };

            Formatter = new RegularTimeFormatter(TimeAccuracy.Milliseconds);
            Settings = new ManualTimerSettings();
            UpdateTimeFormat();
            Cache = new GraphicsCache();
        }

        public static void DrawBackground(Graphics g, Color settingsColor1, Color settingsColor2,
            float width, float height, GradientType gradientType) {
            var background1 = settingsColor1;
            var background2 = settingsColor2;
            if(background1.A > 0 || gradientType != GradientType.Plain && background2.A > 0) {
                LinearGradientBrush gradientBrush = new LinearGradientBrush(
                    new PointF(0, 0),
                    gradientType == GradientType.Horizontal ? new PointF(width, 0) : new PointF(0, height),
                    background1,
                    gradientType == GradientType.Plain ? background1 : background2);
                g.FillRectangle(gradientBrush, 0, 0, width, height);
            }
        }

        private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height) {
            DrawBackground(g, Settings.BackgroundColor, Settings.BackgroundColor2, width, height, Settings.BackgroundGradient);

            if(state.LayoutSettings.TimerFont != TimerFont || Settings.DecimalsSize != PreviousDecimalsSize) {
                TimerFont = state.LayoutSettings.TimerFont;
                TimerDecimalPlacesFont = new Font(TimerFont.FontFamily.Name, (TimerFont.Size / 50f) * (Settings.DecimalsSize), TimerFont.Style, GraphicsUnit.Pixel);
                PreviousDecimalsSize = Settings.DecimalsSize;
            }

            BigTextLabel.Font = BigMeasureLabel.Font = TimerFont;
            SmallTextLabel.Font = TimerDecimalPlacesFont;

            BigMeasureLabel.SetActualWidth(g);
            SmallTextLabel.SetActualWidth(g);

            var oldMatrix = g.Transform;
            var unscaledWidth = Math.Max(10, BigMeasureLabel.ActualWidth + SmallTextLabel.ActualWidth + 11);
            var unscaledHeight = 45f;
            var widthFactor = (width - 14) / (unscaledWidth - 14);
            var heightFactor = height / unscaledHeight;
            var adjustValue = !Settings.CenterTimer ? 7f : 0f;
            var scale = Math.Min(widthFactor, heightFactor);
            g.TranslateTransform(width - adjustValue, height / 2);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-unscaledWidth + adjustValue, -0.5f * unscaledHeight);
            if(Settings.CenterTimer) {
                g.TranslateTransform((-(width - unscaledWidth * scale) / 2f) / scale, 0);
            }

            DrawUnscaled(g, state, unscaledWidth, unscaledHeight);
            ActualWidth = scale * (SmallTextLabel.ActualWidth + BigTextLabel.ActualWidth);
            g.Transform = oldMatrix;
        }

        public void DrawUnscaled(Graphics g, LiveSplitState state, float width, float height) {
            BigTextLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
            BigTextLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
            BigTextLabel.HasShadow = state.LayoutSettings.DropShadows;
            SmallTextLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
            SmallTextLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
            SmallTextLabel.HasShadow = state.LayoutSettings.DropShadows;

            UpdateTimeFormat();

            var smallFont = TimerDecimalPlacesFont;
            var bigFont = TimerFont;
            var sizeMultiplier = bigFont.Size / bigFont.FontFamily.GetEmHeight(bigFont.Style);
            var smallSizeMultiplier = smallFont.Size / bigFont.FontFamily.GetEmHeight(bigFont.Style);
            var ascent = sizeMultiplier * bigFont.FontFamily.GetCellAscent(bigFont.Style);
            var descent = sizeMultiplier * bigFont.FontFamily.GetCellDescent(bigFont.Style);
            var smallAscent = smallSizeMultiplier * smallFont.FontFamily.GetCellAscent(smallFont.Style);
            var shift = (height - ascent - descent) / 2f;

            BigTextLabel.X = width - 499 - SmallTextLabel.ActualWidth;
            SmallTextLabel.X = width - SmallTextLabel.ActualWidth - 6;
            BigTextLabel.Y = shift;
            SmallTextLabel.Y = shift + ascent - smallAscent;
            BigTextLabel.Height = 150f;
            SmallTextLabel.Height = 150f;

            BigTextLabel.IsMonospaced = true;
            SmallTextLabel.IsMonospaced = true;

            if(Settings.ShowGradient && BigTextLabel.Brush is SolidBrush) {
                var originalColor = (BigTextLabel.Brush as SolidBrush).Color;
                originalColor.ToHSV(out double h, out double s, out double v);

                var bottomColor = ColorExtensions.FromHSV(h, s, 0.8 * v);
                var topColor = ColorExtensions.FromHSV(h, 0.5 * s, Math.Min(1, 1.5 * v + 0.1));

                var bigTimerGradiantBrush = new LinearGradientBrush(
                    new PointF(BigTextLabel.X, BigTextLabel.Y),
                    new PointF(BigTextLabel.X, BigTextLabel.Y + ascent + descent),
                    topColor,
                    bottomColor);
                var smallTimerGradiantBrush = new LinearGradientBrush(
                    new PointF(SmallTextLabel.X, SmallTextLabel.Y),
                    new PointF(SmallTextLabel.X, SmallTextLabel.Y + ascent + descent + smallFont.Size - bigFont.Size),
                    topColor,
                    bottomColor);

                BigTextLabel.Brush = bigTimerGradiantBrush;
                SmallTextLabel.Brush = smallTimerGradiantBrush;
            }

            BigTextLabel.Draw(g);
            SmallTextLabel.Draw(g);
        }

        protected void UpdateTimeFormat() {
            switch(Settings.DigitsFormat) {
                case "1": Formatter.DigitsFormat = DigitsFormat.SingleDigitSeconds; break;
                case "01": Formatter.DigitsFormat = DigitsFormat.DoubleDigitSeconds; break;
                case "0:01": Formatter.DigitsFormat = DigitsFormat.SingleDigitMinutes; break;
                case "00:01": Formatter.DigitsFormat = DigitsFormat.DoubleDigitMinutes; break;
                case "0:00:01": Formatter.DigitsFormat = DigitsFormat.SingleDigitHours; break;
                default: Formatter.DigitsFormat = DigitsFormat.DoubleDigitHours; break;
            }

            switch(Settings.Accuracy) {
                case ".234": Formatter.Accuracy = TimeAccuracy.Milliseconds; break;
                case ".23": Formatter.Accuracy = TimeAccuracy.Hundredths; break;
                case ".2": Formatter.Accuracy = TimeAccuracy.Tenths; break;
                default: Formatter.Accuracy = TimeAccuracy.Seconds; break;
            }
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) {
            DrawGeneral(g, state, width, VerticalHeight);
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) {
            DrawGeneral(g, state, HorizontalWidth, height);
        }

        public Control GetSettingsControl(LayoutMode mode) {
            Settings.Mode = mode;
            return Settings;
        }

        public void SetSettings(System.Xml.XmlNode settings) {
            Settings.SetSettings(settings);
        }

        public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document) {
            return Settings.GetSettings(document);
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) {
            Cache.Restart();

            if(Value != null) {
                var timeString = Formatter.Format(Value);
                int dotIndex = timeString.IndexOf(".");
                if(dotIndex == -1) {
                    SmallTextLabel.Text = "";
                    BigTextLabel.Text = timeString.Substring(0);
                } else {
                    SmallTextLabel.Text = timeString.Substring(dotIndex);
                    BigTextLabel.Text = timeString.Substring(0, dotIndex);
                }
            } else {
                SmallTextLabel.Text = TimeFormatConstants.DASH;
                BigTextLabel.Text = "";
            }

            BigTextLabel.ForeColor = Settings.OverrideSplitColors ? Settings.TimerColor : state.LayoutSettings.TextColor;
            SmallTextLabel.ForeColor = Settings.OverrideSplitColors ? Settings.TimerColor : state.LayoutSettings.TextColor;

            Cache["TimerText"] = BigTextLabel.Text + SmallTextLabel.Text;
            if(BigTextLabel.Brush != null && invalidator != null) {
                Cache["TimerColor"] = BigTextLabel.ForeColor.ToArgb();
            }

            if(invalidator != null && Cache.HasChanged) {
                invalidator.Invalidate(0, 0, width, height);
            }
        }

        public void Dispose() { }

        public int GetSettingsHashCode() => Settings.GetSettingsHashCode();
    }
}