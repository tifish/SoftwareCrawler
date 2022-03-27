namespace SoftwareCrawler
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.dataGridViewContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.downloadSelectedToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.downloadAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.testSelectedToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.testAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.reloadToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.openWebPageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openDownloadDirectoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.errorMessageLabel = new System.Windows.Forms.Label();
            this.showDevToolsButton = new System.Windows.Forms.Button();
            this.softwareListDataGridView = new System.Windows.Forms.DataGridView();
            this.dataGridViewContextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.softwareListDataGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // dataGridViewContextMenuStrip
            // 
            this.dataGridViewContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.downloadSelectedToolStripMenuItem,
            this.downloadAllToolStripMenuItem,
            this.toolStripSeparator1,
            this.testSelectedToolStripMenuItem,
            this.testAllToolStripMenuItem,
            this.toolStripSeparator2,
            this.reloadToolStripMenuItem,
            this.toolStripSeparator3,
            this.openWebPageToolStripMenuItem,
            this.openDownloadDirectoryToolStripMenuItem});
            this.dataGridViewContextMenuStrip.Name = "dataGridViewContextMenuStrip";
            this.dataGridViewContextMenuStrip.Size = new System.Drawing.Size(210, 176);
            // 
            // downloadSelectedToolStripMenuItem
            // 
            this.downloadSelectedToolStripMenuItem.Name = "downloadSelectedToolStripMenuItem";
            this.downloadSelectedToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.downloadSelectedToolStripMenuItem.Text = "&Download selected";
            this.downloadSelectedToolStripMenuItem.Click += new System.EventHandler(this.downloadSelectedToolStripMenuItem_Click);
            // 
            // downloadAllToolStripMenuItem
            // 
            this.downloadAllToolStripMenuItem.Name = "downloadAllToolStripMenuItem";
            this.downloadAllToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.downloadAllToolStripMenuItem.Text = "Download &all";
            this.downloadAllToolStripMenuItem.Click += new System.EventHandler(this.downloadAllToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(206, 6);
            // 
            // testSelectedToolStripMenuItem
            // 
            this.testSelectedToolStripMenuItem.Name = "testSelectedToolStripMenuItem";
            this.testSelectedToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.testSelectedToolStripMenuItem.Text = "&Test selected";
            this.testSelectedToolStripMenuItem.Click += new System.EventHandler(this.testSelectedToolStripMenuItem_Click);
            // 
            // testAllToolStripMenuItem
            // 
            this.testAllToolStripMenuItem.Name = "testAllToolStripMenuItem";
            this.testAllToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.testAllToolStripMenuItem.Text = "T&est all";
            this.testAllToolStripMenuItem.Click += new System.EventHandler(this.testAllToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(206, 6);
            // 
            // reloadToolStripMenuItem
            // 
            this.reloadToolStripMenuItem.Name = "reloadToolStripMenuItem";
            this.reloadToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.reloadToolStripMenuItem.Text = "&Reload";
            this.reloadToolStripMenuItem.Click += new System.EventHandler(this.reloadToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(206, 6);
            // 
            // openWebPageToolStripMenuItem
            // 
            this.openWebPageToolStripMenuItem.Name = "openWebPageToolStripMenuItem";
            this.openWebPageToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.openWebPageToolStripMenuItem.Text = "Open web page";
            this.openWebPageToolStripMenuItem.Click += new System.EventHandler(this.openWebPageToolStripMenuItem_Click);
            // 
            // openDownloadDirectoryToolStripMenuItem
            // 
            this.openDownloadDirectoryToolStripMenuItem.Name = "openDownloadDirectoryToolStripMenuItem";
            this.openDownloadDirectoryToolStripMenuItem.Size = new System.Drawing.Size(209, 22);
            this.openDownloadDirectoryToolStripMenuItem.Text = "Open download directory";
            this.openDownloadDirectoryToolStripMenuItem.Click += new System.EventHandler(this.openDownloadDirectoryToolStripMenuItem_Click);
            // 
            // errorMessageLabel
            // 
            this.errorMessageLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.errorMessageLabel.AutoSize = true;
            this.errorMessageLabel.Location = new System.Drawing.Point(0, 650);
            this.errorMessageLabel.Name = "errorMessageLabel";
            this.errorMessageLabel.Size = new System.Drawing.Size(81, 15);
            this.errorMessageLabel.TabIndex = 5;
            this.errorMessageLabel.Text = "Error message";
            // 
            // showDevToolsButton
            // 
            this.showDevToolsButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.showDevToolsButton.Location = new System.Drawing.Point(1137, 646);
            this.showDevToolsButton.Name = "showDevToolsButton";
            this.showDevToolsButton.Size = new System.Drawing.Size(115, 23);
            this.showDevToolsButton.TabIndex = 4;
            this.showDevToolsButton.Text = "&Show DevTools";
            this.showDevToolsButton.UseVisualStyleBackColor = true;
            this.showDevToolsButton.Click += new System.EventHandler(this.showDevToolsButton_Click);
            // 
            // softwareListDataGridView
            // 
            this.softwareListDataGridView.AllowUserToOrderColumns = true;
            this.softwareListDataGridView.AllowUserToResizeRows = false;
            this.softwareListDataGridView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.softwareListDataGridView.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.softwareListDataGridView.ClipboardCopyMode = System.Windows.Forms.DataGridViewClipboardCopyMode.Disable;
            this.softwareListDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.softwareListDataGridView.ContextMenuStrip = this.dataGridViewContextMenuStrip;
            this.softwareListDataGridView.Location = new System.Drawing.Point(0, 0);
            this.softwareListDataGridView.Name = "softwareListDataGridView";
            this.softwareListDataGridView.RowTemplate.Height = 25;
            this.softwareListDataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.softwareListDataGridView.Size = new System.Drawing.Size(1264, 630);
            this.softwareListDataGridView.TabIndex = 3;
            this.softwareListDataGridView.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.softwareListDataGridView_CellEndEdit);
            this.softwareListDataGridView.CurrentCellChanged += new System.EventHandler(this.softwareListDataGridView_CurrentCellChanged);
            this.softwareListDataGridView.UserAddedRow += new System.Windows.Forms.DataGridViewRowEventHandler(this.softwareListDataGridView_UserAddedRow);
            this.softwareListDataGridView.UserDeletedRow += new System.Windows.Forms.DataGridViewRowEventHandler(this.softwareListDataGridView_UserDeletedRow);
            this.softwareListDataGridView.MouseDown += new System.Windows.Forms.MouseEventHandler(this.softwareListDataGridView_MouseDown);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1264, 681);
            this.Controls.Add(this.errorMessageLabel);
            this.Controls.Add(this.showDevToolsButton);
            this.Controls.Add(this.softwareListDataGridView);
            this.Name = "MainForm";
            this.Text = "Software Crawler";
            this.dataGridViewContextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.softwareListDataGridView)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private ContextMenuStrip dataGridViewContextMenuStrip;
        private ToolStripMenuItem downloadSelectedToolStripMenuItem;
        private ToolStripMenuItem downloadAllToolStripMenuItem;
        private ToolStripMenuItem reloadToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem testSelectedToolStripMenuItem;
        private ToolStripMenuItem testAllToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator2;
        private Label errorMessageLabel;
        private Button showDevToolsButton;
        private DataGridView softwareListDataGridView;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripMenuItem openWebPageToolStripMenuItem;
        private ToolStripMenuItem openDownloadDirectoryToolStripMenuItem;
    }
}