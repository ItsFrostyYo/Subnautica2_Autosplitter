using LiveSplit.Subnautica2.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2ItemSplitSettings : Subnautica2BaseSettings
    {
        public bool PickUp { get; private set; }
        public int Count { get; private set; }
        public bool IsCount { get; private set; }
        public bool OnlySplitOnce { get; private set; }

        public override FlowLayoutPanel MainPanel => flowMain;
        public override FlowLayoutPanel Options => flowOptions;
        public override RadioButton Alpha => RdAlpha;

        private void BtnAddCondition_Click(object sender, EventArgs e) => BtnAddSplitClick(sender, e, isSubCondition: true);
        private void BtnRemove_Click(object sender, EventArgs e) => BtnRemoveClick(sender, e);
        private void BtnEdit_Click(object sender, EventArgs e) => BtnEditClick(sender, e);
        private void flowMain_Paint(object sender, PaintEventArgs e) => flowMainPaint(sender, e);
        private void flowMain_DragEnter(object sender, DragEventArgs e) => flowMainDragEnter(sender, e);
        private void flowMain_DragLeave(object sender, EventArgs e) => flowMainDragLeave(sender, e);
        private void flowMain_DragDrop(object sender, DragEventArgs e) => flowMainDragDrop(sender, e);
        private void flowMain_DragOver(object sender, DragEventArgs e) => flowMainDragOver(sender, e);
        private void RdSort_CheckedChanged(object sender, EventArgs e) => RdSortCheckedChanged(sender, e);

        public Subnautica2ItemSplitSettings(ItemSplit split)
        {
            InitializeComponent();

            Splits = split.Conditions != null ? split.Conditions.Select(c => c.DeepCopy()).ToList() : new List<Subnautica2Split>();
            OnlySplitOnce = split.OnlySplitOnce;
            PickUp = split.PickUp;
            Count = split.Count;
            IsCount = split.IsCount;

            ChkSplitOnce.Enabled = !(split.IsSubCondition || Subnautica2Settings.OrderedLiveSplit || Subnautica2Settings.OrderedAutoSplits);
            RdPickUp.Enabled = !split.IsSubCondition;
            RdDrop.Enabled = !split.IsSubCondition;

            LoadSettings();
        }

        public override void UpdateSplits()
        {
            PickUp = RdPickUp.Checked;
            OnlySplitOnce = ChkSplitOnce.Checked;
            tbCount.Enabled = chkCount.Checked;
            IsCount = chkCount.Checked;
            Count = int.TryParse(tbCount.Text, out int count) ? count : 1;

            base.UpdateSplits();
        }

        public override void ControlChanged(object sender, EventArgs e)
        {
            if (IsLoading)
                return;

            UpdateSplits();
        }

        private void RdPickUp_CheckedChanged(object sender, EventArgs e) => PickUp = RdPickUp.Checked;

        public override void LoadSettings()
        {
            IsLoading = true;

            chkCount.Checked = IsCount;
            tbCount.Text = Count.ToString();
            tbCount.Enabled = IsCount;
            RdPickUp.Checked = PickUp;
            RdDrop.Checked = !PickUp;
            ChkSplitOnce.Checked = OnlySplitOnce;

            base.LoadSettings();
            IsLoading = false;
        }

        private void tbCount_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
                e.Handled = true;
        }

        private void tbCount_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(tbCount.Text, out int value))
            {
                if (value < 1) tbCount.Text = "1";
                else if (value > 48) tbCount.Text = "48";

                tbCount.SelectionStart = tbCount.Text.Length;
            }

            ControlChanged(sender, e);
        }

        private void chkCount_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkCount.Checked && tbCount.Text == string.Empty)
                tbCount.Text = "1";
            ControlChanged(sender, e);
        }
    }
}
