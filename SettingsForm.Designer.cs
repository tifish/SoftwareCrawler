namespace SoftwareCrawler;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        proxyTextBox = new TextBox();
        label1 = new Label();
        downloadRetryCountNumericUpDown = new NumericUpDown();
        label2 = new Label();
        downloadRetryIntervalNumericUpDown = new NumericUpDown();
        label3 = new Label();
        loadPageEndTimeoutNumericUpDown = new NumericUpDown();
        label4 = new Label();
        tryClickCountNumericUpDown = new NumericUpDown();
        label5 = new Label();
        tryClickIntervalNumericUpDown = new NumericUpDown();
        label6 = new Label();
        startDownloadTimeoutNumericUpDown = new NumericUpDown();
        label7 = new Label();
        downloadTimeoutNumericUpDown = new NumericUpDown();
        label8 = new Label();
        externalJavascriptEditorTextBox = new TextBox();
        label9 = new Label();
        defaultDownloadDirectoryTextBox = new TextBox();
        defaultDownloadDirectoryLabel = new Label();
        browseButton = new Button();
        okButton = new Button();
        cancelButton = new Button();
        rootTableLayoutPanel = new TableLayoutPanel();
        flowLayoutPanel2 = new FlowLayoutPanel();
        toolButtonFlowLayoutPanel = new FlowLayoutPanel();
        ((System.ComponentModel.ISupportInitialize)downloadRetryCountNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)downloadRetryIntervalNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)loadPageEndTimeoutNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)tryClickCountNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)tryClickIntervalNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)startDownloadTimeoutNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)downloadTimeoutNumericUpDown).BeginInit();
        rootTableLayoutPanel.SuspendLayout();
        flowLayoutPanel2.SuspendLayout();
        toolButtonFlowLayoutPanel.SuspendLayout();
        SuspendLayout();
        // 
        // proxyTextBox
        // 
        proxyTextBox.Anchor = AnchorStyles.Left;
        proxyTextBox.Location = new Point(160, 3);
        proxyTextBox.Name = "proxyTextBox";
        proxyTextBox.Size = new Size(337, 23);
        proxyTextBox.TabIndex = 0;
        // 
        // label1
        // 
        label1.Anchor = AnchorStyles.Left;
        label1.AutoSize = true;
        label1.Location = new Point(3, 7);
        label1.Name = "label1";
        label1.Size = new Size(36, 15);
        label1.TabIndex = 1;
        label1.Text = "Proxy";
        // 
        // downloadRetryCountNumericUpDown
        // 
        downloadRetryCountNumericUpDown.Anchor = AnchorStyles.Left;
        downloadRetryCountNumericUpDown.Location = new Point(160, 32);
        downloadRetryCountNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        downloadRetryCountNumericUpDown.Name = "downloadRetryCountNumericUpDown";
        downloadRetryCountNumericUpDown.Size = new Size(72, 23);
        downloadRetryCountNumericUpDown.TabIndex = 2;
        // 
        // label2
        // 
        label2.Anchor = AnchorStyles.Left;
        label2.AutoSize = true;
        label2.Location = new Point(3, 36);
        label2.Name = "label2";
        label2.Size = new Size(122, 15);
        label2.TabIndex = 3;
        label2.Text = "Download retry count";
        // 
        // downloadRetryIntervalNumericUpDown
        // 
        downloadRetryIntervalNumericUpDown.Anchor = AnchorStyles.Left;
        downloadRetryIntervalNumericUpDown.Location = new Point(160, 61);
        downloadRetryIntervalNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        downloadRetryIntervalNumericUpDown.Name = "downloadRetryIntervalNumericUpDown";
        downloadRetryIntervalNumericUpDown.Size = new Size(72, 23);
        downloadRetryIntervalNumericUpDown.TabIndex = 4;
        // 
        // label3
        // 
        label3.Anchor = AnchorStyles.Left;
        label3.AutoSize = true;
        label3.Location = new Point(3, 65);
        label3.Name = "label3";
        label3.Size = new Size(146, 15);
        label3.TabIndex = 5;
        label3.Text = "Download retry interval (s)";
        // 
        // loadPageEndTimeoutNumericUpDown
        // 
        loadPageEndTimeoutNumericUpDown.Anchor = AnchorStyles.Left;
        loadPageEndTimeoutNumericUpDown.Location = new Point(160, 90);
        loadPageEndTimeoutNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        loadPageEndTimeoutNumericUpDown.Name = "loadPageEndTimeoutNumericUpDown";
        loadPageEndTimeoutNumericUpDown.Size = new Size(72, 23);
        loadPageEndTimeoutNumericUpDown.TabIndex = 6;
        // 
        // label4
        // 
        label4.Anchor = AnchorStyles.Left;
        label4.AutoSize = true;
        label4.Location = new Point(3, 94);
        label4.Name = "label4";
        label4.Size = new Size(120, 15);
        label4.TabIndex = 7;
        label4.Text = "Page load timeout (s)";
        // 
        // tryClickCountNumericUpDown
        // 
        tryClickCountNumericUpDown.Anchor = AnchorStyles.Left;
        tryClickCountNumericUpDown.Location = new Point(160, 119);
        tryClickCountNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        tryClickCountNumericUpDown.Name = "tryClickCountNumericUpDown";
        tryClickCountNumericUpDown.Size = new Size(72, 23);
        tryClickCountNumericUpDown.TabIndex = 8;
        // 
        // label5
        // 
        label5.Anchor = AnchorStyles.Left;
        label5.AutoSize = true;
        label5.Location = new Point(3, 123);
        label5.Name = "label5";
        label5.Size = new Size(94, 15);
        label5.TabIndex = 9;
        label5.Text = "Click retry count";
        // 
        // tryClickIntervalNumericUpDown
        // 
        tryClickIntervalNumericUpDown.Anchor = AnchorStyles.Left;
        tryClickIntervalNumericUpDown.Location = new Point(160, 148);
        tryClickIntervalNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        tryClickIntervalNumericUpDown.Name = "tryClickIntervalNumericUpDown";
        tryClickIntervalNumericUpDown.Size = new Size(72, 23);
        tryClickIntervalNumericUpDown.TabIndex = 10;
        // 
        // label6
        // 
        label6.Anchor = AnchorStyles.Left;
        label6.AutoSize = true;
        label6.Location = new Point(3, 152);
        label6.Name = "label6";
        label6.Size = new Size(118, 15);
        label6.TabIndex = 11;
        label6.Text = "Click retry interval (s)";
        // 
        // startDownloadTimeoutNumericUpDown
        // 
        startDownloadTimeoutNumericUpDown.Anchor = AnchorStyles.Left;
        startDownloadTimeoutNumericUpDown.Location = new Point(160, 177);
        startDownloadTimeoutNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        startDownloadTimeoutNumericUpDown.Name = "startDownloadTimeoutNumericUpDown";
        startDownloadTimeoutNumericUpDown.Size = new Size(72, 23);
        startDownloadTimeoutNumericUpDown.TabIndex = 12;
        // 
        // label7
        // 
        label7.Anchor = AnchorStyles.Left;
        label7.AutoSize = true;
        label7.Location = new Point(3, 181);
        label7.Name = "label7";
        label7.Size = new Size(148, 15);
        label7.TabIndex = 13;
        label7.Text = "Start download timeout (s)";
        // 
        // downloadTimeoutNumericUpDown
        // 
        downloadTimeoutNumericUpDown.Anchor = AnchorStyles.Left;
        downloadTimeoutNumericUpDown.Location = new Point(160, 206);
        downloadTimeoutNumericUpDown.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
        downloadTimeoutNumericUpDown.Name = "downloadTimeoutNumericUpDown";
        downloadTimeoutNumericUpDown.Size = new Size(72, 23);
        downloadTimeoutNumericUpDown.TabIndex = 14;
        // 
        // label8
        // 
        label8.Anchor = AnchorStyles.Left;
        label8.AutoSize = true;
        label8.Location = new Point(3, 210);
        label8.Name = "label8";
        label8.Size = new Size(122, 15);
        label8.TabIndex = 15;
        label8.Text = "Download timeout (s)";
        // 
        // externalJavascriptEditorTextBox
        // 
        externalJavascriptEditorTextBox.Anchor = AnchorStyles.Left;
        externalJavascriptEditorTextBox.Location = new Point(3, 4);
        externalJavascriptEditorTextBox.Name = "externalJavascriptEditorTextBox";
        externalJavascriptEditorTextBox.Size = new Size(333, 23);
        externalJavascriptEditorTextBox.TabIndex = 16;
        // 
        // label9
        // 
        label9.Anchor = AnchorStyles.Left;
        label9.AutoSize = true;
        label9.Location = new Point(3, 243);
        label9.Name = "label9";
        label9.Size = new Size(136, 15);
        label9.TabIndex = 17;
        label9.Text = "External Javascript editor";
        // 
        // defaultDownloadDirectoryTextBox
        // 
        defaultDownloadDirectoryTextBox.Anchor = AnchorStyles.Left;
        defaultDownloadDirectoryTextBox.Location = new Point(160, 272);
        defaultDownloadDirectoryTextBox.Name = "defaultDownloadDirectoryTextBox";
        defaultDownloadDirectoryTextBox.Size = new Size(336, 23);
        defaultDownloadDirectoryTextBox.TabIndex = 17;
        // 
        // defaultDownloadDirectoryLabel
        // 
        defaultDownloadDirectoryLabel.Anchor = AnchorStyles.Left;
        defaultDownloadDirectoryLabel.AutoSize = true;
        defaultDownloadDirectoryLabel.Location = new Point(3, 276);
        defaultDownloadDirectoryLabel.Name = "defaultDownloadDirectoryLabel";
        defaultDownloadDirectoryLabel.Size = new Size(151, 15);
        defaultDownloadDirectoryLabel.TabIndex = 18;
        defaultDownloadDirectoryLabel.Text = "Default download directory";
        // 
        // browseButton
        // 
        browseButton.Anchor = AnchorStyles.Left;
        browseButton.AutoSize = true;
        browseButton.Location = new Point(342, 3);
        browseButton.Name = "browseButton";
        browseButton.Size = new Size(75, 25);
        browseButton.TabIndex = 18;
        browseButton.Text = "Browse...";
        browseButton.UseVisualStyleBackColor = true;
        browseButton.Click += browseButton_Click;
        // 
        // okButton
        // 
        okButton.Anchor = AnchorStyles.Left;
        okButton.AutoSize = true;
        okButton.Location = new Point(3, 3);
        okButton.Name = "okButton";
        okButton.Size = new Size(75, 25);
        okButton.TabIndex = 19;
        okButton.Text = "OK";
        okButton.UseVisualStyleBackColor = true;
        okButton.Click += okButton_Click;
        // 
        // cancelButton
        // 
        cancelButton.Anchor = AnchorStyles.Left;
        cancelButton.AutoSize = true;
        cancelButton.Location = new Point(84, 3);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(75, 25);
        cancelButton.TabIndex = 20;
        cancelButton.Text = "Cancel";
        cancelButton.UseVisualStyleBackColor = true;
        cancelButton.Click += cancelButton_Click;
        // 
        // rootTableLayoutPanel
        // 
        rootTableLayoutPanel.AutoSize = true;
        rootTableLayoutPanel.ColumnCount = 2;
        rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle());
        rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootTableLayoutPanel.Controls.Add(label1, 0, 0);
        rootTableLayoutPanel.Controls.Add(proxyTextBox, 1, 0);
        rootTableLayoutPanel.Controls.Add(label2, 0, 1);
        rootTableLayoutPanel.Controls.Add(downloadRetryCountNumericUpDown, 1, 1);
        rootTableLayoutPanel.Controls.Add(label3, 0, 2);
        rootTableLayoutPanel.Controls.Add(downloadRetryIntervalNumericUpDown, 1, 2);
        rootTableLayoutPanel.Controls.Add(label4, 0, 3);
        rootTableLayoutPanel.Controls.Add(loadPageEndTimeoutNumericUpDown, 1, 3);
        rootTableLayoutPanel.Controls.Add(label5, 0, 4);
        rootTableLayoutPanel.Controls.Add(tryClickCountNumericUpDown, 1, 4);
        rootTableLayoutPanel.Controls.Add(label6, 0, 5);
        rootTableLayoutPanel.Controls.Add(tryClickIntervalNumericUpDown, 1, 5);
        rootTableLayoutPanel.Controls.Add(label7, 0, 6);
        rootTableLayoutPanel.Controls.Add(startDownloadTimeoutNumericUpDown, 1, 6);
        rootTableLayoutPanel.Controls.Add(label8, 0, 7);
        rootTableLayoutPanel.Controls.Add(downloadTimeoutNumericUpDown, 1, 7);
        rootTableLayoutPanel.Controls.Add(label9, 0, 8);
        rootTableLayoutPanel.Controls.Add(flowLayoutPanel2, 1, 8);
        rootTableLayoutPanel.Controls.Add(defaultDownloadDirectoryLabel, 0, 9);
        rootTableLayoutPanel.Controls.Add(defaultDownloadDirectoryTextBox, 1, 9);
        rootTableLayoutPanel.Controls.Add(toolButtonFlowLayoutPanel, 1, 10);
        rootTableLayoutPanel.Location = new Point(0, 0);
        rootTableLayoutPanel.Name = "rootTableLayoutPanel";
        rootTableLayoutPanel.RowCount = 11;
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle());
        rootTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        rootTableLayoutPanel.Size = new Size(584, 347);
        rootTableLayoutPanel.TabIndex = 21;
        // 
        // flowLayoutPanel2
        // 
        flowLayoutPanel2.Anchor = AnchorStyles.Left;
        flowLayoutPanel2.AutoSize = true;
        flowLayoutPanel2.Controls.Add(externalJavascriptEditorTextBox);
        flowLayoutPanel2.Controls.Add(browseButton);
        flowLayoutPanel2.Location = new Point(160, 235);
        flowLayoutPanel2.Name = "flowLayoutPanel2";
        flowLayoutPanel2.Size = new Size(420, 31);
        flowLayoutPanel2.TabIndex = 23;
        // 
        // toolButtonFlowLayoutPanel
        // 
        toolButtonFlowLayoutPanel.Anchor = AnchorStyles.Right;
        toolButtonFlowLayoutPanel.AutoSize = true;
        toolButtonFlowLayoutPanel.Controls.Add(okButton);
        toolButtonFlowLayoutPanel.Controls.Add(cancelButton);
        toolButtonFlowLayoutPanel.Location = new Point(419, 307);
        toolButtonFlowLayoutPanel.Name = "toolButtonFlowLayoutPanel";
        toolButtonFlowLayoutPanel.Size = new Size(162, 31);
        toolButtonFlowLayoutPanel.TabIndex = 22;
        // 
        // SettingsForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        ClientSize = new Size(587, 347);
        Controls.Add(rootTableLayoutPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Settings";
        ((System.ComponentModel.ISupportInitialize)downloadRetryCountNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)downloadRetryIntervalNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)loadPageEndTimeoutNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)tryClickCountNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)tryClickIntervalNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)startDownloadTimeoutNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)downloadTimeoutNumericUpDown).EndInit();
        rootTableLayoutPanel.ResumeLayout(false);
        rootTableLayoutPanel.PerformLayout();
        flowLayoutPanel2.ResumeLayout(false);
        flowLayoutPanel2.PerformLayout();
        toolButtonFlowLayoutPanel.ResumeLayout(false);
        toolButtonFlowLayoutPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private TextBox proxyTextBox;
    private Label label1;
    private NumericUpDown downloadRetryCountNumericUpDown;
    private Label label2;
    private NumericUpDown downloadRetryIntervalNumericUpDown;
    private Label label3;
    private NumericUpDown loadPageEndTimeoutNumericUpDown;
    private Label label4;
    private NumericUpDown tryClickCountNumericUpDown;
    private Label label5;
    private NumericUpDown tryClickIntervalNumericUpDown;
    private Label label6;
    private NumericUpDown startDownloadTimeoutNumericUpDown;
    private Label label7;
    private NumericUpDown downloadTimeoutNumericUpDown;
    private Label label8;
    private TextBox externalJavascriptEditorTextBox;
    private Label label9;
    private TextBox defaultDownloadDirectoryTextBox;
    private Label defaultDownloadDirectoryLabel;
    private Button browseButton;
    private Button okButton;
    private Button cancelButton;
    private TableLayoutPanel rootTableLayoutPanel;
    private FlowLayoutPanel toolButtonFlowLayoutPanel;
    private FlowLayoutPanel flowLayoutPanel2;
}
