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
            components = new System.ComponentModel.Container();
            dataGridViewContextMenuStrip = new ContextMenuStrip(components);
            downloadSelectedToolStripMenuItem = new ToolStripMenuItem();
            downloadAllToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            testSelectedToolStripMenuItem = new ToolStripMenuItem();
            testAllToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            cancelToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator4 = new ToolStripSeparator();
            insertNewToolStripMenuItem = new ToolStripMenuItem();
            deleteToolStripMenuItem = new ToolStripMenuItem();
            reloadToolStripMenuItem = new ToolStripMenuItem();
            editScriptToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            openWebPageToolStripMenuItem = new ToolStripMenuItem();
            openWebPageInBrowserToolStripMenuItem = new ToolStripMenuItem();
            openDownloadDirectoryToolStripMenuItem = new ToolStripMenuItem();
            errorMessageLabel = new Label();
            showDevToolsButton = new Button();
            softwareListDataGridView = new DataGridView();
            rootTableLayoutPanel = new TableLayoutPanel();
            toolbarFlowLayoutPanel = new FlowLayoutPanel();
            clearCookieButton = new Button();
            dataGridViewContextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)softwareListDataGridView).BeginInit();
            rootTableLayoutPanel.SuspendLayout();
            toolbarFlowLayoutPanel.SuspendLayout();
            SuspendLayout();
            // 
            // dataGridViewContextMenuStrip
            // 
            dataGridViewContextMenuStrip.Items.AddRange(new ToolStripItem[] { downloadSelectedToolStripMenuItem, downloadAllToolStripMenuItem, toolStripSeparator1, testSelectedToolStripMenuItem, testAllToolStripMenuItem, toolStripSeparator2, cancelToolStripMenuItem, toolStripSeparator4, insertNewToolStripMenuItem, deleteToolStripMenuItem, reloadToolStripMenuItem, editScriptToolStripMenuItem, toolStripSeparator3, openWebPageToolStripMenuItem, openWebPageInBrowserToolStripMenuItem, openDownloadDirectoryToolStripMenuItem });
            dataGridViewContextMenuStrip.Name = "dataGridViewContextMenuStrip";
            dataGridViewContextMenuStrip.Size = new Size(216, 292);
            // 
            // downloadSelectedToolStripMenuItem
            // 
            downloadSelectedToolStripMenuItem.Name = "downloadSelectedToolStripMenuItem";
            downloadSelectedToolStripMenuItem.Size = new Size(215, 22);
            downloadSelectedToolStripMenuItem.Text = "&Download selected";
            downloadSelectedToolStripMenuItem.Click += downloadSelectedToolStripMenuItem_Click;
            // 
            // downloadAllToolStripMenuItem
            // 
            downloadAllToolStripMenuItem.Name = "downloadAllToolStripMenuItem";
            downloadAllToolStripMenuItem.Size = new Size(215, 22);
            downloadAllToolStripMenuItem.Text = "Download &all";
            downloadAllToolStripMenuItem.Click += downloadAllToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(212, 6);
            // 
            // testSelectedToolStripMenuItem
            // 
            testSelectedToolStripMenuItem.Name = "testSelectedToolStripMenuItem";
            testSelectedToolStripMenuItem.Size = new Size(215, 22);
            testSelectedToolStripMenuItem.Text = "&Test selected";
            testSelectedToolStripMenuItem.Click += testSelectedToolStripMenuItem_Click;
            // 
            // testAllToolStripMenuItem
            // 
            testAllToolStripMenuItem.Name = "testAllToolStripMenuItem";
            testAllToolStripMenuItem.Size = new Size(215, 22);
            testAllToolStripMenuItem.Text = "Test all";
            testAllToolStripMenuItem.Click += testAllToolStripMenuItem_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(212, 6);
            // 
            // cancelToolStripMenuItem
            // 
            cancelToolStripMenuItem.Name = "cancelToolStripMenuItem";
            cancelToolStripMenuItem.Size = new Size(215, 22);
            cancelToolStripMenuItem.Text = "&Cancel";
            cancelToolStripMenuItem.Click += cancelToolStripMenuItem_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new Size(212, 6);
            // 
            // insertNewToolStripMenuItem
            // 
            insertNewToolStripMenuItem.Name = "insertNewToolStripMenuItem";
            insertNewToolStripMenuItem.Size = new Size(215, 22);
            insertNewToolStripMenuItem.Text = "Insert &new";
            insertNewToolStripMenuItem.Click += insertNewToolStripMenuItem_Click;
            // 
            // deleteToolStripMenuItem
            // 
            deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
            deleteToolStripMenuItem.Size = new Size(215, 22);
            deleteToolStripMenuItem.Text = "De&lete";
            // 
            // reloadToolStripMenuItem
            // 
            reloadToolStripMenuItem.Name = "reloadToolStripMenuItem";
            reloadToolStripMenuItem.Size = new Size(215, 22);
            reloadToolStripMenuItem.Text = "&Reload";
            reloadToolStripMenuItem.Click += reloadToolStripMenuItem_Click;
            // 
            // editScriptToolStripMenuItem
            // 
            editScriptToolStripMenuItem.Name = "editScriptToolStripMenuItem";
            editScriptToolStripMenuItem.Size = new Size(215, 22);
            editScriptToolStripMenuItem.Text = "&Edit script";
            editScriptToolStripMenuItem.Click += editScriptToolStripMenuItem_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(212, 6);
            // 
            // openWebPageToolStripMenuItem
            // 
            openWebPageToolStripMenuItem.Name = "openWebPageToolStripMenuItem";
            openWebPageToolStripMenuItem.Size = new Size(215, 22);
            openWebPageToolStripMenuItem.Text = "Open &web page";
            openWebPageToolStripMenuItem.Click += openWebPageToolStripMenuItem_Click;
            // 
            // openWebPageInBrowserToolStripMenuItem
            // 
            openWebPageInBrowserToolStripMenuItem.Name = "openWebPageInBrowserToolStripMenuItem";
            openWebPageInBrowserToolStripMenuItem.Size = new Size(215, 22);
            openWebPageInBrowserToolStripMenuItem.Text = "Open web page in &browser";
            openWebPageInBrowserToolStripMenuItem.Click += openWebPageInBrowserToolStripMenuItem_Click;
            // 
            // openDownloadDirectoryToolStripMenuItem
            // 
            openDownloadDirectoryToolStripMenuItem.Name = "openDownloadDirectoryToolStripMenuItem";
            openDownloadDirectoryToolStripMenuItem.Size = new Size(215, 22);
            openDownloadDirectoryToolStripMenuItem.Text = "&Open download directory";
            openDownloadDirectoryToolStripMenuItem.Click += openDownloadDirectoryToolStripMenuItem_Click;
            // 
            // errorMessageLabel
            // 
            errorMessageLabel.Anchor = AnchorStyles.Left;
            errorMessageLabel.AutoSize = true;
            errorMessageLabel.Location = new Point(3, 655);
            errorMessageLabel.Name = "errorMessageLabel";
            errorMessageLabel.Size = new Size(81, 15);
            errorMessageLabel.TabIndex = 5;
            errorMessageLabel.Text = "Error message";
            // 
            // showDevToolsButton
            // 
            showDevToolsButton.Anchor = AnchorStyles.Left;
            showDevToolsButton.Location = new Point(3, 4);
            showDevToolsButton.Name = "showDevToolsButton";
            showDevToolsButton.Size = new Size(115, 23);
            showDevToolsButton.TabIndex = 4;
            showDevToolsButton.Text = "&Show DevTools";
            showDevToolsButton.UseVisualStyleBackColor = true;
            showDevToolsButton.Click += showDevToolsButton_Click;
            // 
            // softwareListDataGridView
            // 
            softwareListDataGridView.AllowUserToOrderColumns = true;
            softwareListDataGridView.AllowUserToResizeRows = false;
            softwareListDataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            softwareListDataGridView.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
            softwareListDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            rootTableLayoutPanel.SetColumnSpan(softwareListDataGridView, 2);
            softwareListDataGridView.ContextMenuStrip = dataGridViewContextMenuStrip;
            softwareListDataGridView.Location = new Point(3, 3);
            softwareListDataGridView.Name = "softwareListDataGridView";
            softwareListDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            softwareListDataGridView.Size = new Size(1258, 638);
            softwareListDataGridView.TabIndex = 3;
            softwareListDataGridView.CellEndEdit += softwareListDataGridView_CellEndEdit;
            softwareListDataGridView.CurrentCellChanged += softwareListDataGridView_CurrentCellChanged;
            softwareListDataGridView.CurrentCellDirtyStateChanged += softwareListDataGridView_CurrentCellDirtyStateChanged;
            softwareListDataGridView.UserAddedRow += softwareListDataGridView_UserAddedRow;
            softwareListDataGridView.UserDeletedRow += softwareListDataGridView_UserDeletedRow;
            softwareListDataGridView.MouseDown += softwareListDataGridView_MouseDown;
            // 
            // rootTableLayoutPanel
            // 
            rootTableLayoutPanel.ColumnCount = 2;
            rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle());
            rootTableLayoutPanel.Controls.Add(softwareListDataGridView, 0, 0);
            rootTableLayoutPanel.Controls.Add(errorMessageLabel, 0, 1);
            rootTableLayoutPanel.Controls.Add(toolbarFlowLayoutPanel, 1, 1);
            rootTableLayoutPanel.Dock = DockStyle.Fill;
            rootTableLayoutPanel.Location = new Point(0, 0);
            rootTableLayoutPanel.Name = "rootTableLayoutPanel";
            rootTableLayoutPanel.RowCount = 2;
            rootTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootTableLayoutPanel.RowStyles.Add(new RowStyle());
            rootTableLayoutPanel.Size = new Size(1264, 681);
            rootTableLayoutPanel.TabIndex = 8;
            // 
            // toolbarFlowLayoutPanel
            // 
            toolbarFlowLayoutPanel.AutoSize = true;
            toolbarFlowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toolbarFlowLayoutPanel.Controls.Add(showDevToolsButton);
            toolbarFlowLayoutPanel.Controls.Add(clearCookieButton);
            toolbarFlowLayoutPanel.Location = new Point(1047, 647);
            toolbarFlowLayoutPanel.Name = "toolbarFlowLayoutPanel";
            toolbarFlowLayoutPanel.Size = new Size(214, 31);
            toolbarFlowLayoutPanel.TabIndex = 6;
            toolbarFlowLayoutPanel.WrapContents = false;
            // 
            // clearCookieButton
            // 
            clearCookieButton.AutoSize = true;
            clearCookieButton.Location = new Point(124, 3);
            clearCookieButton.Name = "clearCookieButton";
            clearCookieButton.Size = new Size(87, 25);
            clearCookieButton.TabIndex = 8;
            clearCookieButton.Text = "Clear cookies";
            clearCookieButton.UseVisualStyleBackColor = true;
            clearCookieButton.Click += clearCookieButton_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 681);
            Controls.Add(rootTableLayoutPanel);
            Name = "MainForm";
            Text = "Software Crawler";
            dataGridViewContextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)softwareListDataGridView).EndInit();
            rootTableLayoutPanel.ResumeLayout(false);
            rootTableLayoutPanel.PerformLayout();
            toolbarFlowLayoutPanel.ResumeLayout(false);
            toolbarFlowLayoutPanel.PerformLayout();
            ResumeLayout(false);
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
        private ToolStripMenuItem cancelToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator4;
        private TableLayoutPanel rootTableLayoutPanel;
        private FlowLayoutPanel toolbarFlowLayoutPanel;
        private Button clearCookieButton;
        private ToolStripMenuItem openWebPageInBrowserToolStripMenuItem;
        private ToolStripMenuItem editScriptToolStripMenuItem;
        private ToolStripMenuItem insertNewToolStripMenuItem;
        private ToolStripMenuItem deleteToolStripMenuItem;
    }
}
