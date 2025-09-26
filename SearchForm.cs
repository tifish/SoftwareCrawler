using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoftwareCrawler
{
    public partial class SearchForm : Form
    {
        public string SearchText => searchTextBox.Text;
        public bool MatchCase => matchCaseCheckBox.Checked;
        public bool FirstMatchPerRow => firstMatchPerRowCheckBox.Checked;

        public event EventHandler? SearchNext;
        public event EventHandler? SearchPrevious;
        public event EventHandler? SearchTextChanged;

        private TextBox searchTextBox = null!;
        private CheckBox matchCaseCheckBox = null!;
        private CheckBox firstMatchPerRowCheckBox = null!;
        private Button findNextButton = null!;
        private Button findPreviousButton = null!;
        private Button closeButton = null!;
        private Label resultsLabel = null!;

        public SearchForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            searchTextBox = new TextBox();
            matchCaseCheckBox = new CheckBox();
            firstMatchPerRowCheckBox = new CheckBox();
            findNextButton = new Button();
            findPreviousButton = new Button();
            closeButton = new Button();
            resultsLabel = new Label();

            SuspendLayout();

            // SearchForm
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(400, 120);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Find";
            TopMost = true;

            // searchTextBox
            searchTextBox.Location = new Point(12, 12);
            searchTextBox.Name = "searchTextBox";
            searchTextBox.Size = new Size(200, 23);
            searchTextBox.TabIndex = 0;
            searchTextBox.TextChanged += SearchTextBox_TextChanged;
            searchTextBox.KeyDown += SearchTextBox_KeyDown;

            // matchCaseCheckBox
            matchCaseCheckBox.AutoSize = true;
            matchCaseCheckBox.Location = new Point(12, 41);
            matchCaseCheckBox.Name = "matchCaseCheckBox";
            matchCaseCheckBox.Size = new Size(85, 19);
            matchCaseCheckBox.TabIndex = 1;
            matchCaseCheckBox.Text = "Match case";
            matchCaseCheckBox.UseVisualStyleBackColor = true;
            matchCaseCheckBox.CheckedChanged += MatchCaseCheckBox_CheckedChanged;

            // firstMatchPerRowCheckBox
            firstMatchPerRowCheckBox.AutoSize = true;
            firstMatchPerRowCheckBox.Location = new Point(12, 66);
            firstMatchPerRowCheckBox.Name = "firstMatchPerRowCheckBox";
            firstMatchPerRowCheckBox.Size = new Size(130, 19);
            firstMatchPerRowCheckBox.TabIndex = 2;
            firstMatchPerRowCheckBox.Text = "First match per row";
            firstMatchPerRowCheckBox.Checked = true;
            firstMatchPerRowCheckBox.UseVisualStyleBackColor = true;
            firstMatchPerRowCheckBox.CheckedChanged += FirstMatchPerRowCheckBox_CheckedChanged;

            // findPreviousButton
            findPreviousButton.Location = new Point(218, 12);
            findPreviousButton.Name = "findPreviousButton";
            findPreviousButton.Size = new Size(75, 23);
            findPreviousButton.TabIndex = 3;
            findPreviousButton.Text = "Previous";
            findPreviousButton.UseVisualStyleBackColor = true;
            findPreviousButton.Click += FindPreviousButton_Click;

            // findNextButton
            findNextButton.Location = new Point(299, 12);
            findNextButton.Name = "findNextButton";
            findNextButton.Size = new Size(75, 23);
            findNextButton.TabIndex = 4;
            findNextButton.Text = "Next";
            findNextButton.UseVisualStyleBackColor = true;
            findNextButton.Click += FindNextButton_Click;

            // closeButton
            closeButton.Location = new Point(299, 41);
            closeButton.Name = "closeButton";
            closeButton.Size = new Size(75, 23);
            closeButton.TabIndex = 5;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            closeButton.Click += CloseButton_Click;

            // resultsLabel
            resultsLabel.AutoSize = true;
            resultsLabel.Location = new Point(12, 95);
            resultsLabel.Name = "resultsLabel";
            resultsLabel.Size = new Size(0, 15);
            resultsLabel.TabIndex = 6;

            // Add controls
            Controls.Add(searchTextBox);
            Controls.Add(matchCaseCheckBox);
            Controls.Add(firstMatchPerRowCheckBox);
            Controls.Add(findNextButton);
            Controls.Add(findPreviousButton);
            Controls.Add(closeButton);
            Controls.Add(resultsLabel);

            ResumeLayout(false);
            PerformLayout();

            // Set initial focus
            ActiveControl = searchTextBox;
        }

        private void SearchTextBox_TextChanged(object? sender, EventArgs e)
        {
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (e.Shift)
                    SearchPrevious?.Invoke(this, EventArgs.Empty);
                else
                    SearchNext?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                SearchPrevious?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                SearchNext?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void MatchCaseCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FirstMatchPerRowCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FindNextButton_Click(object? sender, EventArgs e)
        {
            SearchNext?.Invoke(this, EventArgs.Empty);
        }

        private void FindPreviousButton_Click(object? sender, EventArgs e)
        {
            SearchPrevious?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();
        }

        public void UpdateResults(int currentMatch, int totalMatches)
        {
            if (totalMatches == 0)
                resultsLabel.Text = "No matches found";
            else
                resultsLabel.Text = $"{currentMatch} of {totalMatches}";
        }

        public void FocusSearchBox()
        {
            searchTextBox.Focus();
            searchTextBox.SelectAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
