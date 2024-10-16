using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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

    private readonly TaskCompletionSource<bool> _onLoadTaskCompletionSource = new();

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        using (new DownloadUIDisabler(this))
        {
            await Settings.Load();

            var parentForm = new Form();
            await Browser.Init(parentForm);
            if (parentForm != null)
                parentForm.Size = new Size(1280, 720);

            await Reload();
        }

        _onLoadTaskCompletionSource.TrySetResult(true);
    }

    private async Task Reload()
    {
        await SoftwareManager.Load();
        var bindingList = new BindingList<SoftwareItem>(SoftwareManager.Items);
        softwareListDataGridView.DataSource = new BindingSource(bindingList, null);
        softwareListDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        foreach (DataGridViewColumn column in softwareListDataGridView.Columns)
            if (column.Width > 400)
                column.Width = 400;
    }

    private SoftwareItem? _currentDownloadItem;

    public async Task<bool> DownloadAll()
    {
        Logger.Information("DownloadAll starts");

        await _onLoadTaskCompletionSource.Task;

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
                if (!await item.Download(retryCount: Settings.DownloadRetryCount))
                    success = false;
            }
        }

        Logger.Information("DownloadAll ends with success = {Success}", success);
        return success;
    }

    public async Task<bool> DownloadSelected()
    {
        Logger.Information("DownloadSelected starts");

        await _onLoadTaskCompletionSource.Task;

        _hasCancelled = false;
        var success = true;

        using (new DownloadUIDisabler(this))
        {
            var items = GetSelectedItems();

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
                if (!await item.Download(retryCount: Settings.DownloadRetryCount))
                    success = false;
            }
        }

        Logger.Information("DownloadSelected ends with success = {Success}", success);
        return success;
    }

    private List<SoftwareItem> GetSelectedItems()
    {
        var items = softwareListDataGridView.SelectedRows.OfType<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.DataBoundItem)
            .OfType<SoftwareItem>()
            .ToList();
        return items;
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
            var items = GetSelectedItems();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
                item.ResetStatus();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
            {
                _currentDownloadItem = item;
                // ReSharper disable once RedundantSuppressNullableWarningExpression
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

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var pattern = "[" + Regex.Escape(new string(invalidChars)) + "]";
        return Regex.Replace(fileName, pattern, "_");
    }

    private async void editScriptToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = (SoftwareItem)softwareListDataGridView.CurrentRow.DataBoundItem;
        // Unescape the return character
        var script = item.XPathOrScripts.Replace("`n", "\r\n");

        // Save script to a temp file or reload from file
        var tempScriptDir = Path.Join(Path.GetTempPath(), "SoftwareCrawler");
        Directory.CreateDirectory(tempScriptDir);
        var tempScriptFilePath = Path.Join(tempScriptDir, SanitizeFileName(item.Name) + ".js");

        if (File.Exists(tempScriptFilePath))
            if (MessageBox.Show("The script file already exists. Press Yes to reload or No to override?", "",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                item.XPathOrScripts = await File.ReadAllTextAsync(tempScriptFilePath);
                File.Delete(tempScriptFilePath);
                return;
            }

        await File.WriteAllTextAsync(tempScriptFilePath, script, new UTF8Encoding(true));

        // Edit the script with an external editor
        var editor = Settings.ExternalJavascriptEditor;
        if (editor == "")
            editor = "notepad.exe";

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = editor,
            Arguments = tempScriptFilePath,
            UseShellExecute = true,
        });
        if (proc == null)
            return;

        MessageBox.Show("Edit the script and save it. Then click OK to reload the script.");

        // Read script from the temp file
        script = await File.ReadAllTextAsync(tempScriptFilePath);
        script = script.Trim(); // Trim end of file
        File.Delete(tempScriptFilePath);
        script = script.Replace("\r\n", "`n").Replace("\n", "`n");
        item.XPathOrScripts = script;

        await SoftwareManager.Save();
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

    private async void openWebPageToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem;
        await Browser.Load(item!.WebPage);
    }

    private void openWebPageInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
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

        new[] { item.DownloadDirectory, item.DownloadDirectory2 }
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

    private void Restart()
    {
        Process.Start(Application.ExecutablePath);
        Application.Exit();
    }

    private void softwareListDataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
    {
        if (softwareListDataGridView.IsCurrentCellDirty
            && softwareListDataGridView.CurrentCell.OwningColumn is DataGridViewCheckBoxColumn)
            softwareListDataGridView.EndEdit();
    }

    private void clearCookieButton_Click(object sender, EventArgs e)
    {
        // clear cache
        Cef.GetGlobalCookieManager().DeleteCookies("", "");
    }
}
