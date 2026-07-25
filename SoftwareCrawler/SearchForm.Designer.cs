using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoftwareCrawler
{
    partial class SearchForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private TextBox searchTextBox = null!;
        private CheckBox matchCaseCheckBox = null!;
        private CheckBox firstMatchPerRowCheckBox = null!;
        private Button findNextButton = null!;
        private Button findPreviousButton = null!;
        private Button closeButton = null!;
        private Label resultsLabel = null!;
        private TableLayoutPanel mainTableLayoutPanel = null!;
        private FlowLayoutPanel checkBoxFlowLayoutPanel = null!;
        private FlowLayoutPanel buttonFlowLayoutPanel = null!;

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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            searchTextBox = new TextBox();
            matchCaseCheckBox = new CheckBox();
            firstMatchPerRowCheckBox = new CheckBox();
            findNextButton = new Button();
            findPreviousButton = new Button();
            closeButton = new Button();
            resultsLabel = new Label();
            mainTableLayoutPanel = new TableLayoutPanel();
            checkBoxFlowLayoutPanel = new FlowLayoutPanel();
            buttonFlowLayoutPanel = new FlowLayoutPanel();

            mainTableLayoutPanel.SuspendLayout();
            checkBoxFlowLayoutPanel.SuspendLayout();
            buttonFlowLayoutPanel.SuspendLayout();
            SuspendLayout();

            // SearchForm
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ClientSize = new Size(450, 140);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MinimumSize = new Size(300, 100);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Find";
            TopMost = true;

            // mainTableLayoutPanel
            mainTableLayoutPanel.AutoSize = true;
            mainTableLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            mainTableLayoutPanel.ColumnCount = 2;
            mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            mainTableLayoutPanel.Dock = DockStyle.Fill;
            mainTableLayoutPanel.Location = new Point(0, 0);
            mainTableLayoutPanel.Margin = new Padding(10);
            mainTableLayoutPanel.Name = "mainTableLayoutPanel";
            mainTableLayoutPanel.Padding = new Padding(10);
            mainTableLayoutPanel.RowCount = 3;
            mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayoutPanel.TabIndex = 0;

            // searchTextBox
            searchTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            searchTextBox.Location = new Point(13, 13);
            searchTextBox.Name = "searchTextBox";
            searchTextBox.Size = new Size(279, 23);
            searchTextBox.TabIndex = 0;
            searchTextBox.TextChanged += SearchTextBox_TextChanged;
            searchTextBox.KeyDown += SearchTextBox_KeyDown;

            // buttonFlowLayoutPanel
            buttonFlowLayoutPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            buttonFlowLayoutPanel.AutoSize = true;
            buttonFlowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonFlowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonFlowLayoutPanel.Location = new Point(298, 10);
            buttonFlowLayoutPanel.Name = "buttonFlowLayoutPanel";
            mainTableLayoutPanel.SetRowSpan(buttonFlowLayoutPanel, 2);
            buttonFlowLayoutPanel.Size = new Size(139, 60);
            buttonFlowLayoutPanel.TabIndex = 1;
            buttonFlowLayoutPanel.WrapContents = true;

            // findPreviousButton
            findPreviousButton.AutoSize = true;
            findPreviousButton.Location = new Point(3, 3);
            findPreviousButton.Name = "findPreviousButton";
            findPreviousButton.Size = new Size(65, 25);
            findPreviousButton.TabIndex = 0;
            findPreviousButton.Text = "Previous";
            findPreviousButton.UseVisualStyleBackColor = true;
            findPreviousButton.Click += FindPreviousButton_Click;

            // findNextButton
            findNextButton.AutoSize = true;
            findNextButton.Location = new Point(74, 3);
            findNextButton.Name = "findNextButton";
            findNextButton.Size = new Size(44, 25);
            findNextButton.TabIndex = 1;
            findNextButton.Text = "Next";
            findNextButton.UseVisualStyleBackColor = true;
            findNextButton.Click += FindNextButton_Click;

            // closeButton
            closeButton.AutoSize = true;
            closeButton.Location = new Point(3, 34);
            closeButton.Name = "closeButton";
            closeButton.Size = new Size(44, 25);
            closeButton.TabIndex = 2;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            closeButton.Click += CloseButton_Click;

            // checkBoxFlowLayoutPanel
            checkBoxFlowLayoutPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            checkBoxFlowLayoutPanel.AutoSize = true;
            checkBoxFlowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            checkBoxFlowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
            checkBoxFlowLayoutPanel.Location = new Point(13, 42);
            checkBoxFlowLayoutPanel.Name = "checkBoxFlowLayoutPanel";
            checkBoxFlowLayoutPanel.Size = new Size(279, 23);
            checkBoxFlowLayoutPanel.TabIndex = 2;
            checkBoxFlowLayoutPanel.WrapContents = true;

            // matchCaseCheckBox
            matchCaseCheckBox.AutoSize = true;
            matchCaseCheckBox.Location = new Point(3, 3);
            matchCaseCheckBox.Name = "matchCaseCheckBox";
            matchCaseCheckBox.Size = new Size(85, 19);
            matchCaseCheckBox.TabIndex = 0;
            matchCaseCheckBox.Text = "Match case";
            matchCaseCheckBox.UseVisualStyleBackColor = true;
            matchCaseCheckBox.CheckedChanged += MatchCaseCheckBox_CheckedChanged;

            // firstMatchPerRowCheckBox
            firstMatchPerRowCheckBox.AutoSize = true;
            firstMatchPerRowCheckBox.Checked = true;
            firstMatchPerRowCheckBox.Location = new Point(94, 3);
            firstMatchPerRowCheckBox.Name = "firstMatchPerRowCheckBox";
            firstMatchPerRowCheckBox.Size = new Size(130, 19);
            firstMatchPerRowCheckBox.TabIndex = 1;
            firstMatchPerRowCheckBox.Text = "First match per row";
            firstMatchPerRowCheckBox.UseVisualStyleBackColor = true;
            firstMatchPerRowCheckBox.CheckedChanged += FirstMatchPerRowCheckBox_CheckedChanged;

            // resultsLabel
            resultsLabel.Anchor = AnchorStyles.Left;
            resultsLabel.AutoSize = true;
            mainTableLayoutPanel.SetColumnSpan(resultsLabel, 2);
            resultsLabel.Location = new Point(13, 78);
            resultsLabel.Name = "resultsLabel";
            resultsLabel.Size = new Size(0, 15);
            resultsLabel.TabIndex = 3;

            // Add controls to containers
            buttonFlowLayoutPanel.Controls.Add(findPreviousButton);
            buttonFlowLayoutPanel.Controls.Add(findNextButton);
            buttonFlowLayoutPanel.Controls.Add(closeButton);

            checkBoxFlowLayoutPanel.Controls.Add(matchCaseCheckBox);
            checkBoxFlowLayoutPanel.Controls.Add(firstMatchPerRowCheckBox);

            mainTableLayoutPanel.Controls.Add(searchTextBox, 0, 0);
            mainTableLayoutPanel.Controls.Add(buttonFlowLayoutPanel, 1, 0);
            mainTableLayoutPanel.Controls.Add(checkBoxFlowLayoutPanel, 0, 1);
            mainTableLayoutPanel.Controls.Add(resultsLabel, 0, 2);

            // Add main container to form
            Controls.Add(mainTableLayoutPanel);

            mainTableLayoutPanel.ResumeLayout(false);
            mainTableLayoutPanel.PerformLayout();
            buttonFlowLayoutPanel.ResumeLayout(false);
            buttonFlowLayoutPanel.PerformLayout();
            checkBoxFlowLayoutPanel.ResumeLayout(false);
            checkBoxFlowLayoutPanel.PerformLayout();
            ResumeLayout(false);
            PerformLayout();

            // Set initial focus
            ActiveControl = searchTextBox;
        }

        #endregion
    }
}
