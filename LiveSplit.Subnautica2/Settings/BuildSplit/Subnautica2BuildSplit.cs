using LiveSplit.Subnautica2.Enums;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public sealed class Subnautica2BuildSplit : Subnautica2SplitSetting
    {
        private readonly BuildSplit _split;
        private readonly ComboBox cboBuildables;
        private readonly Button btnEdit;
        private readonly Button btnRemove;
        private readonly Button btnOptions;
        private readonly PictureBox dragHandle;
        private int mouseX;
        private int mouseY;

        public Subnautica2BuildSplit() : this(new BuildSplit(Buildable.None, true, false)) { }

        public Subnautica2BuildSplit(BuildSplit split)
        {
            _split = split ?? new BuildSplit(Buildable.None, true, false);
            var resources = new ComponentResourceManager(typeof(Subnautica2CraftSplit));
            AutoSize = true;
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            Margin = new Padding(2);
            Size = new Size(469, 47);

            dragHandle = new PictureBox { Cursor = Cursors.SizeAll, Image = (Image)resources.GetObject("picHandle.Image"), Location = new Point(3, 12), Name = "picHandle", Size = new Size(20, 20) };
            var label = new Label { AutoSize = true, Location = new Point(26, 2), Text = "Build" };
            cboBuildables = new ComboBox { Location = new Point(29, 18), Name = "cboBuildables", Size = new Size(343, 21), DisplayMember = "Display", ValueMember = "Value" };
            ConfigureSearchableCombo(cboBuildables);
            btnOptions = new Button { Font = new Font("Microsoft Sans Serif", 8.25F), Location = new Point(376, 16), Name = "BtnOptions", Size = new Size(26, 23), Text = "⚙", UseVisualStyleBackColor = true };
            btnRemove = new Button { Image = (Image)resources.GetObject("btnRemove.Image"), Location = new Point(408, 16), Name = "btnRemove", Size = new Size(26, 23), UseVisualStyleBackColor = true };
            btnEdit = new Button { Location = new Point(440, 16), Name = "btnEdit", Size = new Size(26, 23), Text = "✏", UseVisualStyleBackColor = true };

            cboBuildables.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboBuildables.SelectedIndexChanged += (o, e) =>
            {
                if (!IsLoading && !IsComboSearchUpdating(cboBuildables) && cboBuildables.SelectedValue is Buildable value)
                    _split.Buildable = value;
            };
            btnOptions.Click += BtnOptionsClick;
            dragHandle.MouseDown += (o, e) => { mouseX = e.X; mouseY = e.Y; };
            dragHandle.MouseMove += (o, e) => { if (e.Button == MouseButtons.Left && Math.Abs(mouseX - e.X) + Math.Abs(mouseY - e.Y) > 6) DoDragDrop(this, DragDropEffects.All); };

            Controls.Add(dragHandle);
            Controls.Add(label);
            Controls.Add(cboBuildables);
            Controls.Add(btnOptions);
            Controls.Add(btnRemove);
            Controls.Add(btnEdit);
        }

        private void BtnOptionsClick(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2CraftSplitSettings(_split);
            var dialog = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _split.OnlySplitOnce = splitSettings.OnlySplitOnce;
                _split.Conditions = splitSettings.Splits;
            }
        }

        public override ComboBox ComboBox => cboBuildables;
        public override Button BtnEdit => btnEdit;
        public override Button BtnRemove => btnRemove;
        public override SplitName SplitName => SplitName.Build;
        public override Subnautica2Split Split => _split;
    }

    public sealed class BuildSplit : Subnautica2Split
    {
        public Buildable Buildable { get; set; }

        public BuildSplit(Buildable buildable, bool onlySplitOnce, bool isSubCondition)
        {
            Buildable = buildable;
            OnlySplitOnce = onlySplitOnce;
            SplitName = SplitName.Build;
            IsSubCondition = isSubCondition;
        }

        public override string GetDescription() => $"Build {Localization.GetDisplayName(Buildable)}";
    }
}
