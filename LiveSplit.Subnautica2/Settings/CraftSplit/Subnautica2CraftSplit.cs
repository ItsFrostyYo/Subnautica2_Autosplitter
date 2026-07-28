using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2CraftSplit : Subnautica2SplitSetting
    {
        public CraftSplit _split;

        private int mX = 0;
        private int mY = 0;
        private bool isDragging = false;

        public Subnautica2CraftSplit() : this(new CraftSplit(Craftable.None, onlySplitOnce: true, isSubCondition: false)) { }
        public Subnautica2CraftSplit(CraftSplit craftSplit)
        {
            InitializeComponent();

            _split = craftSplit ?? new CraftSplit(Craftable.None, onlySplitOnce: true, isSubCondition: false);

            ConfigureSearchableCombo(cboCraftables);
            cboCraftables.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboCraftables.DisplayMember = "Display";
            cboCraftables.ValueMember = "Value";
        }

        private void BtnOptions_Click(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2CraftSplitSettings(_split);
            var settings = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };

            if (settings.ShowDialog() == DialogResult.OK)
            {
                _split.OnlySplitOnce = splitSettings.OnlySplitOnce;
                _split.Conditions = splitSettings.Splits;
            }
        }

        private void cboName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsLoading || IsComboSearchUpdating(cboCraftables))
                return;

            if (cboCraftables.SelectedValue is Craftable craftable)
                _split.Craftable = craftable;
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

        public override ComboBox ComboBox => this.cboCraftables;
        public override Button BtnEdit => this.btnEdit;
        public override Button BtnRemove => this.btnRemove;
        public override SplitName SplitName => SplitName.Craft;
        public override Subnautica2Split Split => this._split;
    }

    public class CraftSplit : Subnautica2Split
    {
        public Craftable Craftable { get; set; }

        public CraftSplit(Craftable craftable, bool onlySplitOnce, bool isSubCondition)
        {
            Craftable = craftable;
            this.OnlySplitOnce = onlySplitOnce;
            this.SplitName = SplitName.Craft;
            this.IsSubCondition = isSubCondition;
        }
        public override string GetDescription() => $"Craft {Localization.GetDisplayName(Craftable)}";
    }
}
