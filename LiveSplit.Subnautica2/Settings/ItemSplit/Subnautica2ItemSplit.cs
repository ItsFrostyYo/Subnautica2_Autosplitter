using LiveSplit.Subnautica2.Enums;
using LiveSplit.Subnautica2.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2ItemSplit : Subnautica2SplitSetting
    {        
        public ItemSplit _split;

        private int mX = 0;
        private int mY = 0;
        private bool isDragging = false;

        public Subnautica2ItemSplit() : this(new ItemSplit(InventoryItem.None, onlySplitOnce: true, isSubCondition: false)) { }
        public Subnautica2ItemSplit(ItemSplit split)
        {
            InitializeComponent();

            _split = split ?? new ItemSplit(InventoryItem.None, onlySplitOnce: true, isSubCondition: false);

            cboItem.DropDownStyle = ComboBoxStyle.DropDownList;
            cboItem.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboItem.DisplayMember = "Display";
            cboItem.ValueMember = "Value";
        }

        private void BtnOptions_Click(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2ItemSplitSettings(_split);
            var settings = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };

            if (settings.ShowDialog() == DialogResult.OK)
            {
                _split.OnlySplitOnce = splitSettings.OnlySplitOnce;
                _split.PickUp = splitSettings.PickUp;
                _split.Conditions = splitSettings.Splits;
                _split.Count = splitSettings.Count;
                _split.IsCount = splitSettings.IsCount;
            }
        }

        private void cboName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsLoading)
                return;

            if (cboItem.SelectedValue is InventoryItem t)
                _split.Item = t;
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

        public override ComboBox ComboBox => this.cboItem;
        public override Button BtnEdit => this.btnEdit;
        public override Button BtnRemove => this.btnRemove;
        public override SplitName SplitName => SplitName.Inventory;
        public override Subnautica2Split Split => this._split;
    }

    public class ItemSplit : Subnautica2Split
    {
        public InventoryItem Item { get; set; }
        public bool PickUp { get; set; } = true;
        public int Count { get; set; } = 1;
        public bool IsCount { get; set; } = false;
        public bool AlreadySplitInvChanging { get; set; } = false;

        public ItemSplit(InventoryItem item, bool onlySplitOnce, bool isSubCondition)
        {
            Item = item;
            this.OnlySplitOnce = onlySplitOnce;
            this.SplitName = SplitName.Inventory;
            this.IsSubCondition = isSubCondition;
        }
        public override string GetDescription() => 
            PickUp ? $"Pickup {Localization.GetDisplayName(Item)} Split" : $"Drop {Localization.GetDisplayName(Item)} Split";
    }
}
