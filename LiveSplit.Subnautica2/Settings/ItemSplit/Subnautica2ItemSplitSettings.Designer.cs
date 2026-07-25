namespace LiveSplit.Subnautica2
{
    partial class Subnautica2ItemSplitSettings
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.InvSplitOptions_GroupBox = new System.Windows.Forms.GroupBox();
            this.Other_GroupBox = new System.Windows.Forms.GroupBox();
            this.ChkSplitOnce = new System.Windows.Forms.CheckBox();
            this.BtnAddCondition = new System.Windows.Forms.Button();
            this.InvSplit_GroupBox = new System.Windows.Forms.GroupBox();
            this.chkCount = new System.Windows.Forms.CheckBox();
            this.tbCount = new System.Windows.Forms.TextBox();
            this.RdDrop = new System.Windows.Forms.RadioButton();
            this.RdPickUp = new System.Windows.Forms.RadioButton();
            this.SortBy_GroupBox = new System.Windows.Forms.GroupBox();
            this.RdAlpha = new System.Windows.Forms.RadioButton();
            this.RdType = new System.Windows.Forms.RadioButton();
            this.flowMain = new System.Windows.Forms.FlowLayoutPanel();
            this.flowOptions = new System.Windows.Forms.FlowLayoutPanel();
            this.InvSplitOptions_GroupBox.SuspendLayout();
            this.Other_GroupBox.SuspendLayout();
            this.InvSplit_GroupBox.SuspendLayout();
            this.SortBy_GroupBox.SuspendLayout();
            this.flowMain.SuspendLayout();
            this.flowOptions.SuspendLayout();
            this.SuspendLayout();
            // 
            // InvSplitOptions_GroupBox
            // 
            this.InvSplitOptions_GroupBox.Controls.Add(this.Other_GroupBox);
            this.InvSplitOptions_GroupBox.Controls.Add(this.BtnAddCondition);
            this.InvSplitOptions_GroupBox.Controls.Add(this.InvSplit_GroupBox);
            this.InvSplitOptions_GroupBox.Controls.Add(this.SortBy_GroupBox);
            this.InvSplitOptions_GroupBox.Location = new System.Drawing.Point(3, 3);
            this.InvSplitOptions_GroupBox.Name = "InvSplitOptions_GroupBox";
            this.InvSplitOptions_GroupBox.Padding = new System.Windows.Forms.Padding(0);
            this.InvSplitOptions_GroupBox.Size = new System.Drawing.Size(466, 135);
            this.InvSplitOptions_GroupBox.TabIndex = 6;
            this.InvSplitOptions_GroupBox.TabStop = false;
            this.InvSplitOptions_GroupBox.Text = "Inventory Split";
            // 
            // Other_GroupBox
            // 
            this.Other_GroupBox.Controls.Add(this.ChkSplitOnce);
            this.Other_GroupBox.Location = new System.Drawing.Point(296, 15);
            this.Other_GroupBox.Name = "Other_GroupBox";
            this.Other_GroupBox.Size = new System.Drawing.Size(164, 81);
            this.Other_GroupBox.TabIndex = 8;
            this.Other_GroupBox.TabStop = false;
            this.Other_GroupBox.Text = "Others";
            // 
            // ChkSplitOnce
            // 
            this.ChkSplitOnce.AutoSize = true;
            this.ChkSplitOnce.Location = new System.Drawing.Point(6, 23);
            this.ChkSplitOnce.Name = "ChkSplitOnce";
            this.ChkSplitOnce.Size = new System.Drawing.Size(99, 17);
            this.ChkSplitOnce.TabIndex = 0;
            this.ChkSplitOnce.Text = "Only Split Once";
            this.ChkSplitOnce.UseVisualStyleBackColor = true;
            this.ChkSplitOnce.CheckedChanged += new System.EventHandler(this.ControlChanged);
            // 
            // BtnAddCondition
            // 
            this.BtnAddCondition.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.BtnAddCondition.Location = new System.Drawing.Point(6, 102);
            this.BtnAddCondition.Name = "BtnAddCondition";
            this.BtnAddCondition.Size = new System.Drawing.Size(116, 23);
            this.BtnAddCondition.TabIndex = 0;
            this.BtnAddCondition.Text = "Add Condion";
            this.BtnAddCondition.UseVisualStyleBackColor = true;
            this.BtnAddCondition.Click += new System.EventHandler(this.BtnAddCondition_Click);
            // 
            // InvSplit_GroupBox
            // 
            this.InvSplit_GroupBox.Controls.Add(this.chkCount);
            this.InvSplit_GroupBox.Controls.Add(this.tbCount);
            this.InvSplit_GroupBox.Controls.Add(this.RdDrop);
            this.InvSplit_GroupBox.Controls.Add(this.RdPickUp);
            this.InvSplit_GroupBox.Location = new System.Drawing.Point(128, 15);
            this.InvSplit_GroupBox.Name = "InvSplit_GroupBox";
            this.InvSplit_GroupBox.Size = new System.Drawing.Size(162, 81);
            this.InvSplit_GroupBox.TabIndex = 7;
            this.InvSplit_GroupBox.TabStop = false;
            this.InvSplit_GroupBox.Text = "Inventory Split";
            // 
            // chkCount
            // 
            this.chkCount.AutoSize = true;
            this.chkCount.Location = new System.Drawing.Point(70, 20);
            this.chkCount.Name = "chkCount";
            this.chkCount.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.chkCount.Size = new System.Drawing.Size(86, 17);
            this.chkCount.TabIndex = 3;
            this.chkCount.Text = ":Min Change";
            this.chkCount.UseVisualStyleBackColor = true;
            this.chkCount.CheckedChanged += new System.EventHandler(this.chkCount_CheckedChanged);
            // 
            // tbCount
            // 
            this.tbCount.Enabled = false;
            this.tbCount.Location = new System.Drawing.Point(73, 43);
            this.tbCount.Name = "tbCount";
            this.tbCount.Size = new System.Drawing.Size(54, 20);
            this.tbCount.TabIndex = 2;
            this.tbCount.Text = "1";
            this.tbCount.TextChanged += new System.EventHandler(this.tbCount_TextChanged);
            this.tbCount.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.tbCount_KeyPress);
            // 
            // RdDrop
            // 
            this.RdDrop.AutoSize = true;
            this.RdDrop.Location = new System.Drawing.Point(6, 42);
            this.RdDrop.Name = "RdDrop";
            this.RdDrop.Size = new System.Drawing.Size(48, 17);
            this.RdDrop.TabIndex = 1;
            this.RdDrop.Text = "Drop";
            this.RdDrop.UseVisualStyleBackColor = true;
            this.RdDrop.CheckedChanged += new System.EventHandler(this.RdPickUp_CheckedChanged);
            // 
            // RdPickUp
            // 
            this.RdPickUp.AutoSize = true;
            this.RdPickUp.Checked = true;
            this.RdPickUp.Location = new System.Drawing.Point(6, 19);
            this.RdPickUp.Name = "RdPickUp";
            this.RdPickUp.Size = new System.Drawing.Size(61, 17);
            this.RdPickUp.TabIndex = 0;
            this.RdPickUp.TabStop = true;
            this.RdPickUp.Text = "Pick up";
            this.RdPickUp.UseVisualStyleBackColor = true;
            this.RdPickUp.CheckedChanged += new System.EventHandler(this.RdPickUp_CheckedChanged);
            // 
            // SortBy_GroupBox
            // 
            this.SortBy_GroupBox.Controls.Add(this.RdAlpha);
            this.SortBy_GroupBox.Controls.Add(this.RdType);
            this.SortBy_GroupBox.Location = new System.Drawing.Point(6, 15);
            this.SortBy_GroupBox.Name = "SortBy_GroupBox";
            this.SortBy_GroupBox.Size = new System.Drawing.Size(116, 81);
            this.SortBy_GroupBox.TabIndex = 6;
            this.SortBy_GroupBox.TabStop = false;
            this.SortBy_GroupBox.Text = "Sort Split Selects By";
            // 
            // RdAlpha
            // 
            this.RdAlpha.AutoSize = true;
            this.RdAlpha.Checked = true;
            this.RdAlpha.Location = new System.Drawing.Point(6, 42);
            this.RdAlpha.Name = "RdAlpha";
            this.RdAlpha.Size = new System.Drawing.Size(83, 17);
            this.RdAlpha.TabIndex = 3;
            this.RdAlpha.TabStop = true;
            this.RdAlpha.Text = "Alphabetical";
            this.RdAlpha.UseVisualStyleBackColor = true;
            this.RdAlpha.CheckedChanged += new System.EventHandler(this.RdSort_CheckedChanged);
            // 
            // RdType
            // 
            this.RdType.AutoSize = true;
            this.RdType.Location = new System.Drawing.Point(6, 19);
            this.RdType.Name = "RdType";
            this.RdType.Size = new System.Drawing.Size(49, 17);
            this.RdType.TabIndex = 2;
            this.RdType.Text = "Type";
            this.RdType.UseVisualStyleBackColor = true;
            this.RdType.CheckedChanged += new System.EventHandler(this.RdSort_CheckedChanged);
            // 
            // flowMain
            // 
            this.flowMain.AllowDrop = true;
            this.flowMain.AutoSize = true;
            this.flowMain.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowMain.Controls.Add(this.flowOptions);
            this.flowMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowMain.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowMain.Location = new System.Drawing.Point(0, 0);
            this.flowMain.Margin = new System.Windows.Forms.Padding(0);
            this.flowMain.Name = "flowMain";
            this.flowMain.Size = new System.Drawing.Size(472, 141);
            this.flowMain.TabIndex = 0;
            this.flowMain.WrapContents = false;
            this.flowMain.DragDrop += new System.Windows.Forms.DragEventHandler(this.flowMain_DragDrop);
            this.flowMain.DragEnter += new System.Windows.Forms.DragEventHandler(this.flowMain_DragEnter);
            this.flowMain.DragOver += new System.Windows.Forms.DragEventHandler(this.flowMain_DragOver);
            this.flowMain.DragLeave += new System.EventHandler(this.flowMain_DragLeave);
            this.flowMain.Paint += new System.Windows.Forms.PaintEventHandler(this.flowMain_Paint);
            // 
            // flowOptions
            // 
            this.flowOptions.AutoSize = true;
            this.flowOptions.Controls.Add(this.InvSplitOptions_GroupBox);
            this.flowOptions.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowOptions.Location = new System.Drawing.Point(0, 0);
            this.flowOptions.Margin = new System.Windows.Forms.Padding(0);
            this.flowOptions.Name = "flowOptions";
            this.flowOptions.Size = new System.Drawing.Size(472, 141);
            this.flowOptions.TabIndex = 2;
            // 
            // Subnautica2ItemSplitSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.flowMain);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "Subnautica2ItemSplitSettings";
            this.Size = new System.Drawing.Size(472, 141);
            this.InvSplitOptions_GroupBox.ResumeLayout(false);
            this.Other_GroupBox.ResumeLayout(false);
            this.Other_GroupBox.PerformLayout();
            this.InvSplit_GroupBox.ResumeLayout(false);
            this.InvSplit_GroupBox.PerformLayout();
            this.SortBy_GroupBox.ResumeLayout(false);
            this.SortBy_GroupBox.PerformLayout();
            this.flowMain.ResumeLayout(false);
            this.flowMain.PerformLayout();
            this.flowOptions.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox InvSplitOptions_GroupBox;
        private System.Windows.Forms.GroupBox Other_GroupBox;
        private System.Windows.Forms.CheckBox ChkSplitOnce;
        private System.Windows.Forms.Button BtnAddCondition;
        private System.Windows.Forms.GroupBox InvSplit_GroupBox;
        private System.Windows.Forms.RadioButton RdDrop;
        private System.Windows.Forms.RadioButton RdPickUp;
        private System.Windows.Forms.GroupBox SortBy_GroupBox;
        private System.Windows.Forms.RadioButton RdAlpha;
        private System.Windows.Forms.RadioButton RdType;
        private System.Windows.Forms.FlowLayoutPanel flowMain;
        private System.Windows.Forms.FlowLayoutPanel flowOptions;
        private System.Windows.Forms.TextBox tbCount;
        private System.Windows.Forms.CheckBox chkCount;
    }
}
