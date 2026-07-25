using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2EncySplit : Subnautica2SplitSetting
    {
        public EncySplit _split;

        private int mX = 0;
        private int mY = 0;
        private bool isDragging = false;

        public Subnautica2EncySplit() : this(new EncySplit(EncyEntry.None, onlySplitOnce: true, isSubCondition: false)) { }
        public Subnautica2EncySplit(EncySplit encySplit)
        {
            InitializeComponent();

            _split = encySplit ?? new EncySplit(EncyEntry.None, onlySplitOnce: true, isSubCondition: false);

            cboEncy.DropDownStyle = ComboBoxStyle.DropDown;
            cboEncy.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cboEncy.AutoCompleteSource = AutoCompleteSource.ListItems;
            cboEncy.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboEncy.DisplayMember = "Display";
            cboEncy.ValueMember = "Value";
            cboEncy.TextChanged += cboName_TextChanged;
        }

        private void BtnOptions_Click(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2EncySplitSettings(_split);
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

            UpdateEntryFromCombo();
        }

        private void cboName_TextChanged(object sender, EventArgs e)
        {
            if (IsLoading)
                return;

            UpdateEntryFromCombo();
        }

        private void UpdateEntryFromCombo()
        {
            string text = cboEncy.Text?.Trim() ?? string.Empty;

            if (cboEncy.SelectedValue is EncyEntry selected)
            {
                string display = Localization.GetDisplayName(selected);
                if (text.Equals(selected.ToString(), StringComparison.OrdinalIgnoreCase)
                    || text.Equals(display, StringComparison.OrdinalIgnoreCase))
                {
                    _split.Entry = selected;
                    _split.EntryName = selected == EncyEntry.None ? string.Empty : selected.ToString();
                    return;
                }
            }

            EncyEntry parsed = Subnautica2SplitSetting.GetEncyEntry(text);
            _split.Entry = parsed;
            _split.EntryName = parsed == EncyEntry.None ? text : parsed.ToString();
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

        public override ComboBox ComboBox => this.cboEncy;
        public override Button BtnEdit => this.btnEdit;
        public override Button BtnRemove => this.btnRemove;
        public override SplitName SplitName => SplitName.Encyclopedia;
        public override Subnautica2Split Split => this._split;
    }

    public class EncySplit : Subnautica2Split
    {
        public EncyEntry Entry { get; set; }
        public string EntryName { get; set; }
        public string EntryKey => Entry == EncyEntry.None ? (EntryName ?? string.Empty).Trim() : Entry.ToString();

        public EncySplit(EncyEntry entry, bool onlySplitOnce, bool isSubCondition)
        {
            Entry = entry;
            EntryName = entry == EncyEntry.None ? string.Empty : entry.ToString();
            this.OnlySplitOnce = onlySplitOnce;
            this.SplitName = SplitName.Encyclopedia;
            this.IsSubCondition = isSubCondition;
        }

        public EncySplit(string entryName, bool onlySplitOnce, bool isSubCondition)
            : this(Subnautica2SplitSetting.GetEncyEntry(entryName), onlySplitOnce, isSubCondition)
        {
            EntryName = Entry == EncyEntry.None ? (entryName ?? string.Empty).Trim() : Entry.ToString();
        }

        public override string GetDescription()
        {
            string display = Entry == EncyEntry.None ? EntryKey : Localization.GetDisplayName(Entry);
            return $"{display} in Encyclopedia Split";
        }
    }
}
