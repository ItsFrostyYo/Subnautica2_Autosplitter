using LiveSplit.Subnautica2.Enums;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public sealed class Subnautica2StoryGoalSplit : Subnautica2SplitSetting
    {
        public readonly ComboBox cboStoryGoal;
        private readonly Button btnEdit;
        private readonly Button btnRemove;
        private readonly Button btnOptions;
        private readonly PictureBox dragHandle;
        private readonly StoryGoalSplit split;
        private int mouseX;
        private int mouseY;

        public Subnautica2StoryGoalSplit() : this(new StoryGoalSplit(StoryGoal.None, true, false)) { }

        public Subnautica2StoryGoalSplit(StoryGoalSplit value)
        {
            split = value ?? new StoryGoalSplit(StoryGoal.None, true, false);
            AutoSize = true;
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            Margin = new Padding(2);
            Size = new Size(469, 47);

            var resources = new ComponentResourceManager(typeof(Subnautica2CraftSplit));
            dragHandle = new PictureBox
            {
                Cursor = Cursors.SizeAll,
                Image = (Image)resources.GetObject("picHandle.Image"),
                Location = new Point(3, 12),
                Name = "picHandle",
                Size = new Size(20, 20)
            };
            var label = new Label { AutoSize = true, Location = new Point(26, 2), Text = "Story Goal" };
            cboStoryGoal = new ComboBox
            {
                DisplayMember = "Display",
                ValueMember = "Value",
                Location = new Point(29, 18),
                Size = new Size(343, 21)
            };
            ConfigureSearchableCombo(cboStoryGoal);
            cboStoryGoal.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            cboStoryGoal.SelectedIndexChanged += (o, e) =>
            {
                if (!IsLoading && !IsComboSearchUpdating(cboStoryGoal) && cboStoryGoal.SelectedValue is StoryGoal goal)
                    split.Goal = goal;
            };

            btnOptions = new Button { Location = new Point(376, 16), Size = new Size(26, 23), Text = "\u2699" };
            btnRemove = new Button
            {
                Image = (Image)resources.GetObject("btnRemove.Image"),
                Location = new Point(408, 16),
                Size = new Size(26, 23)
            };
            btnEdit = new Button { Location = new Point(440, 16), Size = new Size(26, 23), Text = "\u270F" };
            btnOptions.Click += BtnOptionsClick;
            dragHandle.MouseDown += (o, e) => { mouseX = e.X; mouseY = e.Y; };
            dragHandle.MouseMove += (o, e) =>
            {
                if (e.Button == MouseButtons.Left
                    && Math.Abs(mouseX - e.X) + Math.Abs(mouseY - e.Y) > 6)
                    DoDragDrop(this, DragDropEffects.All);
            };

            Controls.Add(dragHandle);
            Controls.Add(label);
            Controls.Add(cboStoryGoal);
            Controls.Add(btnOptions);
            Controls.Add(btnRemove);
            Controls.Add(btnEdit);
        }

        private void BtnOptionsClick(object sender, EventArgs e)
        {
            var splitSettings = new Subnautica2BlueprintSplitSettings(split);
            var dialog = new SplitSettingsDialog(splitSettings) { StartPosition = FormStartPosition.CenterParent };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            split.OnlySplitOnce = splitSettings.OnlySplitOnce;
            split.Conditions = splitSettings.Splits;
        }

        public override ComboBox ComboBox => cboStoryGoal;
        public override Button BtnEdit => btnEdit;
        public override Button BtnRemove => btnRemove;
        public override SplitName SplitName => SplitName.StoryGoal;
        public override Subnautica2Split Split => split;
    }

    public sealed class StoryGoalSplit : Subnautica2Split
    {
        public StoryGoal Goal { get; set; }

        public StoryGoalSplit(StoryGoal goal, bool onlySplitOnce, bool isSubCondition)
        {
            Goal = goal;
            OnlySplitOnce = onlySplitOnce;
            SplitName = SplitName.StoryGoal;
            IsSubCondition = isSubCondition;
        }

        public override string GetDescription() => $"{Localization.GetDisplayName(Goal)} Story Goal Split";
    }
}
