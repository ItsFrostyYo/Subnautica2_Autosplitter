using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public partial class Subnautica2BiomeSplit : Subnautica2SplitSetting
    {
        public BiomeSplit _split;

        private int mX = 0;
        private int mY = 0;
        private bool isDragging = false;

        public Subnautica2BiomeSplit() : this(new BiomeSplit((Biome.None, Biome.None), onlySplitOnce: true, isSubCondition: false)) { }
        public Subnautica2BiomeSplit(BiomeSplit biomeSplit)
        {
            InitializeComponent();

            _split = biomeSplit ?? new BiomeSplit((Biome.None, Biome.None), onlySplitOnce: true, isSubCondition: false);

            if (_split.IsSubCondition)
            {
                ComboBox2.Visible = false;
                pictureBox1.Visible = false;
                picHandle.Left = 3;
                picHandle.Top = 13;
                btnEdit.Top = 16;
                btnRemove.Top = 16;
                BtnOptions.Top = 16;
            }

            cboBiome1.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboBiome1.DisplayMember = "Display";
            cboBiome1.ValueMember = "Value";

            cboBiome2.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboBiome2.DisplayMember = "Display";
            cboBiome2.ValueMember = "Value";
        }

        private void BtnOptions_Click(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2BiomeSplitSettings(_split);
            var settings = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };

            if (settings.ShowDialog() == DialogResult.OK)
            {
                _split.OnlySplitOnce = splitSettings.OnlySplitOnce;
                _split.Conditions = splitSettings.Splits;
            }
        }

        private void cboBiome_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsLoading)
                return;

            if (cboBiome1.SelectedValue is Biome biome1 && cboBiome2.SelectedValue is Biome biome2)
            {
                _split.Biomes.Biome1 = biome1;
                _split.Biomes.Biome2 = biome2;
            }
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

        public override ComboBox ComboBox => this.cboBiome1;
        public override ComboBox ComboBox2 => this.cboBiome2;
        public override Button BtnEdit => this.btnEdit;
        public override Button BtnRemove => this.btnRemove;
        public override SplitName SplitName => SplitName.Biome;
        public override Subnautica2Split Split => this._split;
    }

    public class BiomeSplit : Subnautica2Split
    {
        public (Biome Biome1, Biome Biome2) Biomes;

        public BiomeSplit((Biome biome1, Biome biome2) biomes, bool onlySplitOnce, bool isSubCondition)
        {
            Biomes.Biome1 = biomes.biome1;
            Biomes.Biome2 = biomes.biome2;
            this.OnlySplitOnce = onlySplitOnce;
            this.IsSubCondition = isSubCondition;
            this.SplitName = SplitName.Biome;
        }
        public override string GetDescription() => $"From {Biomes.Biome1} to {Biomes.Biome2} Split";
    }
}
