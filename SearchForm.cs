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


        public SearchForm()
        {
            InitializeComponent();
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
