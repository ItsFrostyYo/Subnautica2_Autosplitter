namespace LiveSplit.VoxSplitter {
    partial class SplitsGenerator {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if(disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.TableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.LabelHelp = new System.Windows.Forms.Label();
            this.Button = new System.Windows.Forms.Button();
            this.ListView = new LiveSplit.VoxSplitter.NewListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.TableLayoutPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // TableLayoutPanel
            // 
            this.TableLayoutPanel.AutoSize = true;
            this.TableLayoutPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.TableLayoutPanel.ColumnCount = 3;
            this.TableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33332F));
            this.TableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33334F));
            this.TableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33334F));
            this.TableLayoutPanel.Controls.Add(this.ListView, 0, 0);
            this.TableLayoutPanel.Controls.Add(this.LabelHelp, 0, 1);
            this.TableLayoutPanel.Controls.Add(this.Button, 3, 1);
            this.TableLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TableLayoutPanel.Location = new System.Drawing.Point(0, 0);
            this.TableLayoutPanel.Name = "TableLayoutPanel";
            this.TableLayoutPanel.RowCount = 2;
            this.TableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.TableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.TableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.TableLayoutPanel.Size = new System.Drawing.Size(372, 432);
            this.TableLayoutPanel.TabIndex = 1;
            // 
            // LabelHelp
            // 
            this.LabelHelp.AutoSize = true;
            this.TableLayoutPanel.SetColumnSpan(this.LabelHelp, 2);
            this.LabelHelp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.LabelHelp.ForeColor = System.Drawing.SystemColors.ControlDark;
            this.LabelHelp.Location = new System.Drawing.Point(3, 392);
            this.LabelHelp.Name = "LabelHelp";
            this.LabelHelp.Size = new System.Drawing.Size(241, 40);
            this.LabelHelp.TabIndex = 2;
            this.LabelHelp.Text = "Drag to order, Click to rename";
            this.LabelHelp.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Button
            // 
            this.Button.AutoSize = true;
            this.Button.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Button.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Button.Location = new System.Drawing.Point(250, 395);
            this.Button.Name = "Button";
            this.Button.Size = new System.Drawing.Size(119, 34);
            this.Button.TabIndex = 1;
            this.Button.Text = "Ok";
            this.Button.UseVisualStyleBackColor = true;
            this.Button.Click += new System.EventHandler(this.Button_Click);
            // 
            // ListView
            // 
            this.ListView.Alignment = System.Windows.Forms.ListViewAlignment.Left;
            this.ListView.AllowDrop = true;
            this.ListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
            this.TableLayoutPanel.SetColumnSpan(this.ListView, 3);
            this.ListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ListView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.ListView.HideSelection = false;
            this.ListView.LabelEdit = true;
            this.ListView.LabelWrap = false;
            this.ListView.Location = new System.Drawing.Point(3, 3);
            this.ListView.MinimumSize = new System.Drawing.Size(300, 300);
            this.ListView.MultiSelect = false;
            this.ListView.Name = "ListView";
            this.ListView.ShowGroups = false;
            this.ListView.Size = new System.Drawing.Size(366, 386);
            this.ListView.TabIndex = 0;
            this.ListView.UseCompatibleStateImageBehavior = false;
            this.ListView.View = System.Windows.Forms.View.Details;
            this.ListView.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.ListView_ItemDrag);
            this.ListView.Click += new System.EventHandler(this.ListView_Click);
            this.ListView.DragEnter += new System.Windows.Forms.DragEventHandler(this.ListView_DragEnter);
            this.ListView.DragOver += new System.Windows.Forms.DragEventHandler(this.ListView_DragOver);
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "";
            this.columnHeader1.Width = 341;
            // 
            // SplitsGenerator
            // 
            this.AcceptButton = this.Button;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ClientSize = new System.Drawing.Size(372, 432);
            this.Controls.Add(this.TableLayoutPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.HelpButton = true;
            this.MaximizeBox = false;
            this.Name = "SplitsGenerator";
            this.Text = "Splits Generator";
            this.TableLayoutPanel.ResumeLayout(false);
            this.TableLayoutPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public NewListView ListView;
        private System.Windows.Forms.TableLayoutPanel TableLayoutPanel;
        private System.Windows.Forms.Button Button;
        private System.Windows.Forms.Label LabelHelp;
        private System.Windows.Forms.ColumnHeader columnHeader1;
    }
}