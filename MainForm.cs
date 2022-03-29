using System.ComponentModel;
using System.Diagnostics;
using CefSharp;

namespace SoftwareCrawler;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private class DownloadUIDisabler : IDisposable
    {
        private readonly MainForm _mainForm;

        public DownloadUIDisabler(MainForm mainForm)
        {
            _mainForm = mainForm;

            _mainForm.downloadSelectedToolStripMenuItem.Enabled = false;
            _mainForm.downloadAllToolStripMenuItem.Enabled = false;
            _mainForm.testSelectedToolStripMenuItem.Enabled = false;
            _mainForm.testAllToolStripMenuItem.Enabled = false;
            _mainForm.reloadToolStripMenuItem.Enabled = false;

            _mainForm.cancelToolStripMenuItem.Enabled = true;
        }

        public void Dispose()
        {
            _mainForm.downloadSelectedToolStripMenuItem.Enabled = true;
            _mainForm.downloadAllToolStripMenuItem.Enabled = true;
            _mainForm.testSelectedToolStripMenuItem.Enabled = true;
            _mainForm.testAllToolStripMenuItem.Enabled = true;
            _mainForm.reloadToolStripMenuItem.Enabled = true;

            _mainForm.cancelToolStripMenuItem.Enabled = false;
            
            _mainForm._currentDownloadItem = null;
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        using (new DownloadUIDisabler(this))
        {
            await Reload();
            await Browser.Init();

            // var browserForm = new Form();
            // browserForm.Controls.Add(WebBrowser.Browser);
            // WebBrowser.Browser.Dock = DockStyle.Fill;
            // browserForm.Show();
            // browserForm.Size = new Size(1280, 720);
        }
    }

    private async Task Reload()
    {
        await SoftwareManager.Load();
        var bindingList = new BindingList<SoftwareItem>(SoftwareManager.Items);
        softwareListDataGridView.DataSource = new BindingSource(bindingList, null);
    }

    private SoftwareItem? _currentDownloadItem;

    public async Task<bool> DownloadAll()
    {
        Logger.Information("DownloadAll starts.");

        _hasCancelled = false;
        var success = true;

        using (new DownloadUIDisabler(this))
        {
            var items = SoftwareManager.Items.ToList();

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                item.ResetStatus();
            }

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                _currentDownloadItem = item;
                if (!await item.Download(retryCount: 3))
                    success = false;
            }
        }

        Logger.Information($"DownloadAll ends with success = {success}.");
        return success;
    }

    public async Task<bool> DownloadSelected()
    {
        Logger.Information("DownloadSelected starts.");

        _hasCancelled = false;
        var success = true;

        using (new DownloadUIDisabler(this))
        {
            var items = new List<SoftwareItem>();
            foreach (DataGridViewRow row in softwareListDataGridView.SelectedRows)
                if (row.DataBoundItem is SoftwareItem item)
                    items.Add(item);

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                item.ResetStatus();
            }

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                _currentDownloadItem = item;
                if (!await item.Download(retryCount: 3))
                    success = false;
            }
        }

        Logger.Information($"DownloadSelected ends with success = {success}.");
        return success;
    }

    private async void downloadSelectedToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await DownloadSelected();
    }

    private async void downloadAllToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await DownloadAll();
    }

    private async void testSelectedToolStripMenuItem_Click(object sender, EventArgs e)
    {
        _hasCancelled = false;

        using (new DownloadUIDisabler(this))
        {
            var items = softwareListDataGridView.SelectedRows.OfType<DataGridViewRow>()
                .Select(row => row.DataBoundItem as SoftwareItem)
                .Where(item => item != null)
                .ToList();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
                item!.ResetStatus();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
            {
                _currentDownloadItem = item;
                await item!.Download(true);
            }
        }
    }

    private async void testAllToolStripMenuItem_Click(object sender, EventArgs e)
    {
        _hasCancelled = false;

        using (new DownloadUIDisabler(this))
        {
            foreach (var item in SoftwareManager.Items.TakeWhile(_ => !_hasCancelled))
                item.ResetStatus();

            foreach (var item in SoftwareManager.Items.TakeWhile(_ => !_hasCancelled))
            {
                _currentDownloadItem = item;
                await item.Download(true);
            }
        }
    }

    private async void reloadToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await Reload();
    }

    private async void softwareListDataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        await SoftwareManager.Save();
    }

    private void softwareListDataGridView_MouseDown(object sender, MouseEventArgs e)
    {
        // Right click selects the row
        if (e.Button != MouseButtons.Right)
            return;

        var hit = softwareListDataGridView.HitTest(e.X, e.Y);
        if (hit.RowIndex == -1)
            return;

        var rowUnderCursor = softwareListDataGridView.Rows[hit.RowIndex];
        if (!rowUnderCursor.Selected)
            softwareListDataGridView.CurrentCell = rowUnderCursor.Cells[hit.ColumnIndex];
    }

    private async void softwareListDataGridView_UserAddedRow(object sender, DataGridViewRowEventArgs e)
    {
        await SoftwareManager.Save();
    }

    private async void softwareListDataGridView_UserDeletedRow(object sender, DataGridViewRowEventArgs e)
    {
        await SoftwareManager.Save();
    }

    private void softwareListDataGridView_CurrentCellChanged(object sender, EventArgs e)
    {
        // errorMessageLabel bind to selected SoftwareItem 
        errorMessageLabel.DataBindings.Clear();

        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        errorMessageLabel.DataBindings.Add(new Binding("Text", softwareListDataGridView.CurrentRow.DataBoundItem, "ErrorMessage"));
    }

    private void showDevToolsButton_Click(object sender, EventArgs e)
    {
        Browser.WebBrowser.ShowDevTools();
    }

    private void openWebPageToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = (softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem)!.WebPage,
            UseShellExecute = true,
        });
    }

    private void openDownloadDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = (softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem)!;

        new[] {item.DownloadDirectory, item.DownloadDirectory2}
            .Where(dir => !string.IsNullOrEmpty(dir))
            .ToList().ForEach(dir =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            });
    }

    private bool _hasCancelled;

    private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
    {
        _hasCancelled = true;
        _currentDownloadItem?.CancelDownload();
    }
}
