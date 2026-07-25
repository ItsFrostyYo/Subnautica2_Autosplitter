using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2BlueprintSplit : Subnautica2SplitSetting
    {
        public BlueprintSplit _split;

        private int mX = 0;
        private int mY = 0;
        private bool isDragging = false;

        public Subnautica2BlueprintSplit() : this(new BlueprintSplit(Unlockable.None, onlySplitOnce: true, isSubCondition: false)) { }
        public Subnautica2BlueprintSplit(BlueprintSplit blueprintSplit)
        {
            InitializeComponent();

            _split = blueprintSplit ?? new BlueprintSplit(Unlockable.None, onlySplitOnce: true, isSubCondition: false);

            cboBlueprint.DropDownStyle = ComboBoxStyle.DropDownList;
            cboBlueprint.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboBlueprint.DisplayMember = "Display";
            cboBlueprint.ValueMember = "Value";
        }

        private void BtnOptions_Click(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2BlueprintSplitSettings(_split);
            var settings = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };

            if (settings.ShowDialog() == DialogResult.OK)
            {
                _split.OnlySplitOnce = splitSettings.OnlySplitOnce;
                _split.Conditions = splitSettings.Splits;
            }
        }

        private void cboName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsLoading)
                return;

            if (cboBlueprint.SelectedValue is Unlockable u)
                _split.Blueprint = u;
        }

        private void picHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int num1 = mX - e.X;
                    int num2 = mY - e.Y;
                    if (((num1 * num1) + (num2 * num2)) > 20)
                    {
                        DoDragDrop(this, DragDropEffects.All);
                        isDragging = true;
                        return;
                    }
                }
            }
        }

        private void picHandle_MouseDown(object sender, MouseEventArgs e)
        {
            mX = e.X;
            mY = e.Y;
            isDragging = false;
        }

        public override ComboBox ComboBox => this.cboBlueprint;
        public override Button BtnEdit => this.btnEdit;
        public override Button BtnRemove => this.btnRemove;
        public override SplitName SplitName => SplitName.Blueprint;
        public override Subnautica2Split Split => this._split;
    }

    public class BlueprintSplit : Subnautica2Split
    {
        public Unlockable Blueprint { get; set; }

        public BlueprintSplit(Unlockable bp, bool onlySplitOnce, bool isSubCondition)
        {
            Blueprint = bp;
            this.OnlySplitOnce = onlySplitOnce;
            this.SplitName = SplitName.Blueprint;
            this.IsSubCondition = isSubCondition;
        }
        public override string GetDescription() => $"{Localization.GetDisplayName(Blueprint)} unlock Split";
    }
}
