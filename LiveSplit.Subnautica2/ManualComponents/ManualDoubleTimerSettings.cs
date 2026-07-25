using LiveSplit.Model;
using LiveSplit.UI;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace Voxif.AutoSplitter {
    public partial class ManualDoubleTimerSettings : UserControl {
        
        public new float Height { get; set; }
        public new float Width { get; set; }

        public LiveSplitState CurrentState { get; set; }

        public float TimerSizeRatio { get; set; }

        public bool ShowGradientFirstTimer { get; set; }
        public bool ShowGradientSecondTimer { get; set; }
        public bool OverrideColorFirstTimer { get; set; }
        public bool OverrideColorSecondTimer { get; set; }

        public float DecimalsSizeFirstTimer { get; set; }
        public float DecimalsSizeSecondTimer { get; set; }

        public Color ColorFirstTimer { get; set; }
        public Color ColorSecondTimer { get; set; }

        public Color BackgroundColor { get; set; }
        public Color BackgroundColor2 { get; set; }

        public GradientType BackgroundGradient { get; set; }
        public string GradientString {
            get => BackgroundGradient.ToString();
            set => BackgroundGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
        }

        public string DigitsFormatFirstTimer { get; set; }
        public string AccuracyFirstTimer { get; set; }
        private string FormatFirstTimer {
            get => DigitsFormatFirstTimer + AccuracyFirstTimer;
            set {
                SplitFormat(value, out string digits, out string accuracy);
                DigitsFormatFirstTimer = digits;
                AccuracyFirstTimer = accuracy;
            }
        }
        public string DigitsFormatSecondTimer { get; set; }
        public string AccuracySecondTimer { get; set; }
        private string FormatSecondTimer {
            get => DigitsFormatSecondTimer + AccuracySecondTimer;
            set {
                SplitFormat(value, out string digits, out string accuracy);
                DigitsFormatSecondTimer = digits;
                AccuracySecondTimer = accuracy;
            }
        }

        private void SplitFormat(string value, out string digits, out string accuracy) {
            var decimalIndex = value.IndexOf('.');
            if(decimalIndex < 0) {
                digits = value;
                accuracy = "";
            } else {
                digits = value.Substring(0, decimalIndex);
                accuracy = value.Substring(decimalIndex);
            }
        }

        public LayoutMode Mode { get; set; }

        public ManualDoubleTimerSettings() {
            InitializeComponent();

            Height = 75;
            Width = 200;
            TimerSizeRatio = 40;

            ShowGradientFirstTimer = true;
            ShowGradientSecondTimer = true;
            OverrideColorFirstTimer = false;
            OverrideColorSecondTimer = false;

            ColorFirstTimer = Color.FromArgb(170, 170, 170);
            ColorSecondTimer = Color.FromArgb(170, 170, 170);

            FormatFirstTimer = "1.23";
            FormatSecondTimer = "1.23";

            BackgroundColor = Color.Transparent;
            BackgroundColor2 = Color.Transparent;
            BackgroundGradient = GradientType.Plain;

            DecimalsSizeFirstTimer = 35f;
            DecimalsSizeSecondTimer = 35f;

            CmbGradientType.DataBindings.Add("SelectedItem", this, "GradientString", false, DataSourceUpdateMode.OnPropertyChanged);
            BtnColor1.DataBindings.Add("BackColor", this, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
            BtnColor2.DataBindings.Add("BackColor", this, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
            TrkRatio.DataBindings.Add("Value", this, "TimerSizeRatio", false, DataSourceUpdateMode.OnPropertyChanged);

            ChkOverrideColorFirstTimer.DataBindings.Add("Checked", this, "OverrideColorFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            BtnColorFirstTimer.DataBindings.Add("BackColor", this, "ColorFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            ChkShowGradientFirstTimer.DataBindings.Add("Checked", this, "ShowGradientFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            CmbDigitsFormatFirstTimer.DataBindings.Add("SelectedItem", this, "DigitsFormatFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            CmbAccuracyFirstTimer.DataBindings.Add("SelectedItem", this, "AccuracyFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            TrkDecimalsSizeFirstTimer.DataBindings.Add("Value", this, "DecimalsSizeFirstTimer", false, DataSourceUpdateMode.OnPropertyChanged);

            ChkOverrideColorSecondTimer.DataBindings.Add("Checked", this, "OverrideColorSecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            BtnColorSecondTimer.DataBindings.Add("BackColor", this, "ColorSecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            ChkShowGradientSecondTimer.DataBindings.Add("Checked", this, "ShowGradientSecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            CmbDigitsFormatSecondTimer.DataBindings.Add("SelectedItem", this, "DigitsFormatSecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            CmbAccuracySecondTimer.DataBindings.Add("SelectedItem", this, "AccuracySecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
            TrkDecimalsSizeSecondTimer.DataBindings.Add("Value", this, "DecimalsSizeSecondTimer", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        private void ChkOverrideColorFirstTimer_CheckedChanged(object sender, EventArgs e) {
            LabelColorFirstTimer.Enabled = BtnColorFirstTimer.Enabled = ChkOverrideColorFirstTimer.Checked;
        }

        private void ChkOverrideColorSecondTimer_CheckedChanged(object sender, EventArgs e) {
            LabelColorSecondTimer.Enabled = BtnColorSecondTimer.Enabled = ChkOverrideColorSecondTimer.Checked;
        }

        private void CmbDigitsFormatFirstTimer_SelectedIndexChanged(object sender, EventArgs e) {
            DigitsFormatFirstTimer = CmbDigitsFormatFirstTimer.SelectedItem.ToString();
        }

        private void CmbDigitsFormatSecondTimer_SelectedIndexChanged(object sender, EventArgs e) {
            DigitsFormatSecondTimer = CmbDigitsFormatSecondTimer.SelectedItem.ToString();
        }

        private void CmbAccuracyFirstTimer_SelectedIndexChanged(object sender, EventArgs e) {
            AccuracyFirstTimer = CmbAccuracyFirstTimer.SelectedItem.ToString();
        }

        private void CmbAccuracySecondTimer_SelectedIndexChanged(object sender, EventArgs e) {
            AccuracySecondTimer = CmbAccuracySecondTimer.SelectedItem.ToString();
        }

        private void CmbGradientType_SelectedIndexChanged(object sender, EventArgs e) {
            var selectedText = CmbGradientType.SelectedItem.ToString();
            BtnColor1.Visible = selectedText != "Plain";
            BtnColor2.DataBindings.Clear();
            BtnColor2.DataBindings.Add("BackColor", this, BtnColor1.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
            GradientString = CmbGradientType.SelectedItem.ToString();
        }

        public void SetSettings(XmlNode node) {
            var element = (XmlElement)node;
            GradientString = SettingsHelper.ParseString(element["BackgroundGradient"], GradientType.Plain.ToString());
            BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"], Color.Transparent);
            BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"], Color.Transparent);
            Height = SettingsHelper.ParseInt(element["Height"]);
            Width = SettingsHelper.ParseInt(element["Width"]);
            TimerSizeRatio = SettingsHelper.ParseFloat(element["TimerSizeRatio"]);
            
            OverrideColorFirstTimer = SettingsHelper.ParseBool(element["OverrideColorFirstTimer"]);
            ColorFirstTimer = SettingsHelper.ParseColor(element["ColorFirstTimer"]);
            ShowGradientFirstTimer = SettingsHelper.ParseBool(element["ShowGradientFirstTimer"]);
            FormatFirstTimer = SettingsHelper.ParseString(element["FormatFirstTimer"]);
            DecimalsSizeFirstTimer = SettingsHelper.ParseFloat(element["DecimalsSizeFirstTimer"], 35f);
            
            OverrideColorSecondTimer = SettingsHelper.ParseBool(element["OverrideColorSecondTimer"]);
            ColorSecondTimer = SettingsHelper.ParseColor(element["ColorSecondTimer"]);
            ShowGradientSecondTimer = SettingsHelper.ParseBool(element["ShowGradientSecondTimer"]);
            FormatSecondTimer = SettingsHelper.ParseString(element["FormatSecondTimer"]);
            DecimalsSizeSecondTimer = SettingsHelper.ParseFloat(element["DecimalsSizeSecondTimer"], 35f);
        }

        public XmlNode GetSettings(XmlDocument document) {
            var parent = document.CreateElement("Settings");
            CreateSettingsNode(document, parent);
            return parent;
        }

        private int CreateSettingsNode(XmlDocument document, XmlElement parent) {
            return SettingsHelper.CreateSetting(document, parent, "Version", "1.0")
                 ^ SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient)
                 ^ SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor)
                 ^ SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2)
                 ^ SettingsHelper.CreateSetting(document, parent, "Height", Height)
                 ^ SettingsHelper.CreateSetting(document, parent, "Width", Width)
                 ^ SettingsHelper.CreateSetting(document, parent, "TimerSizeRatio", TimerSizeRatio)

                 ^ SettingsHelper.CreateSetting(document, parent, "OverrideColorFirstTimer", OverrideColorFirstTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "ColorFirstTimer", ColorFirstTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "ShowGradientFirstTimer", ShowGradientFirstTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "FormatFirstTimer", FormatFirstTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "DecimalsSizeFirstTimer", DecimalsSizeFirstTimer)

                 ^ SettingsHelper.CreateSetting(document, parent, "OverrideColorSecondTimer", OverrideColorSecondTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "ColorSecondTimer", ColorSecondTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "ShowGradientSecondTimer", ShowGradientSecondTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "FormatSecondTimer", FormatSecondTimer)
                 ^ SettingsHelper.CreateSetting(document, parent, "DecimalsSizeSecondTimer", DecimalsSizeSecondTimer);
        }

        public int GetSettingsHashCode() => CreateSettingsNode(null, null);

        private void ColorButtonClick(object sender, EventArgs e) => SettingsHelper.ColorButtonClick((Button)sender, this);

        private void DetailedTimerSettings_Load(object sender, EventArgs e) {
            ChkOverrideColorFirstTimer_CheckedChanged(null, null);
            
            if(Mode == LayoutMode.Horizontal) {
                TrkSize.DataBindings.Clear();
                TrkSize.Minimum = 50;
                TrkSize.Maximum = 500;
                TrkSize.DataBindings.Add("Value", this, "Width", false, DataSourceUpdateMode.OnPropertyChanged);
                LblSize.Text = "Width:";
            } else {
                TrkSize.DataBindings.Clear();
                TrkSize.Minimum = 20;
                TrkSize.Maximum = 150;
                TrkSize.DataBindings.Add("Value", this, "Height", false, DataSourceUpdateMode.OnPropertyChanged);
                LblSize.Text = "Height:";
            }
        }
    }
}